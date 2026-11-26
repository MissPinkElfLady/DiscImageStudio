using DiscImageStudio.Core;
using DiscImageStudio.ImageIO;
using DvdImageSolver.Encoding;
using DvdImageSolver.Solver;

namespace DvdImageSolver.Diffraction;

/// <summary>
/// Writes one colour picture over the whole disc (the "wide" colour coverage), for the app's colour mode. The picture
/// is placed where the greyscale writer would place it: same square, same start angle, data running clockwise.
/// </summary>
public static class DiffractionColorDisc
{
    /// <summary>Cell size of the colour picture, in millimetres.</summary>
    public const double CellMm = 0.05;

    /// <summary>
    /// Streams the user sectors from LBA 0 to <see cref="ImageMappingOptions.OutputSectors"/>. The image is the square
    /// of side 2 x <see cref="ImageMappingOptions.SamplingOuterRadiusMm"/> centred on the hub, y down.
    /// </summary>
    public static FastDvdStreamSummary WriteStream(string imagePath, Stream output, MultiBlockSolveOptions options,
        Action<FastDvdStreamProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ReadCompatibility)
            throw new ArgumentException("Colour pictures cannot use read compatibility.", nameof(options));
        DiffractionLayout layout = BuildLayout(imagePath, options.ImageMapping, cancellationToken);
        return FastDispersionImageWriter.WriteScrambledBlockStream(
            (lba, block) => layout.FillBlock(lba, block), output,
            options with { Algorithm = SolverAlgorithm.Dispersion }, progress, cancellationToken);
    }

    /// <summary>
    /// Colour counterpart of <see cref="CalibrationPreviewRenderer.Render"/>: walks the track, takes the colour the
    /// picture encodes at the generated position (red, green and blue levels after quantisation, as the design shows
    /// in its own colours under the right lamp) and splats it at the actual position. Unwritten areas are black.
    /// Tone and thresholds in <paramref name="options"/> are ignored.
    /// </summary>
    public static void RenderPreview(string imagePath, string outputPath, CalibrationPreviewOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Clockwise) throw new ArgumentException("Colour pictures need data running clockwise.", nameof(options));
        if (options.PreviewSize < 16 || options.SamplesPerSector < 1 || options.FillSectors == 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The preview size, samples and sectors must be positive.");
        double trackLengthMm = options.TotalSectors * (double)DvdEccBlockEncoder.ChannelBitsPerSector
            * options.ChannelBitLengthNm * 1e-6;
        DvdTrackGeometry generated = DvdTrackGeometry.Create(options.GeneratedInnerRadiusMm,
            options.GeneratedOuterRadiusMm, trackLengthMm, options.GeneratedPitchLinear,
            options.GeneratedPitchQuadratic, options.GeneratedPitchCubic);
        DvdTrackGeometry actual = DvdTrackGeometry.Create(options.ActualInnerRadiusMm, options.ActualOuterRadiusMm,
            trackLengthMm, options.ActualPitchLinear, options.ActualPitchQuadratic, options.ActualPitchCubic);
        int size = options.PreviewSize;
        double imageRadius = options.SourceImageOuterRadiusMm ?? options.GeneratedOuterRadiusMm;
        double previewRadius = options.PreviewOuterRadiusMm ?? options.ActualOuterRadiusMm;
        // Cells about one preview pixel wide: the colours are the same as at full resolution, just coarser.
        double cell = Math.Max(CellMm, Math.Round(2 * previewRadius / size / CellMm) * CellMm);
        int n = Math.Max(1, (int)Math.Round(2 * imageRadius / cell, MidpointRounding.ToEven));
        double side = n * cell;
        var (red, green, blue) = DiffractionImages.LinearRgbCellsBinned(imagePath, n, n, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        DiffractionPicture picture = DiffractionColor.Build("colour", (0.0, 0.0), (side, side), red, green, blue,
            cellMm: cell, wide: true);
        int[] cellRgb = new int[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int Channel(int v) => EncodeSrgb(picture.Gains[v] > 0 ? picture.Levels[v][j, i] / picture.Gains[v] : 0);
                cellRgb[j * n + i] = (Channel(0) << 16) | (Channel(1) << 8) | Channel(2);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        int[] sumR = new int[size * size], sumG = new int[size * size], sumB = new int[size * size], count = new int[size * size];
        double centre = (size - 1) / 2.0;
        double mmPerPixel = 2 * previewRadius / (size - 1);
        double bitMm = options.ChannelBitLengthNm * 1e-6;
        double startMm = options.StartLba * (double)DvdEccBlockEncoder.ChannelBitsPerSector * bitMm;
        double endMm = (options.StartLba + (double)options.FillSectors) * DvdEccBlockEncoder.ChannelBitsPerSector * bitMm;
        double visibleInner = actual.RadiusAtArcLengthFast(startMm), visibleOuter = actual.RadiusAtArcLengthFast(endMm);
        long totalSamples = checked((long)options.FillSectors * options.SamplesPerSector);
        double bitsPerSample = DvdEccBlockEncoder.ChannelBitsPerSector / (double)options.SamplesPerSector;
        double firstBit = options.StartLba * (double)DvdEccBlockEncoder.ChannelBitsPerSector;
        int workers = Environment.ProcessorCount;
        ParallelOptions parallel = new() { CancellationToken = cancellationToken };
        Parallel.For(0, workers, parallel, worker =>
        {
            for (long k = worker, iteration = 0; k < totalSamples; k += workers, iteration++)
            {
                if ((iteration & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();
                double arcMm = (firstBit + (k + 0.5) * bitsPerSample) * bitMm;
                // The greyscale writer's polar angle with the start angle at 0: minus the track angle, y up.
                (double rg, double tg) = generated.AtArcLengthFast(arcMm);
                int rgb = picture.TryCell(rg * Math.Cos(-tg), rg * Math.Sin(-tg), out int j, out int i) ? cellRgb[j * n + i] : 0;
                (double ra, double ta) = actual.AtArcLengthFast(arcMm);
                double pr = centre * ra / previewRadius;
                int px = (int)Math.Round(centre + pr * Math.Cos(-ta)), py = (int)Math.Round(centre - pr * Math.Sin(-ta));
                if ((uint)px >= (uint)size || (uint)py >= (uint)size) continue;
                int o = py * size + px;
                Interlocked.Add(ref sumR[o], (rgb >> 16) & 255);
                Interlocked.Add(ref sumG[o], (rgb >> 8) & 255);
                Interlocked.Add(ref sumB[o], rgb & 255);
                Interlocked.Increment(ref count[o]);
            }
        });

        cancellationToken.ThrowIfCancellationRequested();
        byte[] pixels = new byte[size * size * 4];
        Parallel.For(0, size, parallel, py =>
        {
            double y = (centre - py) * mmPerPixel;
            for (int px = 0; px < size; px++)
            {
                double x = (px - centre) * mmPerPixel, r = Math.Sqrt(x * x + y * y);
                if (r < visibleInner || r > visibleOuter) continue;
                int o = py * size + px, c = count[o];
                if (c > 0)
                {
                    pixels[o * 4] = (byte)((sumB[o] + c / 2) / c);
                    pixels[o * 4 + 1] = (byte)((sumG[o] + c / 2) / c);
                    pixels[o * 4 + 2] = (byte)((sumR[o] + c / 2) / c);
                }

                pixels[o * 4 + 3] = 255;
            }
        });

        cancellationToken.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(outputPath);
        if (Path.GetDirectoryName(full) is string directory) Directory.CreateDirectory(directory);
        RasterCodec.SavePng(full, size, size, pixels, grayscale: false);
    }

    private static int EncodeSrgb(double linear)
    {
        linear = Math.Clamp(linear, 0.0, 1.0);
        double v = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        return (int)Math.Round(255 * v);
    }

    internal static DiffractionLayout BuildLayout(string imagePath, ImageMappingOptions mapping,
        CancellationToken cancellationToken = default)
    {
        if (!mapping.Clockwise)
            throw new ArgumentException("Colour pictures need data running clockwise.", nameof(mapping));
        double radius = mapping.SamplingOuterRadiusMm;
        if (!double.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapping), "The image radius must be positive.");
        DvdTrackGeometry geometry = DvdTrackGeometry.Create(mapping.InnerRadiusMm, mapping.OuterRadiusMm,
            mapping.TotalSectors * (double)DiffractionLayout.BitsPerSector * mapping.ChannelBitLengthNm * 1e-6,
            mapping.PitchLinear, mapping.PitchQuadratic, mapping.PitchCubic);
        int n = Math.Max(1, (int)Math.Round(2 * radius / CellMm, MidpointRounding.ToEven));
        double size = n * CellMm;
        var (red, green, blue) = DiffractionImages.LinearRgbCellsBinned(imagePath, n, n, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        DiffractionPicture picture = DiffractionColor.Build("colour", (0.0, 0.0), (size, size), red, green, blue,
            cellMm: CellMm, wide: true);
        // The greyscale writer puts StartAngleDegrees at track angle 0, measured counter-clockwise from +x; the
        // layout measures from the top, clockwise. The two agree when thetaRef = StartAngle - 90 degrees.
        double thetaRef = (mapping.StartAngleDegrees - 90.0) * Math.PI / 180.0;
        return new DiffractionLayout([picture], geometry, mapping.ChannelBitLengthNm, thetaRef: thetaRef);
    }
}
