namespace DvdImageSolver.Diffraction;

/// <summary>
/// The parts of DiscDiffractStudio's viewing model (<c>dvdgrating/viewing.py</c>, GPL-3.0-or-later) that decide
/// what is written: the along-track direction number u = (l + e) . t, the design pose of viewer and lamp,
/// and the colour of each carrier (for white balance).
///
/// A stretch of track with period P sends wavelength lambda from the lamp to the eye where u = lambda / P;
/// l and e are unit vectors from the spot to the lamp and the eye, t the track direction.
/// </summary>
internal static class DiffractionViewing
{
    /// <summary>Channel bit in micrometres, as used when the carriers were designed.</summary>
    internal const double TUm = 0.1333;

    /// <summary>User bytes per sector over code-word slots per sector (26 x 93).</summary>
    internal const double PayloadShare = 2048.0 / 2418.0;

    internal readonly record struct Vec3(double X, double Y, double Z);

    /// <summary>u at a canvas point for an eye and a point lamp given as (x, y, z) mm, z above the disc.</summary>
    internal static double TangentialU(double x, double y, Vec3 eye, Vec3 lamp)
    {
        double r = Math.Sqrt(x * x + y * y);
        double gx = 0, gy = 0;
        foreach (Vec3 q in (ReadOnlySpan<Vec3>)[eye, lamp])
        {
            double dx = q.X - x, dy = q.Y - y, dz = q.Z;
            double n = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            gx += dx / n;
            gy += dy / n;
        }

        return (gx * y - gy * x) / r;
    }

    /// <summary>
    /// The single eye midway between the two eyes, and the lamp, of a viewer squarely in front of the hub, placed so
    /// that the mean |u| of the two eyes at <paramref name="target"/> is <paramref name="uCyclopean"/>. Lamp "top":
    /// straight above the line of sight (pictures seen by tilting); "right": to the right (seen by turning).
    /// </summary>
    internal static (Vec3 Eye, Vec3 Lamp) Cyclopean((double X, double Y) target, double uCyclopean, bool lampTop,
        double eyeDistance = 500.0, double eyeSep = 65.0, double lampDistance = 1000.0)
    {
        Vec3 left = new(-eyeSep / 2, 0, eyeDistance), right = new(eyeSep / 2, 0, eyeDistance);
        Vec3 LampAt(double a) => lampTop
            ? new Vec3(0.0, lampDistance * Math.Sin(a), lampDistance * Math.Cos(a))
            : new Vec3(lampDistance * Math.Sin(a), 0.0, lampDistance * Math.Cos(a));
        double Miss(double a)
        {
            Vec3 lamp = LampAt(a);
            double u = 0.5 * (TangentialU(target.X, target.Y, left, lamp) + TangentialU(target.X, target.Y, right, lamp));
            return Math.Abs(u) - uCyclopean;
        }

        double lo = 0.0, hi = 85.0 * Math.PI / 180.0;
        for (int k = 0; k < 60; k++)
        {
            double mid = 0.5 * (lo + hi);
            if (Miss(mid) < 0) lo = mid;
            else hi = mid;
        }

        return (new Vec3(0.5 * (left.X + right.X), 0.5 * (left.Y + right.Y), 0.5 * (left.Z + right.Z)), LampAt(0.5 * (lo + hi)));
    }

    // ------------------------------------------------------------------ colour

    private static readonly double[] Lambda = [.. Enumerable.Range(0, 201).Select(k => 380.0 + 2 * k)];
    private static readonly double[] D65 = [0.9505, 1.0, 1.089];
    private const int UCount = 2801;
    private const double UMax = 1.4;
    private static readonly double[] UGrid = BuildUGrid();
    private static readonly double[][] Weight = BuildWeight();

    /// <summary>CIE 1931 2-degree colour matching functions, multi-lobe fit of Wyman, Sloan and Shirley (2013).</summary>
    internal static (double X, double Y, double Z) Cmf(double lam)
    {
        static double Lobe(double lam, double mu, double s1, double s2)
        {
            double t = (lam - mu) / (lam < mu ? s1 : s2);
            return Math.Exp(-0.5 * t * t);
        }

        double x = 1.056 * Lobe(lam, 599.8, 37.9, 31.0) + 0.362 * Lobe(lam, 442.0, 16.0, 26.7) - 0.065 * Lobe(lam, 501.1, 20.4, 26.2);
        double y = 0.821 * Lobe(lam, 568.8, 46.9, 40.5) + 0.286 * Lobe(lam, 530.9, 16.3, 31.1);
        double z = 1.217 * Lobe(lam, 437.0, 11.8, 36.0) + 0.681 * Lobe(lam, 459.0, 26.0, 13.8);
        return (x, y, z);
    }

    /// <summary>White LED: blue pump plus phosphor (relative spectral power).</summary>
    internal static double LedSpectrum(double lam)
    {
        double b = (lam - 452.0) / 9.5;
        double blue = Math.Exp(-0.5 * b * b);
        double p = (lam - 560.0) / (lam < 560.0 ? 42.0 : 68.0);
        return 0.75 * blue + Math.Exp(-0.5 * p * p);
    }

    /// <summary>
    /// XYZ of a carrier's own lines at u, under a white LED, averaged over u +- 0.01 (the angular acceptance of
    /// lamp and pupil). Equal to DiscDiffractStudio's <c>Response.signal_xyz</c>.
    /// </summary>
    internal static double[] SignalXyz(IReadOnlyList<(double PeriodBits, double Amplitude)> lines, double u)
    {
        double[][] table = SignalTable(lines);
        double a = Math.Abs(u);
        return [Interp(a, UGrid, table[0]), Interp(a, UGrid, table[1]), Interp(a, UGrid, table[2])];
    }

    private static double[][] SignalTable(IReadOnlyList<(double PeriodBits, double Amplitude)> lines)
    {
        double[][] xyz = [new double[UCount], new double[UCount], new double[UCount]];
        foreach ((double periodBits, double amp) in lines)
        {
            double p = periodBits * TUm;
            for (int i = 0; i < UCount; i++)
            {
                double lam = p * UGrid[i] * 1e3;
                for (int c = 0; c < 3; c++)
                    xyz[c][i] += amp * amp * p * Interp(lam, Lambda, Weight[c], 0.0, 0.0);
            }
        }

        const int k = 20;                                    // round(0.01 / du)
        double[][] smooth = new double[3][];
        for (int c = 0; c < 3; c++)
        {
            smooth[c] = new double[UCount];
            for (int i = 0; i < UCount; i++)
            {
                double sum = 0;
                for (int m = -k; m <= k; m++) sum += PayloadShare * xyz[c][Math.Clamp(i + m, 0, UCount - 1)];
                smooth[c][i] = sum / (2 * k + 1);
            }
        }

        return smooth;
    }

    /// <summary>
    /// Share of the surface each string carrier needs so that equal R = G = B is D65 white.
    /// eyes: the indices of the three carriers an eye sees as red, green and blue, and that eye's u.
    /// </summary>
    internal static double[] WhiteBalance(IReadOnlyList<StringCarrier> nominal,
        IReadOnlyList<((int R, int G, int B) Trio, double U)> eyes)
    {
        double[] need = new double[nominal.Count];
        foreach (((int r, int g, int b), double u) in eyes)
        {
            int[] trio = [r, g, b];
            double[,] mt = new double[3, 3];                 // M transposed: column k is carrier k's XYZ
            for (int k = 0; k < 3; k++)
            {
                double[] xyz = SignalXyz(nominal[trio[k]].Lines, u);
                for (int c = 0; c < 3; c++) mt[c, k] = xyz[c];
            }

            double[] x = Solve3(mt, D65);
            for (int k = 0; k < 3; k++) need[trio[k]] = x[k];
        }

        double total = need.Sum();
        return [.. need.Select(v => v / total)];
    }

    /// <summary>Turn counts adding up to <paramref name="cycle"/>, in proportion to <paramref name="need"/>, at least one each.</summary>
    internal static int[] WholeTurns(IReadOnlyList<double> need, int cycle)
    {
        int[] counts = [.. need.Select(v => Math.Max((int)Math.Round(v * cycle, MidpointRounding.ToEven), 1))];
        while (counts.Sum() != cycle)
        {
            bool over = counts.Sum() > cycle;
            int best = 0;
            for (int k = 1; k < counts.Length; k++)
            {
                double ek = (double)counts[k] / cycle - need[k], eb = (double)counts[best] / cycle - need[best];
                if (over ? ek > eb : ek < eb) best = k;
            }

            counts[best] += over ? -1 : 1;
        }

        return counts;
    }

    /// <summary>numpy.interp: linear, clamped to the end values (or <paramref name="left"/> / <paramref name="right"/>).</summary>
    internal static double Interp(double x, double[] xp, double[] fp, double? left = null, double? right = null)
    {
        if (x < xp[0]) return left ?? fp[0];
        if (x > xp[^1]) return right ?? fp[^1];
        if (x == xp[^1]) return fp[^1];
        int lo = 0, hi = xp.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >>> 1;
            if (xp[mid] <= x) lo = mid;
            else hi = mid;
        }

        double slope = (fp[lo + 1] - fp[lo]) / (xp[lo + 1] - xp[lo]);
        return slope * (x - xp[lo]) + fp[lo];
    }

    private static double[] Solve3(double[,] a, double[] b)
    {
        double[,] m = (double[,])a.Clone();
        double[] y = (double[])b.Clone();
        for (int col = 0; col < 3; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 3; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (pivot != col)
            {
                for (int c = 0; c < 3; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
                (y[col], y[pivot]) = (y[pivot], y[col]);
            }

            for (int r = col + 1; r < 3; r++)
            {
                double f = m[r, col] / m[col, col];
                for (int c = col; c < 3; c++) m[r, c] -= f * m[col, c];
                y[r] -= f * y[col];
            }
        }

        double[] x = new double[3];
        for (int r = 2; r >= 0; r--)
        {
            double s = y[r];
            for (int c = r + 1; c < 3; c++) s -= m[r, c] * x[c];
            x[r] = s / m[r, r];
        }

        return x;
    }

    private static double[] BuildUGrid()
    {
        double[] u = new double[UCount];
        double step = UMax / (UCount - 1);
        for (int i = 0; i < UCount; i++) u[i] = i * step;
        u[^1] = UMax;
        return u;
    }

    private static double[][] BuildWeight()
    {
        double[][] w = [new double[Lambda.Length], new double[Lambda.Length], new double[Lambda.Length]];
        for (int k = 0; k < Lambda.Length; k++)
        {
            double s = LedSpectrum(Lambda[k]);
            var (x, y, z) = Cmf(Lambda[k]);
            (w[0][k], w[1][k], w[2][k]) = (s * x, s * y, s * z);
        }

        return w;
    }
}
