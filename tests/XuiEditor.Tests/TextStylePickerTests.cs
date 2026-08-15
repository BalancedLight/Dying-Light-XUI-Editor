using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Schema;
using XuiEditor.Wpf;
using XuiEditor.Wpf.Controls;
using XuiEditor.Wpf.Models;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TextStylePickerTests
{
    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void PickerSearchesStockStylesAndPreservesUnknownComposerBits()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        UiLocalization.Apply("En");
        TextStylePicker picker = new()
        {
            Value = "33024",
        };
        List<string> commits = [];
        picker.ValueCommitted += (_, eventArgs) =>
            commits.Add(eventArgs.Value);
        Window host = new()
        {
            Content = picker,
            Width = 360,
            Height = 80,
            Opacity = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        host.Show();
        try
        {
            StringAssert.Contains(picker.SummaryForTesting, "Unknown bits");
            picker.OpenForTesting();
            Assert.IsTrue(picker.IsPopupOpenForTesting);
            Assert.HasCount(2, picker.VisibleGroupsForTesting);
            Assert.AreEqual(
                18,
                picker.VisibleGroupsForTesting.Sum(static group =>
                    group.Items.Count));

            FrameworkElement popup = picker.PopupContentForTesting;
            popup.UpdateLayout();
            Border surface = (Border)popup;
            Assert.IsTrue(IsDark(surface.Background));

            Button[] firstStyleTiles = VisualDescendants(popup)
                .OfType<Button>()
                .Where(static button =>
                    button.Tag is TextStylePickerItem)
                .Take(3)
                .ToArray();
            Assert.HasCount(3, firstStyleTiles);
            double[] firstTileRows = firstStyleTiles
                .Select(tile => tile.TranslatePoint(new Point(), popup).Y)
                .ToArray();
            Assert.AreEqual(firstTileRows[0], firstTileRows[1], 0.5);
            Assert.AreEqual(firstTileRows[0], firstTileRows[2], 0.5);

            picker.SearchForTesting("0x00001010");
            Assert.AreEqual(
                0x1010,
                picker.VisibleGroupsForTesting
                    .SelectMany(static group => group.Items)
                    .Single().Profile.RawValue);
            Assert.IsTrue(picker.SelectForTesting(0x1114));
            Assert.AreEqual("4372", picker.Value);
            Assert.AreEqual("4372", commits.Single());

            picker.Value = "33024";
            picker.ApplyCompositionForTesting(
                bold: true,
                italic: false,
                underline: false,
                XuiTextHorizontalStyle.Center,
                verticalMiddle: false,
                compatibility0010: false,
                scaleAware: false,
                compatibility4000: false);
            Assert.IsTrue(XuiTextStyleCodec.TryParse(
                picker.Value,
                out XuiDecodedTextStyle composed));
            Assert.AreEqual(0x8000, composed.UnknownBits);
            Assert.IsTrue(composed.Bold);
            Assert.AreEqual(
                XuiTextHorizontalStyle.Center,
                composed.HorizontalAlignment);

            picker.ApplyCustomForTesting("0x1410");
            Assert.AreEqual("5136", picker.Value);
            picker.ApplyCustomForTesting("invalid");
            Assert.AreEqual("5136", picker.Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(
                picker.CustomErrorForTesting));
        }
        finally
        {
            host.Close();
        }
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void InspectorPickerCommitsMixedSelectionAsOneUndoableBatch()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>200</Width><Height>100</Height>" +
            "</Properties><MyText><Properties><Id>A</Id><TextStyle>256" +
            "</TextStyle></Properties></MyText><MyText><Properties><Id>B</Id>" +
            "<TextStyle>512</TextStyle></Properties></MyText></XuiCanvas>");
        XuiSyntaxNode[] nodes = XuiModelReader.VisualDescendants(document.Root)
            .Where(static node => node.Name == "MyText")
            .ToArray();
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        window.SelectNodeKeysForTesting(nodes.Select(static node => node.Key));

        InspectorPropertyRow row = window.InspectorProperties.Single(
            static property => property.Name == "TextStyle");
        Assert.IsTrue(row.IsMixed);
        Assert.IsTrue(row.IsTextStyleEditor);
        window.SelectTextStyleForTesting(0x1410);

        Assert.IsTrue(document.History.CanUndo);
        Assert.IsTrue(nodes.All(node =>
            XuiModelReader.GetPropertyValue(
                document.SyntaxTree.FindByKey(node.Key)!,
                document.Text,
                "TextStyle") == "5136"));
        document.Undo();
        Assert.IsFalse(document.History.CanUndo);
        Assert.AreEqual(
            "256",
            XuiModelReader.GetPropertyValue(
                document.SyntaxTree.FindByKey(nodes[0].Key)!,
                document.Text,
                "TextStyle"));
        Assert.AreEqual(
            "512",
            XuiModelReader.GetPropertyValue(
                document.SyntaxTree.FindByKey(nodes[1].Key)!,
                document.Text,
                "TextStyle"));
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void AddPropertyUsesTextStylePickerBeforeInsertion()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiPropertyDefinition definition =
            XuiClassCatalog.Default.FindProperty("TextStyle")!;
        AddXuiPropertyWindow window = new(
            "Text",
            [definition],
            authoredNames: []);

        Assert.IsTrue(window.SelectDefinitionForTesting("TextStyle"));
        Assert.IsTrue(window.TextStyleEditorVisibleForTesting);
        Assert.IsTrue(window.TextStylePickerForTesting.SelectForTesting(
            0x1110));
        Assert.IsTrue(window.AcceptForTesting());
        Assert.AreEqual("TextStyle", window.PropertyName);
        Assert.AreEqual("4368", window.PropertyValue);
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ScaleAwareStyleCorrectsNonUniformTextContentScale()
    {
        App application = Application.Current as App ?? new App();
        application.InitializeComponent();
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>200</Width><Height>100</Height>" +
            "</Properties><MyText><Properties><Id>T</Id><Width>80</Width>" +
            "<Height>30</Height><Scale>2,1,1</Scale><Text>test</Text>" +
            "<TextStyle>1</TextStyle></Properties></MyText></XuiCanvas>");
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiEditor.Core.Layout.XuiViewport(200, 100),
            0);
        XuiRenderNode node = frame.Nodes.Single(static node => node.Id == "T");
        XuiViewportControl viewport = new();
        viewport.SetFrame(frame);

        System.Numerics.Matrix3x2 correction =
            viewport.RetainedContentTransformForTesting(node.Key);
        Assert.AreEqual(0.5f, correction.M11, 0.0001f);
        Assert.AreEqual(1f, correction.M22, 0.0001f);
    }

    private static bool IsDark(Brush brush) =>
        brush is SolidColorBrush solid &&
        solid.Color.R < 128 &&
        solid.Color.G < 128 &&
        solid.Color.B < 128;

    private static IEnumerable<DependencyObject> VisualDescendants(
        DependencyObject root)
    {
        for (int index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            DependencyObject child =
                VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in
                     VisualDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}
