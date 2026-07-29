using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Editing;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Navigation;
using XuiEditor.Core.Schema;
using XuiEditor.Core.Values;

namespace XuiEditor.Tests;

[TestClass]
public sealed class Chrome6SemanticTests
{
    [TestMethod]
    public void EmbeddedCatalogCoversStockCorpusAndTimelineVocabulary()
    {
        XuiClassCatalog catalog = XuiClassCatalog.Default;

        Assert.IsTrue(catalog.Properties.Count >= 182);
        Assert.IsTrue(catalog.Classes.Count >= 349);
        Assert.AreEqual(21, catalog.TimelinePropertyNames.Count);
        CollectionAssert.Contains(
            catalog.TimelinePropertyNames.ToArray(),
            "Text");
        CollectionAssert.Contains(
            catalog.TimelinePropertyNames.ToArray(),
            "Play");

        XuiPropertyDefinition pivot =
            catalog.FindProperty("Pivot")!;
        Assert.AreEqual(XuiPropertyType.Vector3, pivot.Type);
        Assert.AreEqual(
            XuiEvidenceLevel.DyingLightBinary,
            pivot.Evidence);
        Assert.AreEqual(XuiPreviewSupport.Exact, pivot.PreviewSupport);

        XuiPropertyDefinition maskMaterial =
            catalog.FindProperty("AARectangleMaskMaterial")!;
        Assert.IsFalse(maskMaterial.IsAnimatable);
        CollectionAssert.Contains(maskMaterial.Flags.ToArray(), "noanim");
    }

    [TestMethod]
    public void CatalogResolvesInheritedPropertiesAndGhostDefaults()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<IUIProgressText><Properties><Id>T</Id><Text>hello</Text>" +
            "</Properties></IUIProgressText></XuiCanvas>");
        XuiSyntaxNode node = XuiModelReader.VisualDescendants(document.Root)
            .Single(candidate => candidate.Name == "IUIProgressText");

        XuiResolvedClassDefinition resolved =
            XuiClassCatalog.Default.ResolveClass(node, document.Text);
        Assert.AreEqual("IUIProgressText", resolved.Class.Name);
        Assert.IsTrue(resolved.Inheritance.Count >= 2);
        Assert.IsTrue(resolved.Properties.Any(property =>
            property.Name == "ColorControlSequenceEnabled"));

        XuiCatalogPropertySelection opacity =
            XuiClassCatalog.Default
                .SelectProperties([node], document.Text, includeAdvanced: false)
                .Single(property => property.Definition.Name == "Opacity");
        Assert.IsFalse(opacity.IsAuthored);
        Assert.AreEqual("1", opacity.EffectiveValue);
        Assert.IsNull(XuiModelReader.GetPropertyValue(
            node,
            document.Text,
            "Opacity"));
    }

    [TestMethod]
    public void TextStyleKnownMutationsPreserveEveryOtherSixteenBitFlag()
    {
        XuiKnownTextStyle[] flags =
        [
            XuiKnownTextStyle.Italic,
            XuiKnownTextStyle.Bold,
            XuiKnownTextStyle.Underline,
            XuiKnownTextStyle.VerticalMiddle,
        ];
        for (int raw = 0; raw <= ushort.MaxValue; raw++)
        {
            int unknown = XuiTextStyleCodec.Decode(raw).UnmappedBits;
            foreach (XuiKnownTextStyle flag in flags)
            {
                int enabled = XuiTextStyleCodec.SetFlag(raw, flag, true);
                int disabled = XuiTextStyleCodec.SetFlag(raw, flag, false);
                Assert.AreEqual(
                    unknown,
                    XuiTextStyleCodec.Decode(enabled).UnmappedBits);
                Assert.AreEqual(
                    unknown,
                    XuiTextStyleCodec.Decode(disabled).UnmappedBits);
            }

            int centered = XuiTextStyleCodec.SetHorizontalAlignment(
                raw,
                XuiTextHorizontalStyle.Center);
            Assert.AreEqual(
                unknown,
                XuiTextStyleCodec.Decode(centered).UnmappedBits);
        }
    }

    [TestMethod]
    public void TextStyleParsesDecimalAndHexAndKeepsCompatibilityBits()
    {
        Assert.IsTrue(XuiTextStyleCodec.TryParse("0x540F", out var style));
        Assert.IsTrue(style.Bold);
        Assert.IsTrue(style.Italic);
        Assert.IsTrue(style.Underline);
        Assert.AreEqual(
            XuiTextHorizontalStyle.Center,
            style.HorizontalAlignment);
        Assert.AreEqual(0x4001, style.UnmappedBits);

        int left = XuiTextStyleCodec.SetHorizontalAlignment(
            style.RawValue,
            XuiTextHorizontalStyle.Left);
        Assert.AreEqual(0x4001, XuiTextStyleCodec.Decode(left).UnmappedBits);
        Assert.AreEqual("21519", XuiTextStyleCodec.ToDecimalString(style.RawValue));
        Assert.AreEqual("0x0000540F", XuiTextStyleCodec.ToHexString(style.RawValue));
    }

    [TestMethod]
    public void StandaloneTextPropertiesOverrideLegacyStylePreview()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<MyText><Properties><Id>T</Id><Width>90</Width><Height>30</Height>" +
            "<TextStyle>5388</TextStyle><Bold>false</Bold><Italic>false</Italic>" +
            "<Underline>false</Underline><HorizontalAlign>right</HorizontalAlign>" +
            "<VerticalAlign>bottom</VerticalAlign></Properties></MyText></XuiCanvas>");

        XuiRenderNode node = DyingLightLayoutEngine
            .Evaluate(document, XuiViewport.Default, 0)
            .Nodes
            .Single(candidate => candidate.Id == "T");
        Assert.IsFalse(node.Bold);
        Assert.IsFalse(node.Italic);
        Assert.IsFalse(node.Underline);
        Assert.AreEqual(
            XuiTextHorizontalAlignment.Right,
            node.HorizontalTextAlignment);
        Assert.AreEqual(
            XuiTextVerticalAlignment.Bottom,
            node.VerticalTextAlignment);
    }

    [TestMethod]
    public void PivotPresetsPreserveZAndNeverClampAuthoredValues()
    {
        XuiVector3 center = XuiPivotEditing.ApplyPreset(
            XuiPivotPreset.Center,
            new XuiVector2(101.5, 33.25),
            -7.75);
        Assert.AreEqual(50.75, center.X, 0.000001);
        Assert.AreEqual(16.625, center.Y, 0.000001);
        Assert.AreEqual(-7.75, center.Z, 0.000001);

        XuiVector3 position = XuiPivotEditing.CompensatePosition(
            new XuiVector3(-10.5, 200.25, 9),
            new XuiVector3(-25.5, 500, 2),
            new XuiVector3(150.25, -70.5, 8),
            new XuiVector3(1.5, 0.25, 1),
            37.5);
        Assert.IsTrue(double.IsFinite(position.X));
        Assert.IsTrue(double.IsFinite(position.Y));
        Assert.AreEqual(9, position.Z);
    }

    [TestMethod]
    public void AnimatedScaleOrRotationDisablesConstantPivotCompensation()
    {
        XuiTrack pivot = new(
            XuiTimelineProperty.Pivot,
            0,
            []);
        XuiTrack scale = new(
            XuiTimelineProperty.Scale,
            0,
            []);
        Assert.IsTrue(XuiPivotEditing.CanPreserveVisualPosition(
            [new XuiTimeline("T", "root", [pivot], null!)],
            "T"));
        Assert.IsFalse(XuiPivotEditing.CanPreserveVisualPosition(
            [new XuiTimeline("T", "root", [scale], null!)],
            "T"));
    }

    [TestMethod]
    public void NavigationResolvesDirectRelativeMissingAndAmbiguousPaths()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<AdvGroup><Properties><Id>A</Id></Properties>" +
            "<MyImage><Properties><Id>Source</Id></Properties></MyImage>" +
            "<MyImage><Properties><Id>Target</Id></Properties></MyImage>" +
            "</AdvGroup><AdvGroup><Properties><Id>B</Id></Properties>" +
            "<MyImage><Properties><Id>Duplicate</Id></Properties></MyImage>" +
            "</AdvGroup><MyImage><Properties><Id>Duplicate</Id></Properties></MyImage>" +
            "<MyImage><Properties /></MyImage></XuiCanvas>");
        XuiSyntaxNode source = ById(document, "Source");
        XuiSyntaxNode target = ById(document, "Target");
        XuiNavigationPathResolver resolver =
            new(document.Root, document.Text);

        Assert.AreEqual(
            XuiNavigationResolutionStatus.Resolved,
            resolver.Resolve(source, "Target").Status);
        Assert.AreEqual(
            XuiNavigationResolutionStatus.Resolved,
            resolver.Resolve(source, @"..\Target").Status);
        Assert.AreEqual(
            XuiNavigationResolutionStatus.Missing,
            resolver.Resolve(source, "Absent").Status);
        Assert.AreEqual(
            XuiNavigationResolutionStatus.Ambiguous,
            resolver.Resolve(source, "Duplicate").Status);
        Assert.IsTrue(resolver.TryCreateStablePath(
            source,
            target,
            out string path,
            out _));
        Assert.AreEqual("Target", path);

        XuiSyntaxNode groupB = ById(document, "B");
        XuiSyntaxNode nestedDuplicate =
            XuiModelReader.VisualChildren(groupB).Single();
        Assert.IsTrue(resolver.TryCreateStablePath(
            groupB,
            nestedDuplicate,
            out string relativeChild,
            out _));
        Assert.AreEqual(@".\Duplicate", relativeChild);
        Assert.AreEqual(
            nestedDuplicate.Key,
            resolver.Resolve(groupB, relativeChild).Target?.Key);

        XuiSyntaxNode noId = XuiModelReader.VisualDescendants(document.Root)
            .Single(node => XuiModelReader.GetPropertyValue(
                node,
                document.Text,
                "Id") is null);
        Assert.IsFalse(resolver.TryCreateStablePath(
            source,
            noId,
            out _,
            out string? error));
        StringAssert.Contains(error, "no authored Id");
    }

    [TestMethod]
    public void CatalogClassElementFactoryUsesExplicitStockClass()
    {
        string xml = XuiElementFactory.CreateXml(
            new XuiElementCreationRequest
            {
                Preset = XuiElementPreset.CatalogClass,
                Id = "T_Custom",
                Width = 40,
                Height = 20,
                Position = new XuiVector3(-2.5, 7.25, 3),
                ElementName = "IUIProgressText",
            });

        StringAssert.StartsWith(xml, "<IUIProgressText>");
        StringAssert.Contains(xml, "<Id>T_Custom</Id>");
        StringAssert.Contains(xml, "<Position>-2.500000,7.250000,3.000000</Position>");
        Assert.Throws<InvalidOperationException>(() =>
            XuiElementFactory.CreateXml(
                new XuiElementCreationRequest
                {
                    Preset = XuiElementPreset.CatalogClass,
                    Id = "Bad",
                    Width = 1,
                    Height = 1,
                    ElementName = "../bad",
                }));
    }

    [TestMethod]
    public async Task AssetCatalogSeparatesBrowsingFromResolutionAndCopiesReadOnlySources()
    {
        using TestDirectory directory = new();
        string stock = directory.File("stock");
        string workspace = directory.File("workspace");
        Directory.CreateDirectory(stock);
        await File.WriteAllTextAsync(
            Path.Combine(stock, "screen.xui"),
            "<XuiCanvas><Properties><Width>100</Width><Height>50</Height></Properties></XuiCanvas>");
        await File.WriteAllTextAsync(
            Path.Combine(stock, "textures.def"),
            "Texture(\"atlas.dds\",64,32)\n{\nWhole(\"stock_icon\")\n}\n");
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(
                stock,
                XuiAssetRootKind.ExtractedDyingLight,
                true),
        ],
            directory.File("cache"));
        await resolver.RebuildAsync();

        DyingLightXuiAssetCatalog catalog = new(resolver);
        XuiCatalogAsset screen = catalog.Assets.Single(asset =>
            asset.Kind == XuiCatalogAssetKind.Screen &&
            asset.Name == "screen");
        XuiCatalogAsset texture = catalog.Assets.Single(asset =>
            asset.Kind == XuiCatalogAssetKind.Texture &&
            asset.Name == "stock_icon");
        Assert.IsTrue(screen.IsReadOnly);
        Assert.AreEqual(new XuiVector2(64, 32), texture.LogicalSize);

        string copied = await catalog.CopyToWorkspaceAsync(
            screen,
            workspace);
        Assert.IsTrue(File.Exists(copied));
        Assert.IsTrue(copied.StartsWith(
            Path.GetFullPath(workspace),
            StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<IOException>(
            async () => await catalog.CopyToWorkspaceAsync(
                screen,
                workspace));
    }

    [TestMethod]
    public async Task WorkspaceReferenceTransactionPreflightsBacksUpAndRebinds()
    {
        using TestDirectory directory = new();
        string workspace = directory.File("workspace");
        XuiWorkspaceResourceService service = new(workspace);
        string created = await service.CreateScreenAsync(
            Path.Combine("menu", "screen.xui"));
        await File.WriteAllTextAsync(
            created,
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<MyImage><Properties><Id>I</Id><ImagePath>old_icon</ImagePath>" +
            "</Properties></MyImage></XuiCanvas>");

        XuiReferencePreflight preflight =
            await service.PreflightReplacementAsync(
                "old_icon",
                "new_icon");
        Assert.AreEqual(1, preflight.Replacements.Count);
        XuiReferenceTransactionResult result =
            await service.ApplyReplacementAsync(preflight);
        Assert.AreEqual(1, result.ChangedFiles);
        Assert.AreEqual(1, result.ChangedReferences);
        Assert.IsTrue(Directory.Exists(result.BackupDirectory));
        StringAssert.Contains(
            await File.ReadAllTextAsync(created),
            "<ImagePath>new_icon</ImagePath>");
        Assert.AreEqual(
            1,
            await service.UndoReplacementAsync(result));
        StringAssert.Contains(
            await File.ReadAllTextAsync(created),
            "<ImagePath>old_icon</ImagePath>");

        string renamed = service.RenameLooseXui(
            created,
            Path.Combine("menu", "renamed.xui"));
        Assert.IsTrue(File.Exists(renamed));
        string trash = service.DeleteLooseXui(renamed);
        Assert.IsFalse(File.Exists(renamed));
        Assert.IsTrue(File.Exists(trash));
        Assert.Throws<InvalidOperationException>(() =>
            service.RenameLooseXui(
                trash,
                Path.Combine("..", "escape.xui")));
    }

    [TestMethod]
    public async Task StaleReferencePreflightCommitsNothing()
    {
        using TestDirectory directory = new();
        string workspace = directory.File("workspace");
        XuiWorkspaceResourceService service = new(workspace);
        string screen = await service.CreateScreenAsync("screen.xui");
        await File.WriteAllTextAsync(
            screen,
            "<XuiCanvas><Properties><Width>1</Width><Height>1</Height></Properties>" +
            "<MyImage><Properties><Id>I</Id><ImagePath>old</ImagePath>" +
            "</Properties></MyImage></XuiCanvas>");
        XuiReferencePreflight preflight =
            await service.PreflightReplacementAsync("old", "new");
        await File.WriteAllTextAsync(
            screen,
            (await File.ReadAllTextAsync(screen))
            .Replace(">old<", ">changed<", StringComparison.Ordinal));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ApplyReplacementAsync(preflight));
        StringAssert.Contains(
            await File.ReadAllTextAsync(screen),
            "<ImagePath>changed</ImagePath>");
    }

    [TestMethod]
    public async Task VisualResourcesRenameWithReferencesAndDeleteRecoverably()
    {
        using TestDirectory directory = new();
        string workspace = directory.File("workspace");
        XuiWorkspaceResourceService service = new(workspace);
        string library = await service.CreateVisualAsync(
            Path.Combine("menu", "visuals.xui"),
            "PanelV",
            75.5,
            22.25);
        string screen = await service.CreateScreenAsync(
            Path.Combine("menu", "screen.xui"));
        await File.WriteAllTextAsync(
            screen,
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<AdvButton><Properties><Id>B</Id><Visual>PanelV</Visual>" +
            "</Properties></AdvButton></XuiCanvas>");

        XuiReferencePreflight preflight =
            await service.PreflightVisualRenameAsync(
                library,
                "PanelV",
                "RenamedPanelV");
        Assert.AreEqual(2, preflight.Replacements.Count);
        Assert.AreEqual(2, preflight.Files.Count);
        XuiReferenceTransactionResult renamed =
            await service.ApplyReplacementAsync(preflight);
        Assert.AreEqual(2, renamed.ChangedFiles);
        StringAssert.Contains(
            await File.ReadAllTextAsync(library),
            "<Id>RenamedPanelV</Id>");
        StringAssert.Contains(
            await File.ReadAllTextAsync(screen),
            "<Visual>RenamedPanelV</Visual>");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DeleteLooseVisualAsync(
                library,
                "RenamedPanelV"));
        await File.WriteAllTextAsync(
            screen,
            (await File.ReadAllTextAsync(screen))
            .Replace(
                "<Visual>RenamedPanelV</Visual>",
                string.Empty,
                StringComparison.Ordinal));
        XuiVisualDeleteResult deleted =
            await service.DeleteLooseVisualAsync(
                library,
                "RenamedPanelV");
        Assert.IsTrue(File.Exists(deleted.BackupFile));
        Assert.DoesNotContain(
            "<XuiVisual>",
            await File.ReadAllTextAsync(library),
            StringComparison.Ordinal);

        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(
                workspace,
                XuiAssetRootKind.Workspace,
                false),
        ],
            directory.File("cache"));
        await resolver.RebuildAsync();
        Assert.IsFalse(resolver.VisualTemplates.Any(visual =>
            visual.Id == "RenamedPanelV"));
        string trash = service.DeleteLooseXui(library);
        Assert.IsTrue(File.Exists(trash));
        await resolver.RebuildAsync();
        Assert.IsFalse(resolver.Files.Any(file =>
            file.RelativePath.EndsWith(
                "visuals.xui",
                StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ReferenceTransactionRollsBackEarlierFilesOnWriteFailure()
    {
        using TestDirectory directory = new();
        string workspace = directory.File("workspace");
        Directory.CreateDirectory(workspace);
        string first = Path.Combine(workspace, "a.xui");
        string second = Path.Combine(workspace, "b.xui");
        const string content =
            "<XuiCanvas><Properties><Width>1</Width><Height>1</Height></Properties>" +
            "<MyImage><Properties><Id>I</Id><ImagePath>old</ImagePath>" +
            "</Properties></MyImage></XuiCanvas>";
        await File.WriteAllTextAsync(first, content);
        await File.WriteAllTextAsync(second, content);
        XuiWorkspaceResourceService service = new(workspace);
        XuiReferencePreflight preflight =
            await service.PreflightReplacementAsync("old", "new");

        File.SetAttributes(second, FileAttributes.ReadOnly);
        try
        {
            await Assert.ThrowsAsync<Exception>(
                async () => await service.ApplyReplacementAsync(preflight));
            StringAssert.Contains(
                await File.ReadAllTextAsync(first),
                "<ImagePath>old</ImagePath>");
        }
        finally
        {
            File.SetAttributes(second, FileAttributes.Normal);
        }
    }

    private static XuiSyntaxNode ById(
        XuiDocument document,
        string id) =>
        XuiModelReader.VisualDescendants(document.Root)
            .Single(node => XuiModelReader.GetId(node, document.Text) == id);
}
