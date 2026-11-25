using DiscImageStudio.Core;
using DvdImageSolver.Encoding;

namespace DvdImageSolver.Diffraction;

/// <summary>
/// Pictures on a background of the off byte, mapped onto the calibrated spiral. Ported from
/// DiscDiffractStudio's <c>multiview.DiscLayout</c> and <c>BlockMapper</c> (single-byte carriers only;
/// GPL-3.0-or-later).
///
/// A byte at radius r and unwrapped spiral angle theta (0 at the inner radius, as <see cref="DvdTrackGeometry"/>
/// counts it) sits at X = r sin(theta - thetaRef), Y = r cos(theta - thetaRef) on the canvas: data runs
/// clockwise as the data side is seen, and thetaRef is the spiral angle that points to the top.
/// </summary>
internal sealed class DiffractionLayout
{
    internal const int PayloadBytes = DvdEccBlockEncoder.PayloadBytesPerSector;
    internal const int SectorsPerBlock = DvdEccBlockEncoder.SectorCount;
    internal const int BitsPerSector = DvdEccBlockEncoder.ChannelBitsPerSector;
    private const int BitsPerSyncFrame = 1488;
    private const int DataColumns = 172;

    /// <summary>Channel-bit centre of every payload byte of an ECC block, relative to the block start (increasing).</summary>
    internal static readonly int[] BlockPayloadCentres = BuildBlockPayloadCentres();

    /// <summary>For payload byte p of a sector: is the code word just before it payload byte p - 1?</summary>
    private static readonly bool[] PrevIsPayload = BuildPrevIsPayload();

    private readonly DvdTrackGeometry _geometry;
    private readonly double _bitMm;
    private readonly (double Centre, double Half, double RMin, double RMax)[] _sectors;

    internal DiffractionLayout(IReadOnlyList<DiffractionPicture> pictures, DvdTrackGeometry geometry,
        double channelBitLengthNm = 133.3, byte offByte = DiffractionCarriers.OffByte, double thetaRef = 0.0,
        IReadOnlyList<(byte From, byte To, byte? Bridge)>? bridges = null)
    {
        if (pictures.Count == 0) throw new ArgumentException("A layout needs at least one picture.");
        Pictures = [.. pictures];
        OffByte = offByte;
        ThetaRef = thetaRef;
        _geometry = geometry;
        _bitMm = channelBitLengthNm * 1e-6;
        Bridges = bridges is null ? DiffractionCarriers.FindBridges(BytesUsed()) : [.. bridges];
        const double margin = 0.1;
        _sectors = new (double, double, double, double)[Pictures.Length];
        for (int k = 0; k < Pictures.Length; k++)
        {
            var (rMin, rMax, phi, half) = Pictures[k].PolarSpan();
            _sectors[k] = (ThetaRef + phi, Math.Min(Math.PI, half + margin / Math.Max(rMin, 1.0)), rMin - margin, rMax + margin);
        }
    }

    internal DiffractionPicture[] Pictures { get; }
    internal byte OffByte { get; }
    internal double ThetaRef { get; }
    internal List<(byte From, byte To, byte? Bridge)> Bridges { get; }
    internal DvdTrackGeometry Geometry => _geometry;
    internal double SectorMm => BitsPerSector * _bitMm;

    /// <summary>Carrier and off bytes, ascending.</summary>
    internal byte[] BytesUsed()
    {
        SortedSet<byte> used = [OffByte];
        foreach (DiffractionPicture p in Pictures)
        {
            used.Add(p.OffByte);
            foreach (byte c in p.Carriers) used.Add(c);
        }

        return [.. used];
    }

    /// <summary>Scrambled-domain byte for a payload byte centred at radius r and unwrapped spiral angle theta.</summary>
    internal byte CodeAt(double r, double theta)
    {
        double phi = theta - ThetaRef;
        double x = r * Math.Sin(phi), y = r * Math.Cos(phi);
        byte code = OffByte;
        foreach (DiffractionPicture p in Pictures)
        {
            if (p.TryCell(x, y, out int j, out int i))
                code = p.CodeFor(j, i, p.TurnIndex(theta, ThetaRef));
        }

        return code;
    }

    /// <summary>Outermost radius any picture reaches.</summary>
    internal double OuterRadius() => Pictures.Max(p => p.PolarSpan().RMax);

    /// <summary>First LBA after the recorded area: pictures plus a margin of background, on an ECC-block boundary.</summary>
    internal uint LastSector(uint totalSectors, double marginMm = 0.3)
    {
        double radius = Math.Min(OuterRadius() + marginMm, _geometry.OuterRadiusMm);
        long lba = (long)Math.Ceiling(_geometry.ArcLengthAtRadius(radius) / SectorMm);
        return (uint)Math.Min((lba + 15) / 16 * 16, totalSectors / 16 * 16);
    }

    /// <summary>
    /// Fills the scrambled-domain bytes (16 x 2048, recording order) of the ECC block starting at
    /// <paramref name="lba0"/>. Returns false when the whole block is background.
    /// </summary>
    internal bool FillBlock(uint lba0, Span<byte> scrambled)
    {
        if (scrambled.Length != SectorsPerBlock * PayloadBytes)
            throw new ArgumentException("An ECC block holds 16 x 2048 payload bytes.", nameof(scrambled));
        scrambled.Fill(OffByte);
        List<(int Start, int Stop)> slices = Slices(lba0);
        if (slices.Count == 0) return false;
        long firstBit = (long)lba0 * BitsPerSector;
        foreach ((int start, int stop) in slices)
        {
            for (int k = start; k < stop; k++)
            {
                (double r, double theta) = _geometry.AtArcLengthFast((firstBit + BlockPayloadCentres[k]) * _bitMm);
                scrambled[k] = CodeAt(r, theta);
            }
        }

        ApplyBridges(scrambled);
        return true;
    }

    /// <summary>The stretches of an ECC block's payload that can lie inside a picture, in track order.</summary>
    internal List<(int Start, int Stop)> Slices(uint lba0)
    {
        double s0 = (double)lba0 * BitsPerSector * _bitMm;
        double blockMm = SectorsPerBlock * BitsPerSector * _bitMm;
        (double r0, double t0) = _geometry.AtArcLengthFast(s0);
        (double r1, double t1) = _geometry.AtArcLengthFast(Math.Min(s0 + blockMm, _geometry.TrackLengthMm));
        List<(int, int)> found = [];
        foreach (var (centre, half, rMin, rMax) in _sectors)
        {
            if (r1 < rMin || r0 > rMax) continue;
            double lo = centre - half + 2 * Math.PI * Math.Ceiling((t0 - centre - half) / (2 * Math.PI));
            while (lo <= t1)
            {
                double sa = ArcAtAngle(lo), sb = ArcAtAngle(lo + 2 * half);
                int ia = LowerBound(BlockPayloadCentres, (sa - s0) / _bitMm - 64);
                int ib = LowerBound(BlockPayloadCentres, (sb - s0) / _bitMm + 64);
                if (ib > ia) found.Add((ia, ib));
                lo += 2 * Math.PI;
            }
        }

        found.Sort();
        List<(int, int)> merged = [];
        foreach ((int ia, int ib) in found)
        {
            if (merged.Count > 0 && ia <= merged[^1].Item2)
                merged[^1] = (merged[^1].Item1, Math.Max(merged[^1].Item2, ib));
            else
                merged.Add((ia, ib));
        }

        return merged;
    }

    /// <summary>
    /// Writes the bridges: where a run of a carrier begins right after a run of another inside a sync
    /// frame and the switch would leave the recorder a choice, its first byte becomes the bridge byte.
    /// </summary>
    internal void ApplyBridges(Span<byte> scrambled)
    {
        if (Bridges.Count == 0) return;
        byte[] prev = new byte[scrambled.Length];
        for (int k = 0; k < scrambled.Length; k++)
            prev[k] = scrambled[(k % PayloadBytes == 0) ? k + PayloadBytes - 1 : k - 1];
        foreach ((byte a, byte b, byte? c) in Bridges)
        {
            if (c is not byte bridge) continue;
            for (int k = 0; k < scrambled.Length; k++)
            {
                if (scrambled[k] == b && prev[k] == a && PrevIsPayload[k % PayloadBytes])
                    scrambled[k] = bridge;
            }
        }
    }

    private double ArcAtAngle(double theta)
        => _geometry.ArcLengthAtAngle(Math.Clamp(theta, 0.0, _geometry.TotalAngleRadians));

    private static int LowerBound(int[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (sorted[mid] < value) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    private static int[] BuildBlockPayloadCentres()
    {
        int[] centres = new int[SectorsPerBlock * PayloadBytes];
        for (int sector = 0; sector < SectorsPerBlock; sector++)
        {
            for (int p = 0; p < PayloadBytes; p++)
            {
                int fb = 12 + p, row = fb / DataColumns, col = fb % DataColumns;
                centres[sector * PayloadBytes + p] = sector * BitsPerSector
                    + (row * 2 + col / 91) * BitsPerSyncFrame + 32 + (col % 91) * 16 + 8;
            }
        }

        return centres;
    }

    private static bool[] BuildPrevIsPayload()
    {
        bool[] ok = new bool[PayloadBytes];
        for (int p = 0; p < PayloadBytes; p++)
        {
            int col = (12 + p) % DataColumns;
            ok[p] = col != 0 && col != 91;
        }

        ok[0] = false;
        return ok;
    }
}
