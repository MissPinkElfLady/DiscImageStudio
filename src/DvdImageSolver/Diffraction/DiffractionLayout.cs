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

    /// <summary>Payload byte that directly follows a SYNC code.</summary>
    private static readonly bool[] FrameStart = BuildFrameStart();

    /// <summary>Codes from here on refer to carrier strings.</summary>
    internal const int StringCode = 256;

    [ThreadStatic] private static int[]? t_codes;
    [ThreadStatic] private static bool[]? t_fromString;
    [ThreadStatic] private static double[]? t_phases;

    /// <summary>Extra filler bytes a phase-locked string piece may be delayed by to land on its target phase.</summary>
    internal const int MaxPhaseDelayBytes = 16;

    private readonly byte[][] _stringData;
    private readonly short[][] _stringTrim;
    private readonly double[] _stringPeriod;
    private readonly Func<double, double, int>? _codeAt;
    private readonly Func<double, double, int, double>? _phaseAt;
    private readonly double _outerRadius;
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
        // String carriers of all pictures are numbered here: group g's variant w is code 256 + 256 g + w.
        List<CarrierGroup> groups = [];
        foreach (DiffractionPicture p in Pictures)
        {
            for (int v = 0; v < p.ViewCount; v++)
            {
                if (p.Carriers[v].Group is not CarrierGroup g) continue;
                p.Codes[v] = StringCode + CarrierGroup.Variants * groups.Count;
                groups.Add(g);
            }
        }

        if (StringCode + CarrierGroup.Variants * groups.Count > ushort.MaxValue + 1)
            throw new ArgumentException("Too many string carriers in one layout.");
        if (groups.Count > 0 && OffByte != StringCarrier.Filler)
            throw new ArgumentException($"String carriers need 0x{StringCarrier.Filler:X2} as the background byte.");
        Groups = [.. groups];
        _stringData = new byte[groups.Count * CarrierGroup.Variants][];
        _stringTrim = new short[groups.Count * CarrierGroup.Variants][];
        _stringPeriod = new double[groups.Count * CarrierGroup.Variants];
        FillStringTables(groups, _stringData, _stringTrim, _stringPeriod);

        const double margin = 0.1;
        _sectors = new (double, double, double, double)[Pictures.Length];
        for (int k = 0; k < Pictures.Length; k++)
        {
            var (rMin, rMax, phi, half) = Pictures[k].PolarSpan();
            _sectors[k] = (ThetaRef + phi, Math.Min(Math.PI, half + margin / Math.Max(rMin, 1.0)), rMin - margin, rMax + margin);
        }
    }

    /// <summary>
    /// A layout without pictures: <paramref name="codeAt"/> gives the code of every payload byte from its radius and
    /// unwrapped spiral angle (string carrier g of <paramref name="groups"/> is code 256 + 256 g + variant), between
    /// <paramref name="innerRadiusMm"/> and <paramref name="outerRadiusMm"/>. <paramref name="phaseAt"/>, when given,
    /// phase-locks string pieces: it returns the phase (in cycles) the grating should have at a byte, or NaN for none.
    /// A locked piece is delayed by up to <see cref="MaxPhaseDelayBytes"/> filler bytes so that its string starts
    /// where that phase is nearest a whole number, which lets neighbouring turns carry a grating with a chosen slant.
    /// </summary>
    internal DiffractionLayout(IReadOnlyList<CarrierGroup> groups, Func<double, double, int> codeAt,
        Func<double, double, int, double>? phaseAt, double innerRadiusMm, double outerRadiusMm,
        DvdTrackGeometry geometry, double channelBitLengthNm = 133.3)
    {
        if (groups.Count == 0) throw new ArgumentException("A ring layout needs at least one carrier string.");
        Pictures = [];
        OffByte = StringCarrier.Filler;
        ThetaRef = 0;
        _geometry = geometry;
        _bitMm = channelBitLengthNm * 1e-6;
        _codeAt = codeAt;
        _phaseAt = phaseAt;
        _outerRadius = outerRadiusMm;
        Bridges = [];
        Groups = [.. groups];
        _stringData = new byte[groups.Count * CarrierGroup.Variants][];
        _stringTrim = new short[groups.Count * CarrierGroup.Variants][];
        _stringPeriod = new double[groups.Count * CarrierGroup.Variants];
        FillStringTables(groups, _stringData, _stringTrim, _stringPeriod);
        _sectors = [(0.0, Math.PI, innerRadiusMm - 0.1, outerRadiusMm + 0.1)];
    }

    private static void FillStringTables(IReadOnlyList<CarrierGroup> groups, byte[][] data, short[][] trim, double[] period)
    {
        for (int g = 0; g < groups.Count; g++)
        {
            for (int w = 0; w < groups[g].Strings.Length; w++)
            {
                data[g * CarrierGroup.Variants + w] = groups[g].Strings[w].Data;
                trim[g * CarrierGroup.Variants + w] = groups[g].Strings[w].Trim;
                period[g * CarrierGroup.Variants + w] = groups[g].Strings[w].PeriodBits;
            }
        }
    }

    internal DiffractionPicture[] Pictures { get; }
    internal byte OffByte { get; }
    internal double ThetaRef { get; }
    internal List<(byte From, byte To, byte? Bridge)> Bridges { get; }
    internal CarrierGroup[] Groups { get; }
    internal DvdTrackGeometry Geometry => _geometry;
    internal double SectorMm => BitsPerSector * _bitMm;

    /// <summary>Carrier and off bytes, ascending.</summary>
    internal byte[] BytesUsed()
    {
        SortedSet<byte> used = [OffByte];
        foreach (DiffractionPicture p in Pictures)
        {
            used.Add(p.OffByte);
            foreach (DiffractionCarrier c in p.Carriers)
                if (c.Byte is byte b) used.Add(b);
        }

        return [.. used];
    }

    /// <summary>Code (a byte, or a string code) for a payload byte centred at radius r and unwrapped spiral angle theta.</summary>
    internal int CodeAt(double r, double theta)
    {
        if (_codeAt is not null) return _codeAt(r, theta);
        double phi = theta - ThetaRef;
        double x = r * Math.Sin(phi), y = r * Math.Cos(phi);
        int code = OffByte;
        foreach (DiffractionPicture p in Pictures)
        {
            if (p.TryCell(x, y, out int j, out int i))
                code = p.CodeFor(j, i, p.TurnIndex(theta, ThetaRef));
        }

        return code;
    }

    /// <summary>Outermost radius any picture reaches.</summary>
    internal double OuterRadius() => Pictures.Length == 0 ? _outerRadius : Pictures.Max(p => p.PolarSpan().RMax);

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
        int[] codes = t_codes ??= new int[SectorsPerBlock * PayloadBytes];
        bool[] fromString = t_fromString ??= new bool[SectorsPerBlock * PayloadBytes];
        double[] phases = t_phases ??= new double[SectorsPerBlock * PayloadBytes];
        Array.Fill(codes, OffByte);
        if (_phaseAt is not null) Array.Fill(phases, double.NaN);
        long firstBit = (long)lba0 * BitsPerSector;
        foreach ((int start, int stop) in slices)
        {
            for (int k = start; k < stop; k++)
            {
                (double r, double theta) = _geometry.AtArcLengthFast((firstBit + BlockPayloadCentres[k]) * _bitMm);
                codes[k] = CodeAt(r, theta);
                if (_phaseAt is not null && codes[k] >= StringCode) phases[k] = _phaseAt(r, theta, codes[k]);
            }
        }

        Array.Clear(fromString);
        for (int sector = 0; sector < SectorsPerBlock; sector++)
        {
            ReadOnlySpan<int> c = codes.AsSpan(sector * PayloadBytes, PayloadBytes);
            Span<byte> output = scrambled.Slice(sector * PayloadBytes, PayloadBytes);
            if (c.ContainsAnyInRange(StringCode, int.MaxValue))
            {
                ResolveStrings(c, output, fromString.AsSpan(sector * PayloadBytes, PayloadBytes),
                    _phaseAt is null ? default : phases.AsSpan(sector * PayloadBytes, PayloadBytes));
            }
            else
            {
                for (int k = 0; k < PayloadBytes; k++) output[k] = (byte)c[k];
            }
        }

        ApplyBridges(scrambled, fromString);
        return true;
    }

    /// <summary>
    /// Writes out the carrier strings of one sector (DiscDiffractStudio's <c>DiscLayout._resolve_sectors</c>).
    /// A "piece" is a maximal stretch of one sync frame's payload with the same carrier. A string piece gets the
    /// first bytes of its string, so that its marks are exactly those designed:
    /// <list type="bullet">
    /// <item>it begins at the start of a sync frame, or behind two filler bytes (one is enough when the filler itself
    /// began at the start of the frame); otherwise its first one or two bytes become filler. The same holds where a
    /// string meets another carrier without filler in between;</item>
    /// <item>the variant (period) is taken where the piece begins and kept to its end;</item>
    /// <item>a piece followed by filler inside the frame is shortened where its length is a bad length.</item>
    /// </list>
    /// </summary>
    internal void ResolveStrings(ReadOnlySpan<int> c, Span<byte> output, Span<bool> lit,
        ReadOnlySpan<double> phases = default)
    {
        const int n = PayloadBytes;
        Span<int> group = stackalloc int[n];
        Span<int> start = stackalloc int[n];
        Span<int> end = stackalloc int[n];
        Span<int> need = stackalloc int[n];
        for (int k = 0; k < n; k++)
            group[k] = c[k] >= StringCode ? StringCode + (c[k] - StringCode) / CarrierGroup.Variants * CarrierGroup.Variants : c[k];
        for (int k = 0; k < n; k++)
        {
            bool brk = k == 0 || FrameStart[k] || group[k] != group[k - 1];
            start[k] = brk ? k : start[k - 1];
        }

        int next = n;
        for (int k = n - 1; k >= 0; k--)
        {
            end[k] = next;                                   // first break after k
            bool brk = k == 0 || FrameStart[k] || group[k] != group[k - 1];
            if (brk) next = k;
        }

        for (int k = 0; k < n; k++)
        {
            bool isString = c[k] >= StringCode;
            bool fill = group[k] == OffByte;
            bool frameStart = FrameStart[k], sectorStart = k == 0;
            int previous = Math.Max(k - 1, 0);
            bool prevString = k > 0 && c[previous] >= StringCode;
            bool prevFill = k > 0 && group[previous] == OffByte && !frameStart && !sectorStart;
            int fillLength = k - start[previous];
            int behindFiller = FrameStart[start[previous]] ? 0 : Math.Clamp(2 - fillLength, 0, 2);
            bool touching = k > 0 && (isString || prevString);
            int value = frameStart ? 0 : sectorStart ? 2 : prevFill ? behindFiller : touching ? 2 : 0;
            bool applies = isString || (!fill && touching && !prevFill && !frameStart && !sectorStart);
            need[k] = applies ? value : 0;
        }

        if (!phases.IsEmpty)
        {
            // Phase lock: delay each locked piece by whole filler bytes until its string starts where the target
            // phase, taken at the start of the byte, is closest to a whole number of cycles.
            for (int k = 0; k < n; k++)
            {
                if (start[k] != k || c[k] < StringCode) continue;
                int first = k + need[k];
                if (first >= end[k] || double.IsNaN(phases[first])) continue;
                double period = _stringPeriod[c[first] - StringCode];
                int best = 0;
                double bestError = double.PositiveInfinity;
                for (int e = 0; e <= MaxPhaseDelayBytes && first + e < end[k] - 8; e++)
                {
                    double phase = phases[first + e] - 8.0 / period;
                    double error = Math.Abs(phase - Math.Round(phase));
                    if (error < bestError - 1e-9) (best, bestError) = (e, error);
                }

                need[k] += best;
            }
        }

        for (int k = 0; k < n; k++)
        {
            bool isString = c[k] >= StringCode;
            int forced = need[start[k]];
            int offset = k - start[k] - forced;
            int first = Math.Min(start[k] + forced, n - 1);
            int length = end[k] - start[k] - forced;
            int sid = isString && length > 0 ? c[first] - StringCode : 0;
            bool followed = end[k] < n && !FrameStart[end[k]];
            int trim = followed && isString && length > 0 ? _stringTrim[sid][Math.Clamp(length, 0, StringCarrier.RunBytes)] : 0;
            bool on = isString && offset >= 0 && offset < length - trim;
            lit[k] = on;
            output[k] = on ? _stringData[sid][offset]
                : isString ? StringCarrier.Filler
                : offset < 0 ? OffByte : (byte)c[k];
        }
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
    internal void ApplyBridges(Span<byte> scrambled, ReadOnlySpan<bool> fromString = default)
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
                bool ok = PrevIsPayload[k % PayloadBytes];
                if (ok && !fromString.IsEmpty)
                {
                    int before = k % PayloadBytes == 0 ? k + PayloadBytes - 1 : k - 1;
                    ok = !fromString[k] && !fromString[before];
                }

                if (ok && scrambled[k] == b && prev[k] == a)
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

    private static bool[] BuildFrameStart()
    {
        bool[] start = new bool[PayloadBytes];
        for (int p = 0; p < PayloadBytes; p++)
        {
            int col = (12 + p) % DataColumns;
            start[p] = col == 0 || col == 91;
        }

        return start;
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
