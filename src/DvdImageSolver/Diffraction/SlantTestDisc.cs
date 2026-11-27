using DiscImageStudio.Core;

namespace DvdImageSolver.Diffraction;

/// <summary>
/// Test disc for slanted gratings: concentric bands of one carrier string, each with the string phase-locked so that
/// the grating shifts by a fixed fraction of a cycle from one turn to the next ("slip"). A slip tilts the grating
/// lines away from the radial direction, so the grating sends light partly across the track instead of only along
/// it, and the bright spots of a band move round the disc by the tilt angle. Control bands are unlocked (the
/// carrier as normally written). Two bands lock only blocks of a few turns with gaps between them, to see whether
/// short blocks are enough (as frames or colours sharing turns would need).
/// </summary>
internal static class SlantTestDisc
{
    /// <summary>One band: radii, slip in cycles per turn (null: unlocked), and locked turns per block / gap turns (0: solid).</summary>
    internal sealed record Band(string Label, double InnerMm, double OuterMm, double? Slip, int BlockTurns = 0, int GapTurns = 0);

    /// <summary>Angular width of the patches whose grating lines are straight, in degrees.</summary>
    internal const double PatchDegrees = 10.0;

    /// <summary>The green colour carrier (16T / 2^(1/5) = 13.93T): its period is not a whole number of bytes, so a delay of whole bytes moves its phase.</summary>
    internal static double PeriodBits => 16.0 / DiffractionColor.ColorQ;

    /// <summary>The default bands from the inside out, between 25 and 57 mm.</summary>
    internal static Band[] DefaultBands()
    {
        (string Label, double? Slip, int Block, int Gap)[] plan =
        [
            ("A control (unlocked)", null, 0, 0),
            ("B slip 0", 0.0, 0, 0),
            ("C slip +1/8", 0.125, 0, 0),
            ("D slip +1/4", 0.25, 0, 0),
            ("E slip +3/8", 0.375, 0, 0),
            ("F slip 1/2", 0.5, 0, 0),
            ("G slip -3/8", -0.375, 0, 0),
            ("H slip -1/4", -0.25, 0, 0),
            ("I slip -1/8", -0.125, 0, 0),
            ("J slip +1/4, blocks of 8 turns", 0.25, 8, 16),
            ("K slip +1/4, blocks of 4 turns", 0.25, 4, 8),
            ("L control (unlocked)", null, 0, 0),
        ];
        const double inner = 25.0, outer = 57.0, gap = 0.5;
        double width = (outer - inner) / plan.Length;
        return [.. plan.Select((p, k) => new Band(p.Label, inner + k * width, inner + (k + 1) * width - gap, p.Slip, p.Block, p.Gap))];
    }

    internal static DiffractionLayout BuildLayout(IReadOnlyList<Band> bands, DvdTrackGeometry geometry, double channelBitLengthNm)
    {
        double periodBits = PeriodBits;
        CarrierGroup group = new([StringCarrierTable.Get(periodBits)]);
        int code = DiffractionLayout.StringCode;
        double periodMm = periodBits * channelBitLengthNm * 1e-6;
        double patch = PatchDegrees * Math.PI / 180.0;
        Band? Find(double r)
        {
            foreach (Band b in bands)
                if (r >= b.InnerMm && r < b.OuterMm) return b;
            return null;
        }

        int CodeAt(double r, double theta)
        {
            if (Find(r) is not Band b) return StringCarrier.Filler;
            if (b.BlockTurns <= 0) return code;
            long turn = (long)Math.Floor(theta / (2 * Math.PI));
            return turn % (b.BlockTurns + b.GapTurns) < b.BlockTurns ? code : StringCarrier.Filler;
        }

        double PhaseAt(double r, double theta, int _)
        {
            if (Find(r) is not Band { Slip: double slip }) return double.NaN;
            // Straight grating lines within each patch: along-track distance from the patch centre on this turn,
            // plus the slip for every turn (unwrapped angle / 2 pi counts turns, whatever the pitch).
            double centre = (Math.Floor(theta / patch) + 0.5) * patch;
            return r * (theta - centre) / periodMm + slip * theta / (2 * Math.PI);
        }

        double rMin = bands.Min(b => b.InnerMm), rMax = bands.Max(b => b.OuterMm);
        return new DiffractionLayout([group], CodeAt, PhaseAt, rMin, rMax, geometry, channelBitLengthNm);
    }

    /// <summary>
    /// Tilt of the grating lines from the radial direction for a slip, in degrees, with the track pitch in mm:
    /// atan(slip x period / pitch).
    /// </summary>
    internal static double TiltDegrees(double slip, double periodMm, double pitchMm)
        => Math.Atan(slip * periodMm / pitchMm) * 180.0 / Math.PI;
}
