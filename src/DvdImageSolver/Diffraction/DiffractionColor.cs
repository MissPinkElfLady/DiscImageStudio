namespace DvdImageSolver.Diffraction;

/// <summary>
/// One colour picture, seen by tilting the disc. Its red, green and blue channels are written on three string
/// carriers whose periods stand in the ratio 2^(1/5) (16T, 13.93T, 12.13T): at the design tilt an eye sees them
/// as 614, 535 and 466 nm, so equal channels look white. With two copies the same picture is written again on
/// the next three carriers (10.56T, 9.19T, 8T), which line up at a second tilt; that is DiscDiffractStudio's
/// colour flip (<c>presets._colorflip</c>, GPL-3.0-or-later) with the same image as both pictures.
///
/// Each carrier's period is compensated across the picture in steps of 0.5% (<see cref="Chirped"/>), so the whole
/// picture shows the colour its centre shows from the design pose, and the carriers' turn shares are balanced so
/// that equal R = G = B is D65 white under a white LED.
/// </summary>
internal static class DiffractionColor
{
    internal static readonly double ColorQ = Math.Pow(2.0, 1.0 / 5.0);
    internal const double ColorULeft = 0.288;
    internal const int ColorCycle = 32;
    internal const double ChirpStep = 1.005;
    internal const int OwnSteps = 16;
    internal const double PeriodWritableMin = 6.4, PeriodWritableMax = 22.0;
    internal const double SlotRadius = 43.0;

    internal static int GridSize { get; } =
        (int)Math.Floor(Math.Log(PeriodWritableMax / PeriodWritableMin) / Math.Log(ChirpStep) + 1e-9) + 1;

    /// <summary>The six carriers and the u at which each trio is red, green and blue (colorflip_plan).</summary>
    internal static (double[] Periods, double[] UTrio) Plan()
    {
        double[] periods = new double[6];
        for (int k = 0; k < 6; k++) periods[k] = 16.0 / Math.Pow(ColorQ, k);
        return (periods, [ColorULeft, ColorULeft * Math.Pow(ColorQ, 3)]);
    }

    /// <summary>The o'clock position a centre is nearest to: decides which way the tracks run through the picture.</summary>
    internal static int NearestSlot((double X, double Y) centre)
        => Math.Abs(centre.Y) >= Math.Abs(centre.X) ? (centre.Y >= 0 ? 12 : 6) : (centre.X > 0 ? 3 : 9);

    internal static (double X, double Y) SlotCentre(int slot) => slot switch
    {
        12 => (0.0, SlotRadius),
        3 => (SlotRadius, 0.0),
        6 => (0.0, -SlotRadius),
        _ => (-SlotRadius, 0.0),
    };

    /// <summary>
    /// Builds the picture. <paramref name="red"/>, <paramref name="green"/> and <paramref name="blue"/> are linear-light
    /// channels on the cells of the picture as drawn (upright, before <paramref name="quarterTurns"/>).
    /// </summary>
    internal static DiffractionPicture Build(string name, (double X, double Y) centreMm, (double W, double H) sizeMm,
        double[,] red, double[,] green, double[,] blue, int copies = 1, int quarterTurns = 0, double cellMm = 0.05,
        double brightness = 1.0, bool wide = false, bool lampTop = true)
    {
        if (copies is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(copies),
                "A colour picture takes 1 or 2 copies: a third would need gratings shorter than the 6.4T that can be written.");
        int q = ((quarterTurns % 4) + 4) % 4;
        // Designed for the place it comes to when the disc is turned until the picture is upright.
        (double x, double y) = centreMm;
        for (int k = 0; k < q; k++) (x, y) = (y, -x);
        (double W, double H) drawnSize = q % 2 == 1 ? (sizeMm.H, sizeMm.W) : sizeMm;
        Place place;
        if (wide)
        {
            place = new Place((x, y), drawnSize, cellMm, SlotCentre(3));
        }
        else
        {
            int slot = NearestSlot((x, y));
            if (lampTop ? slot is 12 or 6 : slot is 3 or 9)
                throw new ArgumentException(lampTop
                    ? $"Picture {name}: with the lamp above, a colour picture is seen by tilting the disc, so its tracks must " +
                      "run up and down through it. Put it at 3 or 9 o'clock, give it a quarter turn, or use \"lamp\": \"side\"."
                    : $"Picture {name}: with the lamp to the side, a colour picture is seen by turning the disc, so its tracks " +
                      "must run left and right through it. Put it at 12 or 6 o'clock, or give it a quarter turn.");
            place = new Place((x, y), drawnSize, cellMm, SlotCentre(slot));
        }

        (double[] periods, double[] uTrio) = Plan();
        List<((int, int, int), double)> eyes = [((0, 1, 2), uTrio[0])];
        if (copies == 2) eyes.Add(((3, 4, 5), uTrio[1]));
        int n = 3 * copies;
        periods = periods[..n];
        double[] need = DiffractionViewing.WhiteBalance([.. periods.Select(StringCarrierTable.Get)], eyes);
        int[] turns = DiffractionViewing.WholeTurns(need, ColorCycle);
        double[] gains = new double[n];
        for (int k = 0; k < n; k++) gains[k] = need[k] / ((double)turns[k] / turns.Sum());
        double most = gains.Max();
        for (int k = 0; k < n; k++) gains[k] = gains[k] / most * brightness;

        List<DiffractionCarrier> carriers = [];
        double[][,] channels = [red, green, blue];
        List<double[,]> views = [.. Enumerable.Range(0, n).Select(k => Clip(channels[k % 3]))];
        for (int k = 0; k < n; k++)
        {
            // Wide: every cell is tuned for the light direction that suits it best (its tracks across the line from
            // the lamp), which by symmetry depends only on its distance from the hub.
            var (eye, lamp) = DiffractionViewing.Cyclopean(place.Aim, uTrio[k / 3], lampTop: wide || lampTop);
            double[,] stretch = wide ? RadialStretchMap(place, eye, lamp) : StretchMap(place, eye, lamp);
            carriers.Add(Chirped(periods[k], stretch).Rotated(q));
        }

        return new DiffractionPicture(name, centreMm, sizeMm, views, carriers, linear: true, gains, turns, q, cellMm,
            StringCarrier.Filler);
    }

    internal sealed record Place((double X, double Y) Centre, (double W, double H) Size, double Cell, (double X, double Y) Aim)
    {
        internal int Nx => (int)Math.Round(Size.W / Cell, MidpointRounding.ToEven);
        internal int Ny => (int)Math.Round(Size.H / Cell, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Period factor per cell for a wide picture: what the cell needs to show the aim point's colour when the eye and
    /// lamp are turned about the hub until the cell lies where the aim lies (3 o'clock for a lamp above). That pose
    /// sees the cell at the same distance from the hub on the aim's axis, so the factor depends only on the radius.
    /// </summary>
    internal static double[,] RadialStretchMap(Place place, DiffractionViewing.Vec3 eye, DiffractionViewing.Vec3 lamp)
    {
        double atAim = Math.Abs(DiffractionViewing.TangentialU(place.Aim.X, place.Aim.Y, eye, lamp));
        double aimR = Math.Sqrt(place.Aim.X * place.Aim.X + place.Aim.Y * place.Aim.Y);
        (double ax, double ay) = (place.Aim.X / aimR, place.Aim.Y / aimR);
        double[,] s = new double[place.Ny, place.Nx];
        for (int j = 0; j < place.Ny; j++)
        {
            double y = place.Centre.Y + place.Size.H / 2 - (j + 0.5) * place.Cell;
            for (int i = 0; i < place.Nx; i++)
            {
                double x = place.Centre.X - place.Size.W / 2 + (i + 0.5) * place.Cell;
                double r = Math.Sqrt(x * x + y * y);
                double v = atAim / Math.Abs(DiffractionViewing.TangentialU(r * ax, r * ay, eye, lamp));
                s[j, i] = double.IsNaN(v) ? double.PositiveInfinity : v;
            }
        }

        return s;
    }

    /// <summary>
    /// Period factor per cell that makes the whole picture show the wavelength its aim point shows, for this eye
    /// and lamp: u at the aim over u at the cell (infinite where the tracks do not deflect at all).
    /// </summary>
    internal static double[,] StretchMap(Place place, DiffractionViewing.Vec3 eye, DiffractionViewing.Vec3 lamp)
    {
        double atAim = Math.Abs(DiffractionViewing.TangentialU(place.Aim.X, place.Aim.Y, eye, lamp));
        double[,] s = new double[place.Ny, place.Nx];
        for (int j = 0; j < place.Ny; j++)
        {
            double y = place.Centre.Y + place.Size.H / 2 - (j + 0.5) * place.Cell;
            for (int i = 0; i < place.Nx; i++)
            {
                double x = place.Centre.X - place.Size.W / 2 + (i + 0.5) * place.Cell;
                double v = atAim / Math.Abs(DiffractionViewing.TangentialU(x, y, eye, lamp));
                s[j, i] = double.IsNaN(v) ? double.PositiveInfinity : v;
            }
        }

        return s;
    }

    /// <summary>
    /// A carrier whose period follows <paramref name="stretch"/> per cell in steps of 0.5%, kept inside the writable
    /// 6.4T..22T. Up to 16 steps from the nominal period the variants are that period times a power of 1.005;
    /// further out they come from one grid of periods shared by all carriers.
    /// </summary>
    internal static CarrierGroup Chirped(double periodBits, double[,] stretch)
    {
        double step = Math.Log(ChirpStep);
        int wLo = Math.Min((int)Math.Ceiling(Math.Log(PeriodWritableMin / periodBits) / step - 1e-9), 0);
        int wHi = Math.Max((int)Math.Floor(Math.Log(PeriodWritableMax / periodBits) / step + 1e-9), 0);
        int ny = stretch.GetLength(0), nx = stretch.GetLength(1);
        int[,] w = new int[ny, nx];
        int lo = 0, hi = 0;
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                double v = Math.Clamp(Math.Log(stretch[j, i]) / step, wLo, wHi);
                int k = (int)Math.Round(v, MidpointRounding.ToEven);
                w[j, i] = k;
                lo = Math.Min(lo, k);
                hi = Math.Max(hi, k);
            }
        }

        List<StringCarrier> variants = [];
        for (int k = lo; k <= hi; k++) variants.Add(StringCarrierTable.Get(VariantPeriod(periodBits, k)));
        byte[,] map = new byte[ny, nx];
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                map[j, i] = (byte)(w[j, i] - lo);
        return new CarrierGroup(variants, map, -lo);
    }

    /// <summary>
    /// Period of the variant k steps of 0.5% from a carrier's nominal period: its own step up to 16 steps out, a
    /// period of the shared grid beyond that.
    /// </summary>
    internal static double VariantPeriod(double periodBits, int k)
    {
        // DiscDiffractStudio's table holds the shared grid in full, but only the own steps its placements asked for;
        // a missing own step takes the nearest grid period, at most 0.25 % away.
        double own = periodBits * Math.Pow(ChirpStep, k);
        if (Math.Abs(k) <= OwnSteps && StringCarrierTable.Contains(own)) return own;
        int j = (int)Math.Round(Math.Log(periodBits * Math.Pow(ChirpStep, k) / PeriodWritableMin) / Math.Log(ChirpStep),
            MidpointRounding.ToEven);
        return PeriodWritableMin * Math.Pow(ChirpStep, Math.Clamp(j, 0, GridSize - 1));
    }

    private static double[,] Clip(double[,] v)
    {
        double[,] c = new double[v.GetLength(0), v.GetLength(1)];
        for (int j = 0; j < v.GetLength(0); j++)
            for (int i = 0; i < v.GetLength(1); i++)
                c[j, i] = Math.Clamp(v[j, i], 0.0, 1.0);
        return c;
    }
}
