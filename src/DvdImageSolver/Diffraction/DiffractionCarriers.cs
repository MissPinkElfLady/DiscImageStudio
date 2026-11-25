using System.Numerics;
using DvdImageSolver.Encoding;

namespace DvdImageSolver.Diffraction;

/// <summary>
/// Single-byte carriers: one scrambled-domain byte repeated along the track is an exactly periodic
/// mark pattern, i.e. an along-track diffraction grating whose period decides which colour leaves
/// in which direction. Ported from DiscDiffractStudio's <c>dvdgrating/carriers.py</c> (GPL-3.0-or-later).
///
/// A picture may switch carrier from one byte to the next only where the switch never leaves the
/// recorder a choice (substitution table, or the state 1/4 alternative it uses for DSV control).
/// Where a switch would, a "bridge" byte is written in place of the first byte of the new run.
/// States are 0..3 here (ECMA-267 states 1..4).
/// </summary>
internal static class DiffractionCarriers
{
    /// <summary>Background byte: 6.4T, outside both viewing bands, so it looks dark.</summary>
    internal const byte OffByte = 0xA5;

    /// <summary>16T, 10.67T and 8T: equally spaced in sin(theta).</summary>
    internal static ReadOnlySpan<byte> ViewBytes => [0x92, 0xB9, 0x66];

    /// <summary>Bytes tried as bridges, in order of preference.</summary>
    internal static ReadOnlySpan<byte> BridgeCandidates => [0xA5, 0xB9, 0x92, 0x66];

    /// <summary>Period in channel bits of the strongest line of a repeated carrier byte.</summary>
    internal static double PeriodBits(byte value) => value switch
    {
        0x92 => 16.0,
        0xB9 => 32.0 / 3.0,
        0x66 => 8.0,
        0xA5 => 6.4,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"0x{value:X2} is not a known carrier byte."),
    };

    /// <summary>Equal white for every view: a carrier collects lamp light in proportion to its period.</summary>
    internal static double[] DefaultGains(ReadOnlySpan<byte> carriers)
    {
        double shortest = double.PositiveInfinity;
        foreach (byte c in carriers) shortest = Math.Min(shortest, PeriodBits(c));
        double[] gains = new double[carriers.Length];
        for (int k = 0; k < gains.Length; k++) gains[k] = shortest / PeriodBits(carriers[k]);
        return gains;
    }

    internal readonly record struct StepResult(bool Legal, bool Ambiguous, ushort Word, int NextState, int NextTrailingZeros);

    /// <summary>One main-table code word written from (state, trailing zeros of the previous word).</summary>
    internal static StepResult Step(byte value, int state, int trailingZeros)
    {
        ushort word = EfmPlusTables.LookupMain(value, (byte)(state + 1)).Word;
        bool legal = trailingZeros + LeadingZeros(word) is >= 2 and <= 10;
        bool ambiguous = value <= 87;
        if (state is 0 or 3)
        {
            ushort other = EfmPlusTables.LookupMain(value, (byte)(state == 0 ? 4 : 1)).Word;
            ambiguous |= trailingZeros + LeadingZeros(other) is >= 2 and <= 10;
        }
        int next = EfmPlusTables.LookupMain(value, (byte)(state + 1)).NextState - 1;
        return new StepResult(legal, ambiguous, word, next, TrailingZeros(word));
    }

    internal static int LeadingZeros(ushort word) => word == 0 ? 16 : BitOperations.LeadingZeroCount((uint)word) - 16;

    internal static int TrailingZeros(ushort word) => word == 0 ? 16 : BitOperations.TrailingZeroCount((uint)word);

    /// <summary>The (state, tz) pairs the modulator cycles through once <paramref name="value"/> has been repeated.</summary>
    internal static SortedSet<(int State, int Tz)> SteadyStates(byte value, int settle = 12)
    {
        SortedSet<(int, int)> output = [];
        for (int s0 = 0; s0 < 4; s0++)
        {
            for (int tz0 = 0; tz0 <= 10; tz0++)
            {
                if (!Step(value, s0, tz0).Legal) continue;
                int s = s0, tz = tz0;
                for (int k = 0; k < settle + 8; k++)
                {
                    StepResult step = Step(value, s, tz);
                    if (!step.Legal) throw new InvalidOperationException($"0x{value:X2} cannot be repeated.");
                    s = step.NextState;
                    tz = step.NextTrailingZeros;
                    if (k >= settle) output.Add((s, tz));
                }
            }
        }

        return output;
    }

    /// <summary>Is the switch from a run of <paramref name="a"/> to a run of <paramref name="b"/> legal and free of recorder choices?</summary>
    internal static bool IsCleanSwitch(byte a, byte b,
        IReadOnlyDictionary<byte, SortedSet<(int State, int Tz)>> steady)
    {
        bool legal = true, ambiguous = false;
        SortedSet<(int, int)> target = steady[b];
        foreach ((int state, int tz) in steady[a])
        {
            int s = state, t = tz, k = 0;
            while (legal && !target.Contains((s, t)) && k < 8)
            {
                StepResult step = Step(b, s, t);
                legal &= step.Legal;
                ambiguous |= step.Ambiguous;
                s = step.NextState;
                t = step.NextTrailingZeros;
                k++;
            }

            for (int n = 0; n < 4 && legal; n++)
            {
                StepResult step = Step(b, s, t);
                legal &= step.Legal;
                ambiguous |= step.Ambiguous;
                s = step.NextState;
                t = step.NextTrailingZeros;
            }
        }

        return legal && !ambiguous;
    }

    /// <summary>
    /// For every ordered pair of <paramref name="values"/> whose switch is not clean, the byte written in
    /// place of the first byte of the new run (null when none of the candidates helps). Same order as
    /// DiscDiffractStudio's <c>find_bridges</c>: ascending values, then itertools.permutations order.
    /// </summary>
    internal static List<(byte From, byte To, byte? Bridge)> FindBridges(IEnumerable<byte> values)
    {
        byte[] sorted = [.. new SortedSet<byte>(values)];
        SortedSet<byte> all = [.. sorted];
        foreach (byte c in BridgeCandidates) all.Add(c);
        Dictionary<byte, SortedSet<(int, int)>> steady = all.ToDictionary(v => v, v => SteadyStates(v));
        List<(byte, byte, byte?)> output = [];
        foreach (byte a in sorted)
        {
            foreach (byte b in sorted)
            {
                if (a == b || IsCleanSwitch(a, b, steady)) continue;
                byte? bridge = null;
                foreach (byte c in BridgeCandidates)
                {
                    if (c != a && c != b && IsCleanSwitch(a, c, steady) && IsCleanSwitch(c, b, steady))
                    {
                        bridge = c;
                        break;
                    }
                }

                output.Add((a, b, bridge));
            }
        }

        return output;
    }
}
