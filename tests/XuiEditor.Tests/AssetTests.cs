using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Values;

namespace XuiEditor.Tests;

[TestClass]
public sealed class AssetTests
{
    [TestMethod]
    public void TextureDefinitionParserCoversWholeRectNineSliceAtlasAndTiles()
    {
        string source =
            "Atlas(2048,2048,2,\"DXT5\",\"BigFirst\",\"hash\")\n" +
            "Texture(\"atlas.dds\", 64, 32)\n{\n" +
            "Whole(\"whole\")\n" +
            "Rect(\"plain\", 1, 2, 11, 12)\n" +
            "RectWithCorner(\"nine\", 3, 4, 23, 24, 5, 6)\n" +
            "Tileset(\"frame\")\n{\n" +
            "Rect(\"middle_rect\", 10, 10, 20, 20)\n" +
            "CornerTopLeft(\"plain\",100,0)\n" +
            "Top(\"plain\",90,0)\n" +
            "Middle(\"middle_rect\",100,0)\n" +
            "}\n}\n";

        TextureDefinitionParseResult parsed =
            TextureDefinitionParser.Parse(source, "test.def");

        Assert.IsFalse(parsed.Diagnostics.Any());
        XuiTextureRegion whole = parsed.Regions.Single(static region =>
            region.Name == "whole");
        Assert.AreEqual(new XuiRect(0, 0, 64, 32), whole.SourceRectangle);
        XuiTextureRegion rectangle = parsed.Regions.Single(static region =>
            region.Name == "plain");
        Assert.AreEqual(new XuiRect(1, 2, 10, 10), rectangle.SourceRectangle);
        XuiTextureRegion nine = parsed.Regions.Single(static region =>
            region.Name == "nine");
        Assert.AreEqual(XuiTexturePrimitive.RectangleWithCorner, nine.Primitive);
        Assert.AreEqual(new XuiVector2(5, 6), nine.CornerSize);
        XuiTextureRegion tileSet = parsed.Regions.Single(static region =>
            region.Name == "frame");
        Assert.AreEqual(3, tileSet.TileParts.Count);
        Assert.AreEqual(XuiTileRole.Middle, tileSet.TileParts[^1].Role);
    }

    [TestMethod]
    public void FontDefinitionsAndStylesResolveWithExplicitApproximation()
    {
        FontDefinitionParseResult parsed = FontDefinitionParser.Parse(
        [
            (
                "basicfonts.scr",
                "FontAlias(\"boxed regular 15\", \"boxed_regular\", 15, 0, \"\", 1.0, \"font.dds\")"),
            (
                "fontstyles.scr",
                "Scaling(0.66667)\nFontStyle(\"boxed_r_15\", \"boxed regular 15\", 1, 1, 0, 1)"),
        ]);

        Assert.AreEqual(1, parsed.Fonts.Count);
        Assert.AreEqual(1, parsed.Styles.Count);
        Assert.AreEqual("boxed regular 15", parsed.Styles[0].EngineFontId);
    }

    [TestMethod]
    public void ResolverUsesConfiguredFontMappingsAndRejectsMissingFontFiles()
    {
        DyingLightAssetResolver resolver = new(
            [],
            fontMappings: new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["engine_heading"] = "Segoe UI",
                ["missing_file"] = @"Z:\fonts\not-present.ttf",
            });

        ResolvedFont installed = resolver.ResolveFont("ENGINE_HEADING", 19);
        ResolvedFont missing = resolver.ResolveFont("missing_file", 19);

        Assert.AreEqual("Segoe UI", installed.Family);
        Assert.IsFalse(installed.IsApproximation);
        Assert.IsTrue(missing.IsApproximation);
        Assert.IsTrue(missing.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-FONT002"));
    }

    [TestMethod]
    public async Task BitmapMetricsDriveTextMeasurementAndAutoSize()
    {
        using TestDirectory directory = new();
        string root = directory.File("fonts");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "basicfonts.scr"),
            "FontAlias(\"boxed regular\", \"boxed_regular\", 16, 0, \"\", 1.0, \"boxed_regular_16.dds\")");
        await File.WriteAllTextAsync(
            Path.Combine(root, "fontstyles.scr"),
            "Scaling(1.0)\nFontStyle(\"boxed_r_16\", \"boxed regular\", 1, 0, 0, 1)");
        await File.WriteAllTextAsync(
            Path.Combine(root, "boxed_regular_16.fm"),
            """
            Name("Boxed regular")
            MapWidth(64)
            MapHeight(64)
            FontHeight(16)
            Char(63,8,0,0,8,16,0)
            Char(65,8,8,0,16,16,0)
            """);
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));
        await resolver.RebuildAsync();

        XuiTextMeasurement measured = resolver.MeasureText(
            "boxed_r_16",
            "AA",
            requestedSize: 0,
            maximumWidth: 0,
            multiline: false,
            uppercase: false);
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<AdvGroup><Properties><Id>Parent</Id><Width>100</Width><Height>100</Height>" +
            "</Properties><MyText><Properties><Id>Auto</Id><Width>1</Width><Height>1</Height>" +
            "<Position>2,3,0</Position>" +
            "<Text>AA</Text><Font>boxed_r_16</Font><AutoSizeToText>true</AutoSizeToText>" +
            "<AutoSizeParentToText>true</AutoSizeParentToText>" +
            "</Properties></MyText></AdvGroup></XuiCanvas>");
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
                document,
                new XuiViewport(100, 100),
                0,
                resolver);
        XuiRenderNode node = frame.Nodes.Single(static candidate =>
            candidate.Id == "Auto");
        XuiRenderNode parent = frame.Nodes.Single(static candidate =>
            candidate.Id == "Parent");

        Assert.IsTrue(measured.IsExact);
        Assert.AreEqual(16, measured.Width, 0.001);
        Assert.AreEqual(16, measured.Height, 0.001);
        Assert.AreEqual(measured.Width, node.Size.X, 0.001);
        Assert.AreEqual(measured.Height, node.Size.Y, 0.001);
        Assert.AreEqual(18, parent.Size.X, 0.001);
        Assert.AreEqual(19, parent.Size.Y, 0.001);
    }

    [TestMethod]
    public void ExtractedRootsAreAlwaysEffectivelyReadOnly()
    {
        XuiAssetRoot extracted = new(
            Environment.CurrentDirectory,
            XuiAssetRootKind.ExtractedDyingLight,
            false);
        XuiAssetRoot loose = new(
            Environment.CurrentDirectory,
            XuiAssetRootKind.LooseMod,
            false);

        Assert.IsTrue(extracted.EffectiveIsReadOnly);
        Assert.IsFalse(loose.EffectiveIsReadOnly);
    }

    [TestMethod]
    public async Task ResolverLoadsLooseModLocalizationSources()
    {
        using TestDirectory directory = new();
        string root = directory.File("PakAssets");
        string localeDirectory = Path.Combine(root, "Locale", "En");
        Directory.CreateDirectory(localeDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(localeDirectory, "loader_texts_all.scr"),
            """
            !String(s, s)
            // Mod-owned strings are data, not executable scripts.
            String("DLW_Common_Browse", "Browse workshop")
            String("DLW_Quoted", "A \"quoted\" value")
            """);
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"),
            locale: "En");

        await resolver.RebuildAsync();

        Assert.IsNotNull(resolver.Localization);
        Assert.AreEqual(
            "Browse workshop",
            resolver.ResolveText("&DLW_Common_Browse&"));
        Assert.AreEqual(
            "A \"quoted\" value",
            resolver.ResolveText("&DLW_Quoted&"));
        Assert.IsFalse(resolver.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-LOC004"));
    }

    [TestMethod]
    public async Task ResolverHonorsRootPrecedenceAndDecodesDdsCrop()
    {
        using TestDirectory directory = new();
        string workspace = directory.File("workspace");
        string extracted = directory.File("extracted");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(extracted);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(extracted, "screen.xui"),
            "<XuiCanvas />");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(workspace, "screen.xui"),
            "<XuiCanvas><Properties /></XuiCanvas>");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(workspace, "textures.def"),
            "Texture(\"test.dds\",4,4)\n{\nRect(\"crop\",1,1,3,3)\n}\n");
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(workspace, "test.dds"),
            CreateUncompressedDds());
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(workspace, XuiAssetRootKind.Workspace, false),
            new XuiAssetRoot(extracted, XuiAssetRootKind.ExtractedDyingLight, true),
        ],
            directory.File("cache"));

        await resolver.RebuildAsync();
        XuiResolvedFile? file = resolver.ResolveFile("screen.xui");
        ResolvedTexture? texture = await resolver.ResolveTextureAsync("crop");

        Assert.IsNotNull(file);
        Assert.AreEqual(
            System.IO.Path.Combine(workspace, "screen.xui"),
            file.Path);
        Assert.IsNotNull(texture);
        Assert.AreEqual(2, texture.Width);
        Assert.AreEqual(2, texture.Height);
        Assert.AreEqual(16, texture.BgraPixels.Length);
        Assert.IsTrue(texture.ContentHash.Length == 64);
    }

    [TestMethod]
    public async Task ResolverComposesAllTilesetRolesAndAppliesRotationModes()
    {
        using TestDirectory directory = new();
        string root = directory.File("workspace");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(root, "textures.def"),
            """
            Texture("test.dds",4,4)
            {
                Tileset("frame")
                {
                    Rect("tl",0,0,1,1)
                    Rect("top",1,0,3,1)
                    Rect("tr",3,0,4,1)
                    Rect("left",0,1,1,2)
                    Rect("middle",1,1,2,2)
                    Rect("right",3,1,4,2)
                    Rect("bl",0,3,1,4)
                    Rect("bottom",1,3,2,4)
                    Rect("br",3,3,4,4)
                    CornerTopLeft("tl",100,0)
                    Top("top",100,1)
                    CornerTopRight("tr",100,0)
                    Left("left",100,0)
                    Middle("middle",100,0)
                    Right("right",100,0)
                    CornerBottomLeft("bl",100,0)
                    Bottom("bottom",100,0)
                    CornerBottomRight("br",100,0)
                }
            }
            """);
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(root, "test.dds"),
            CreateUncompressedDds());
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));

        await resolver.RebuildAsync();
        ResolvedTexture? texture = await resolver.ResolveTextureAsync("frame");

        Assert.IsNotNull(texture);
        Assert.HasCount(9, texture.TileParts);
        Assert.IsFalse(texture.IsApproximation);
        ResolvedTileTexturePart top = texture.TileParts.Single(static part =>
            part.Role == XuiTileRole.Top);
        Assert.AreEqual(1, top.Width);
        Assert.AreEqual(2, top.Height);
        Assert.AreEqual(1 + 1 + 1, texture.Width);
        Assert.AreEqual(1 + 2 + 1, texture.Height);
    }

    [TestMethod]
    public async Task TilesetVariantSelectionIsDeterministicAndExplicit()
    {
        using TestDirectory directory = new();
        string root = directory.File("workspace");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(root, "textures.def"),
            """
            Texture("test.dds",4,4)
            {
                Tileset("frame")
                {
                    Rect("low",0,0,1,1)
                    Rect("high",1,0,2,1)
                    Top("low",20,0)
                    Top("high",80,0)
                }
            }
            """);
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(root, "test.dds"),
            CreateUncompressedDds());
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));

        await resolver.RebuildAsync();
        ResolvedTexture? texture = await resolver.ResolveTextureAsync("frame");

        Assert.IsNotNull(texture);
        Assert.HasCount(1, texture.TileParts);
        Assert.AreEqual("high", texture.TileParts[0].RegionName);
        Assert.IsTrue(texture.IsApproximation);
        Assert.IsTrue(texture.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-ASSET004"));
    }

    [TestMethod]
    public async Task TextureCacheInvalidatesWhenSourceBytesChange()
    {
        using TestDirectory directory = new();
        string root = directory.File("workspace");
        Directory.CreateDirectory(root);
        string ddsPath = System.IO.Path.Combine(root, "test.dds");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(root, "textures.def"),
            "Texture(\"test.dds\",4,4)\n{\nWhole(\"whole\")\n}\n");
        await File.WriteAllBytesAsync(ddsPath, CreateUncompressedDds(0));
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));

        await resolver.RebuildAsync();
        ResolvedTexture? before = await resolver.ResolveTextureAsync("whole");
        await File.WriteAllBytesAsync(ddsPath, CreateUncompressedDds(90));
        await resolver.RebuildAsync();
        ResolvedTexture? after = await resolver.ResolveTextureAsync("whole");

        Assert.IsNotNull(before);
        Assert.IsNotNull(after);
        Assert.AreNotEqual(before.ContentHash, after.ContentHash);
        CollectionAssert.AreNotEqual(before.BgraPixels, after.BgraPixels);
    }

    [TestMethod]
    public async Task CorruptDdsFailsToABoundedPlaceholderWithDiagnostic()
    {
        using TestDirectory directory = new();
        string root = directory.File("workspace");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(root, "textures.def"),
            "Texture(\"bad.dds\",4096,2048)\n{\nWhole(\"bad\")\n}\n");
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(root, "bad.dds"),
            [0x44, 0x44, 0x53, 0x20, 0x00]);
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));

        await resolver.RebuildAsync();
        ResolvedTexture? texture = await resolver.ResolveTextureAsync("bad");

        Assert.IsNotNull(texture);
        Assert.AreEqual(512, texture.Width);
        Assert.AreEqual(512, texture.Height);
        Assert.AreEqual(512 * 512 * 4, texture.BgraPixels.Length);
        Assert.IsTrue(texture.IsApproximation);
        Assert.IsTrue(texture.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-ASSET012"));
    }

    [TestMethod]
    public void ArgbParserHandlesCanonicalAndObservedShortHex()
    {
        Assert.IsTrue(XuiValueParser.TryColor("0xff123456", out XuiColor canonical));
        Assert.AreEqual(0xff123456u, canonical.Argb);
        Assert.IsTrue(XuiValueParser.TryColor("0xcfd6800", out XuiColor shortValue));
        Assert.AreEqual(0x0cfd6800u, shortValue.Argb);
    }

    private static byte[] CreateUncompressedDds(byte blueOffset = 0)
    {
        byte[] result = new byte[128 + (4 * 4 * 4)];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(result, 0);
        Span<byte> header = result.AsSpan(4, 124);
        BinaryPrimitives.WriteInt32LittleEndian(header, 124);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 0x0000100f);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], 4);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], 4);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt32LittleEndian(header[72..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header[76..], 0x41);
        BinaryPrimitives.WriteInt32LittleEndian(header[84..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header[88..], 0x00ff0000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[92..], 0x0000ff00);
        BinaryPrimitives.WriteUInt32LittleEndian(header[96..], 0x000000ff);
        BinaryPrimitives.WriteUInt32LittleEndian(header[100..], 0xff000000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[104..], 0x1000);
        for (int index = 128; index < result.Length; index += 4)
        {
            result[index] = unchecked((byte)(blueOffset + index - 128));
            result[index + 1] = 40;
            result[index + 2] = 200;
            result[index + 3] = 255;
        }

        return result;
    }
}
