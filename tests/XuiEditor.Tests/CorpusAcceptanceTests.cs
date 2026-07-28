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
        XuiRenderNode respawnText = frame.Nodes.Single(static node =>
            node.Id == "T_Time" &&
            node.Text == "Respawn in 10sec" &&
            node.TextColorRuns.Count == 1);
        Assert.IsTrue(respawnText.ColorControlSequenceEnabled);
        Assert.AreEqual(11, respawnText.TextColorRuns[0].Start);
        Assert.AreEqual(5, respawnText.TextColorRuns[0].Length);
        Assert.AreEqual(
            0xffdc8a1au,
            respawnText.TextColorRuns[0].Color.Argb);
        Assert.IsFalse(frame.Diagnostics.Any(diagnostic =>
            diagnostic.NodeKey == respawnText.SelectionKey &&
            diagnostic.Code.StartsWith(
                "XUI-TEXT",
                StringComparison.Ordinal)));
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
        Assert.IsTrue(
            window.ViewportForTesting.NavigationCacheActiveForTesting,
            "Repeated camera movement should use the temporary flattened HUD cache.");
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
        Assert.IsFalse(texture.IsApproximation);
        Assert.IsTrue(texture.BgraPixels
            .Where(static (_, index) => index % 4 == 3)
            .Any(static alpha => alpha > 0));
        Assert.IsTrue(texture.BgraPixels
            .Where(static (_, index) => index % 4 != 3)
            .Any(static channel => channel > 0));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(IrisuHud));
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [Timeout(60_000)]
    public void IrisuSelectedImageLoadsAndProducesAnImageDrawing()
    {
        if (!File.Exists(IrisuHud))
        {
            Assert.Inconclusive(
                "The external Irisu Workshop project is not installed on this test host.");
        }

        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        byte[] before = File.ReadAllBytes(IrisuHud);
        XuiDocument document = XuiDocument.OpenAsync(IrisuHud)
            .GetAwaiter()
            .GetResult();
        XuiSyntaxNode image = document.Root
            .DescendantsAndSelf()
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) ==
                "I_Irisu_00");
        XuiDocumentAssetContext context =
            XuiDocumentAssetContext.Discover(IrisuHud);
        DyingLightAssetResolver resolver = new(
        [
            context.Root,
        ]);
        resolver.RebuildAsync().GetAwaiter().GetResult();
        using MainWindow window = new();
        window.SetPreviewScenarioForTesting("authored");
        window.AttachDocumentForTesting(document);
        window.SelectNodeKeysForTesting([image.Key]);
        window.SetAssetResolverForTesting(resolver);

        Stopwatch wait = Stopwatch.StartNew();
        while (!window.ViewportForTesting.TextureLoadedForTesting(
                   "irisu_attack_00") &&
               wait.Elapsed < TimeSpan.FromSeconds(10))
        {
            window.Dispatcher.Invoke(
                static () => { },
                System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(5);
        }

        Assert.IsTrue(
            window.ViewportForTesting.TextureLoadedForTesting(
                "irisu_attack_00"),
            "The selected project texture did not get priority over the HUD texture backlog.");
        Assert.IsTrue(
            window.ViewportForTesting.RetainedNodeHasImageDrawingForTesting(
                image.Key),
            "The loaded Irisu DDS was not attached to its retained image visual.");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(IrisuHud));
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [Timeout(60_000)]
    public void IrisuTimelinePanelBuildsOnlyTheSelectedScope()
    {
        if (!File.Exists(IrisuHud))
        {
            Assert.Inconclusive(
                "The external Irisu Workshop project is not installed on this test host.");
        }

        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        byte[] before = File.ReadAllBytes(IrisuHud);
        XuiDocument document = XuiDocument.OpenAsync(IrisuHud)
            .GetAwaiter()
            .GetResult();
        XuiSyntaxNode zone = document.Root
            .DescendantsAndSelf()
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) ==
                "HudZoneInfoDI");
        XuiSyntaxNode image = document.Root
            .DescendantsAndSelf()
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) ==
                "I_Irisu_00");
        using MainWindow window = new();
        window.SetPreviewScenarioForTesting("authored");
        window.AttachDocumentForTesting(document);
        XuiTimelineWorkspace initialWorkspace =
            window.TimelineWorkspaceForTesting!;
        XuiTimelineScopeCatalog catalog = initialWorkspace.Catalog;
        XuiTimelineScope rootScope = catalog
            .ForTarget("HUD_DI")[0];
        XuiTimelineScope zoneScope = catalog
            .ForTarget("G_Group")
            .First(scope => scope.OwnerId == "HudZoneInfoDI");
        XuiTimelineScope imageScope = catalog
            .ForTarget("I_Irisu_00")
            .First(scope => scope.OwnerId == "G_Group");
        XuiSyntaxNode[] selectionProbeNodes = catalog.Scopes
            .Select(static scope => scope.Owner)
            .DistinctBy(static node => node.Key)
            .Take(100)
            .ToArray();
        Assert.HasCount(100, selectionProbeNodes);

        // Reveal the paths once so the measured pass represents ordinary warm
        // navigation rather than first-use hierarchy expansion.
        foreach (XuiSyntaxNode node in selectionProbeNodes)
        {
            window.SelectNodeKeysForTesting([node.Key]);
        }

        int hierarchyResets = 0;
        window.HierarchyRows.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Action ==
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                hierarchyResets++;
            }
        };
        long selectionSamples =
            window.LayoutEvaluationCountForTesting;
        Stopwatch selectionClock = Stopwatch.StartNew();
        foreach (XuiSyntaxNode node in selectionProbeNodes)
        {
            window.SelectNodeKeysForTesting([node.Key]);
            Assert.IsFalse(window.RawXmlMaterializedForTesting);
        }

        selectionClock.Stop();
        Assert.IsLessThan(
            TimeSpan.FromMilliseconds(500),
            selectionClock.Elapsed,
            $"100 warm Irisu scope selections took {selectionClock.Elapsed.TotalMilliseconds:0.0} ms.");
        Assert.AreEqual(
            selectionSamples,
            window.LayoutEvaluationCountForTesting);
        Assert.AreEqual(0, hierarchyResets);

        Assert.AreEqual(0, rootScope.ComposedTick);
        Assert.AreEqual(3, zoneScope.ComposedTick);
        Assert.AreEqual(1, imageScope.ComposedTick);
        Assert.AreEqual(3, initialWorkspace.TickFor(zoneScope.ScopeKey));
        Assert.AreEqual(1, initialWorkspace.TickFor(imageScope.ScopeKey));
        Assert.IsTrue(Visible(
            window.ViewportForTesting.FrameForTesting!,
            "I_Irisu_00"),
            "The composed default should settle both parent and image scopes.");

        window.SelectNodeKeysForTesting([zone.Key]);
        XuiTimelineWorkspace selectedWorkspace =
            window.TimelineWorkspaceForTesting!;
        string zoneScopeKey =
            selectedWorkspace.ActiveScope!.ScopeKey;
        Assert.AreEqual(
            3,
            selectedWorkspace.TickFor(zoneScopeKey));
        Assert.IsTrue(selectedWorkspace.ActiveTickIsComposed);
        StringAssert.Contains(
            window.TimelineScopeLabelForTesting,
            "composed");
        Assert.IsTrue(Visible(
            window.ViewportForTesting.FrameForTesting!,
            "I_Irisu_00"));

        window.SelectNodeKeysForTesting([image.Key]);
        window.SetAllInScopeForTesting(true);
        XuiTimelineWorkspace workspace =
            window.TimelineWorkspaceForTesting!;
        Assert.AreEqual("G_Group", workspace.ActiveScope?.OwnerId);
        Assert.AreEqual(3, window.TimelineForTesting.VisibleTrackCountForTesting);
        Assert.AreEqual(5, window.NamedFrameCountForTesting);
        Assert.IsGreaterThan(
            1_800,
            workspace.Catalog.Scopes.Sum(static scope =>
                scope.Timelines.Count));

        long contentRedraws =
            window.ViewportForTesting.NodeContentRedrawCountForTesting;
        long presentationUpdates =
            window.ViewportForTesting.NodePresentationUpdateCountForTesting;
        long cameraUpdates =
            window.ViewportForTesting.CameraUpdateCountForTesting;
        Stopwatch scopedScrub = Stopwatch.StartNew();
        window.SetTimelineTickForTesting(4);
        Assert.IsTrue(Visible(
            window.ViewportForTesting.FrameForTesting!,
            "I_Irisu_00"));
        window.SetTimelineTickForTesting(5);
        Assert.IsTrue(Visible(
            window.ViewportForTesting.FrameForTesting!,
            "I_Irisu_00"));
        window.SetTimelineTickForTesting(20);
        Assert.IsFalse(Visible(
            window.ViewportForTesting.FrameForTesting!,
            "I_Irisu_00"));
        Assert.IsTrue(Visible(
            window.ViewportForTesting.FrameForTesting!,
            "I_Irisu_01"));
        scopedScrub.Stop();
        Console.WriteLine(
            $"Three warm Irisu WPF scope samples: {scopedScrub.Elapsed.TotalMilliseconds:0.0} ms");
        Assert.IsLessThan(
            TimeSpan.FromMilliseconds(100),
            scopedScrub.Elapsed,
            $"Three warm Irisu WPF samples took {scopedScrub.Elapsed.TotalMilliseconds:0.0} ms.");
        Assert.AreEqual(0, workspace.TickFor(rootScope.ScopeKey));
        Assert.AreEqual(3, workspace.TickFor(zoneScopeKey));
        Assert.AreEqual(0, hierarchyResets);
        Assert.IsLessThanOrEqualTo(
            contentRedraws + 1,
            window.ViewportForTesting.NodeContentRedrawCountForTesting,
            "Only the newly visible Irisu image may need its deferred content populated.");
        Assert.IsLessThanOrEqualTo(
            presentationUpdates + 4,
            window.ViewportForTesting.NodePresentationUpdateCountForTesting,
            "Scoped Show changes should not reapply presentation to the whole HUD.");
        Assert.AreEqual(
            cameraUpdates,
            window.ViewportForTesting.CameraUpdateCountForTesting);

        double[] frameMilliseconds = new double[60];
        for (int frameIndex = 0;
             frameIndex < frameMilliseconds.Length;
             frameIndex++)
        {
            Stopwatch frameClock = Stopwatch.StartNew();
            window.SetTimelineTickForTesting(
                frameIndex % 2 == 0 ? 4 : 20);
            frameClock.Stop();
            frameMilliseconds[frameIndex] =
                frameClock.Elapsed.TotalMilliseconds;
        }

        Array.Sort(frameMilliseconds);
        double p95Milliseconds =
            frameMilliseconds[
                (int)Math.Ceiling(frameMilliseconds.Length * 0.95) - 1];
        Assert.IsLessThanOrEqualTo(
            33.0,
            p95Milliseconds,
            $"Warm Irisu scope playback p95 was {p95Milliseconds:0.0} ms.");
        Assert.AreEqual(0, hierarchyResets);
        Assert.IsLessThanOrEqualTo(
            contentRedraws + 1,
            window.ViewportForTesting.NodeContentRedrawCountForTesting,
            "Repeated scope playback must reuse retained content after the first reveal.");
        Assert.IsFalse(document.IsDirty);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(IrisuHud));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task IrisuHudUsesIndependentRootAndImageTimelineScopes()
    {
        if (!File.Exists(IrisuHud))
        {
            Assert.Inconclusive(
                "The external Irisu Workshop project is not installed on this test host.");
        }

        byte[] before = await File.ReadAllBytesAsync(IrisuHud);
        XuiDocument document = await XuiDocument.OpenAsync(IrisuHud);
        DyingLightLayoutSession session =
            DyingLightLayoutSession.Compile(document);
        XuiTimelineScope rootScope =
            session.TimelineScopes.ForTarget("HUD_DI")[0];
        XuiTimelineScope imageScope = session.TimelineScopes
            .ForTarget("I_Irisu_00")
            .First(scope => scope.OwnerId == "G_Group");
        XuiTimelineScope zoneScope = session.TimelineScopes
            .ForTarget("G_Group")
            .First(scope => scope.OwnerId == "HudZoneInfoDI");

        Assert.HasCount(3, imageScope.Timelines);
        Assert.AreEqual(
            3,
            imageScope.Timelines.Sum(static timeline =>
                timeline.Tracks.Count));
        Assert.HasCount(5, imageScope.NamedFrames);
        XuiTrack zoneShow = zoneScope.Timelines
            .Single(static timeline => timeline.TargetId == "G_Group")
            .Tracks
            .Single(static track =>
                track.Property == XuiTimelineProperty.Show);
        Assert.IsTrue(TimelineEvaluator.Sample(zoneShow, 3)!.Boolean);
        XuiTrack imageShow = imageScope.Timelines
            .Single(static timeline => timeline.TargetId == "I_Irisu_00")
            .Tracks
            .Single();
        Assert.IsTrue(TimelineEvaluator.Sample(imageShow, 4)!.Boolean);

        XuiRenderFrame tickFour = SampleIrisu(
            session,
            rootScope,
            zoneScope,
            imageScope,
            4);
        XuiRenderFrame tickFive = SampleIrisu(
            session,
            rootScope,
            zoneScope,
            imageScope,
            5);
        XuiRenderFrame tickTwenty = SampleIrisu(
            session,
            rootScope,
            zoneScope,
            imageScope,
            20);
        Assert.IsTrue(
            Visible(tickFour, "I_Irisu_00"),
            DescribeVisibilityChain(tickFour, "I_Irisu_00"));
        Assert.IsTrue(Visible(tickFive, "I_Irisu_00"));
        Assert.IsFalse(Visible(tickTwenty, "I_Irisu_00"));
        Assert.IsTrue(Visible(tickTwenty, "I_Irisu_01"));

        XuiTimelineEvaluationState hiddenRoot =
            XuiTimelineEvaluationState.ScopeLocal(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [rootScope.ScopeKey] = 5,
                    [zoneScope.ScopeKey] = 3,
                    [imageScope.ScopeKey] = 5,
                });
        XuiRenderFrame hidden = session.Sample(
            XuiViewport.Default,
            hiddenRoot);
        Assert.IsFalse(Visible(hidden, "I_Irisu_00"));

        int samplesBeforeWarmScrub =
            session.TimelineScopeEvaluationCount;
        Stopwatch warmScrub = Stopwatch.StartNew();
        for (int tick = 4; tick <= 20; tick++)
        {
            _ = SampleIrisu(
                session,
                rootScope,
                zoneScope,
                imageScope,
                tick);
        }

        warmScrub.Stop();
        Assert.AreEqual(
            samplesBeforeWarmScrub,
            session.TimelineScopeEvaluationCount,
            "Ticks between identical step keyframes should reuse the previous incremental frame without sampling unrelated HUD scopes.");
        Assert.IsLessThan(
            TimeSpan.FromSeconds(5),
            warmScrub.Elapsed,
            $"Warm scoped HUD scrubbing took {warmScrub.Elapsed.TotalMilliseconds:0} ms.");
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(IrisuHud));
    }

    private static XuiRenderFrame SampleIrisu(
        DyingLightLayoutSession session,
        XuiTimelineScope rootScope,
        XuiTimelineScope zoneScope,
        XuiTimelineScope imageScope,
        int imageTick) =>
        session.Sample(
            XuiViewport.Default,
            XuiTimelineEvaluationState.ScopeLocal(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [rootScope.ScopeKey] = 0,
                    [zoneScope.ScopeKey] = 3,
                    [imageScope.ScopeKey] = imageTick,
                }));

    private static bool Visible(XuiRenderFrame frame, string id) =>
        frame.Nodes.Any(node =>
            node.Id == id &&
            node.IsShown &&
            node.Opacity > 0);

    private static string DescribeVisibilityChain(
        XuiRenderFrame frame,
        string id)
    {
        XuiRenderNode? node = frame.Nodes.FirstOrDefault(candidate =>
            candidate.Id == id);
        if (node is null)
        {
            return $"{id} was not present in the render frame.";
        }

        Dictionary<string, XuiRenderNode> byKey = frame.Nodes
            .ToDictionary(static candidate => candidate.Key);
        List<string> chain = [];
        while (node is not null)
        {
            chain.Add(
                $"{node.Id}/{node.ElementName}: shown={node.IsShown}, opacity={node.Opacity:0.###}");
            node = node.ParentKey is not null &&
                   byKey.TryGetValue(
                       node.ParentKey,
                       out XuiRenderNode? parent)
                ? parent
                : null;
        }

        return string.Join(" <- ", chain);
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
