using System.Diagnostics;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Layout;
using XuiEditor.Wpf;

namespace XuiEditor.Tests;

[TestClass]
public sealed class CorpusAcceptanceTests
{
    private const string ExtractedRoot =
        @"D:\Backups\Assets\Dying Light Extraction\Dying Light Files";
    private const string IrisuHud =
        @"E:\SteamLibrary\steamapps\common\Dying Light\DevTools\workshop\Irisu-Syndrome-DL\data\menu\hud\hud.xui";

    [TestMethod]
    public async Task RepresentativeStockDocumentsParseAndEvaluate()
    {
        RequireCorpus();
        string[] relativePaths =
        [
            @"data\menu\scr\menuoptionscontrolskeyboard.xui",
            @"data\menu\scr\menumain_pc.xui",
            @"data\menu\scr\menuskin.xui",
            @"data\menu\scr\intro.xui",
            @"data\menu\scr\menubountybrief.xui",
            @"data\menu\scr\menuyesnodialog.xui",
            @"data\menu\hud\hud_skin.xui",
        ];

        foreach (string relativePath in relativePaths)
        {
            string path = Path.Combine(ExtractedRoot, relativePath);
            byte[] before = await File.ReadAllBytesAsync(path);
            XuiDocument document = await XuiDocument.OpenAsync(path);
            XuiTimelineSet timelines = XuiTimelineParser.Parse(document);
            XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
                document,
                XuiViewport.Default,
                0);

            Assert.IsGreaterThan(0, frame.Nodes.Count, relativePath);
            Assert.IsFalse(timelines.Diagnostics.Any(static diagnostic =>
                diagnostic.Code == "XUI-TL005"),
                relativePath + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    timelines.Diagnostics
                        .Where(static diagnostic => diagnostic.Code == "XUI-TL005")
                        .Take(20)
                        .Select(static diagnostic => diagnostic.Message)));
            if (relativePath.EndsWith(
                    "menubountybrief.xui",
                    StringComparison.OrdinalIgnoreCase))
            {
                XuiRenderNode content = frame.Nodes.Single(static node =>
                    node.Id == "G_Content");
                Assert.AreEqual(440, content.Position.X, 0.001);
                Assert.AreEqual(60, content.Position.Y, 0.001);
            }

            if (relativePath.EndsWith(
                    "menuyesnodialog.xui",
                    StringComparison.OrdinalIgnoreCase))
            {
                XuiRenderNode dialog = frame.Nodes.Single(static node =>
                    node.Id == "DialogWindow");
                XuiRenderNode yes = frame.Nodes.Single(static node =>
                    node.Id == "ButtonYes");
                XuiRenderNode no = frame.Nodes.Single(static node =>
                    node.Id == "ButtonNo");
                XuiRenderNode ok = frame.Nodes.Single(static node =>
                    node.Id == "ButtonOk");
                Assert.AreEqual(365, dialog.Position.X, 0.001);
                Assert.AreEqual(249.333206, dialog.Position.Y, 0.001);
                Assert.AreEqual(506, no.Position.X, 0.001);
                Assert.IsTrue(yes.IsShown);
                Assert.IsTrue(no.IsShown);
                Assert.IsFalse(ok.IsShown);
                Assert.IsLessThanOrEqualTo(
                    no.WorldBounds.X,
                    yes.WorldBounds.X + yes.WorldBounds.Width);
            }

            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path));
        }
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task LargeHudParsesAndEvaluatesWithinInteractiveBudget()
    {
        RequireCorpus();
        string path = Path.Combine(ExtractedRoot, @"data\menu\hud\hud.xui");
        Stopwatch stopwatch = Stopwatch.StartNew();

        XuiDocument document = await XuiDocument.OpenAsync(path);
        TimeSpan parseTime = stopwatch.Elapsed;
        XuiTimelineSet timelines = XuiTimelineParser.Parse(document);
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            XuiViewport.Default,
            0);
        TimeSpan total = stopwatch.Elapsed;

        Assert.IsGreaterThan(1_000, frame.Nodes.Count);
        XuiRenderNode[] whitePaintNodes = frame.Nodes
            .Where(static node =>
                node.ImagePath.Trim().Equals(
                    "white",
                    StringComparison.OrdinalIgnoreCase) &&
                node.Kind is XuiRenderKind.Image or XuiRenderKind.Rectangle)
            .ToArray();
        Assert.IsGreaterThan(100, whitePaintNodes.Length);
        Assert.IsTrue(whitePaintNodes.All(static node =>
            node.PaintKind == XuiPaintKind.SolidColor));
        HashSet<string> supportedMaterialKeys = frame.Nodes
            .Where(static node =>
                node.Material.Trim().Equals(
                    "menu_antialias.mat",
                    StringComparison.OrdinalIgnoreCase) &&
                node.Kind is XuiRenderKind.Image or XuiRenderKind.Rectangle)
            .Select(static node => node.Key)
            .ToHashSet(StringComparer.Ordinal);
        Assert.IsGreaterThan(0, supportedMaterialKeys.Count);
        Assert.IsFalse(frame.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "XUI-LAYOUT004" &&
            diagnostic.NodeKey is not null &&
            supportedMaterialKeys.Contains(diagnostic.NodeKey)));
        XuiDiagnostic[] materialDiagnostics = frame.Diagnostics
            .Where(static diagnostic => diagnostic.Code == "XUI-LAYOUT004")
            .ToArray();
        Assert.IsLessThan(
            100,
            materialDiagnostics.Length,
            "Material diagnostics should be aggregated by profile/material.");
        Assert.IsTrue(materialDiagnostics
            .GroupBy(static diagnostic => diagnostic.Message)
            .All(static group => group.Count() == 1));
        Assert.IsFalse(timelines.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-TL005"));
        Assert.IsLessThan(TimeSpan.FromSeconds(8), parseTime);
        Assert.IsLessThan(TimeSpan.FromSeconds(15), total);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [Timeout(30_000)]
    public void LargeHudUsesRetainedWorkspaceForNavigation()
    {
        RequireCorpus();
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        string path = Path.Combine(ExtractedRoot, @"data\menu\hud\hud.xui");
        XuiDocument document = XuiDocument.OpenAsync(path)
            .GetAwaiter()
            .GetResult();
        using MainWindow window = new()
        {
            Width = 1500,
            Height = 930,
        };
        Stopwatch attachClock = Stopwatch.StartNew();
        window.AttachDocumentForTesting(document);
        attachClock.Stop();

        Assert.IsGreaterThan(
            1_000,
            window.ViewportForTesting.RetainedNodeVisualCountForTesting);
        Assert.IsLessThan(TimeSpan.FromSeconds(5), attachClock.Elapsed);

        long contentRedraws =
            window.ViewportForTesting.NodeContentRedrawCountForTesting;
        Stopwatch navigationClock = Stopwatch.StartNew();
        for (int index = 0; index < 30; index++)
        {
            window.ViewportForTesting.ZoomBy(index % 2 == 0 ? 1.05 : 1 / 1.05);
        }

        navigationClock.Stop();
        Assert.AreEqual(
            contentRedraws,
            window.ViewportForTesting.NodeContentRedrawCountForTesting);
        Assert.IsLessThan(
            TimeSpan.FromMilliseconds(500),
            navigationClock.Elapsed);

        Stopwatch filterClock = Stopwatch.StartNew();
        window.SetHierarchyFilterForTesting("Hud");
        filterClock.Stop();
        Assert.IsLessThan(
            TimeSpan.FromMilliseconds(150),
            filterClock.Elapsed);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task MenuLibrariesResolveStockVisualsAndTextureDefinitions()
    {
        RequireCorpus();
        string menuRoot = Path.Combine(ExtractedRoot, "data", "menu");
        string menuTextureRoot = Path.Combine(
            Path.GetDirectoryName(ExtractedRoot)!,
            "Textures",
            "menuCommon");
        if (!Directory.Exists(menuTextureRoot))
        {
            Assert.Inconclusive(
                "The external extracted menu DDS root is not installed on this test host.");
        }

        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(
                menuRoot,
                XuiAssetRootKind.ExtractedDyingLight,
                true),
            new XuiAssetRoot(
                menuTextureRoot,
                XuiAssetRootKind.ExtractedDyingLight,
                true),
        ]);
        await resolver.RebuildAsync();
        string mainMenuPath = Path.Combine(
            menuRoot,
            "scr",
            "menumain_pc.xui");
        XuiDocument document = await XuiDocument.OpenAsync(mainMenuPath);
        XuiDocument dialogDocument = await XuiDocument.OpenAsync(
            Path.Combine(
                menuRoot,
                "scr",
                "menuyesnodialog.xui"));

        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            XuiViewport.Default,
            0,
            resolver);
        XuiRenderFrame dialogFrame = DyingLightLayoutEngine.Evaluate(
            dialogDocument,
            XuiViewport.Default,
            0,
            resolver);
        ResolvedTexture? button =
            await resolver.ResolveTextureAsync("button_0");
        ResolvedTexture? buttonTile =
            await resolver.ResolveTextureAsync("button_tile");

        Assert.IsNotNull(resolver.ResolveVisual("UIBRepairSlotV"));
        Assert.IsNotNull(resolver.ResolveTextureDefinition("button_0"));
        Assert.IsNotNull(button);
        Assert.AreEqual(256, button.Width);
        Assert.AreEqual(64, button.Height);
        Assert.IsNotNull(buttonTile);
        Assert.HasCount(3, buttonTile.TileParts);
        Assert.IsTrue(frame.Nodes.Any(static node => node.VisualResolved));
        Assert.IsGreaterThan(100, frame.Nodes.Count);
        XuiRenderNode dialogWindow = dialogFrame.Nodes.Single(static node =>
            node.Id == "DialogWindow");
        XuiRenderNode yes = dialogFrame.Nodes.Single(static node =>
            node.Id == "ButtonYes");
        XuiRenderNode no = dialogFrame.Nodes.Single(static node =>
            node.Id == "ButtonNo");
        XuiRenderNode ok = dialogFrame.Nodes.Single(static node =>
            node.Id == "ButtonOk");
        Assert.AreEqual(365, dialogWindow.Position.X, 0.001);
        Assert.AreEqual(249.333206, dialogWindow.Position.Y, 0.001);
        Assert.AreEqual(550 - no.Size.X, no.Position.X, 0.001);
        Assert.IsTrue(yes.IsShown);
        Assert.IsTrue(no.IsShown);
        Assert.IsFalse(ok.IsShown);
        Assert.IsLessThanOrEqualTo(
            no.WorldBounds.X,
            yes.WorldBounds.X + yes.WorldBounds.Width);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task IrisuProjectResolvesItsOwnHudTextureReadOnly()
    {
        if (!File.Exists(IrisuHud))
        {
            Assert.Inconclusive(
                "The external Irisu Workshop project is not installed on this test host.");
        }

        byte[] before = await File.ReadAllBytesAsync(IrisuHud);
        XuiDocumentAssetContext context =
            XuiDocumentAssetContext.Discover(IrisuHud);
        DyingLightAssetResolver resolver = new(
        [
            context.Root,
        ]);

        await resolver.RebuildAsync();
        XuiTextureRegion? definition =
            resolver.ResolveTextureDefinition("irisu_attack_00");
        ResolvedTexture? texture =
            await resolver.ResolveTextureAsync("irisu_attack_00");

        Assert.IsNotNull(definition);
        Assert.IsNotNull(definition.DefinitionRoot);
        Assert.AreEqual(context.Root.FullPath, definition.DefinitionRoot.FullPath);
        Assert.IsNotNull(texture);
        StringAssert.Contains(
            texture.SourcePath,
            Path.Combine(
                "menu",
                "hud",
                "irisu_attack",
                "irisu_attack_00.dds"));
        Assert.AreEqual(1280, texture.Width);
        Assert.AreEqual(720, texture.Height);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(IrisuHud));
    }

    private static void RequireCorpus()
    {
        if (!Directory.Exists(ExtractedRoot))
        {
            Assert.Inconclusive(
                "The external Dying Light extraction is not installed on this test host.");
        }
    }
}
