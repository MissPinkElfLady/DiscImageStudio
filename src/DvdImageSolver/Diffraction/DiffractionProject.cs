using System.Globalization;
using System.Text.Json;
using DiscImageStudio.Core;
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
/// 9 o'clock, or at 12 / 6 o'clock with a quarter turn; "copies": 1 or 2 tilts where its colours are right).
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
            var (red, green, blue) = DiffractionImages.LinearRgbCells(images[0], vy, vx);
            return DiffractionColor.Build(name, (centre[0], centre[1]), (size[0], size[1]), red, green, blue, copies,
                quarterTurns, cell, brightness);
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
