using System.Security.Cryptography;
using DiscImageStudio.Core;

namespace DvdImageSolver.Diffraction;

/// <summary>
/// Parity with DiscDiffractStudio's Python engine. The expected values come from
/// tools/DiffractionParity/make_vectors.py run against DiscDiffractStudio; the layout below must stay
/// identical to the one built there.
/// </summary>
internal static class DiffractionSelfTests
{
    // make_vectors.py: SHA-256 of the scrambled-domain bytes (16 x 2048) of every 2003rd ECC block that
    // touches a picture, from DiscDiffractStudio's BlockMapper.block_bytes.
    private static readonly (uint Lba, string Sha256)[] ExpectedBlocks =
    [
        (416624u, "9cded54b8c9296387d5cff8993045a6025fd9e5e440161b47466946d650188b5"),
        (448672u, "0333e21690dafefd423d1a30f5b1d7922b75f4d3010fd3ae376c15a6ee385191"),
        (512768u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
        (576864u, "579a27352079c804a91e0c0962d82f8da5f94705b13171a16da57fafa4fd52b6"),
        (608912u, "1906b55f57280bd87bf93818b9a880f3914dd0605f71e664481cb880a969d832"),
        (640960u, "218df70e68e2c329bad6264f1d6c084480ef7422e80554b73e676dbbd8614ab0"),
        (673008u, "2eeb8255e393356d83e63688b5b2b5669bac177783657d0e88310d9e9c0f74df"),
        (705056u, "f4d1f4473d0b4c21801f6f1cb9f67419aa45e641f77219a0d42958cf0f8ba865"),
        (737104u, "8367cbfa8755abdce2d4a57c831fdfa54eb7f5de3599fae141c9c0a55d7835b3"),
        (769152u, "1c97277456d81894c0079bd83258f71851f281b4c425243ba77f164c80f2f11e"),
        (801200u, "9800307dedc7cad553f110881dd00034c7672d967bd4ed7115fc4ca82caa783b"),
        (833248u, "42e4ae112529d602609245b5089b6b82cecfa1f2e6dcc308cae772a52951640c"),
        (865296u, "3ad24a87f5246e7b296ce1a52787eaefc3ce902ff9337687b9c4603192b362a0"),
        (897344u, "3467f7cef59c5f9cf177d724da7421e3b3981416bf55fd7b1ad648f9dfe23cc9"),
        (929392u, "2e6c7338be0ced1a0a660e409ec934782e7eea481b6c600e8fd647175b3d85e9"),
        (961440u, "d31f1c3aa1d6acc3b48d8a79eecf16cab077db5f40c6bd1c29ae76259009e72c"),
        (993488u, "3c436d503823c44f632a8eafa0cfb98dd625703294e7f2849d196e40b5282d26"),
        (1025536u, "d0795d95d3fa59f1a0017a2c5becc7a1904418b1b557a3e3a92a948b3cd7fa43"),
        (1057584u, "35dfc7d630ddd8e7356747d80f2954d9fbca1119a481001f387a7e067241bdc3"),
        (1089632u, "dee09b6bf0feb680c502321a784bd8f10ad55d66c1c2bce84d3e0f4654d45f10"),
        (1121680u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
        (1153728u, "6de0cd29bc526a0f65dd1ac9f2e75548c74051edd871350950a850a7c98a59d1"),
        (1185776u, "e3868d2b211b117321325ee5e5dedabc01a48b3fdddd6ff5b0d50fdd14530704"),
        (1217824u, "1ee5a8a45eb8e25aa33c4168716c51a0f373f47437a1a90c8bb8a6ee2a7e150e"),
        (1249872u, "8f3709f3c3a21bbaa4178843d184a2c585a938875fdd89ca4a4dddb964ad946d"),
        (1281920u, "a18d9f5d30156ea896fca9d65b0eb102fb9278a27b1e4a387d751aee944d8e6b"),
        (1313968u, "cf0859cbd65962320fc0ac24e0258f3daa48f27abecd107e97c9bcfde7c73ebf"),
        (1346016u, "b7dfc36d379de2ff37112277d112e3610db0efbd93ec059c846bac5c166fc8eb"),
        (1378064u, "8bc538e176e4a9943dd99c7a94a08fcabfee63ec51090e1497e7c720baae3d45"),
        (1410112u, "a8ff4ecc4250be8dc79b376e1d087a61e8ad30c7bd6ecfb9c2b6df76815194b7"),
        (1442160u, "d9fa30f19425e2d55aeb4b3c164b2287edcb532f2a671ff40ae4140c3a9ff17d"),
        (1474208u, "dc6b26f14de136145be941e50c05cc6f64a1b60bb9c9b62c5716d9ed0104cd5a"),
        (1506256u, "c56352ee5b2906801331d7a2cc7cc87d8622dc9b8d0c47ea35a4d902f30fe178"),
        (1538304u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
        (1570352u, "bfaf79c28ad8dea55b9b1ab83bd974cb3144671f5992acbd24f5b3d5fde33bd9"),
        (1602400u, "876cad08aaa941eeeaf090e3923e2ad44d1e10760c33ab4e3d080640f76555ee"),
        (1698544u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
        (1730592u, "b1a8ff99eccab3f01603b136eb07b4020f797a76b65b557c4c9f4c47d402db83"),
        (1762640u, "d66176d4985899cb9c55d9c07520fba62d1c92c27e693927064b1a790e943c21"),
        (1794688u, "5da1aa1b02addb636fb8047de9e5198a149fdb8162727ee93b19f8b7219fdc32"),
        (1826736u, "6eb167b1944583d73d70d638eadbc58b6a07ecd3c85bec43948e5a6e38a07799"),
        (1922880u, "4c69226c68216928d0cede3e74bca9c7c20fdc3ff29f0abc661398f7dbd411b1"),
    ];

    internal static void Run()
    {
        TestBridges();
        TestSpreadCycleAndThresholds();
        TestBlockParity();
        TestScrambledBlockStream();
    }

    private static void TestBridges()
    {
        var bridges = DiffractionCarriers.FindBridges([0x92, 0xB9, 0x66, 0xA5]);
        Check(bridges.Count == 1 && bridges[0] == ((byte)0x66, (byte)0x92, (byte?)0xA5),
            "diffraction bridges: only 0x66 -> 0x92 needs one, and it is 0xA5");
        Check(DiffractionCarriers.SteadyStates(0x92).Count > 0, "0x92 can be repeated");
    }

    private static void TestSpreadCycleAndThresholds()
    {
        var (cycle, rank) = DiffractionPicture.SpreadCycle([1, 2, 1]);
        Check(cycle.SequenceEqual([1, 0, 2, 1]) && rank.SequenceEqual([0, 0, 0, 1]), "spread_cycle(1, 2, 1)");
        Check(DiffractionPicture.TurnThreshold[1] == 16.5 / 32 && DiffractionPicture.TurnThreshold[2] == 8.5 / 32,
            "bit-reversed turn thresholds");
    }

    internal static DiffractionLayout BuildParityLayout()
    {
        // DiscDiffractStudio preset "dis-user-biaoding2".
        DvdTrackGeometry geometry = DvdTrackGeometry.Create(23.991531619344556, 57.96498503049657,
            2_297_888.0 * DiffractionLayout.BitsPerSector * 133.3e-6,
            -3.5014145371817896e-05, 4.087772214354072e-05, -1.5829613261263454e-05);
        DiffractionPicture[] pictures =
        [
            new("stereo", (0.0, 43.0), (24.0, 24.0), [Views(480, 480, 7, 3, 101), Views(480, 480, 5, 11, 97)], [0x92, 0xB9]),
            new("three", (43.0, 0.0), (20.0, 16.0),
                [Views(320, 400, 3, 7, 89), Views(320, 400, 13, 2, 83), Views(320, 400, 1, 1, 61)],
                [0x92, 0xB9, 0x66], gains: [1.0, 0.8, 0.9], turnCounts: [1, 2, 1], quarterTurns: 1),
            new("single", (-30.0, -30.0), (10.0, 10.0), [Views(200, 200, 9, 4, 53)], [0x66]),
        ];
        return new DiffractionLayout(pictures, geometry);
    }

    private static void TestBlockParity()
    {
        DiffractionLayout layout = BuildParityLayout();
        Check(layout.LastSector(2_297_888) == 2_168_272, "diffraction last sector");
        byte[] block = new byte[DiffractionLayout.SectorsPerBlock * DiffractionLayout.PayloadBytes];
        int mismatches = 0;
        foreach ((uint lba, string expected) in ExpectedBlocks)
        {
            layout.FillBlock(lba, block);
            string actual = Convert.ToHexStringLower(SHA256.HashData(block));
            if (actual != expected)
            {
                mismatches++;
                Console.Error.WriteLine($"diffraction parity: block {lba} differs from DiscDiffractStudio");
            }
        }

        Check(mismatches == 0, $"diffraction parity with DiscDiffractStudio ({mismatches} of {ExpectedBlocks.Length} blocks differ)");
    }

    private static void TestScrambledBlockStream()
    {
        DiffractionLayout layout = BuildParityLayout();
        uint lba0 = ExpectedBlocks[0].Lba;
        var options = new Solver.MultiBlockSolveOptions(lba0, 16, 0x30000, 0, 0, 0, 0, 0, true,
            new Solver.ImageMappingOptions(2_297_888, 23.991531619344556, 57.96498503049657, 133.3, 0, true, 128, 0),
            Solver.SolverAlgorithm.Dispersion, FastParallelism: 1);
        using MemoryStream stream = new();
        Solver.FastDispersionImageWriter.WriteScrambledBlockStream((lba, q) => layout.FillBlock(lba, q), stream, options);
        byte[] user = stream.ToArray();
        byte[] q = new byte[user.Length];
        layout.FillBlock(lba0, q);
        for (int sector = 0; sector < 16; sector++)
        {
            ReadOnlySpan<byte> mask = Encoding.DvdDataFrameBuilder.PayloadScramblerMask(lba0 + (uint)sector + 0x30000);
            for (int k = 0; k < 2048; k++)
                if ((byte)(user[sector * 2048 + k] ^ mask[k]) != q[sector * 2048 + k])
                    throw new InvalidOperationException("selftest failed: scrambled block stream does not descramble to its bytes");
        }
    }

    /// <summary>((i * a + j * b) mod m) / (m - 1) on ny x nx cells, as make_vectors.py builds it.</summary>
    internal static double[,] Views(int nx, int ny, int a, int b, int m)
    {
        double[,] v = new double[ny, nx];
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                v[j, i] = (double)((i * a + j * b) % m) / (m - 1);
        return v;
    }

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException($"selftest failed: {what}");
    }
}
