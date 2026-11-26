using DiscImageStudio.Core;
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
