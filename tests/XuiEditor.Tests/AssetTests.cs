using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Diagnostics;
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
                ["missing_file"] = Path.Combine(
                    Path.GetTempPath(),
                    "XuiEditor.Tests",
                    "not-present.ttf"),
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
    public async Task JapaneseEngineFontUsesUnicodeCapableSystemFallback()
    {
        using TestDirectory directory = new();
        string root = directory.File("fonts");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "basicfonts.scr"),
            "FontAlias(\"jp regular 15\", " +
            "\"dfhsgothic-w3_regular\", 15, 0, \"\", 1.0, " +
            "\"missing.dds\")");
        await File.WriteAllTextAsync(
            Path.Combine(root, "fontstyles.scr"),
            "FontStyle(\"boxed_r_10\", \"jp regular 15\", " +
            "1, 0, 0, 1)");

        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(
                root,
                XuiAssetRootKind.DyingLightProject,
                false),
        ],
            directory.File("cache"));
        await resolver.RebuildAsync();

        ResolvedFont font = resolver.ResolveFont("boxed_r_10", 15);

        Assert.AreEqual("Yu Gothic UI", font.Family);
        Assert.IsTrue(font.IsApproximation);
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
    public async Task LocalizationUsesInstallPaksAndExplicitProjectLocales()
    {
        using TestDirectory directory = new();
        string install = directory.File("Dying Light");
        string dw = Path.Combine(install, "DW");
        string extracted = directory.File("extracted");
        string project = directory.File("project");
        string extractedTexts = Path.Combine(
            extracted,
            "data",
            "maps");
        string japaneseTexts = Path.Combine(
            project,
            "Locale",
            "Jp");
        Directory.CreateDirectory(dw);
        Directory.CreateDirectory(extractedTexts);
        Directory.CreateDirectory(japaneseTexts);
        await File.WriteAllBytesAsync(
            Path.Combine(install, "DyingLightGame.exe"),
            []);
        WriteBinaryPak(
            Path.Combine(dw, "Data0.pak"),
            ("data/menu/scr/minimal.xui", Encoding.UTF8.GetBytes(
                "<XuiCanvas />")));
        WriteBinaryPak(
            Path.Combine(dw, "DataEn.pak"),
            ("data/maps/common_texts_all.bin", BuildStringCatalog(
            [
                ("Language_Test", "Game English"),
                ("Project_Test", "Game English"),
            ])));
        WriteBinaryPak(
            Path.Combine(dw, "DataJp.pak"),
            ("data/maps/common_texts_all.bin", BuildStringCatalog(
            [
                ("Language_Test", "Game Japanese"),
                ("Project_Test", "Game Japanese"),
            ])));
        await File.WriteAllBytesAsync(
            Path.Combine(extractedTexts, "common_texts_all.bin"),
            BuildStringCatalog(
            [
                ("Language_Test", "Extracted English"),
                ("Project_Test", "Extracted English"),
            ]));
        await File.WriteAllTextAsync(
            Path.Combine(japaneseTexts, "project_texts_all.scr"),
            """
            !String(s, s)
            String("Project_Test", "Project Japanese")
            """);
        DyingLightInstallIndex japaneseIndex = new(
            new DyingLightInstallProfile(install, "Jp"));
        DyingLightInstallIndex englishIndex = new(
            new DyingLightInstallProfile(install, "En"));
        await japaneseIndex.RebuildAsync();
        await englishIndex.RebuildAsync();
        XuiAssetRoot[] roots =
        [
            new(
                project,
                XuiAssetRootKind.DyingLightProject,
                false),
            new(
                extracted,
                XuiAssetRootKind.ExtractedDyingLight,
                true),
        ];
        DyingLightAssetResolver japanese = new(
            roots,
            directory.File("cache-jp"),
            sources: [japaneseIndex],
            locale: "Jp");
        DyingLightAssetResolver english = new(
            roots,
            directory.File("cache-en"),
            sources: [englishIndex],
            locale: "En");

        await japanese.RebuildAsync();
        await english.RebuildAsync();

        Assert.AreEqual("Jp", japanese.Localization?.Locale);
        Assert.AreEqual(
            "Game Japanese",
            japanese.ResolveText("Language_Test"));
        Assert.AreEqual(
            "Project Japanese",
            japanese.ResolveText("Project_Test"));
        Assert.AreEqual("En", english.Localization?.Locale);
        Assert.AreEqual(
            "Game English",
            english.ResolveText("Language_Test"));
        Assert.AreEqual(
            "Game English",
            english.ResolveText("Project_Test"));
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
    public async Task ResolverMapsLogicalDefinitionSpaceToScaledPhysicalDds()
    {
        using TestDirectory directory = new();
        string root = directory.File("workspace");
        string cache = directory.File("cache");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "textures.def"),
            """
            Texture("scaled.dds",2,2)
            {
                Whole("whole")
                Rect("right_half",1,0,2,2)
            }
            """);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "scaled.dds"),
            CreateSizedUncompressedDds(
                4,
                4,
                static (x, y) => (byte)((y * 16) + x)));
        XuiAssetRoot assetRoot = new(
            root,
            XuiAssetRootKind.Workspace,
            false);
        DyingLightAssetResolver coldResolver = new([assetRoot], cache);

        await coldResolver.RebuildAsync();
        ResolvedTexture? whole =
            await coldResolver.ResolveTextureAsync("whole");
        ResolvedTexture? right =
            await coldResolver.ResolveTextureAsync("right_half");

        Assert.IsNotNull(whole);
        Assert.AreEqual(4, whole.Width);
        Assert.AreEqual(4, whole.Height);
        Assert.AreEqual(new XuiVector2(2, 2), whole.LogicalSize);
        Assert.AreEqual(new XuiVector2(2, 2), whole.DefinitionToPhysicalScale);
        Assert.AreEqual(new XuiRect(0, 0, 4, 4), whole.PhysicalSourceRectangle);
        Assert.AreEqual(0, whole.BgraPixels[0]);
        Assert.AreEqual(51, whole.BgraPixels[^4]);

        Assert.IsNotNull(right);
        Assert.AreEqual(2, right.Width);
        Assert.AreEqual(4, right.Height);
        Assert.AreEqual(new XuiVector2(1, 2), right.LogicalSize);
        Assert.AreEqual(new XuiRect(2, 0, 2, 4), right.PhysicalSourceRectangle);
        Assert.AreEqual(2, right.BgraPixels[0]);
        Assert.AreEqual(51, right.BgraPixels[^4]);

        DyingLightAssetResolver warmResolver = new([assetRoot], cache);
        await warmResolver.RebuildAsync();
        ResolvedTexture? cached =
            await warmResolver.ResolveTextureAsync("right_half");

        Assert.IsNotNull(cached);
        Assert.AreEqual(right.Width, cached.Width);
        Assert.AreEqual(right.Height, cached.Height);
        Assert.AreEqual(right.LogicalSize, cached.LogicalSize);
        Assert.AreEqual(
            right.DefinitionToPhysicalScale,
            cached.DefinitionToPhysicalScale);
        Assert.AreEqual(
            right.PhysicalSourceRectangle,
            cached.PhysicalSourceRectangle);
        CollectionAssert.AreEqual(right.BgraPixels, cached.BgraPixels);
    }

    [TestMethod]
    public async Task ResolverSupportsIndependentDefinitionScaleAndClampsLogicalBounds()
    {
        using TestDirectory directory = new();
        string root = directory.File("workspace");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "textures.def"),
            """
            Texture("scaled.dds",3,2)
            {
                Rect("middle",1,0,2,1)
                Rect("clipped",-1,-1,2,1)
            }
            """);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "scaled.dds"),
            CreateSizedUncompressedDds(
                6,
                8,
                static (x, y) => (byte)((y * 8) + x)));
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));

        await resolver.RebuildAsync();
        ResolvedTexture? middle =
            await resolver.ResolveTextureAsync("middle");
        ResolvedTexture? clipped =
            await resolver.ResolveTextureAsync("clipped");

        Assert.IsNotNull(middle);
        Assert.AreEqual(new XuiVector2(2, 4), middle.DefinitionToPhysicalScale);
        Assert.AreEqual(new XuiRect(2, 0, 2, 4), middle.PhysicalSourceRectangle);
        Assert.AreEqual(new XuiVector2(1, 1), middle.LogicalSize);
        Assert.AreEqual(2, middle.BgraPixels[0]);
        Assert.AreEqual(27, middle.BgraPixels[^4]);

        Assert.IsNotNull(clipped);
        Assert.AreEqual(new XuiRect(0, 0, 4, 4), clipped.PhysicalSourceRectangle);
        Assert.AreEqual(new XuiVector2(2, 1), clipped.LogicalSize);
        XuiDiagnostic diagnostic = clipped.Diagnostics.Single(static item =>
            item.Code == "XUI-ASSET007");
        StringAssert.Contains(diagnostic.Message, "declared 3x2");
        StringAssert.Contains(diagnostic.Message, "physical DDS bounds 0,0,4,4");
    }

    [TestMethod]
    public async Task ScaledNineSliceAndTilePartsKeepLogicalLayoutSizes()
    {
        using TestDirectory directory = new();
        string root = directory.File("workspace");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "textures.def"),
            """
            Texture("scaled.dds",4,4)
            {
                RectWithCorner("panel",0,0,4,4,1,1)
                Tileset("frame")
                {
                    Rect("top",1,0,3,1)
                    Top("top",100,1)
                }
            }
            """);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "scaled.dds"),
            CreateSizedUncompressedDds(
                8,
                8,
                static (x, y) => (byte)((y * 8) + x)));
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));

        await resolver.RebuildAsync();
        ResolvedTexture? panel =
            await resolver.ResolveTextureAsync("panel");
        ResolvedTexture? frame =
            await resolver.ResolveTextureAsync("frame");

        Assert.IsNotNull(panel);
        Assert.AreEqual(8, panel.Width);
        Assert.AreEqual(8, panel.Height);
        Assert.AreEqual(new XuiVector2(4, 4), panel.LogicalSize);
        Assert.AreEqual(new XuiVector2(2, 2), panel.DefinitionToPhysicalScale);

        Assert.IsNotNull(frame);
        ResolvedTileTexturePart top = frame.TileParts.Single();
        Assert.AreEqual(2, top.Width);
        Assert.AreEqual(4, top.Height);
        Assert.AreEqual(new XuiVector2(1, 2), top.LogicalSize);
        Assert.AreEqual(new XuiVector2(1, 2), frame.LogicalSize);
    }

    [TestMethod]
    public void DocumentAssetContextDiscoversWorkshopDataAndPakAssetsRoots()
    {
        using TestDirectory directory = new();
        string workshopData = directory.File(
            Path.Combine("Workshop", "Project", "data"));
        string workshopXui = Path.Combine(
            workshopData,
            "menu",
            "hud",
            "custom.xui");
        Directory.CreateDirectory(Path.GetDirectoryName(workshopXui)!);
        File.WriteAllText(workshopXui, "<XuiCanvas />");
        string pakAssets = directory.File("PakAssets");
        string loaderXui = Path.Combine(
            pakAssets,
            "XUI",
            "MenuLoader.xui");
        Directory.CreateDirectory(Path.GetDirectoryName(loaderXui)!);
        File.WriteAllText(loaderXui, "<XuiCanvas />");

        XuiDocumentAssetContext workshop =
            XuiDocumentAssetContext.Discover(workshopXui);
        XuiDocumentAssetContext loader =
            XuiDocumentAssetContext.Discover(loaderXui);

        Assert.AreEqual(
            Path.GetFullPath(workshopData),
            workshop.Root.FullPath);
        Assert.AreEqual(
            Path.GetFullPath(pakAssets),
            loader.Root.FullPath);
    }

    private static byte[] BuildStringCatalog(
        IReadOnlyList<(string Key, string Value)> entries)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(
            stream,
            Encoding.UTF8,
            leaveOpen: true);
        writer.Write(1);
        writer.Write(entries.Count);
        foreach ((string key, string value) in entries)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            writer.Write((ushort)keyBytes.Length);
            writer.Write(keyBytes);
            writer.Write((ushort)value.Length);
            writer.Write(Encoding.Unicode.GetBytes(value));
        }

        return stream.ToArray();
    }

    private static void WriteBinaryPak(
        string path,
        params (string Path, byte[] Content)[] entries)
    {
        using ZipArchive archive = ZipFile.Open(
            path,
            ZipArchiveMode.Create);
        foreach ((string entryPath, byte[] content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryPath);
            using Stream stream = entry.Open();
            stream.Write(content);
        }
    }

    [TestMethod]
    public async Task ProjectTextureDefinitionsAndDdsOverrideInstallWithProvenance()
    {
        using TestDirectory directory = new();
        string projectData = directory.File(
            Path.Combine("Workshop", "CustomProject", "data"));
        string definitionDirectory = Path.Combine(
            projectData,
            "menu",
            "texturedefs");
        string firstTextureDirectory = Path.Combine(
            projectData,
            "menu",
            "hud",
            "a");
        string secondTextureDirectory = Path.Combine(
            projectData,
            "menu",
            "hud",
            "b");
        string install = directory.File("install-data");
        Directory.CreateDirectory(definitionDirectory);
        Directory.CreateDirectory(firstTextureDirectory);
        Directory.CreateDirectory(secondTextureDirectory);
        Directory.CreateDirectory(install);
        string projectDefinition = Path.Combine(
            definitionDirectory,
            "custom.def");
        await File.WriteAllTextAsync(
            projectDefinition,
            """
            Texture("shared.dds",4,4)
            {
                Whole("project_texture")
            }
            Texture("fallback.dds",4,4)
            {
                Whole("fallback_texture")
            }
            Texture("missing.dds",4,4)
            {
                Whole("missing_texture")
            }
            """);
        await File.WriteAllBytesAsync(
            Path.Combine(firstTextureDirectory, "shared.dds"),
            CreateUncompressedDds(30));
        await File.WriteAllBytesAsync(
            Path.Combine(secondTextureDirectory, "shared.dds"),
            CreateUncompressedDds(70));
        await File.WriteAllTextAsync(
            Path.Combine(install, "installed.def"),
            """
            Texture("shared.dds",4,4)
            {
                Whole("project_texture")
            }
            """);
        await File.WriteAllBytesAsync(
            Path.Combine(install, "shared.dds"),
            CreateUncompressedDds(110));
        await File.WriteAllBytesAsync(
            Path.Combine(install, "fallback.dds"),
            CreateUncompressedDds(140));
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(
                projectData,
                XuiAssetRootKind.Workspace,
                false),
            new XuiAssetRoot(
                install,
                XuiAssetRootKind.DyingLightInstall,
                true),
        ],
            directory.File("cache"));

        await resolver.RebuildAsync();
        XuiTextureRegion? definition =
            resolver.ResolveTextureDefinition("project_texture");
        ResolvedTexture? project =
            await resolver.ResolveTextureAsync("project_texture");
        ResolvedTexture? fallback =
            await resolver.ResolveTextureAsync("fallback_texture");
        ResolvedTexture? missing =
            await resolver.ResolveTextureAsync("missing_texture");

        Assert.IsNotNull(definition);
        Assert.IsNotNull(definition.DefinitionRoot);
        Assert.AreEqual(
            Path.GetFullPath(projectData),
            definition.DefinitionRoot.FullPath);
        Assert.AreEqual(
            Path.Combine("menu", "texturedefs", "custom.def"),
            definition.DefinitionRelativePath);
        Assert.IsNotNull(project);
        Assert.AreEqual(30, project.BgraPixels[0]);
        Assert.AreEqual(
            Path.Combine(firstTextureDirectory, "shared.dds"),
            project.SourcePath);
        Assert.HasCount(
            1,
            project.Diagnostics.Where(static diagnostic =>
                diagnostic.Code == "XUI-ASSET013"));
        StringAssert.Contains(
            project.Diagnostics.Single(static diagnostic =>
                diagnostic.Code == "XUI-ASSET013").Message,
            "matched 2 files");
        Assert.IsNotNull(fallback);
        Assert.AreEqual(140, fallback.BgraPixels[0]);
        Assert.AreEqual(
            Path.Combine(install, "fallback.dds"),
            fallback.SourcePath);
        Assert.IsNotNull(missing);
        Assert.IsTrue(missing.IsApproximation);
        string missingMessage = missing.Diagnostics.Single(static diagnostic =>
            diagnostic.Code == "XUI-ASSET005").Message;
        StringAssert.Contains(missingMessage, projectDefinition);
        StringAssert.Contains(missingMessage, Path.GetFullPath(projectData));
        StringAssert.Contains(missingMessage, Path.GetFullPath(install));
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
    public async Task UncompressedBgrxDdsDecodesAsOpaqueBgra()
    {
        using TestDirectory directory = new();
        string root = directory.File("workspace");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "textures.def"),
            "Texture(\"bgrx.dds\",4,4)\n{\nWhole(\"bgrx\")\n}\n");
        await File.WriteAllBytesAsync(
            Path.Combine(root, "bgrx.dds"),
            CreateUncompressedDds(25, includeAlphaMask: false));
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));

        await resolver.RebuildAsync();
        ResolvedTexture? texture = await resolver.ResolveTextureAsync("bgrx");

        Assert.IsNotNull(texture);
        CollectionAssert.AreEqual(
            new byte[] { 25, 40, 200, 255 },
            texture.BgraPixels[..4]);
        Assert.IsFalse(texture.IsApproximation);
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

    private static byte[] CreateUncompressedDds(
        byte blueOffset = 0,
        bool includeAlphaMask = true)
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
        BinaryPrimitives.WriteUInt32LittleEndian(
            header[100..],
            includeAlphaMask ? 0xff000000 : 0);
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

    private static byte[] CreateSizedUncompressedDds(
        int width,
        int height,
        Func<int, int, byte> blue)
    {
        byte[] result = new byte[checked(128 + (width * height * 4))];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(result, 0);
        Span<byte> header = result.AsSpan(4, 124);
        BinaryPrimitives.WriteInt32LittleEndian(header, 124);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 0x0000100f);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], height);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], width * 4);
        BinaryPrimitives.WriteInt32LittleEndian(header[72..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header[76..], 0x41);
        BinaryPrimitives.WriteInt32LittleEndian(header[84..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header[88..], 0x00ff0000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[92..], 0x0000ff00);
        BinaryPrimitives.WriteUInt32LittleEndian(header[96..], 0x000000ff);
        BinaryPrimitives.WriteUInt32LittleEndian(header[100..], 0xff000000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[104..], 0x1000);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = 128 + (((y * width) + x) * 4);
                result[offset] = blue(x, y);
                result[offset + 1] = 40;
                result[offset + 2] = 200;
                result[offset + 3] = 255;
            }
        }

        return result;
    }
}
