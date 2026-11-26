namespace DvdImageSolver.Diffraction;

/// <summary>
/// One rectangle of the disc canvas carrying one or more views, each on its own single-byte carrier.
/// Ported from DiscDiffractStudio's <c>multiview.Picture</c> in "tracks" mode (GPL-3.0-or-later).
///
/// Canvas: millimetres with the hub at the origin, X to the right and Y up, as the data side is seen.
/// Turn n of the spiral belongs to view cycle[n mod cycle length]; turn counts let views get unequal
/// shares. Grey comes from how many of a view's turns are lit in a cell: the view's k-th turn is lit
/// where level + cell dither exceeds TurnThreshold[k mod 32] (bit-reversed order, so the lit turns of a
/// dim cell are spread evenly; a 4 x 4 ordered dither fills the steps in between).
/// </summary>
internal sealed class DiffractionPicture
{
    internal const int TurnLevels = 32;

    internal static readonly double[] TurnThreshold = BuildTurnThresholds();
    internal static readonly double[,] CellDither = BuildCellDither();

    private readonly int[] _cycle;
    private readonly int[] _rank;
    private readonly int[] _count;

    /// <param name="name">Shown in messages.</param>
    /// <param name="centerMm">Centre on the canvas (X right, Y up).</param>
    /// <param name="sizeMm">Width and height before rounding to whole cells.</param>
    /// <param name="views">Grey images [row, column], row 0 at the top, values 0..1.</param>
    /// <param name="carriers">One carrier per view: a scrambled-domain byte or a group of carrier strings.</param>
    /// <param name="linear">True when the views are already linear light; otherwise sRGB-encoded.</param>
    /// <param name="gains">Lit share scale per view (to balance brightness); null for 1.</param>
    /// <param name="turnCounts">Turns of one cycle per view; null for one each.</param>
    /// <param name="quarterTurns">Counter-clockwise quarter turns applied to the views on the canvas.</param>
    internal DiffractionPicture(string name, (double X, double Y) centerMm, (double W, double H) sizeMm,
        IReadOnlyList<double[,]> views, IReadOnlyList<DiffractionCarrier> carriers, bool linear = true,
        IReadOnlyList<double>? gains = null, IReadOnlyList<int>? turnCounts = null, int quarterTurns = 0,
        double cellMm = 0.05, byte offByte = DiffractionCarriers.OffByte)
    {
        int n = views.Count;
        if (n < 1 || carriers.Count != n)
            throw new ArgumentException($"Picture {name}: at least one view, one carrier each.");
        if (!(cellMm > 0) || !double.IsFinite(cellMm))
            throw new ArgumentOutOfRangeException(nameof(cellMm));
        Name = name;
        CellMm = cellMm;
        OffByte = offByte;
        Carriers = [.. carriers];
        if (Carriers.Any(c => c.Group is not null) && offByte != StringCarrier.Filler)
            throw new ArgumentException($"Picture {name}: string carriers need 0x{StringCarrier.Filler:X2} as the off byte.");
        foreach (DiffractionCarrier c in Carriers)
        {
            if (c.Group?.Variant is byte[,] map && (map.GetLength(0) != (int)Math.Round(sizeMm.H / cellMm, MidpointRounding.ToEven)
                || map.GetLength(1) != (int)Math.Round(sizeMm.W / cellMm, MidpointRounding.ToEven)))
                throw new ArgumentException($"Picture {name}: a variant map needs one entry per cell.");
        }

        // A string carrier's code is handed out by the layout it is put in.
        Codes = [.. Carriers.Select(c => c.Byte is byte b ? (int)b : -1)];
        Nx = (int)Math.Round(sizeMm.W / cellMm, MidpointRounding.ToEven);
        Ny = (int)Math.Round(sizeMm.H / cellMm, MidpointRounding.ToEven);
        if (Nx < 1 || Ny < 1) throw new ArgumentException($"Picture {name}: smaller than one cell.");
        CenterMm = centerMm;
        SizeMm = (Nx * cellMm, Ny * cellMm);
        Gains = gains is null ? [.. Enumerable.Repeat(1.0, n)] : [.. gains];
        TurnCounts = turnCounts is null ? [.. Enumerable.Repeat(1, n)] : [.. turnCounts];
        if (Gains.Length != n || TurnCounts.Length != n || TurnCounts.Min() < 1)
            throw new ArgumentException($"Picture {name}: one gain and one positive turn count per view.");
        (_cycle, _rank) = SpreadCycle(TurnCounts);
        _count = TurnCounts;

        Levels = new float[n][,];
        for (int v = 0; v < n; v++)
        {
            double[,] image = Rotate(views[v], ((quarterTurns % 4) + 4) % 4);
            if (image.GetLength(0) != Ny || image.GetLength(1) != Nx)
                image = DiffractionImages.Resample(image, Ny, Nx);
            float[,] level = new float[Ny, Nx];
            for (int j = 0; j < Ny; j++)
            {
                for (int i = 0; i < Nx; i++)
                {
                    double t = linear ? Math.Clamp(image[j, i], 0.0, 1.0) : DiffractionImages.SrgbToLinear(image[j, i]);
                    level[j, i] = (float)Math.Clamp(t * Gains[v], 0.0, 1.0);
                }
            }

            Levels[v] = level;
        }
    }

    internal string Name { get; }
    internal (double X, double Y) CenterMm { get; }
    internal (double W, double H) SizeMm { get; }
    internal double CellMm { get; }
    internal int Nx { get; }
    internal int Ny { get; }
    internal byte OffByte { get; }
    internal DiffractionCarrier[] Carriers { get; }

    /// <summary>Code written where view v is lit: the byte, or for a string carrier 256 + 256 g (set by the layout).</summary>
    internal int[] Codes { get; }
    internal double[] Gains { get; }
    internal int[] TurnCounts { get; }
    internal int ViewCount => Carriers.Length;
    internal int CycleTurns => _cycle.Length;

    /// <summary>Lit share of each view's own turns per cell (linear light, 0..1).</summary>
    internal float[][,] Levels { get; }

    internal (double X0, double X1, double Y0, double Y1) Bounds
        => (CenterMm.X - SizeMm.W / 2, CenterMm.X + SizeMm.W / 2, CenterMm.Y - SizeMm.H / 2, CenterMm.Y + SizeMm.H / 2);

    /// <summary>Clockwise angle of the picture centre from the top of the canvas, rad.</summary>
    internal double Azimuth => Math.Atan2(CenterMm.X, CenterMm.Y);

    /// <summary>(r_min, r_max, centre azimuth, azimuth half-width).</summary>
    internal (double RMin, double RMax, double Azimuth, double HalfWidth) PolarSpan()
    {
        var (x0, x1, y0, y1) = Bounds;
        if (x0 < 0 && 0 < x1 && y0 < 0 && 0 < y1)
            return (0.0, Math.Sqrt(Square(Math.Max(-x0, x1)) + Square(Math.Max(-y0, y1))), Azimuth, Math.PI);
        double rMin = double.PositiveInfinity, rMax = 0, half = 0, az = Azimuth;
        for (int edge = 0; edge < 4; edge++)
        {
            for (int k = 0; k < 65; k++)
            {
                double fx = x0 + (x1 - x0) * k / 64.0, fy = y0 + (y1 - y0) * k / 64.0;
                (double ex, double ey) = edge switch
                {
                    0 => (fx, y0),
                    1 => (fx, y1),
                    2 => (x0, fy),
                    _ => (x1, fy),
                };
                double r = Math.Sqrt(ex * ex + ey * ey);
                rMin = Math.Min(rMin, r);
                rMax = Math.Max(rMax, r);
                double d = PositiveModulo(Math.Atan2(ex, ey) - az + Math.PI, 2 * Math.PI) - Math.PI;
                half = Math.Max(half, Math.Abs(d));
            }
        }

        return (rMin, rMax, az, half);
    }

    /// <summary>Cell (j, i) of a canvas point, and whether the point lies inside the picture.</summary>
    internal bool TryCell(double x, double y, out int j, out int i)
    {
        var (x0, x1, y0, y1) = Bounds;
        bool inside = x >= x0 && x < x1 && y > y0 && y <= y1;
        i = Math.Clamp((int)Math.Floor((x - x0) / CellMm), 0, Nx - 1);
        j = Math.Clamp((int)Math.Floor((y1 - y) / CellMm), 0, Ny - 1);
        return inside;
    }

    /// <summary>Spiral turn number, counted so that it changes on the far side of the disc from the picture.</summary>
    internal long TurnIndex(double theta, double thetaRef)
        => (long)Math.Floor((theta - thetaRef - Azimuth) / (2 * Math.PI) + 0.5);

    /// <summary>
    /// The code for a payload byte lying in cell (j, i) on spiral turn <paramref name="turn"/>: a byte (0..255),
    /// or 256 + 256 g + w for variant w of string group g.
    /// </summary>
    internal int CodeFor(int j, int i, long turn)
    {
        long cycle = _cycle.Length;
        int m = (int)(((turn % cycle) + cycle) % cycle);
        int v = _cycle[m];
        long k = (long)Math.Floor((double)turn / cycle) * _count[v] + _rank[m];
        int kk = (int)(((k % TurnLevels) + TurnLevels) % TurnLevels);
        bool lit = (double)Levels[v][j, i] + CellDither[j % 4, i % 4] > TurnThreshold[kk];
        if (!lit) return OffByte;
        int code = Codes[v];
        if (code < 0) throw new InvalidOperationException($"Picture {Name} has string carriers: put it in a layout first.");
        return Carriers[v].Group?.Variant is byte[,] map ? code + map[j, i] : code;
    }

    /// <summary>The view that turn <paramref name="turn"/> belongs to (see <see cref="CodeFor"/>).</summary>
    internal int ViewOfTurn(long turn)
    {
        long cycle = _cycle.Length;
        return _cycle[(int)(((turn % cycle) + cycle) % cycle)];
    }

    /// <summary>Mean lit share of the view (1 = every cell fully lit for that view).</summary>
    internal double LitShare(int view)
    {
        double sum = 0;
        float[,] level = Levels[view];
        for (int j = 0; j < Ny; j++)
            for (int i = 0; i < Nx; i++)
                sum += Math.Clamp(Math.Floor((level[j, i] + CellDither[j % 4, i % 4]) * TurnLevels + 0.5), 0, TurnLevels) / TurnLevels;
        return sum / (Nx * Ny);
    }

    /// <summary>
    /// Hands out sum(counts) consecutive turns to the views, view v getting counts[v] of them, evenly spread.
    /// cycle[m] is the view of turn m of the cycle, rank[m] counts that view's turns within the cycle.
    /// </summary>
    internal static (int[] Cycle, int[] Rank) SpreadCycle(IReadOnlyList<int> counts)
    {
        List<(double Key, int View, int Rank)> slots = [];
        for (int v = 0; v < counts.Count; v++)
            for (int r = 0; r < counts[v]; r++)
                slots.Add(((r + 0.5) / counts[v], v, r));
        slots.Sort();
        return ([.. slots.Select(s => s.View)], [.. slots.Select(s => s.Rank)]);
    }

    /// <summary>np.rot90(image, k): k counter-clockwise quarter turns.</summary>
    internal static T[,] Rotate<T>(T[,] image, int k)
    {
        k = ((k % 4) + 4) % 4;
        if (k == 0) return image;
        int h = image.GetLength(0), w = image.GetLength(1);
        T[,] output = k == 2 ? new T[h, w] : new T[w, h];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                switch (k)
                {
                    case 1: output[w - 1 - c, r] = image[r, c]; break;
                    case 2: output[h - 1 - r, w - 1 - c] = image[r, c]; break;
                    default: output[c, h - 1 - r] = image[r, c]; break;
                }
            }
        }

        return output;
    }

    private static double[] BuildTurnThresholds()
    {
        double[] t = new double[TurnLevels];
        for (int k = 0; k < TurnLevels; k++)
        {
            int reversed = 0;
            for (int b = 0; b < 5; b++) reversed |= ((k >> b) & 1) << (4 - b);
            t[k] = (reversed + 0.5) / TurnLevels;
        }

        return t;
    }

    private static double[,] BuildCellDither()
    {
        int[,] bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
        double[,] d = new double[4, 4];
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                d[j, i] = ((bayer[j, i] + 0.5) / 16 - 0.5) / TurnLevels;
        return d;
    }

    private static double Square(double x) => x * x;

    internal static double PositiveModulo(double a, double m)
    {
        double r = a % m;
        return r < 0 ? r + m : r;
    }
}
