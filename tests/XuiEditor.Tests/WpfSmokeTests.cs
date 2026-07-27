using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Wpf;
using XuiEditor.Wpf.Controls;
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
    public void WhiteAliasRendersAsAuthoredSolidColor()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<IUIAARectangle><Properties><Id>Fill</Id><Width>100</Width><Height>100</Height>" +
            "<ImagePath>white</ImagePath><Color>0xff274665</Color>" +
            "<Material>menu_antialias.mat</Material></Properties></IUIAARectangle>" +
            "</XuiCanvas>");
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(100, 100),
            0);
        XuiViewportControl viewport = new()
        {
            Width = 240,
            Height = 240,
            ShowGrid = false,
            ShowSafeArea = false,
            ShowUnknownBounds = false,
        };
        viewport.SetFrame(frame);
        viewport.Measure(new Size(240, 240));
        viewport.Arrange(new Rect(0, 0, 240, 240));
        viewport.UpdateLayout();
        RenderTargetBitmap bitmap = new(
            240,
            240,
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(viewport);

        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(120, 120, 1, 1), pixel, 4, 0);
        Assert.AreEqual(0x65, pixel[0], 1);
        Assert.AreEqual(0x46, pixel[1], 1);
        Assert.AreEqual(0x27, pixel[2], 1);
        Assert.AreEqual(0xff, pixel[3], 1);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ViewportZoomsToFivePercentAndBackWithoutRulerFailure()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>1280</Width><Height>720</Height>" +
            "</Properties></XuiCanvas>");
        XuiViewportControl viewport = new()
        {
            Width = 900,
            Height = 600,
        };
        viewport.SetFrame(DyingLightLayoutEngine.Evaluate(
            document,
            XuiViewport.Default,
            0));
        viewport.Measure(new Size(900, 600));
        viewport.Arrange(new Rect(0, 0, 900, 600));
        viewport.UpdateLayout();

        for (int index = 0; index < 80; index++)
        {
            viewport.ZoomBy(1 / 1.2);
        }

        Assert.AreEqual(0.05, viewport.Zoom, 0.000001);
        RenderTargetBitmap bitmap = new(
            900,
            600,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(viewport);

        MouseWheelEventArgs wheel = new(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            -120)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
            Source = viewport,
        };
        viewport.RaiseEvent(wheel);
        Assert.AreEqual(0.05, viewport.Zoom, 0.000001);

        for (int index = 0; index < 100; index++)
        {
            viewport.ZoomBy(1.2);
        }

        Assert.AreEqual(32, viewport.Zoom, 0.000001);
        bitmap.Render(viewport);
    }

    [TestMethod]
    public void RulerStepIsFiniteAcrossFormerFailureBoundaryAndInvalidScales()
    {
        Assert.AreEqual(500, XuiViewportControl.SelectRulerStep(0.09));
        Assert.AreEqual(1000, XuiViewportControl.SelectRulerStep(0.089));
        Assert.AreEqual(10, XuiViewportControl.SelectRulerStep(10));

        double[] scales =
        [
            double.Epsilon,
            0,
            -1,
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
        ];
        foreach (double scale in scales)
        {
            double step = XuiViewportControl.SelectRulerStep(scale);
            string message = scale.ToString(CultureInfo.InvariantCulture);
            Assert.IsTrue(double.IsFinite(step), message);
            Assert.IsGreaterThan(0, step, message);
        }
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void NativeControlGalleryUsesDarkClientAreaTemplates()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        CheckBox checkBox = new()
        {
            Content = "Loop",
            IsChecked = true,
            Margin = new Thickness(8),
        };
        Slider slider = new()
        {
            Minimum = 0,
            Maximum = 10,
            Value = 4,
            TickFrequency = 1,
            TickPlacement = TickPlacement.BottomRight,
            Margin = new Thickness(8),
        };
        ScrollBar horizontalScroll = new()
        {
            Orientation = Orientation.Horizontal,
            Minimum = 0,
            Maximum = 100,
            Value = 40,
            ViewportSize = 20,
            Height = 14,
            Margin = new Thickness(8),
        };
        ScrollBar verticalScroll = new()
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 100,
            Value = 40,
            ViewportSize = 20,
            Height = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8),
        };
        Expander expander = new()
        {
            Header = "Raw XML",
            Content = new TextBlock { Text = "<Properties />" },
            IsExpanded = true,
            Margin = new Thickness(8),
        };
        ToolTip toolTip = new()
        {
            Content = "Dark tooltip",
            Style = (Style)application.Resources[typeof(ToolTip)],
        };
        ContextMenu contextMenu = new()
        {
            Items = { new MenuItem { Header = "Dark menu item" } },
            Style = (Style)application.Resources[typeof(ContextMenu)],
        };
        StackPanel gallery = new()
        {
            Width = 520,
            Height = 360,
            Background = (Brush)application.Resources["WindowBrush"],
            Children =
            {
                checkBox,
                slider,
                horizontalScroll,
                verticalScroll,
                expander,
                new Separator(),
            },
        };
        gallery.Measure(new Size(520, 360));
        gallery.Arrange(new Rect(0, 0, 520, 360));
        gallery.UpdateLayout();
        toolTip.ApplyTemplate();
        contextMenu.ApplyTemplate();

        Border checkBorder = (Border)checkBox.Template.FindName(
            "CheckBorder",
            checkBox);
        Track sliderTrack = (Track)slider.Template.FindName(
            "PART_Track",
            slider);
        Track horizontalTrack = (Track)horizontalScroll.Template.FindName(
            "PART_Track",
            horizontalScroll);
        Border expandSite = (Border)expander.Template.FindName(
            "ExpandSite",
            expander);
        Assert.AreEqual(
            ((SolidColorBrush)application.Resources["AccentBrush"]).Color,
            ((SolidColorBrush)checkBorder.Background).Color);
        Assert.AreEqual(4, sliderTrack.Value, 0.001);
        Assert.AreEqual(40, horizontalTrack.Value, 0.001);
        Assert.AreEqual(Visibility.Visible, expandSite.Visibility);
        Assert.IsTrue(IsDark(toolTip.Background));
        Assert.IsTrue(IsDark(contextMenu.Background));

        RenderTargetBitmap bitmap = new(
            520,
            360,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(gallery);
        int stride = 520 * 4;
        byte[] pixels = new byte[stride * 360];
        bitmap.CopyPixels(pixels, stride, 0);
        int nearlyWhitePixels = 0;
        for (int offset = 0; offset <= pixels.Length - 4; offset += 4)
        {
            if (pixels[offset] >= 245 &&
                pixels[offset + 1] >= 245 &&
                pixels[offset + 2] >= 245 &&
                pixels[offset + 3] > 0)
            {
                nearlyWhitePixels++;
            }
        }

        Assert.IsLessThan(
            520 * 360 * 0.03,
            nearlyWhitePixels,
            "A native control rendered a large system-light surface.");
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

    private static bool IsDark(Brush brush) =>
        brush is SolidColorBrush solid &&
        solid.Color.R < 128 &&
        solid.Color.G < 128 &&
        solid.Color.B < 128;

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
