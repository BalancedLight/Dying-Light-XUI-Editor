using System.Globalization;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Values;
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
    public void SystemAndBitmapTextPathsUsePerRunGlyphColors()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>300</Width><Height>80</Height></Properties>" +
            "<MyText><Properties><Id>Colored</Id><Width>300</Width><Height>80</Height>" +
            "<Text>A%COLOR(F90F36)BC%COLOR(reset)D</Text>" +
            "<TextColor>0xff112233</TextColor><PointSize>32</PointSize>" +
            "<ColorControlSequenceEnabled>true</ColorControlSequenceEnabled>" +
            "<Outline>true</Outline><OutlineColor>0xff445566</OutlineColor>" +
            "<Shadow>true</Shadow><ShadowColor>0xff778899</ShadowColor>" +
            "</Properties></MyText></XuiCanvas>");
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(300, 80),
            0);
        XuiRenderNode node = frame.Nodes.Single(static candidate =>
            candidate.Id == "Colored");
        XuiViewportControl viewport = new()
        {
            Width = 500,
            Height = 220,
            ShowGrid = false,
            ShowSafeArea = false,
        };
        viewport.SetFrame(frame);
        viewport.Measure(new Size(500, 220));
        viewport.Arrange(new Rect(0, 0, 500, 220));
        viewport.UpdateLayout();

        IReadOnlyList<XuiColor> drawingColors =
            viewport.RetainedNodeBrushColorsForTesting(node.Key);
        CollectionAssert.Contains(
            drawingColors.ToList(),
            new XuiColor(255, 17, 34, 51));
        CollectionAssert.Contains(
            drawingColors.ToList(),
            new XuiColor(255, 249, 15, 54));
        CollectionAssert.Contains(
            drawingColors.ToList(),
            new XuiColor(255, 68, 85, 102));
        CollectionAssert.Contains(
            drawingColors.ToList(),
            new XuiColor(255, 119, 136, 153));

        CollectionAssert.AreEqual(
            new[]
            {
                new XuiColor(255, 17, 34, 51),
                new XuiColor(255, 249, 15, 54),
                new XuiColor(255, 249, 15, 54),
                new XuiColor(255, 17, 34, 51),
            },
            XuiViewportControl.BitmapGlyphColorsForTesting(node).ToArray());
    }

    [TestMethod]
    public void MissingBitmapUnicodeGlyphKeepsTheReadableSystemFontPath()
    {
        XuiBitmapGlyph question = new(
            '?',
            8,
            new XuiRect(0, 0, 8, 16),
            0,
            false);
        XuiBitmapGlyph latin = new(
            'A',
            8,
            new XuiRect(8, 0, 8, 16),
            0,
            false);
        XuiBitmapGlyph japanese = new(
            'ミ',
            16,
            new XuiRect(16, 0, 16, 16),
            0,
            false);
        XuiBitmapFontMetrics metrics = new(
            "jp",
            "Japanese",
            64,
            64,
            16,
            new Dictionary<int, XuiBitmapGlyph>
            {
                ['?'] = question,
                ['A'] = latin,
                ['ミ'] = japanese,
            },
            "jp.fm");

        Assert.IsTrue(
            XuiViewportControl.BitmapFontSupportsTextForTesting(
                metrics,
                "Aミ"));
        Assert.IsFalse(
            XuiViewportControl.BitmapFontSupportsTextForTesting(
                metrics,
                "日本"),
            "The '?' glyph must not make an incomplete atlas appear " +
            "Unicode-capable.");
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void TextInspectorOffersTypedColorControlCheckboxAndCommitsIt()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<IUIProgressText><Properties><Id>Text</Id><Width>80</Width><Height>20</Height>" +
            "<Text>value</Text></Properties></IUIProgressText></XuiCanvas>");
        XuiSyntaxNode textNode =
            XuiModelReader.VisualDescendants(document.Root)
                .Single(node =>
                    XuiModelReader.GetId(node, document.Text) == "Text");
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        window.SelectNodeKeysForTesting([textNode.Key]);

        InspectorPropertyRow row =
            window.InspectorProperties.Single(property =>
                property.Name == "ColorControlSequenceEnabled");
        Assert.IsTrue(row.IsBooleanToggle);
        Assert.AreEqual("Text / Image", row.Category);
        Assert.AreEqual(false, row.BooleanValue);
        Assert.IsFalse(row.IsUnknown);

        window.SetInspectorBooleanForTesting(
            "ColorControlSequenceEnabled",
            true);

        Assert.AreEqual(
            "true",
            XuiModelReader.GetPropertyValue(
                document.SyntaxTree.FindByKey(textNode.Key)!,
                document.Text,
                "ColorControlSequenceEnabled"));
    }

    [TestMethod]
    public void BitmapFontSpecialGlyphsUseRgbMaskChannel()
    {
        Assert.AreEqual(
            24,
            XuiViewportControl.SelectFontMaskCoverage(
                blue: 8,
                green: 16,
                red: 24,
                alpha: 200,
                variableAlpha: true,
                specialGlyph: true));
        Assert.AreEqual(
            200,
            XuiViewportControl.SelectFontMaskCoverage(
                blue: 8,
                green: 16,
                red: 24,
                alpha: 200,
                variableAlpha: true,
                specialGlyph: false));
        Assert.AreEqual(
            24,
            XuiViewportControl.SelectFontMaskCoverage(
                blue: 8,
                green: 16,
                red: 24,
                alpha: byte.MaxValue,
                variableAlpha: false,
                specialGlyph: false));
    }

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
    public void ViewportLoadingOverlayIsNestedAndUsesAnimatedPackagedGears()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        using MainWindow window = new();

        Assert.IsFalse(window.ViewportLoadingOverlayVisibleForTesting);
        Uri animationUri = new(
            "/DyingLightXuiEditor;component/Assets/MovingGears.gif",
            UriKind.Relative);
        using Stream animationStream =
            Application.GetResourceStream(animationUri).Stream;
        GifBitmapDecoder decoder = new(
            animationStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        Assert.IsGreaterThan(
            1,
            decoder.Frames.Count,
            "The packaged loading image must retain multiple GIF frames.");

        using (window.BeginViewportLoadingForTesting())
        {
            Assert.IsTrue(window.ViewportLoadingOverlayVisibleForTesting);
            using (window.BeginViewportLoadingForTesting())
            {
                Assert.IsTrue(window.ViewportLoadingOverlayVisibleForTesting);
            }

            Assert.IsTrue(window.ViewportLoadingOverlayVisibleForTesting);
        }

        Assert.IsFalse(window.ViewportLoadingOverlayVisibleForTesting);
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
    public void TransparentSnapshotExportsVisibleXuiAtTwoTimesDesignResolution()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>50</Height></Properties>" +
            "<EngineRuntimeMystery><Properties><Id>Unknown</Id><Width>100</Width>" +
            "<Height>50</Height></Properties></EngineRuntimeMystery>" +
            "<IUIAARectangle><Properties><Id>Visible</Id><Width>20</Width>" +
            "<Height>20</Height><Position>10,10,0</Position>" +
            "<ImagePath>white</ImagePath><Color>0xffff0000</Color>" +
            "<Material>menu_antialias.mat</Material></Properties></IUIAARectangle>" +
            "<IUIAARectangle><Properties><Id>Hidden</Id><Width>20</Width>" +
            "<Height>20</Height><Position>40,10,0</Position>" +
            "<ImagePath>white</ImagePath><Color>0xff0000ff</Color>" +
            "<Material>menu_antialias.mat</Material></Properties></IUIAARectangle>" +
            "</XuiCanvas>");
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode hidden =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Hidden");
        window.SetEditorHiddenForTesting(hidden.Key, hidden: true);
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"xui-transparent-snapshot-{Guid.NewGuid():N}.png");

        try
        {
            BitmapSource bitmap =
                window.ExportTransparentPngForTesting(outputPath);

            Assert.AreEqual(200, bitmap.PixelWidth);
            Assert.AreEqual(100, bitmap.PixelHeight);
            Assert.IsTrue(File.Exists(outputPath));
            Assert.IsTrue(window.ViewportForTesting.ShowUnknownBounds);

            using FileStream stream = File.OpenRead(outputPath);
            PngBitmapDecoder decoder = new(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            BitmapFrame png = decoder.Frames.Single();
            Assert.AreEqual(200, png.PixelWidth);
            Assert.AreEqual(100, png.PixelHeight);

            byte[] transparent = new byte[4];
            png.CopyPixels(
                new Int32Rect(4, 4, 1, 1),
                transparent,
                4,
                0);
            Assert.AreEqual(
                0,
                transparent[3],
                "Canvas chrome and unknown-control bounds must not be exported.");

            byte[] red = new byte[4];
            png.CopyPixels(
                new Int32Rect(30, 30, 1, 1),
                red,
                4,
                0);
            Assert.AreEqual(0x00, red[0], 1);
            Assert.AreEqual(0x00, red[1], 1);
            Assert.AreEqual(0xff, red[2], 1);
            Assert.AreEqual(0xff, red[3], 1);

            byte[] hiddenBlue = new byte[4];
            png.CopyPixels(
                new Int32Rect(90, 30, 1, 1),
                hiddenBlue,
                4,
                0);
            Assert.AreEqual(
                0,
                hiddenBlue[3],
                "Editor-hidden nodes must stay hidden in the snapshot.");
        }
        finally
        {
            File.Delete(outputPath);
        }
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
    public void MainMenuDropdownUsesEditorOwnedDarkTemplate()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        MenuItem child = new()
        {
            Header = "Open",
            InputGestureText = "Ctrl+O",
        };
        MenuItem root = new()
        {
            Header = "File",
            Items = { child },
        };
        Menu menu = new()
        {
            Items = { root },
        };
        Window host = new()
        {
            Width = 360,
            Height = 180,
            Content = menu,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
        };

        try
        {
            host.Show();
            root.ApplyTemplate();
            root.IsSubmenuOpen = true;
            host.Dispatcher.Invoke(
                static () => { },
                System.Windows.Threading.DispatcherPriority.Render);
            child.ApplyTemplate();

            Border itemBorder = (Border)child.Template.FindName(
                "ItemBorder",
                child);
            Border submenuBorder = (Border)root.Template.FindName(
                "SubmenuBorder",
                root);
            Assert.IsTrue(root.OverridesDefaultStyle);
            Assert.IsTrue(child.OverridesDefaultStyle);
            Assert.IsTrue(IsDark(itemBorder.Background));
            Assert.IsTrue(IsDark(submenuBorder.Background));
            Assert.AreEqual(
                ((SolidColorBrush)application.Resources["AccentBrush"]).Color,
                ((SolidColorBrush)submenuBorder.BorderBrush).Color);
        }
        finally
        {
            root.IsSubmenuOpen = false;
            host.Close();
        }
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void PreviewControlsAreAbsentFromToolbarAndManualDataGrid()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        using MainWindow window = new();
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(1280, 760));
        content.Arrange(new Rect(0, 0, 1280, 760));
        content.UpdateLayout();

        Assert.IsNull(window.FindName("PreviewScenarioCombo"));
        Assert.IsNull(window.FindName("PreviewPropertiesGrid"));
        Assert.IsFalse(
            Descendants(content)
                .OfType<TabItem>()
                .Any(static tab =>
                    string.Equals(
                        tab.Header?.ToString(),
                        "Preview Data",
                        StringComparison.Ordinal)));
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ResourceSettingsCommitSelectedInstallLanguage()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        using TestDirectory directory = new();
        string install = directory.File("Dying Light");
        string dw = Path.Combine(install, "DW");
        Directory.CreateDirectory(dw);
        File.WriteAllBytes(
            Path.Combine(install, "DyingLightGame.exe"),
            []);
        File.WriteAllBytes(Path.Combine(dw, "Data0.pak"), []);
        File.WriteAllBytes(Path.Combine(dw, "DataJp.pak"), []);
        EditorSettings settings = new()
        {
            DyingLightInstallPath = install,
            Locale = "En",
        };
        AssetRootsWindow window = new(settings);
        ComboBox locale = (ComboBox)window.FindName("LocaleCombo");
        Button accept = (Button)window.FindName("AcceptButton");

        Assert.IsFalse(locale.IsEditable);
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            locale.SelectedItem = "Jp";
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));

        Assert.AreEqual(true, window.ShowDialog());
        Assert.AreEqual("Jp", settings.Locale);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void RetainedViewportUpdatesCameraAndLiveTransformsWithoutRepaintingNodes()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<AdvGroup><Properties><Id>Parent</Id><Width>50</Width><Height>50</Height>" +
            "<Position>10,10,0</Position><ClipChildren>1</ClipChildren></Properties>" +
            "<IUIAARectangle><Properties><Id>Child</Id><Width>10</Width><Height>10</Height>" +
            "<Position>5,5,0</Position><ImagePath>white</ImagePath>" +
            "<Color>0xff445566</Color></Properties></IUIAARectangle></AdvGroup>" +
            "</XuiCanvas>");
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(100, 100),
            0);
        XuiRenderNode parent = frame.Nodes.Single(static node =>
            node.Id == "Parent");
        XuiRenderNode child = frame.Nodes.Single(static node =>
            node.Id == "Child");
        XuiViewportControl viewport = new()
        {
            Width = 600,
            Height = 400,
            ShowGrid = false,
            ShowSafeArea = false,
        };
        viewport.SetFrame(frame);
        viewport.Measure(new Size(600, 400));
        viewport.Arrange(new Rect(0, 0, 600, 400));
        viewport.UpdateLayout();

        Assert.AreEqual(
            frame.Nodes.Count,
            viewport.RetainedNodeVisualCountForTesting);
        Assert.IsTrue(
            viewport.RetainedContainerHasClipForTesting(parent.Key));
        Assert.IsTrue(
            viewport.RetainedContainerHasClipForTesting(child.Key));
        long redraws = viewport.NodeContentRedrawCountForTesting;
        long presentationUpdates =
            viewport.NodePresentationUpdateCountForTesting;
        long cameraUpdates = viewport.CameraUpdateCountForTesting;
        viewport.SetFrame(frame);
        Assert.AreEqual(
            redraws,
            viewport.NodeContentRedrawCountForTesting);
        Assert.AreEqual(
            presentationUpdates,
            viewport.NodePresentationUpdateCountForTesting);
        Assert.AreEqual(
            cameraUpdates,
            viewport.CameraUpdateCountForTesting);

        viewport.ZoomBy(1.2);
        Assert.AreEqual(redraws, viewport.NodeContentRedrawCountForTesting);
        Assert.IsGreaterThan(
            cameraUpdates,
            viewport.CameraUpdateCountForTesting);

        viewport.SetSelectedKeys([parent.Key, child.Key]);
        var originalParent =
            viewport.RetainedLocalTransformForTesting(parent.Key);
        var originalChild =
            viewport.RetainedLocalTransformForTesting(child.Key);
        viewport.PreviewTransformForTesting(
            parent.Key,
            XuiTransformKind.Move,
            new XuiVector2(8, 6));
        var movedParent =
            viewport.RetainedLocalTransformForTesting(parent.Key);
        var unchangedChild =
            viewport.RetainedLocalTransformForTesting(child.Key);
        Assert.AreNotEqual(originalParent.M31, movedParent.M31);
        Assert.AreEqual(originalChild, unchangedChild);
        Assert.AreEqual(redraws, viewport.NodeContentRedrawCountForTesting);
        viewport.CancelTransformForTesting();
        Assert.AreEqual(
            originalParent,
            viewport.RetainedLocalTransformForTesting(parent.Key));

        viewport.PreviewTransformForTesting(
            parent.Key,
            XuiTransformKind.Rotate,
            default,
            rotationDelta: 30);
        Assert.AreNotEqual(
            originalParent,
            viewport.RetainedLocalTransformForTesting(parent.Key));
        viewport.CancelTransformForTesting();
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void MultiSelectionRotationCommitsRootNodesAsOneUndoStep()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<AdvGroup><Properties><Id>Parent</Id><Width>50</Width><Height>50</Height></Properties>" +
            "<MyImage><Properties><Id>Child</Id><Width>10</Width><Height>10</Height>" +
            "</Properties></MyImage></AdvGroup>" +
            "<MyImage><Properties><Id>Other</Id><Width>10</Width><Height>10</Height>" +
            "</Properties></MyImage></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode parent = XuiModelReader.VisualDescendants(document.Root)
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Parent");
        XuiSyntaxNode child = XuiModelReader.VisualDescendants(document.Root)
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Child");
        XuiSyntaxNode other = XuiModelReader.VisualDescendants(document.Root)
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Other");
        window.SelectNodeKeysForTesting(
            [parent.Key, child.Key, other.Key]);

        window.CommitTransformForTesting(
            new XuiTransformCommittedEventArgs(
                parent.Key,
                XuiTransformKind.Rotate,
                default,
                default,
                30,
                default));

        XuiSyntaxNode currentParent =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Parent");
        XuiSyntaxNode currentChild =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Child");
        XuiSyntaxNode currentOther =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Other");
        Assert.AreEqual(
            "30.000000",
            XuiModelReader.GetPropertyValue(
                currentParent,
                document.Text,
                "Rotation"));
        Assert.IsNull(XuiModelReader.GetPropertyValue(
            currentChild,
            document.Text,
            "Rotation"));
        Assert.AreEqual(
            "30.000000",
            XuiModelReader.GetPropertyValue(
                currentOther,
                document.Text,
                "Rotation"));
        Assert.AreEqual(
            "Rotate selection",
            document.History.UndoDescription);

        document.Undo();
        Assert.AreEqual(source, document.Text);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void MoveCommitUsesTheSamePositiveAuthoredDeltaForEveryAnchor()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<MyImage><Properties><Id>Leading</Id><Width>10</Width><Height>10</Height>" +
            "<Position>1,2,0</Position><Anchor>3</Anchor></Properties></MyImage>" +
            "<MyImage><Properties><Id>Trailing</Id><Width>10</Width><Height>10</Height>" +
            "<Position>3,4,0</Position><Anchor>12</Anchor></Properties></MyImage>" +
            "<MyImage><Properties><Id>Centered</Id><Width>10</Width><Height>10</Height>" +
            "<Position>5,6,0</Position><Anchor>48</Anchor></Properties></MyImage>" +
            "</XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode[] nodes = XuiModelReader.VisualDescendants(document.Root)
            .ToArray();
        window.SelectNodeKeysForTesting(nodes.Select(static node => node.Key));

        window.CommitTransformForTesting(
            new XuiTransformCommittedEventArgs(
                nodes[0].Key,
                XuiTransformKind.Move,
                new XuiVector2(7, 9),
                default,
                0,
                default));

        Dictionary<string, string?> positions =
            XuiModelReader.VisualDescendants(document.Root)
                .ToDictionary(
                    node => XuiModelReader.GetId(node, document.Text)!,
                    node => XuiModelReader.GetPropertyValue(
                        node,
                        document.Text,
                        "Position"),
                    StringComparer.Ordinal);
        Assert.AreEqual("8.000000,11.000000,0.000000", positions["Leading"]);
        Assert.AreEqual("10.000000,13.000000,0.000000", positions["Trailing"]);
        Assert.AreEqual("12.000000,15.000000,0.000000", positions["Centered"]);
        Assert.AreEqual("Move selection", document.History.UndoDescription);

        document.Undo();
        Assert.AreEqual(source, document.Text);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ViewportHitTestingPrefersSelectedHiddenBodyAndNeverCanvas()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><MyImage><Properties><Id>Hidden</Id>" +
            "<Width>20</Width><Height>20</Height><Position>10,10,0</Position>" +
            "<Show>false</Show></Properties></MyImage></XuiCanvas>");
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(100, 100),
            0);
        XuiRenderNode hidden = frame.Nodes.Single(static node =>
            node.Id == "Hidden");
        XuiViewportControl viewport = new();
        viewport.SetFrame(frame);
        viewport.SetSelectedKeys([hidden.Key]);

        Assert.IsNull(viewport.HitSelectionKeyForTesting(
            new XuiVector2(15, 15)));
        Assert.AreEqual(
            hidden.Key,
            viewport.HitSelectionKeyForTesting(
                new XuiVector2(15, 15),
                selectedBodyFirst: true));
        Assert.IsNull(viewport.HitSelectionKeyForTesting(
            new XuiVector2(90, 90)));
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void AlternateHitTestingCyclesOverlappingSelectionOwners()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><MyImage><Properties><Id>Back</Id><Width>30</Width>" +
            "<Height>30</Height><Position>10,10,0</Position></Properties></MyImage>" +
            "<MyImage><Properties><Id>Front</Id><Width>30</Width>" +
            "<Height>30</Height><Position>10,10,0</Position></Properties></MyImage>" +
            "</XuiCanvas>");
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(100, 100),
            0);
        XuiRenderNode back = frame.Nodes.Single(static node =>
            node.Id == "Back");
        XuiRenderNode front = frame.Nodes.Single(static node =>
            node.Id == "Front");
        XuiViewportControl viewport = new();
        viewport.SetFrame(frame);
        viewport.SetSelectedKeys([front.Key]);

        Assert.AreEqual(
            front.Key,
            viewport.HitSelectionKeyForTesting(new XuiVector2(15, 15)));
        Assert.AreEqual(
            back.Key,
            viewport.HitSelectionKeyForTesting(
                new XuiVector2(15, 15),
                cycle: true));
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void CanvasRootTransformCommitIsRejectedWithoutChangingSource()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><MyImage><Properties><Id>Child</Id><Width>10</Width>" +
            "<Height>10</Height></Properties></MyImage></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        window.SelectNodeKeysForTesting([document.Root.Key]);

        window.CommitTransformForTesting(
            new XuiTransformCommittedEventArgs(
                document.Root.Key,
                XuiTransformKind.Move,
                new XuiVector2(10, 10),
                default,
                0,
                default));

        Assert.AreEqual(source, document.Text);
        Assert.IsFalse(document.History.CanUndo);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void MovingAnimatedPositionOffsetsAuthoredAndEveryKeyAsOneEdit()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><MyImage><Properties><Id>Animated</Id>" +
            "<Width>10</Width><Height>10</Height><Position>5,6,0</Position>" +
            "</Properties></MyImage><Timelines><Timeline><Id>Animated</Id>" +
            "<TimelineProp>Position</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation>" +
            "<Prop>10,20,0</Prop></KeyFrame>" +
            "<KeyFrame><Time>10</Time><Interpolation>0</Interpolation>" +
            "<Prop>30,40,0</Prop></KeyFrame></Timeline></Timelines></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode animated =
            XuiModelReader.VisualDescendants(document.Root).Single();
        window.SelectNodeKeysForTesting([animated.Key]);

        window.CommitTransformForTesting(
            new XuiTransformCommittedEventArgs(
                animated.Key,
                XuiTransformKind.Move,
                new XuiVector2(2, 3),
                default,
                0,
                default));

        XuiSyntaxNode current =
            XuiModelReader.VisualDescendants(document.Root).Single();
        Assert.AreEqual(
            "7.000000,9.000000,0.000000",
            XuiModelReader.GetPropertyValue(
                current,
                document.Text,
                "Position"));
        string[] keyPositions = document.Root
            .FirstElement("Timelines")!
            .FirstElement("Timeline")!
            .Elements("KeyFrame")
            .Select(frameNode =>
                frameNode.Elements("Prop").Single()
                    .GetDecodedValue(document.Text))
            .ToArray();
        Assert.HasCount(2, keyPositions);
        Assert.AreEqual(
            "12.000000,23.000000,0.000000",
            keyPositions[0]);
        Assert.AreEqual(
            "32.000000,43.000000,0.000000",
            keyPositions[1]);
        Assert.AreEqual("Move selection", document.History.UndoDescription);
        CollectionAssert.Contains(
            window.SelectedKeysForTesting.ToList(),
            current.Key);

        document.Undo();
        Assert.AreEqual(source, document.Text);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void MalformedAnimatedPositionRejectsTheWholeMove()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><MyImage><Properties><Id>Animated</Id>" +
            "<Width>10</Width><Height>10</Height><Position>5,6,0</Position>" +
            "</Properties></MyImage><Timelines><Timeline><Id>Animated</Id>" +
            "<TimelineProp>Show</TimelineProp><TimelineProp>Position</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation>" +
            "<Prop>true</Prop><Prop>10,20,0</Prop></KeyFrame>" +
            "<KeyFrame><Time>10</Time><Interpolation>0</Interpolation>" +
            "<Prop>false</Prop></KeyFrame></Timeline></Timelines></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode animated =
            XuiModelReader.VisualDescendants(document.Root).Single();
        window.SelectNodeKeysForTesting([animated.Key]);

        window.CommitTransformForTesting(
            new XuiTransformCommittedEventArgs(
                animated.Key,
                XuiTransformKind.Move,
                new XuiVector2(2, 3),
                default,
                0,
                default));

        Assert.AreEqual(source, document.Text);
        Assert.IsFalse(document.History.CanUndo);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void MovingOffsetsPositionKeysInEveryApplicableScope()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><AdvGroup><Properties><Id>Group</Id></Properties>" +
            "<MyImage><Properties><Id>Animated</Id><Width>10</Width>" +
            "<Height>10</Height><Position>1,2,0</Position></Properties></MyImage>" +
            "<Timelines><Timeline><Id>Animated</Id><TimelineProp>Position</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation>" +
            "<Prop>3,4,0</Prop></KeyFrame></Timeline></Timelines></AdvGroup>" +
            "<Timelines><Timeline><Id>Animated</Id><TimelineProp>Position</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation>" +
            "<Prop>5,6,0</Prop></KeyFrame></Timeline></Timelines></XuiCanvas>");
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode animated =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Animated");

        window.CommitTransformForTesting(
            new XuiTransformCommittedEventArgs(
                animated.Key,
                XuiTransformKind.Move,
                new XuiVector2(10, 20),
                default,
                0,
                default));

        string[] positionKeys = document.Root
            .DescendantsAndSelf()
            .Where(static node => node.Name == "Prop")
            .Select(node => node.GetDecodedValue(document.Text))
            .ToArray();
        Assert.HasCount(2, positionKeys);
        CollectionAssert.AreEquivalent(
            new List<string>
            {
                "13.000000,24.000000,0.000000",
                "15.000000,26.000000,0.000000",
            },
            positionKeys);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void InspectorAddChildSelectsTheNewUndoableElement()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><AdvGroup><Properties><Id>Parent</Id>" +
            "<Width>80</Width><Height>60</Height></Properties>" +
            "<Timelines><NamedFrames /></Timelines></AdvGroup></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode parent =
            XuiModelReader.VisualDescendants(document.Root).Single();

        window.AddChildForTesting(
            parent.Key,
            new XuiElementCreationRequest
            {
                Preset = XuiElementPreset.Rectangle,
                Id = "R_New",
                Width = 30,
                Height = 20,
                Position = new XuiVector3(4, 5, 0),
                Color = "0xff102030",
            });

        XuiSyntaxNode created =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "R_New");
        CollectionAssert.Contains(
            window.SelectedKeysForTesting.ToList(),
            created.Key);
        Assert.IsTrue(window.ExpandedKeysForTesting.Contains(
            created.Parent!.Key));
        Assert.AreEqual("Add R_New", document.History.UndoDescription);
        Assert.IsLessThan(
            document.Text.IndexOf("<Timelines>", StringComparison.Ordinal),
            document.Text.IndexOf("<IUIAARectangle>", StringComparison.Ordinal));

        document.Undo();
        Assert.AreEqual(source, document.Text);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void InspectorAddParentWrapsAndSelectsTheNewGroup()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>80</Height>" +
            "</Properties><MyImage><Properties><Id>Child</Id><Width>20</Width>" +
            "<Height>10</Height><Position>4,5,0</Position></Properties>" +
            "</MyImage></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode child =
            XuiModelReader.VisualDescendants(document.Root).Single();

        window.AddParentForTesting(
            child.Key,
            new XuiElementCreationRequest
            {
                Preset = XuiElementPreset.Group,
                Id = "G_Parent",
                Width = 100,
                Height = 80,
                Position = default,
            });

        XuiSyntaxNode wrapper =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "G_Parent");
        XuiSyntaxNode currentChild =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Child");
        Assert.AreSame(wrapper, currentChild.Parent);
        CollectionAssert.Contains(
            window.SelectedKeysForTesting.ToList(),
            wrapper.Key);
        Assert.IsTrue(window.ExpandedKeysForTesting.Contains(wrapper.Key));
        Assert.AreEqual(
            "Add parent G_Parent",
            document.History.UndoDescription);

        document.Undo();
        Assert.AreEqual(source, document.Text);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void InspectorAddPropertyPreservesSelectionAndIsUndoable()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>80</Height>" +
            "</Properties><MyImage><Properties><Id>Image</Id><Width>20</Width>" +
            "<Height>10</Height></Properties></MyImage></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode image =
            XuiModelReader.VisualDescendants(document.Root).Single();
        window.SelectNodeKeysForTesting([image.Key]);

        window.AddPropertyForTesting(
            image.Key,
            "Opacity",
            "0.500000");

        XuiSyntaxNode current =
            XuiModelReader.VisualDescendants(document.Root).Single();
        Assert.AreEqual(
            "0.500000",
            XuiModelReader.GetPropertyValue(
                current,
                document.Text,
                "Opacity"));
        CollectionAssert.Contains(
            window.SelectedKeysForTesting.ToList(),
            current.Key);
        Assert.AreEqual(
            "Add Opacity",
            document.History.UndoDescription);

        document.Undo();
        Assert.AreEqual(source, document.Text);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void EffectivePreviewStateLivesUnderAnimationAndHeaderButtonsAreSeparated()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        using MainWindow window = new();

        Assert.IsTrue(window.PreviewStateIsInAnimationTabForTesting);
        Assert.IsTrue(window.PreviewStateIsSeparatedFromTransportForTesting);
        Assert.IsTrue(window.HierarchyHeaderButtonsSeparatedForTesting);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void InspectorExplainsAnimatedHiddenStateWithoutSelectionEvaluation()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><MyImage><Properties><Id>Animated</Id>" +
            "<Width>10</Width><Height>10</Height></Properties></MyImage>" +
            "<Timelines><Timeline><Id>Animated</Id><TimelineProp>Show</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation>" +
            "<Prop>true</Prop></KeyFrame><KeyFrame><Time>5</Time>" +
            "<Interpolation>0</Interpolation><Prop>false</Prop></KeyFrame>" +
            "</Timeline></Timelines></XuiCanvas>");
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode animated =
            XuiModelReader.VisualDescendants(document.Root).Single();
        window.SelectNodeKeysForTesting([animated.Key]);
        window.SetTimelineTickForTesting(5);
        long evaluations = window.LayoutEvaluationCountForTesting;

        window.SelectNodeKeysForTesting([animated.Key]);

        Assert.AreEqual(evaluations, window.LayoutEvaluationCountForTesting);
        StringAssert.Contains(window.PreviewStateForTesting, "tick 5");
        StringAssert.Contains(window.PreviewStateForTesting, "Show animation");
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void TextureDiagnosticsDoNotReevaluateTheDocument()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<MyImage><Properties><Id>Image</Id><Width>10</Width><Height>10</Height>" +
            "</Properties></MyImage></XuiCanvas>");
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        long evaluations = window.LayoutEvaluationCountForTesting;

        window.ApplyTextureDiagnosticsForTesting(
            "test_image",
            [
                new XuiDiagnostic(
                    "XUI-ASSET-TEST",
                    XuiDiagnosticSeverity.Info,
                    "Texture completed."),
            ]);

        Assert.AreEqual(
            evaluations,
            window.LayoutEvaluationCountForTesting);
        Assert.IsTrue(window.FilteredDiagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-ASSET-TEST"));
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void TimelineWaitsForSelectionUnlessAllTracksIsExplicit()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<MyImage><Properties><Id>Animated</Id><Width>10</Width><Height>10</Height>" +
            "</Properties></MyImage><Timelines><Timeline><Id>Animated</Id>" +
            "<TimelineProp>Opacity</TimelineProp><KeyFrame><Time>0</Time>" +
            "<Interpolation>0</Interpolation><Prop>1</Prop></KeyFrame>" +
            "</Timeline></Timelines></XuiCanvas>");
        XuiTimelineSet timelines = XuiTimelineParser.Parse(document);
        TimelineEditorControl control = new();

        control.SetData(timelines, [], 0);
        Assert.AreEqual(0, control.VisibleTrackCountForTesting);
        control.SetData(timelines, [], 0, showAllWhenEmpty: true);
        Assert.AreEqual(1, control.VisibleTrackCountForTesting);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void TimelineWorkspaceFiltersAndRemembersActiveScopeState()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<AdvGroup><Properties><Id>HUD</Id></Properties>" +
            "<AdvGroup><Properties><Id>Group</Id></Properties>" +
            "<MyImage><Properties><Id>I0</Id></Properties></MyImage>" +
            "<MyImage><Properties><Id>I1</Id></Properties></MyImage>" +
            "<Timelines><Timeline><Id>I0</Id><TimelineProp>Show</TimelineProp>" +
            "<TimelineProp>Position</TimelineProp><KeyFrame><Time>0</Time>" +
            "<Interpolation>0</Interpolation><Prop>true</Prop><Prop>0,0,0</Prop>" +
            "</KeyFrame><KeyFrame><Time>20</Time><Interpolation>0</Interpolation>" +
            "<Prop>false</Prop><Prop>20,0,0</Prop></KeyFrame></Timeline>" +
            "<Timeline><Id>I1</Id><TimelineProp>Opacity</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>1</Prop>" +
            "</KeyFrame></Timeline><NamedFrames>" +
            "<NamedFrame><Name>Idle</Name><Time>0</Time></NamedFrame>" +
            "<NamedFrame><Name>Show</Name><Time>1</Time><Command>goto</Command>" +
            "<CommandParams>EndShow</CommandParams></NamedFrame>" +
            "<NamedFrame><Name>EndShow</Name><Time>20</Time></NamedFrame>" +
            "</NamedFrames></Timelines></AdvGroup></AdvGroup>" +
            "<Timelines><Timeline><Id>HUD</Id><TimelineProp>Opacity</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>1</Prop>" +
            "</KeyFrame><KeyFrame><Time>5</Time><Interpolation>0</Interpolation>" +
            "<Prop>0</Prop></KeyFrame></Timeline><NamedFrames>" +
            "<NamedFrame><Name>Idle</Name><Time>0</Time></NamedFrame>" +
            "<NamedFrame><Name>Hidden</Name><Time>5</Time></NamedFrame>" +
            "</NamedFrames></Timelines></XuiCanvas>");
        XuiSyntaxNode hud = document.Root.DescendantsAndSelf().Single(node =>
            XuiModelReader.GetId(node, document.Text) == "HUD");
        XuiSyntaxNode first = document.Root.DescendantsAndSelf().Single(node =>
            XuiModelReader.GetId(node, document.Text) == "I0");
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);

        window.SelectNodeKeysForTesting([first.Key]);
        Assert.AreEqual("Group", window.TimelineWorkspaceForTesting!
            .ActiveScope?.OwnerId);
        Assert.AreEqual(2, window.TimelineForTesting.VisibleTrackCountForTesting);
        Assert.AreEqual(3, window.NamedFrameCountForTesting);
        CollectionAssert.AreEquivalent(
            new[]
            {
                window.TimelineWorkspaceForTesting.ActiveScope!.ScopeKey,
            },
            window.TimelineForTesting.VisibleScopeKeysForTesting.ToArray());
        window.GoToNamedFrameForTesting("Show");
        Assert.AreEqual(1, window.TimelineWorkspaceForTesting.ActiveTick);
        window.SetTimelineTickForTesting(4);

        window.SelectNodeKeysForTesting([hud.Key]);
        Assert.AreEqual("HUD", window.TimelineWorkspaceForTesting!
            .ActiveScope?.TargetIds.Single());
        window.SetTimelineTickForTesting(2);
        window.SelectNodeKeysForTesting([first.Key]);
        Assert.AreEqual(4, window.TimelineWorkspaceForTesting!.ActiveTick);

        window.SetAllInScopeForTesting(true);
        Assert.AreEqual(3, window.TimelineForTesting.VisibleTrackCountForTesting);
        StringAssert.Contains(window.TimelineScopeLabelForTesting, "4 / 20");

        window.SelectNodeKeysForTesting([hud.Key, first.Key]);
        Assert.IsTrue(window.TimelineWorkspaceForTesting!.HasMixedSelection);
        Assert.AreEqual(0, window.TimelineForTesting.VisibleTrackCountForTesting);
        Assert.IsFalse(window.TimelineEditingEnabledForTesting);
        Assert.AreEqual("Mixed timeline scopes", window.TimelineScopeLabelForTesting);

        window.AttachDocumentForTesting(document);
        Assert.AreEqual(0, window.TimelineWorkspaceForTesting!.ActiveTick);
        Assert.AreEqual(
            window.TimelineWorkspaceForTesting.Catalog.RootScope?.ScopeKey,
            window.TimelineWorkspaceForTesting.ActiveScope?.ScopeKey);
        Assert.AreEqual(0, window.TimelineWorkspaceForTesting.RememberedTicks.Count);
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
            CreateLargeHierarchy(10, 1_000));
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode group = XuiModelReader.VisualDescendants(document.Root)
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Group002");
        XuiSyntaxNode item = XuiModelReader.VisualDescendants(document.Root)
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Item02199");
        HierarchyRow persistentGroup =
            window.HierarchyRowForTesting(group.Key)!;

        Assert.AreEqual(11, window.HierarchyRows.Count);
        for (int iteration = 0; iteration < 3; iteration++)
        {
            window.SetHierarchyExpansionForTesting(group.Key, true);
            Assert.AreEqual(1_011, window.HierarchyRows.Count);
            window.SetHierarchyExpansionForTesting(group.Key, false);
            Assert.AreEqual(11, window.HierarchyRows.Count);
        }

        window.SetHierarchyExpansionForTesting(group.Key, true);
        Assert.AreSame(
            persistentGroup,
            window.HierarchyRowForTesting(group.Key));
        Assert.AreEqual(1_011, window.HierarchyRows.Count);
        string[] expansionBeforeFilter =
            window.ExpandedKeysForTesting.Order(StringComparer.Ordinal).ToArray();

        int hierarchyResets = 0;
        window.HierarchyRows.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Action == NotifyCollectionChangedAction.Reset)
            {
                hierarchyResets++;
            }
        };
        Stopwatch filterClock = Stopwatch.StartNew();
        window.SetHierarchyFilterForTesting("Item02199");
        filterClock.Stop();
        Assert.IsLessThan(
            TimeSpan.FromMilliseconds(150),
            filterClock.Elapsed,
            $"Indexed hierarchy filtering took {filterClock.Elapsed.TotalMilliseconds:0.0} ms.");
        Assert.AreEqual(1, hierarchyResets);
        Assert.AreEqual(3, window.HierarchyRows.Count);
        window.SetHierarchyFilterForTesting(string.Empty);
        CollectionAssert.AreEqual(
            expansionBeforeFilter,
            window.ExpandedKeysForTesting.Order(StringComparer.Ordinal).ToArray());
        Assert.AreEqual(1_011, window.HierarchyRows.Count);

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
        Assert.AreSame(
            persistentGroup,
            window.HierarchyRowForTesting(group.Key));
        Assert.AreEqual(11, window.HierarchyRows.Count);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [Timeout(20_000)]
    public void WarmScopeSelectionsDoNotEvaluateOrResetTheHud()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            CreateScopeSelectionHierarchy(100));
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        XuiSyntaxNode[] groups = XuiModelReader
            .VisualDescendants(document.Root)
            .Where(static node => node.Name == "AdvGroup")
            .ToArray();
        int hierarchyResets = 0;
        window.HierarchyRows.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Action == NotifyCollectionChangedAction.Reset)
            {
                hierarchyResets++;
            }
        };
        long layoutSamples = window.LayoutEvaluationCountForTesting;

        Stopwatch clock = Stopwatch.StartNew();
        foreach (XuiSyntaxNode group in groups)
        {
            window.SelectNodeKeysForTesting([group.Key]);
            Assert.IsFalse(window.RawXmlMaterializedForTesting);
        }

        clock.Stop();
        Assert.IsLessThan(
            TimeSpan.FromMilliseconds(500),
            clock.Elapsed,
            $"100 indexed scope selections took {clock.Elapsed.TotalMilliseconds:0.0} ms.");
        Assert.AreEqual(
            layoutSamples,
            window.LayoutEvaluationCountForTesting);
        Assert.AreEqual(0, hierarchyResets);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void HierarchyStatesDistinguishDirectAndInheritedOverrides()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<AdvGroup><Properties><Id>Parent</Id></Properties>" +
            "<AdvGroup><Properties><Id>Child</Id></Properties>" +
            "<MyImage><Properties><Id>Grandchild</Id></Properties></MyImage>" +
            "</AdvGroup></AdvGroup></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode parent = XuiModelReader.VisualDescendants(document.Root)
            .Single(node => XuiModelReader.GetId(node, document.Text) == "Parent");
        XuiSyntaxNode child = XuiModelReader.VisualDescendants(document.Root)
            .Single(node => XuiModelReader.GetId(node, document.Text) == "Child");
        XuiSyntaxNode grandchild = XuiModelReader.VisualDescendants(document.Root)
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Grandchild");
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);

        window.SetEditorHiddenForTesting(child.Key, hidden: true);
        window.SetEditorHiddenForTesting(parent.Key, hidden: true);
        window.SetEditorLockedForTesting(parent.Key, locked: true);
        HierarchyRow parentRow = window.HierarchyRowForTesting(parent.Key)!;
        HierarchyRow childRow = window.HierarchyRowForTesting(child.Key)!;
        HierarchyRow grandchildRow =
            window.HierarchyRowForTesting(grandchild.Key)!;

        Assert.AreEqual(
            HierarchyVisibilityState.Hidden,
            parentRow.VisibilityState);
        Assert.AreEqual(
            HierarchyVisibilityState.Hidden,
            childRow.VisibilityState);
        Assert.IsTrue(childRow.CanToggleVisibility);
        Assert.AreEqual(
            HierarchyVisibilityState.HiddenByAncestor,
            grandchildRow.VisibilityState);
        Assert.IsFalse(grandchildRow.CanToggleVisibility);
        StringAssert.Contains(
            grandchildRow.VisibilityToolTip,
            "Child");
        Assert.AreEqual(
            HierarchyLockState.LockedByAncestor,
            childRow.LockState);
        Assert.IsFalse(childRow.CanToggleLock);
        StringAssert.Contains(childRow.LockToolTip, "Parent");
        Assert.AreEqual(0.48, grandchildRow.RowTextOpacity, 0.001);

        window.SetEditorHiddenForTesting(parent.Key, hidden: false);
        Assert.AreEqual(
            HierarchyVisibilityState.Hidden,
            childRow.VisibilityState);
        window.SetEditorHiddenForTesting(child.Key, hidden: false);
        window.SetEditorLockedForTesting(parent.Key, locked: false);
        Assert.AreEqual(
            HierarchyVisibilityState.Visible,
            grandchildRow.VisibilityState);
        Assert.AreEqual(
            HierarchyLockState.Unlocked,
            childRow.LockState);
        Assert.AreEqual(source, document.Text);
        Assert.IsFalse(document.IsDirty);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void HierarchyIsolationKeepsAncestorsAndSubtreeAndCanRestore()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<AdvGroup><Properties><Id>A</Id></Properties>" +
            "<MyImage><Properties><Id>Target</Id></Properties>" +
            "<MyImage><Properties><Id>TargetChild</Id></Properties></MyImage>" +
            "</MyImage><MyImage><Properties><Id>Sibling</Id></Properties></MyImage>" +
            "</AdvGroup><AdvGroup><Properties><Id>B</Id></Properties></AdvGroup>" +
            "</XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        Dictionary<string, XuiSyntaxNode> nodes =
            XuiModelReader.VisualDescendants(document.Root)
                .ToDictionary(
                    node => XuiModelReader.GetId(node, document.Text)!,
                    StringComparer.Ordinal);
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        window.SetEditorHiddenForTesting(nodes["B"].Key, hidden: true);

        window.IsolateHierarchyForTesting(nodes["Target"].Key);

        Assert.AreEqual(
            HierarchyVisibilityState.Visible,
            window.HierarchyRowForTesting(nodes["A"].Key)!.VisibilityState);
        Assert.AreEqual(
            HierarchyVisibilityState.Visible,
            window.HierarchyRowForTesting(nodes["Target"].Key)!.VisibilityState);
        Assert.AreEqual(
            HierarchyVisibilityState.Visible,
            window.HierarchyRowForTesting(nodes["TargetChild"].Key)!
                .VisibilityState);
        Assert.AreEqual(
            HierarchyVisibilityState.Hidden,
            window.HierarchyRowForTesting(nodes["Sibling"].Key)!.VisibilityState);
        Assert.AreEqual(
            HierarchyVisibilityState.Hidden,
            window.HierarchyRowForTesting(nodes["B"].Key)!.VisibilityState);

        window.RestoreHierarchyIsolationForTesting();

        Assert.AreEqual(
            HierarchyVisibilityState.Visible,
            window.HierarchyRowForTesting(nodes["Sibling"].Key)!.VisibilityState);
        Assert.AreEqual(
            HierarchyVisibilityState.Hidden,
            window.HierarchyRowForTesting(nodes["B"].Key)!.VisibilityState);
        Assert.AreEqual(source, document.Text);
        Assert.IsFalse(document.IsDirty);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void RawXmlIsLazyAndLargeSubtreesRequireExplicitLoading()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        string payload = new('x', (256 * 1024) + 1);
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            $"<Unknown>{payload}</Unknown></Properties></XuiCanvas>");
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        window.SelectNodeKeysForTesting([document.Root.Key]);

        Assert.IsFalse(window.RawXmlMaterializedForTesting);
        window.SetRawXmlExpandedForTesting(expanded: true);
        Assert.IsFalse(window.RawXmlMaterializedForTesting);
        StringAssert.Contains(window.RawXmlStatusForTesting, "load explicitly");
        window.SetRawXmlExpandedForTesting(
            expanded: true,
            loadLarge: true);
        Assert.IsTrue(window.RawXmlMaterializedForTesting);
        Assert.IsFalse(document.IsDirty);
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
            AdditionalAssetSources =
            [
                new AdditionalAssetSourceSetting
                {
                    Path = @"D:\Definitions\hudtextures.def",
                    Kind =
                        XuiConfiguredAssetSourceKind.TextureDefinitionFile,
                },
                new AdditionalAssetSourceSetting
                {
                    Path = @"D:\Resources\menu_PC.rpack",
                    Kind = XuiConfiguredAssetSourceKind.Rp6ResourcePack,
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
        Assert.HasCount(2, restored.AdditionalAssetSources);
        Assert.AreEqual(
            XuiConfiguredAssetSourceKind.TextureDefinitionFile,
            restored.AdditionalAssetSources[0].Kind);
        Assert.AreEqual(
            XuiConfiguredAssetSourceKind.Rp6ResourcePack,
            restored.AdditionalAssetSources[1].Kind);
        Assert.AreEqual("Segoe UI", restored.FontMappings["BOXED"]);
    }

    [TestMethod]
    public void ExternalModXuiDiscoversSiblingLocaleRoot()
    {
        using TestDirectory directory = new();
        string pakAssets = directory.File("PakAssets");
        string xui = Path.Combine(pakAssets, "XUI");
        Directory.CreateDirectory(xui);
        Directory.CreateDirectory(Path.Combine(pakAssets, "Locale", "En"));

        string discovered = MainWindow.FindDocumentAssetRoot(xui);

        Assert.AreEqual(
            Path.GetFullPath(pakAssets),
            discovered);
    }

    [TestMethod]
    public void WorkshopXuiDiscoversItsProjectDataRoot()
    {
        using TestDirectory directory = new();
        string data = directory.File(
            Path.Combine("workshop", "ExampleProject", "data"));
        string xui = Path.Combine(data, "menu", "hud", "custom.xui");
        Directory.CreateDirectory(Path.GetDirectoryName(xui)!);
        File.WriteAllText(xui, "<XuiCanvas />");

        string discovered = MainWindow.FindDocumentAssetRoot(xui);

        Assert.AreEqual(Path.GetFullPath(data), discovered);
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

    [TestMethod]
    public void PublishContractIsSingleFileAndUsesMultiResolutionIcon()
    {
        string root = FindDesktopRoot();
        string project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "XuiEditor.Wpf",
            "XuiEditor.Wpf.csproj"));
        StringAssert.Contains(project, "<PublishSingleFile>true</PublishSingleFile>");
        StringAssert.Contains(
            project,
            "<SelfContained Condition=\"'$(RuntimeIdentifier)' != ''\">true</SelfContained>");
        StringAssert.Contains(
            project,
            "<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>");
        StringAssert.Contains(
            project,
            "<ApplicationIcon>Assets\\DyingLightXuiEditor.ico</ApplicationIcon>");

        string iconPath = Path.Combine(
            root,
            "src",
            "XuiEditor.Wpf",
            "Assets",
            "DyingLightXuiEditor.ico");
        byte[] icon = File.ReadAllBytes(iconPath);
        Assert.IsGreaterThan(6, icon.Length);
        Assert.AreEqual(0, BitConverter.ToUInt16(icon, 0));
        Assert.AreEqual(1, BitConverter.ToUInt16(icon, 2));
        Assert.IsGreaterThanOrEqualTo(
            (ushort)8,
            BitConverter.ToUInt16(icon, 4));
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

    private static string CreateScopeSelectionHierarchy(int scopes)
    {
        StringBuilder source = new(
            "<XuiCanvas><Properties><Width>1280</Width><Height>720</Height></Properties>");
        for (int index = 0; index < scopes; index++)
        {
            _ = source.Append(
                CultureInfo.InvariantCulture,
                $"<AdvGroup><Properties><Id>Group{index:000}</Id></Properties><MyImage><Properties><Id>Item{index:000}</Id><Width>10</Width><Height>10</Height></Properties></MyImage><Timelines><Timeline><Id>Item{index:000}</Id><TimelineProp>Opacity</TimelineProp><KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>1</Prop></KeyFrame></Timeline></Timelines></AdvGroup>");
        }

        _ = source.Append("</XuiCanvas>");
        return source.ToString();
    }
}
