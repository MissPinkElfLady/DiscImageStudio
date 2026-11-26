using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace DvdImageSolver.Diffraction;

/// <summary>
/// A carrier of any period: a fixed 91-byte string (one sync frame's worth) whose marks follow a cosine of
/// that period when written from EFMPlus state 1 with no trailing zeros, which is the modulator's state right
/// after a SYNC code and after the filler byte 0xA5 has settled. A lit stretch of n bytes is the first n bytes
/// of the string. Ported from DiscDiffractStudio's <c>dvdgrating/strings.py</c> (GPL-3.0-or-later).
/// </summary>
internal sealed class StringCarrier
{
    internal const int RunBytes = 91;
    internal const byte Filler = 0xA5;

    internal StringCarrier(double periodBits, byte[] data, IReadOnlyList<(double PeriodBits, double Amplitude)> lines,
        IReadOnlyList<int> badLengths)
    {
        if (data.Length != RunBytes) throw new ArgumentException($"A carrier string has {RunBytes} bytes.");
        PeriodBits = periodBits;
        Data = data;
        Lines = [.. lines];
        BadLengths = [.. badLengths];
        HashSet<int> bad = [.. badLengths];
        Trim = new short[RunBytes + 1];
        for (int length = 0; length <= RunBytes; length++)
        {
            short t = 0;
            while (bad.Contains(length - t)) t++;
            Trim[length] = t;
        }
    }

    /// <summary>The period asked for, in channel bits.</summary>
    internal double PeriodBits { get; }

    /// <summary>Scrambled-domain bytes.</summary>
    internal byte[] Data { get; }

    /// <summary>Spectral lines of the whole string, strongest first.</summary>
    internal (double PeriodBits, double Amplitude)[] Lines { get; }

    /// <summary>Prefix lengths after which the filler byte would leave the recorder a choice.</summary>
    internal int[] BadLengths { get; }

    /// <summary>Trim[L]: bytes dropped from the end of a lit stretch of L bytes that is followed by filler.</summary>
    internal short[] Trim { get; }
}

/// <summary>
/// One view's carrier: a string carrier, or several of slightly different period ("variants") with a map
/// saying which one each cell uses (period compensation across the picture).
/// </summary>
internal sealed class CarrierGroup(IReadOnlyList<StringCarrier> strings, byte[,]? variant = null, int nominal = 0)
{
    /// <summary>At most this many variants per group: the code space each group gets.</summary>
    internal const int Variants = 256;

    internal StringCarrier[] Strings { get; } = strings.Count is > 0 and <= Variants
        ? [.. strings] : throw new ArgumentException($"A carrier group has 1 to {Variants} strings.");

    /// <summary>Index into <see cref="Strings"/> per cell [row, column], row 0 at the top; null for the nominal string everywhere.</summary>
    internal byte[,]? Variant { get; } = variant;

    internal int Nominal { get; } = nominal;

    internal double PeriodBits => Strings[Nominal].PeriodBits;

    internal CarrierGroup Rotated(int quarterTurns)
        => Variant is null || quarterTurns % 4 == 0
            ? this
            : new CarrierGroup(Strings, DiffractionPicture.Rotate(Variant, quarterTurns), Nominal);
}

/// <summary>A view's carrier: one repeated byte, or a group of carrier strings.</summary>
internal sealed record DiffractionCarrier(byte? Byte, CarrierGroup? Group)
{
    public static implicit operator DiffractionCarrier(byte value) => new(value, null);

    public static implicit operator DiffractionCarrier(CarrierGroup group) => new(null, group);

    internal string Label => Byte is byte b ? $"0x{b:X2}" : $"{Group!.PeriodBits:F2}T";
}

/// <summary>
/// DiscDiffractStudio's table of carrier strings (data/string_carriers.json), embedded gzip-compressed.
/// Strings are deterministic results of a beam search; this port looks them up and does not synthesise new ones.
/// </summary>
internal static class StringCarrierTable
{
    private const string ResourceName = "DvdImageSolver.Diffraction.string_carriers.json.gz";
    private static readonly Lazy<(double[] Periods, StringCarrier[] Strings)> Table = new(Load);

    internal static int Count => Table.Value.Periods.Length;

    /// <summary>
    /// The string for a period in channel bits. Periods are matched to 1e-11 relative, so that the last-digit
    /// differences of pow() between platforms find the same string.
    /// </summary>
    internal static StringCarrier Get(double periodBits)
        => TryFind(periodBits, out StringCarrier? found) ? found : throw new InvalidOperationException(
            $"No carrier string for a period of {periodBits.ToString("R", CultureInfo.InvariantCulture)}T in the table " +
            "(this picture asks for a grating period DiscDiffractStudio has not synthesised).");

    /// <summary>Whether the table holds a string for this period (see <see cref="Get"/>).</summary>
    internal static bool Contains(double periodBits) => TryFind(periodBits, out _);

    private static bool TryFind(double periodBits, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out StringCarrier? found)
    {
        var (periods, strings) = Table.Value;
        int index = Array.BinarySearch(periods, periodBits);
        if (index >= 0)
        {
            found = strings[index];
            return true;
        }

        int above = ~index;
        int best = -1;
        double bestError = double.PositiveInfinity;
        foreach (int k in new[] { above - 1, above })
        {
            if ((uint)k >= (uint)periods.Length) continue;
            double error = Math.Abs(periods[k] - periodBits) / periodBits;
            if (error < bestError) (best, bestError) = (k, error);
        }

        found = best >= 0 && bestError <= 1e-11 ? strings[best] : null;
        return found is not null;
    }

    private static (double[], StringCarrier[]) Load()
    {
        using Stream compressed = typeof(StringCarrierTable).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        using GZipStream json = new(compressed, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(json);
        List<(double, StringCarrier)> entries = [];
        foreach (JsonProperty entry in document.RootElement.EnumerateObject())
        {
            JsonElement e = entry.Value;
            double period = e.GetProperty("period_bits").GetDouble();
            byte[] data = Convert.FromHexString(e.GetProperty("bytes_hex").GetString()!);
            var lines = e.GetProperty("lines").EnumerateArray()
                .Select(l => (l[0].GetDouble(), l[1].GetDouble())).ToList();
            var bad = e.GetProperty("bad_lengths").EnumerateArray().Select(b => b.GetInt32()).ToList();
            entries.Add((period, new StringCarrier(period, data, lines, bad)));
        }

        entries.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return ([.. entries.Select(x => x.Item1)], [.. entries.Select(x => x.Item2)]);
    }
}
