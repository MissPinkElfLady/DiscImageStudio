using DiscImageStudio.ImageIO;

namespace DvdImageSolver.Diffraction;

/// <summary>Turns image files into the grey cell arrays a <see cref="DiffractionPicture"/> takes.</summary>
internal static class DiffractionImages
{
    /// <summary>Linear-light luminance weights of the sRGB primaries.</summary>
    private const double LumaR = 0.2126, LumaG = 0.7152, LumaB = 0.0722;

    internal static double SrgbToLinear(double v)
    {
        v = Math.Clamp(v, 0.0, 1.0);
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Reads an image, cuts it down around its middle to the cells' aspect ratio, averages it onto ny x nx cells in
    /// linear light and returns the linear-light red, green and blue channels (DiscDiffractStudio's "cells"
    /// sampling, with area averaging in place of Pillow's Lanczos). Transparent pixels count as black.
    /// </summary>
    internal static (double[,] R, double[,] G, double[,] B) LinearRgbCells(string path, int ny, int nx)
    {
        BgraRaster image = RasterCodec.LoadBgra32(path);
        int w = image.Width, h = image.Height;
        double aspect = (double)nx / ny;
        double x0 = 0, y0 = 0, x1 = w, y1 = h, cw = w, ch = h;
        if (cw / ch > aspect)
        {
            double keep = ch * aspect;
            (x0, x1) = ((cw - keep) / 2, (cw - keep) / 2 + keep);
        }
        else if (cw / ch < aspect)
        {
            double keep = cw / aspect;
            (y0, y1) = ((ch - keep) / 2, (ch - keep) / 2 + keep);
        }

        int left = (int)Math.Round(x0, MidpointRounding.ToEven), top = (int)Math.Round(y0, MidpointRounding.ToEven);
        int cropW = Math.Max(1, (int)Math.Round(x1, MidpointRounding.ToEven) - left);
        int cropH = Math.Max(1, (int)Math.Round(y1, MidpointRounding.ToEven) - top);
        double[] lin = new double[256];
        for (int k = 0; k < 256; k++) lin[k] = SrgbToLinear(k / 255.0);
        double[][,] channels = [new double[cropH, cropW], new double[cropH, cropW], new double[cropH, cropW]];
        ReadOnlySpan<byte> px = image.Pixels;
        for (int y = 0; y < cropH; y++)
        {
            for (int x = 0; x < cropW; x++)
            {
                int o = ((top + y) * w + left + x) * 4;
                double a = px[o + 3] / 255.0;
                channels[0][y, x] = a * lin[px[o + 2]];
                channels[1][y, x] = a * lin[px[o + 1]];
                channels[2][y, x] = a * lin[px[o]];
            }
        }

        return (Resample(channels[0], ny, nx), Resample(channels[1], ny, nx), Resample(channels[2], ny, nx));
    }

    /// <summary>Linear-light luminance on ny x nx cells (see <see cref="LinearRgbCells"/>).</summary>
    internal static double[,] LinearLuminanceCells(string path, int ny, int nx)
    {
        var (r, g, b) = LinearRgbCells(path, ny, nx);
        double[,] luminance = new double[ny, nx];
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                luminance[j, i] = LumaR * r[j, i] + LumaG * g[j, i] + LumaB * b[j, i];
        return luminance;
    }

    /// <summary>
    /// Resizes [row, column] data to ny x nx: area averaging along an axis that shrinks, linear
    /// interpolation of cell centres along one that grows.
    /// </summary>
    internal static double[,] Resample(double[,] source, int ny, int nx)
    {
        int h = source.GetLength(0), w = source.GetLength(1);
        if (h == ny && w == nx) return source;
        double[,] rows = new double[h, nx];
        for (int y = 0; y < h; y++)
        {
            for (int i = 0; i < nx; i++) rows[y, i] = Sample1D(k => source[y, k], w, nx, i);
        }

        double[,] output = new double[ny, nx];
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++) output[j, i] = Sample1D(k => rows[k, i], h, ny, j);
        }

        return output;
    }

    private static double Sample1D(Func<int, double> at, int n, int m, int index)
    {
        double scale = (double)n / m;
        if (n >= m)
        {
            double a = index * scale, b = (index + 1) * scale, sum = 0;
            for (int k = (int)Math.Floor(a); k < Math.Min(n, (int)Math.Ceiling(b)); k++)
                sum += at(k) * (Math.Min(b, k + 1) - Math.Max(a, k));
            return sum / scale;
        }

        double centre = (index + 0.5) * scale - 0.5;
        int k0 = Math.Clamp((int)Math.Floor(centre), 0, n - 1), k1 = Math.Min(k0 + 1, n - 1);
        double f = Math.Clamp(centre - k0, 0, 1);
        return at(k0) * (1 - f) + at(k1) * f;
    }
}
