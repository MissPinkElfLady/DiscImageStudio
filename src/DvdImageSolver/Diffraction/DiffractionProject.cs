using System.Globalization;
using System.Text.Json;
using DiscImageStudio.Core;
using DiscImageStudio.ImageIO;
using DvdImageSolver.Solver;

namespace DvdImageSolver.Diffraction;

/// <summary>
/// A diffraction layout file (JSON) and the ISO it describes.
/// <code>
/// {
///   "discproj": "MyCalibration.discproj",          // or "geometry": { innerRadiusMm, outerRadiusMm, sectors,
///                                                   //   pitchLinear, pitchQuadratic, pitchCubic, channelBitLengthNm }
///   "thetaRefDegrees": 0,                           // spiral angle at the top of the canvas (DiscDiffractStudio: 0)
///   "pictures": [
///     { "name": "eyes", "centerMm": [0, 43], "sizeMm": [24, 24], "effect": "flip",
///       "images": ["open.png", "closed.png"], "quarterTurns": 0 }
///   ]
/// }
/// </code>
/// Canvas: millimetres from the hub, X right, Y up, seen from the data side. Effects: "single" (one grey image on
/// 0x92), "flip" and "stereo" (two grey images on 0x92 / 0xB9), "color" (one colour image seen by tilting, at 3 or
/// 9 o'clock, or at 12 / 6 o'clock with a quarter turn; "copies": 1 or 2 tilts where its colours are right;
/// "lamp": "side" for a picture seen by turning, at 12 or 6 o'clock; "coverage": "wide" for one picture as large as
/// the disc, each part of it tuned for the light direction that suits it, so that a lamp shows two opposite wedges
/// of it and moving the lamp round the disc shows the rest).
/// "brightness" scales every view's lit share. "carriers" (e.g. ["0x92", "0xB9", "0x66"]) may
/// be given instead of an effect; "gains" defaults to equal white, "turnCounts" to one turn each.
/// Relative paths are relative to the layout file.
/// </summary>
internal static class DiffractionProject
{
    internal sealed record Loaded(DiffractionLayout Layout, uint TotalSectors, double ChannelBitLengthNm,
        double InnerRadiusMm, double OuterRadiusMm, double PitchLinear, double PitchQuadratic, double PitchCubic);

    internal static Loaded Load(string path)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = document.RootElement;

        double inner, outer, linear = 0, quadratic = 0, cubic = 0, bitNm = 133.3;
        long sectors;
        if (root.TryGetProperty("discproj", out JsonElement discproj))
        {
            ProjectDocument project = ProjectJsonStore.Load(Resolve(baseDir, discproj.GetString()!));
            if (project.DiscFamily != ProjectDocument.DvdFamily)
                throw new ArgumentException("The calibration project must be a DVD project.");
            ProjectGeometry g = project.Geometry;
            (inner, outer, sectors, linear, quadratic, cubic) =
                (g.InnerRadiusMm, g.OuterRadiusMm, g.Sectors, g.PitchLinear, g.PitchQuadratic, g.PitchCubic);
        }
        else if (root.TryGetProperty("geometry", out JsonElement g))
        {
            inner = g.GetProperty("innerRadiusMm").GetDouble();
            outer = g.GetProperty("outerRadiusMm").GetDouble();
            sectors = g.GetProperty("sectors").GetInt64();
            linear = Optional(g, "pitchLinear", 0);
            quadratic = Optional(g, "pitchQuadratic", 0);
            cubic = Optional(g, "pitchCubic", 0);
            bitNm = Optional(g, "channelBitLengthNm", 133.3);
        }
        else
        {
            throw new ArgumentException("The layout needs \"discproj\" or \"geometry\" (the disc calibration).");
        }

        if (sectors is <= 0 or > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(path), "sectors is out of range.");
        DvdTrackGeometry geometry = DvdTrackGeometry.Create(inner, outer,
            sectors * (double)DiffractionLayout.BitsPerSector * bitNm * 1e-6, linear, quadratic, cubic);

        List<DiffractionPicture> pictures = [];
        foreach (JsonElement p in root.GetProperty("pictures").EnumerateArray())
            pictures.Add(LoadPicture(p, baseDir, pictures.Count));
        byte off = root.TryGetProperty("offByte", out JsonElement o) ? ParseByte(o) : DiffractionCarriers.OffByte;
        double thetaRef = Optional(root, "thetaRefDegrees", 0) * Math.PI / 180.0;
        return new Loaded(new DiffractionLayout(pictures, geometry, bitNm, off, thetaRef), (uint)sectors, bitNm,
            inner, outer, linear, quadratic, cubic);
    }

    /// <summary>Writes the ISO (raw user sectors, no file system) from LBA 0 to <paramref name="sectors"/>.</summary>
    internal static FastDvdStreamSummary WriteIso(Loaded loaded, string isoPath, uint sectors, uint psnOffset,
        int parallelism, Action<FastDvdStreamProgress>? progress = null)
    {
        MultiBlockSolveOptions options = new(0, sectors, psnOffset, 0, 0, 0, 0, 0, true,
            new ImageMappingOptions(loaded.TotalSectors, loaded.InnerRadiusMm, loaded.OuterRadiusMm,
                loaded.ChannelBitLengthNm, 0, true, 128, 0, 1, loaded.PitchLinear, loaded.PitchQuadratic, loaded.PitchCubic),
            SolverAlgorithm.Dispersion, FastParallelism: parallelism);
        using FileStream stream = new(isoPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        return FastDispersionImageWriter.WriteScrambledBlockStream(
            (lba, block) => loaded.Layout.FillBlock(lba, block), stream, options, progress);
    }

    /// <summary>
    /// Writes a PNG of the disc seen from the data side. Without <paramref name="lightClockDegrees"/>: the design, each
    /// picture as it shows from its own design pose (colour pictures in colour, the first view of others in grey).
    /// With it: what an eye 50 cm in front of the hub sees of the colour pictures with a small lamp 1 m away, raised as
    /// for the design and standing at that many degrees clockwise from 12 o'clock (0 = above, 90 = to the right),
    /// using the same line-spectrum model as the white balance.
    /// </summary>
    internal static void WritePreview(Loaded loaded, string pngPath, double? lightClockDegrees = null, int pixels = 1200)
    {
        const double DiscMm = 120.0;
        double mmPerPixel = DiscMm / pixels;
        byte[] bgra = new byte[pixels * pixels * 4];
        byte[] srgb = new byte[4096];
        for (int k = 0; k < srgb.Length; k++)
        {
            double v = k / (srgb.Length - 1.0);
            srgb[k] = (byte)Math.Round(255 * (v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055));
        }

        byte Encode(double linear) => srgb[(int)Math.Round(Math.Clamp(linear, 0, 1) * (srgb.Length - 1))];
        DiffractionViewing.Vec3 eye = default, lamp = default;
        double referenceY = 1;
        if (lightClockDegrees is double degrees)
        {
            (eye, DiffractionViewing.Vec3 above) = DiffractionViewing.Cyclopean(DiffractionColor.SlotCentre(3),
                DiffractionColor.ColorULeft, lampTop: true);
            double b = degrees * Math.PI / 180;
            lamp = new DiffractionViewing.Vec3(above.Y * Math.Sin(b), above.Y * Math.Cos(b), above.Z);
            // White: every view of a colour picture fully lit, seen at the design u.
            referenceY = 0;
            foreach (DiffractionPicture picture in loaded.Layout.Pictures)
            {
                if (!IsColour(picture)) continue;
                double y = 0;
                for (int v = 0; v < Math.Min(3, picture.ViewCount); v++)
                {
                    CarrierGroup g = picture.Carriers[v].Group!;
                    y += Share(picture, v) * picture.Gains[v] * DiffractionViewing.SignalXyz(g.Strings[g.Nominal], DiffractionColor.ColorULeft)[1];
                }

                referenceY = Math.Max(referenceY, y);
            }

            if (referenceY <= 0) referenceY = 1;
        }

        for (int py = 0; py < pixels; py++)
        {
            double y = DiscMm / 2 - (py + 0.5) * mmPerPixel;
            for (int px = 0; px < pixels; px++)
            {
                double x = -DiscMm / 2 + (px + 0.5) * mmPerPixel;
                double r = Math.Sqrt(x * x + y * y);
                int o = (py * pixels + px) * 4;
                bool inRing = r >= loaded.InnerRadiusMm && r <= loaded.OuterRadiusMm;
                (double red, double green, double blue) = inRing ? (0.0, 0.0, 0.0) : (0.35, 0.35, 0.37);
                if (inRing)
                {
                    foreach (DiffractionPicture picture in loaded.Layout.Pictures)
                    {
                        if (!picture.TryCell(x, y, out int j, out int i)) continue;
                        bool colour = IsColour(picture);
                        if (lightClockDegrees is null)
                        {
                            double Level(int v) => picture.Gains[v] > 0 ? picture.Levels[v][j, i] / picture.Gains[v] : 0;
                            (red, green, blue) = colour ? (Level(0), Level(1), Level(2)) : (Level(0), Level(0), Level(0));
                            continue;
                        }

                        if (!colour) continue;
                        double u = DiffractionViewing.TangentialU(x, y, eye, lamp);
                        double X = 0, Y = 0, Z = 0;
                        for (int v = 0; v < picture.ViewCount; v++)
                        {
                            CarrierGroup g = picture.Carriers[v].Group!;
                            double w = Share(picture, v) * picture.Levels[v][j, i] / referenceY;
                            if (w <= 0) continue;
                            double[] xyz = DiffractionViewing.SignalXyz(g.Strings[g.Variant is byte[,] map ? map[j, i] : g.Nominal], u);
                            (X, Y, Z) = (X + w * xyz[0], Y + w * xyz[1], Z + w * xyz[2]);
                        }

                        // XYZ (D65 white at Y = 1) to linear sRGB.
                        red = 3.2406 * X - 1.5372 * Y - 0.4986 * Z;
                        green = -0.9689 * X + 1.8758 * Y + 0.0415 * Z;
                        blue = 0.0557 * X - 0.2040 * Y + 1.0570 * Z;
                    }
                }

                (bgra[o], bgra[o + 1], bgra[o + 2], bgra[o + 3]) = (Encode(blue), Encode(green), Encode(red), 255);
            }
        }

        RasterCodec.SavePng(pngPath, pixels, pixels, bgra, grayscale: false);
    }

    private static bool IsColour(DiffractionPicture picture)
        => picture.ViewCount >= 3 && picture.Carriers.All(c => c.Group is not null);

    private static double Share(DiffractionPicture picture, int view)
        => (double)picture.TurnCounts[view] / picture.TurnCounts.Sum();

    private static DiffractionPicture LoadPicture(JsonElement p, string baseDir, int index)
    {
        string name = p.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? $"picture {index + 1}" : $"picture {index + 1}";
        double[] centre = Doubles(p.GetProperty("centerMm"), 2, "centerMm");
        double[] size = Doubles(p.GetProperty("sizeMm"), 2, "sizeMm");
        double cell = Optional(p, "cellMm", 0.05);
        string[] images = [.. p.GetProperty("images").EnumerateArray().Select(x => Resolve(baseDir, x.GetString()!))];
        int quarterTurns = p.TryGetProperty("quarterTurns", out JsonElement q) ? q.GetInt32() : 0;
        double brightness = Optional(p, "brightness", 1.0);
        int nx = (int)Math.Round(size[0] / cell, MidpointRounding.ToEven);
        int ny = (int)Math.Round(size[1] / cell, MidpointRounding.ToEven);
        // The images are sampled in the orientation they are drawn; a quarter turn swaps the cell grid.
        (int vy, int vx) = quarterTurns % 2 == 0 ? (ny, nx) : (nx, ny);
        string effect = p.TryGetProperty("effect", out JsonElement e) ? (e.GetString() ?? "single").ToLowerInvariant() : "single";

        if (!p.TryGetProperty("carriers", out JsonElement c) && effect is "color" or "colour")
        {
            if (images.Length != 1)
                throw new ArgumentException($"Picture {name}: a colour picture takes one image, got {images.Length}.");
            int copies = p.TryGetProperty("copies", out JsonElement k) ? k.GetInt32() : 1;
            string coverage = p.TryGetProperty("coverage", out JsonElement cv) ? (cv.GetString() ?? "spot").ToLowerInvariant() : "spot";
            if (coverage is not ("spot" or "wide"))
                throw new ArgumentException($"Picture {name}: \"coverage\" is \"spot\" or \"wide\", not \"{coverage}\".");
            string lamp = p.TryGetProperty("lamp", out JsonElement lp) ? (lp.GetString() ?? "top").ToLowerInvariant() : "top";
            if (lamp is not ("top" or "side"))
                throw new ArgumentException($"Picture {name}: \"lamp\" is \"top\" or \"side\", not \"{lamp}\".");
            var (red, green, blue) = DiffractionImages.LinearRgbCells(images[0], vy, vx);
            return DiffractionColor.Build(name, (centre[0], centre[1]), (size[0], size[1]), red, green, blue, copies,
                quarterTurns, cell, brightness, wide: coverage == "wide", lampTop: lamp == "top");
        }

        byte[] carriers;
        if (p.TryGetProperty("carriers", out c))
        {
            carriers = [.. c.EnumerateArray().Select(ParseByte)];
        }
        else
        {
            carriers = effect switch
            {
                "single" => [0x92],
                "flip" or "stereo" => [0x92, 0xB9],
                _ => throw new ArgumentException($"Picture {name}: effect \"{effect}\" is not one of single, flip, stereo, color."),
            };
        }

        if (images.Length != carriers.Length)
            throw new ArgumentException($"Picture {name}: {carriers.Length} carrier(s) need {carriers.Length} image(s), got {images.Length}.");
        List<double[,]> views = [.. images.Select(path => DiffractionImages.LinearLuminanceCells(path, vy, vx))];
        double[] gains = p.TryGetProperty("gains", out JsonElement g)
            ? [.. g.EnumerateArray().Select(x => x.GetDouble())]
            : DiffractionCarriers.DefaultGains(carriers);
        gains = [.. gains.Select(x => x * brightness)];
        int[]? turns = p.TryGetProperty("turnCounts", out JsonElement t) ? [.. t.EnumerateArray().Select(x => x.GetInt32())] : null;
        byte off = p.TryGetProperty("offByte", out JsonElement o) ? ParseByte(o) : DiffractionCarriers.OffByte;
        return new DiffractionPicture(name, (centre[0], centre[1]), (size[0], size[1]), views,
            [.. carriers.Select(x => (DiffractionCarrier)x)], linear: true, gains, turns, quarterTurns, cell, off);
    }

    private static string Resolve(string baseDir, string path) => Path.GetFullPath(Path.Combine(baseDir, path));

    private static double Optional(JsonElement e, string key, double fallback)
        => e.TryGetProperty(key, out JsonElement v) ? v.GetDouble() : fallback;

    private static double[] Doubles(JsonElement e, int count, string key)
    {
        double[] values = [.. e.EnumerateArray().Select(x => x.GetDouble())];
        if (values.Length != count) throw new ArgumentException($"{key} takes {count} numbers.");
        return values;
    }

    internal static byte ParseByte(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number) return e.GetByte();
        string s = e.GetString() ?? "";
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? byte.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : byte.Parse(s, CultureInfo.InvariantCulture);
    }
}
