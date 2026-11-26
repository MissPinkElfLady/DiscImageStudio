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
        TestStringRuns();
        TestColor("colour, two copies", TwoCopies, 2);
        TestColor("colour, one copy", OneCopy, 1);
        TestWideColor();
    }

    // make_color_vectors.py (--copies 2 and --copies 1): DiscDiffractStudio's colour flip of the channels below
    // at 3 o'clock (centre 43, 0 mm; 24 x 24 mm), the same image as both pictures, or only the first colour trio.
    private sealed record ColorVectors(int[] Turns, double[] Gains,
        (int Nominal, int Variants, string BytesSha256, string VariantSha256)[] Carriers, (uint Lba, string Sha256)[] Blocks);

    private static readonly ColorVectors TwoCopies = new(
        Turns: [3, 3, 7, 4, 5, 10],
        Gains: [0.8658968861620225, 1.0, 0.8796808820189644, 0.9852129901537552, 0.8959930221612373, 0.9340132097611067],
        Carriers:
        [
            (0, 16, "ec68dc7505a6eb4de3da0f40fa040cd96830b491b3253866a91a060ac7bf7f7b", "d090e8c4e0bfb5ed6f80a15347a189337a804c6b7a985e86671092999c47651b"),
            (0, 16, "fe3104f5ca111a705ac6225b8474b39c769bb3be4ca57afd02b6928060a5280d", "d090e8c4e0bfb5ed6f80a15347a189337a804c6b7a985e86671092999c47651b"),
            (0, 16, "dbad13db182ab4e6ae217a3c2c6f2347ab68010ec3466fc2b007161c2624ab63", "d090e8c4e0bfb5ed6f80a15347a189337a804c6b7a985e86671092999c47651b"),
            (0, 16, "8b3a71caf3d46fc40a8a8024467b7d2da3f894d867fd80ba4bf9f2a4c1ecd7e2", "cba270998b2b52e75d646fb747d5ee68a69a0a4553b65cc7a3c6ce8476b8b8ce"),
            (0, 16, "91b530c270480cba993cd06dbf4b6dea68e2953ed5b061f1600f62366cd261fe", "cba270998b2b52e75d646fb747d5ee68a69a0a4553b65cc7a3c6ce8476b8b8ce"),
            (0, 16, "32f0122cdedd6e6261ac0cb6abc3e8ac91c499deb61c72d94a229225e1920830", "cba270998b2b52e75d646fb747d5ee68a69a0a4553b65cc7a3c6ce8476b8b8ce"),
        ],
        Blocks:
        [
            (320320u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
            (352352u, "1183ceddc32951a336d968c6f09966ecae9a14a0e6c481b6d43ebd17434103ca"),
            (368368u, "5984ef4b0752e57a6036eaef09b57f0a8210c7e6da0d6f4403279d2a4a0358a1"),
            (384384u, "5065cf1438ab2f5520ecc4a11042764aec4f438a1a614377969b1057f6027fcb"),
            (512512u, "4d78a8666928f74807e833f5e655015b3f39edefb8f2ee5381c085953d45c1c5"),
            (544544u, "80ac643429f972134d898e6a14bece042af03b550f07bf7e37dd77e4f734b599"),
            (576576u, "b2a7301364a462967dfeb35b2f3fa81ba87be75461948d908fae81f26d1fcb9e"),
            (592592u, "1c55a0b196b001c1a29e02ddb7fb6f2dd4ec798aaf5d9bc06dfe896c9c6f6a03"),
            (608608u, "d51c87afbaa7ececb9b22ae7a3d747256ed4bda8726dbbf8aa99b168e4a7e786"),
            (720720u, "e5ef1e6cde2a6f563822dd04d1a4ea0f16fa9c072c9e0af9284d22dbe760a890"),
            (752752u, "381eb6b8de2fc3b763d9566a3a56c699afc655599d43741fa2c9847ff2a0d04a"),
            (768768u, "0b71d5db40dd1684b1c96205f05b5a5cd67a332c8f0a63a24299dd3f076771be"),
            (800800u, "bc1e51d8c386c7f007c1c9e5555a15f8336c87a2bd1092f8c4ba6e41a4a2b6b0"),
            (816816u, "3fb9be0ac663e01b8b85ad36ba00de521ae9d0cdfcf55a67fcf4a020a45ebef4"),
            (864864u, "d8e2286d6629eb1e303d391a4a85871a92cc3d3197914ce3eea2002f539dd1bf"),
            (880880u, "bd6c984e897a4b84bd861851b3eeeed191ee34f1522e27735f77010ccedf5283"),
            (912912u, "32e1f20ab6e7f9f9fdb7c798f7587159f24462d1f89c54a6ac3ca2c0998d0a9d"),
            (1009008u, "052e20eea55bf723022475abc2d7ce9218c9150ef7f5b7a026eef5c3ac73baa3"),
            (1073072u, "6c25c0e70325c496c39a128fc19ecdf4c477c46733cd6baac3ca04657dab5eb8"),
            (1105104u, "89bf52a08a8519e62bc4dc9a28dd3f846be1ae90be958316a0e7fb807f157d2e"),
            (1201200u, "9c40344c9c5b70116ea00b284f0d481d823acc6e536834e9a19bb9074be33d53"),
            (1217216u, "e8bba946caaae13241026e8ee2ece301d5e40d48e5fbc7a748e5aff5d459e9fb"),
            (1345344u, "b6caa4eae54fbfbbafd97e126eaaa33272e0d7b50126c7e4f7bf621334e26d81"),
            (1361360u, "fffa1ee1d6b023af4dcb601235ea524cf215c3c0cb6c63ad8df4aa6e5442bd49"),
            (1393392u, "6038d1475b4d3f4dcbd5d688dc11358673105c2a72bf36cef76db4eb77271e11"),
            (1457456u, "d61918ebef1642ba4c680e87dc0288be72823385388242973eed4571bd0e3673"),
            (1473472u, "90d939d5b69d9e4978c514182eb38e275d78fd9de04038524c24521b58e5085e"),
            (1489488u, "069e6d33e24b61f8696856f8ac0bf76ef8b4308eb5f2ecf17e4d2eed42e03893"),
            (1505504u, "58ff38fd742d0524d6ee184c3965a01cfbec018cf51b7a5bf45995d5bdc6bd6a"),
            (1553552u, "b3a4ef6568762e523cb179268e5153151b462dcf20aec7483e5a5497b250b089"),
            (1601600u, "df087e708a6da0147bf48aae5043ca73d922a6114b39f7f74c6fd317ab0d6ff4"),
            (1633632u, "9dd38650ec6202ee704d818e942e15f8bca10106e078bceced5499cdf30fc506"),
            (1665664u, "67b1cc092fab1b3c192ca998c23a23cac52b268ff2054bccd2ded0da6d7b8d3e"),
            (1713712u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
            (1729728u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
            (1745744u, "27fd31acc5e89102f7cb552f3c7a093cc1e5daa7888e9a19348d6804438285e2"),
            (1777776u, "a5f330b0a233052f3d8f626d61430be8ac99720f28209d091de30dda2b42038b"),
            (1793792u, "3e75d486793f5b4ec8964615e6cb0ecbc89b87688e1e32a1d51f997222842402"),
            (1857856u, "9e80ff1213368da46cba95ec936e95f228cb356940d4fd40c1ac15af418b6ad4"),
            (1905904u, "cdf9f9227da8be7a1823273ca7eadde80f4cf6a9460516a93fdab9191292be84"),
            (2034032u, "5c8b95bfc7c630880ce89759a6b78be9808e053e54a67c89703df2dcd33a2603"),
            (2082080u, "b6f9b8eba2b5634ffb5b85b72644de55458120eb5088d11ae1d797844e07a4dd"),
            (2114112u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
            (2130128u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
        ]);

    private static readonly ColorVectors OneCopy = new(
        Turns: [7, 8, 17],
        Gains: [0.9895964413280255, 1.0, 0.9659241057463137],
        Carriers:
        [
            (0, 16, "ec68dc7505a6eb4de3da0f40fa040cd96830b491b3253866a91a060ac7bf7f7b", "d090e8c4e0bfb5ed6f80a15347a189337a804c6b7a985e86671092999c47651b"),
            (0, 16, "fe3104f5ca111a705ac6225b8474b39c769bb3be4ca57afd02b6928060a5280d", "d090e8c4e0bfb5ed6f80a15347a189337a804c6b7a985e86671092999c47651b"),
            (0, 16, "dbad13db182ab4e6ae217a3c2c6f2347ab68010ec3466fc2b007161c2624ab63", "d090e8c4e0bfb5ed6f80a15347a189337a804c6b7a985e86671092999c47651b"),
        ],
        Blocks:
        [
            (320320u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
            (384384u, "3b1d6e28317c81f7b001d1b09c58747d05147abe6336fd71600d900331c0c77e"),
            (576576u, "6028fcf2fe5004ea503a546c65b09f62bd4748b97d1048cc0f2458ecf2786d9b"),
            (720720u, "c0a70ec42664d6e3f6d98a7bce8e766f8948b3161c1156878553b18ac4a3df7f"),
            (800800u, "b00d082335e32f29c694f6f7871947e7b4322a7eaa0370aea320c8eeb713d8bf"),
            (880880u, "6ffedbde7dfcacbaa736cc067b63fe93b06f7f79fb679d53e677614f5bfbba4e"),
            (1073072u, "c16d9944e7ad863582679ec8e1f6ad8be9d17b888c3d4737512422d20ab053af"),
            (1217216u, "d0f7bdc18ba923b71bf08a0a5298c25ed900507adec043db04ce88a547062ab8"),
            (1393392u, "b92032a897e6f6c2b1388981ac7e0d0b99cb364ebfc4b91a83519362c811e237"),
            (1489488u, "ccde600704589318fd27c817735dbf03ad606cab5cfd3b9e9b67e31e3eddace6"),
            (1601600u, "a77af7aebb74a222962d237b6801efb5a7bc68f221295877dd324462fd8bc1a7"),
            (1713712u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
            (1777776u, "5ee5e5bdbe82a6be7528675e3112a5143b127032b44bbdb0fef4322c6267986b"),
            (1905904u, "a15c749a6864035eff48cc592685ce4e69a631cc0eef95c730daaa1043eba1dd"),
            (2114112u, "e755c415eba1d77c6a3b6de6b486ae16f1a2270d794fc12a1773e18e1ff94b94"),
        ]);

    private static void TestColor(string what, ColorVectors expected, int copies)
    {
        DvdTrackGeometry geometry = ParityGeometry();
        DiffractionPicture picture = DiffractionColor.Build("color", (43.0, 0.0), (24.0, 24.0),
            Views(480, 480, 7, 3, 101), Views(480, 480, 5, 11, 97), Views(480, 480, 3, 13, 89), copies);
        Check(picture.TurnCounts.SequenceEqual(expected.Turns), $"{what}: white-balance turn counts");
        for (int k = 0; k < expected.Gains.Length; k++)
            Check(Math.Abs(picture.Gains[k] - expected.Gains[k]) < 1e-9, $"{what}: gain of carrier {k + 1}");
        for (int k = 0; k < expected.Carriers.Length; k++)
        {
            CarrierGroup g = picture.Carriers[k].Group!;
            var e = expected.Carriers[k];
            Check(g.Nominal == e.Nominal && g.Strings.Length == e.Variants, $"{what}: variants of carrier {k + 1}");
            string bytes = Convert.ToHexStringLower(SHA256.HashData(g.Strings.SelectMany(x => x.Data).ToArray()));
            byte[] map = new byte[g.Variant!.Length];
            Buffer.BlockCopy(g.Variant, 0, map, 0, map.Length);
            Check(bytes == e.BytesSha256, $"{what}: carrier strings of carrier {k + 1}");
            Check(Convert.ToHexStringLower(SHA256.HashData(map)) == e.VariantSha256, $"{what}: period compensation map of carrier {k + 1}");
        }

        DiffractionLayout layout = new([picture], geometry);
        Check(layout.Bridges.Count == 0, $"{what}: no bridges");
        byte[] block = new byte[DiffractionLayout.SectorsPerBlock * DiffractionLayout.PayloadBytes];
        int mismatches = 0;
        foreach ((uint lba, string sha) in expected.Blocks)
        {
            layout.FillBlock(lba, block);
            if (Convert.ToHexStringLower(SHA256.HashData(block)) != sha)
            {
                mismatches++;
                Console.Error.WriteLine($"diffraction parity ({what}): block {lba} differs from DiscDiffractStudio");
            }
        }

        Check(mismatches == 0, $"{what}: parity with DiscDiffractStudio ({mismatches} of {expected.Blocks.Length} blocks differ)");
    }

    /// <summary>
    /// A whole-disc colour picture: every cell is written, tuned for the light direction that suits it, so the
    /// periods depend only on the distance from the hub.
    /// </summary>
    private static void TestWideColor()
    {
        DvdTrackGeometry geometry = ParityGeometry();
        const double Cell = 0.5;
        int n = (int)Math.Round(118.0 / Cell);
        double[,] white = new double[n, n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                white[j, i] = 1.0;
        foreach (int copies in new[] { 1, 2 })
        {
            string what = $"wide colour, {copies} cop{(copies == 1 ? "y" : "ies")}";
            DiffractionPicture picture = DiffractionColor.Build("wide", (0.0, 0.0), (118.0, 118.0), white, white, white,
                copies: copies, cellMm: Cell, wide: true);
            bool allLit = true;
            for (int j = 0; j < picture.Ny; j++)
                for (int i = 0; i < picture.Nx; i++)
                    allLit &= picture.Levels[0][j, i] > 0;
            Check(allLit, $"{what}: every cell is written");
            foreach (DiffractionCarrier c in picture.Carriers)
            {
                CarrierGroup g = c.Group!;
                double At(double x, double y)
                {
                    picture.TryCell(x, y, out int j, out int i);
                    return g.Strings[g.Variant![j, i]].PeriodBits;
                }

                double d = 45.0 / Math.Sqrt(2);
                Check(At(45, 0) == At(0, 45) && At(0, 45) == At(-45, 0) && Math.Abs(Math.Log(At(-d, -d) / At(45, 0))) < 0.0051,
                    $"{what}: periods depend only on the radius");
                Check(g.Strings.All(x => x.PeriodBits is >= 6.4 - 1e-9 and <= 22.0 + 1e-9), $"{what}: periods stay writable");
            }

            DiffractionLayout layout = new([picture], geometry);
            byte[] block = new byte[DiffractionLayout.SectorsPerBlock * DiffractionLayout.PayloadBytes];
            layout.FillBlock(1_000_000u, block);
            Check(block.Any(x => x != 0), $"{what}: blocks fill");
        }

        double[,] small = new double[48, 48];
        Check(!Throws(() => DiffractionColor.Build("x", (0.0, 43.0), (24.0, 24.0), small, small, small, cellMm: Cell, lampTop: false)),
            "a colour picture at 12 o'clock is fine with the lamp to the side");
        Check(Throws(() => DiffractionColor.Build("x", (0.0, 43.0), (24.0, 24.0), small, small, small, cellMm: Cell)),
            "a colour picture at 12 o'clock is refused with the lamp above");
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void TestStringRuns()
    {
        StringCarrier s = StringCarrierTable.Get(16.0);
        Check(s.Data.Length == StringCarrier.RunBytes && StringCarrierTable.Count > 2000, "carrier string table");
        Check(StringCarrierTable.Get(16.0 * (1 + 1e-13)) == s, "carrier strings are found despite last-digit differences");
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
        DvdTrackGeometry geometry = ParityGeometry();
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

    /// <summary>DiscDiffractStudio preset "dis-user-biaoding2".</summary>
    private static DvdTrackGeometry ParityGeometry() => DvdTrackGeometry.Create(23.991531619344556, 57.96498503049657,
        2_297_888.0 * DiffractionLayout.BitsPerSector * 133.3e-6,
        -3.5014145371817896e-05, 4.087772214354072e-05, -1.5829613261263454e-05);

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
