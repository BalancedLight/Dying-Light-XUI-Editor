using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Wpf;
using XuiEditor.Wpf.Models;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Tests;

[TestClass]
public sealed class WpfSmokeTests
{
    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void MainWorkspaceRendersAtFixedDpiWithoutAudioControls()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        using MainWindow window = new()
        {
            Width = 1280,
            Height = 760,
        };
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(1280, 760));
        content.Arrange(new Rect(0, 0, 1280, 760));
        content.UpdateLayout();
        RenderTargetBitmap bitmap = new(
            1280,
            760,
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(content);

        int stride = 1280 * 4;
        byte[] pixels = new byte[stride * 760];
        bitmap.CopyPixels(pixels, stride, 0);
        Assert.IsTrue(pixels.Any(static value => value != 0));
        Assert.IsFalse(Descendants(content).Any(static element =>
            element is MediaElement));
        (double hierarchy, double inspector, double timeline) =
            window.PaneSizesForTesting;
        Assert.AreEqual(300, hierarchy, 0.5);
        Assert.AreEqual(360, inspector, 0.5);
        Assert.AreEqual(250, timeline, 0.5);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [Timeout(20_000)]
    public void LargeHierarchyFiltersWithoutOverlapOrExpansionStateLoss()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        using MainWindow window = new()
        {
            Width = 1500,
            Height = 930,
        };
        XuiDocument document = XuiDocument.FromText(
            CreateLargeHierarchy(50, 200));
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode group = XuiModelReader.VisualDescendants(document.Root)
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Group010");
        XuiSyntaxNode item = XuiModelReader.VisualDescendants(document.Root)
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Item02199");

        Assert.AreEqual(51, window.HierarchyRows.Count);
        window.SetHierarchyExpansionForTesting(group.Key, true);
        Assert.AreEqual(251, window.HierarchyRows.Count);
        string[] expansionBeforeFilter =
            window.ExpandedKeysForTesting.Order(StringComparer.Ordinal).ToArray();

        window.SetHierarchyFilterForTesting("Item02199");
        Assert.AreEqual(3, window.HierarchyRows.Count);
        window.SetHierarchyFilterForTesting(string.Empty);
        CollectionAssert.AreEqual(
            expansionBeforeFilter,
            window.ExpandedKeysForTesting.Order(StringComparer.Ordinal).ToArray());
        Assert.AreEqual(251, window.HierarchyRows.Count);

        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(1500, 930));
        content.Arrange(new Rect(0, 0, 1500, 930));
        content.UpdateLayout();
        List<ListBoxItem> realized = Enumerable
            .Range(0, Math.Min(30, window.HierarchyRows.Count))
            .Select(index => window.HierarchyListForTesting
                .ItemContainerGenerator.ContainerFromIndex(index))
            .OfType<ListBoxItem>()
            .ToList();
        Assert.IsGreaterThan(2, realized.Count);
        double previousBottom = double.NegativeInfinity;
        foreach (ListBoxItem row in realized)
        {
            Point origin = row.TranslatePoint(
                new Point(),
                window.HierarchyListForTesting);
            Assert.IsGreaterThanOrEqualTo(
                previousBottom - 0.01,
                origin.Y,
                "Virtualized hierarchy rows overlapped.");
            previousBottom = origin.Y + row.ActualHeight;
            Assert.AreEqual(24, row.ActualHeight, 0.01);
        }

        window.SelectNodeKeysForTesting([item.Key]);
        Assert.IsTrue(window.SelectedKeysForTesting.Contains(item.Key));
        Assert.IsTrue(window.ViewportForTesting.IsSelectedForTesting(item.Key));
        Assert.AreEqual(1, window.TimelineForTesting.VisibleTrackCountForTesting);
        window.SetHierarchyExpansionForTesting(group.Key, false);
        Assert.AreEqual(51, window.HierarchyRows.Count);
    }

    [TestMethod]
    public void PaneAndAssetSettingsRoundTripWithProtectedRoots()
    {
        EditorSettings settings = new()
        {
            WindowWidth = 1440,
            WindowHeight = 900,
            HierarchyWidth = 321,
            InspectorWidth = 377,
            TimelineHeight = 288,
            ShowGrid = false,
            SnapEnabled = false,
            DyingLightInstallPath =
                @"E:\SteamLibrary\steamapps\common\Dying Light",
            Locale = "Pl",
            InputGlyphScheme = XuiInputGlyphScheme.Xbox,
            PreviewScenarioId = "hud-combat",
            ReferenceOverlayOpacity = 0.72,
            AssetRoots =
            [
                new AssetRootSetting
                {
                    Path = @"D:\Extracted",
                    Kind = XuiAssetRootKind.ExtractedDyingLight,
                    IsReadOnly = false,
                },
            ],
        };
        settings.FontMappings["BOXED"] = "Segoe UI";

        EditorSettings restored = EditorSettingsStore.Deserialize(
            EditorSettingsStore.Serialize(settings));

        Assert.AreEqual(321, restored.HierarchyWidth);
        Assert.AreEqual(377, restored.InspectorWidth);
        Assert.AreEqual(288, restored.TimelineHeight);
        Assert.IsFalse(restored.ShowGrid);
        Assert.IsFalse(restored.SnapEnabled);
        Assert.AreEqual(
            @"E:\SteamLibrary\steamapps\common\Dying Light",
            restored.DyingLightInstallPath);
        Assert.AreEqual("Pl", restored.Locale);
        Assert.AreEqual(
            XuiInputGlyphScheme.Xbox,
            restored.InputGlyphScheme);
        Assert.AreEqual("hud-combat", restored.PreviewScenarioId);
        Assert.AreEqual(0.72, restored.ReferenceOverlayOpacity, 0.001);
        Assert.IsTrue(restored.AssetRoots[0].EffectiveIsReadOnly);
        Assert.AreEqual("Segoe UI", restored.FontMappings["BOXED"]);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void GameplayScenarioKeepsExplicitlyHiddenObjectivePanelsHidden()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        using MainWindow window = new();

        window.SetPreviewScenarioForTesting("gameplay");
        XuiRenderContext context = window.PreviewRenderContextForTesting;
        IReadOnlyDictionary<string, string>? hidden =
            context.EffectiveScenario.PropertiesFor(
                "HudStorageObjective",
                "/unused");

        Assert.IsNotNull(hidden);
        Assert.AreEqual("false", hidden["Show"]);
        Assert.IsFalse(context.EffectiveScenario.ForceShownTargets.Contains(
            "HudStorageObjective"));
        Assert.IsTrue(context.EffectiveScenario.ForceShownTargets.Contains(
            "G_Bar"));
        Assert.AreEqual(
            "Gameplay HUD",
            context.EffectiveScenario.ToString());
    }

    [TestMethod]
    public async Task RecoverySnapshotNeverOverwritesTheSourceFile()
    {
        using TestDirectory directory = new();
        string sourcePath = directory.File("source.xui");
        string recoveryDirectory = directory.File("recovery");
        const string original =
            "<XuiCanvas><Properties><Width>1</Width></Properties></XuiCanvas>";
        await File.WriteAllTextAsync(sourcePath, original);
        XuiDocument document = await XuiDocument.OpenAsync(sourcePath);
        XuiPropertyEntry width = XuiModelReader.GetProperty(
            document.Root,
            document.Text,
            "Width")!;
        document.Execute(XuiCommandFactory.SetElementValue(
            document,
            width.Element,
            "2"));

        RecoverySnapshot snapshot = await RecoveryService.WriteAsync(
            document,
            recoveryDirectory);
        IReadOnlyList<RecoverySnapshot> discovered =
            RecoveryService.Find(recoveryDirectory);

        Assert.AreEqual(original, await File.ReadAllTextAsync(sourcePath));
        StringAssert.Contains(
            await File.ReadAllTextAsync(snapshot.ContentPath),
            "<Width>2</Width>");
        Assert.HasCount(1, discovered);
        RecoveryService.Delete(snapshot);
        Assert.IsFalse(File.Exists(snapshot.ContentPath));
        Assert.IsFalse(File.Exists(snapshot.MetadataPath));
    }

    [TestMethod]
    public void EditorSourcesContainNoAudioPlaybackApis()
    {
        string root = FindDesktopRoot();
        string sourceRoot = Path.Combine(root, "src", "XuiEditor.Wpf");
        string combined = string.Join(
            '\n',
            Directory.EnumerateFiles(
                    sourceRoot,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(static path =>
                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("SoundPlayer", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaPlayer", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("<MediaElement", combined, StringComparison.Ordinal);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static string FindDesktopRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "XuiEditor.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ??
               throw new DirectoryNotFoundException(
                   "Could not locate the XUI Editor desktop source root.");
    }

    private static string CreateLargeHierarchy(int groups, int itemsPerGroup)
    {
        StringBuilder source = new(
            "<XuiCanvas><Properties><Width>1280</Width><Height>720</Height></Properties>");
        int itemIndex = 0;
        for (int groupIndex = 0; groupIndex < groups; groupIndex++)
        {
            _ = source.Append(
                System.Globalization.CultureInfo.InvariantCulture,
                $"<AdvGroup><Properties><Id>Group{groupIndex:000}</Id></Properties>");
            for (int childIndex = 0; childIndex < itemsPerGroup; childIndex++)
            {
                _ = source.Append(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"<MyImage><Properties><Id>Item{itemIndex:00000}</Id><Width>10</Width><Height>10</Height></Properties></MyImage>");
                itemIndex++;
            }

            _ = source.Append("</AdvGroup>");
        }

        _ = source.Append(
            "<Timelines><Timeline><Id>Item02199</Id><TimelineProp>Opacity</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>1</Prop></KeyFrame>" +
            "</Timeline></Timelines></XuiCanvas>");
        return source.ToString();
    }
}
