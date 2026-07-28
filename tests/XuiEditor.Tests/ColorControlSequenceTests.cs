using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;

namespace XuiEditor.Tests;

[TestClass]
public sealed class ColorControlSequenceTests
{
    [TestMethod]
    public void EnabledGrammarStripsOnlyExactColorAndResetSequences()
    {
        XuiColorControlParseResult parsed =
            XuiColorControlSequenceParser.Parse(
                "A%COLOR(F90F36)B%COLOR(reset)C" +
                "%COLOR(00ff7F)D%COLOR(RESET)E%s",
                enabled: true);

        Assert.AreEqual("ABCDE%s", parsed.DisplayText);
        Assert.AreEqual(4, parsed.ValidSequenceCount);
        Assert.AreEqual(0, parsed.MalformedSequenceCount);
        Assert.HasCount(2, parsed.ColorRuns);
        Assert.AreEqual(1, parsed.ColorRuns[0].Start);
        Assert.AreEqual(1, parsed.ColorRuns[0].Length);
        Assert.AreEqual(0xfff90f36u, parsed.ColorRuns[0].Color.Argb);
        Assert.AreEqual(3, parsed.ColorRuns[1].Start);
        Assert.AreEqual(1, parsed.ColorRuns[1].Length);
        Assert.AreEqual(0xff00ff7fu, parsed.ColorRuns[1].Color.Argb);
    }

    [TestMethod]
    [DataRow("%color(112233)")]
    [DataRow("%COLOR(123)")]
    [DataRow("%COLOR(11223344)")]
    [DataRow("%COLOR(ZZ1133)")]
    [DataRow("%COLOR(112233")]
    public void MalformedOrUnsupportedSequencesRemainLiteral(string source)
    {
        XuiColorControlParseResult parsed =
            XuiColorControlSequenceParser.Parse(source, enabled: true);

        Assert.AreEqual(source, parsed.DisplayText);
        Assert.AreEqual(0, parsed.ValidSequenceCount);
        Assert.AreEqual(1, parsed.MalformedSequenceCount);
        Assert.IsEmpty(parsed.ColorRuns);
    }

    [TestMethod]
    public void DisabledPropertyLeavesValidTagsLiteralAndPercentSIsUnrelated()
    {
        const string source = "%s %COLOR(F90F36)value%COLOR(reset)";

        XuiColorControlParseResult parsed =
            XuiColorControlSequenceParser.Parse(source, enabled: false);

        Assert.AreEqual(source, parsed.DisplayText);
        Assert.AreEqual(2, parsed.ValidSequenceCount);
        Assert.AreEqual(0, parsed.MalformedSequenceCount);
        Assert.IsEmpty(parsed.ColorRuns);
    }

    [TestMethod]
    public void UppercaseFormattingCannotReinterpretInvalidLowercasePrefix()
    {
        XuiColorControlParseResult parsed =
            XuiColorControlSequenceParser.Parse(
                "%color(112233)text",
                enabled: true);
        XuiTextPresentation presentation =
            XuiTextColorRunFormatter.Prepare(
                parsed.DisplayText,
                parsed.ColorRuns,
                uppercase: true,
                CultureInfo.InvariantCulture);

        Assert.AreEqual("%COLOR(112233)TEXT", presentation.Text);
        Assert.IsEmpty(presentation.ColorRuns);
        Assert.IsTrue(parsed.HasMalformedSequences);
    }

    [TestMethod]
    [DataRow("MyText")]
    [DataRow("MyTextPresenter")]
    [DataRow("IUIProgressText")]
    [DataRow("UISmartText")]
    public void CommonIuiTextDerivedNodesUseTheSameGate(string elementName)
    {
        XuiDocument document = Document(
            $"<{elementName}><Properties><Id>Text</Id><Width>100</Width><Height>20</Height>" +
            "<Text>A%COLOR(F90F36)B</Text>" +
            "<ColorControlSequenceEnabled>true</ColorControlSequenceEnabled>" +
            $"</Properties></{elementName}>");

        XuiRenderNode node = DyingLightLayoutEngine.Evaluate(
                document,
                new XuiViewport(100, 100),
                0)
            .Nodes.Single(static candidate => candidate.Id == "Text");

        Assert.AreEqual(XuiRenderKind.Text, node.Kind);
        Assert.AreEqual("AB", node.Text);
        Assert.HasCount(1, node.TextColorRuns);
    }

    [TestMethod]
    public void LayoutParsesFinalLocalizedTextAndStoresColoredRuns()
    {
        XuiDocument document = Document(
            "<MyText><Properties><Id>Localized</Id><Width>200</Width><Height>40</Height>" +
            "<Text>$DYNAMIC$</Text><ColorControlSequenceEnabled>true</ColorControlSequenceEnabled>" +
            "</Properties></MyText>");

        XuiRenderNode node = DyingLightLayoutEngine.Evaluate(
                document,
                new XuiViewport(100, 100),
                0,
                new LocalizingResolver())
            .Nodes.Single(static candidate =>
                candidate.Id == "Localized");

        Assert.AreEqual("before after", node.Text);
        Assert.IsTrue(node.ColorControlSequenceEnabled);
        Assert.HasCount(1, node.TextColorRuns);
        Assert.AreEqual(7, node.TextColorRuns[0].Start);
        Assert.AreEqual(5, node.TextColorRuns[0].Length);
        Assert.AreEqual(0xff123456u, node.TextColorRuns[0].Color.Argb);
    }

    [TestMethod]
    public void LayoutReportsDisabledMalformedAndNonTextMarkupPrecisely()
    {
        XuiDocument document = Document(
            "<MyText><Properties><Id>Enabled</Id><Width>100</Width><Height>20</Height>" +
            "<Text>A%COLOR(F90F36)B</Text>" +
            "<ColorControlSequenceEnabled>true</ColorControlSequenceEnabled>" +
            "</Properties></MyText>" +
            "<MyText><Properties><Id>Disabled</Id><Width>100</Width><Height>20</Height>" +
            "<Text>A%COLOR(F90F36)B</Text></Properties></MyText>" +
            "<IUIProgressText><Properties><Id>Malformed</Id><Width>100</Width><Height>20</Height>" +
            "<Text>A%color(F90F36)B</Text>" +
            "<ColorControlSequenceEnabled>true</ColorControlSequenceEnabled>" +
            "</Properties></IUIProgressText>" +
            "<MyImage><Properties><Id>WrongType</Id><Width>20</Width><Height>20</Height>" +
            "<Text>A%COLOR(F90F36)B</Text>" +
            "<ColorControlSequenceEnabled>true</ColorControlSequenceEnabled>" +
            "</Properties></MyImage>");

        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(100, 100),
            0);
        XuiRenderNode enabled = frame.Nodes.Single(static node =>
            node.Id == "Enabled");
        XuiRenderNode disabled = frame.Nodes.Single(static node =>
            node.Id == "Disabled");
        XuiRenderNode malformed = frame.Nodes.Single(static node =>
            node.Id == "Malformed");

        Assert.AreEqual("AB", enabled.Text);
        Assert.AreEqual("A%COLOR(F90F36)B", disabled.Text);
        Assert.AreEqual("A%color(F90F36)B", malformed.Text);
        Assert.IsFalse(frame.Diagnostics.Any(diagnostic =>
            diagnostic.NodeKey == enabled.SelectionKey &&
            diagnostic.Code.StartsWith("XUI-TEXT", StringComparison.Ordinal)));
        Assert.IsTrue(frame.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "XUI-TEXT001" &&
            diagnostic.NodeKey == disabled.SelectionKey &&
            diagnostic.Message ==
            "Color markup is present, but ColorControlSequenceEnabled is false. Dying Light will display these tags literally unless the controller enables them at runtime."));
        Assert.IsTrue(frame.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "XUI-TEXT002" &&
            diagnostic.NodeKey == malformed.SelectionKey));
        Assert.IsTrue(frame.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "XUI-TEXT003" &&
            frame.Nodes.Any(node =>
                node.Id == "WrongType" &&
                node.SelectionKey == diagnostic.NodeKey)));
    }

    [TestMethod]
    public void CompiledSessionReusesParsedRunsAcrossNodesAndSamples()
    {
        XuiDocument document = Document(
            "<MyText><Properties><Id>A</Id><Width>100</Width><Height>20</Height>" +
            "<Text>A%COLOR(F90F36)B</Text>" +
            "<ColorControlSequenceEnabled>true</ColorControlSequenceEnabled>" +
            "</Properties></MyText>" +
            "<MyText><Properties><Id>B</Id><Width>100</Width><Height>20</Height>" +
            "<Text>A%COLOR(F90F36)B</Text>" +
            "<ColorControlSequenceEnabled>true</ColorControlSequenceEnabled>" +
            "</Properties></MyText>");
        DyingLightLayoutSession session =
            DyingLightLayoutSession.Compile(document);

        XuiRenderFrame first = session.Sample(
            new XuiViewport(100, 100),
            0);
        int parseCount = session.ColorControlParseCount;
        XuiRenderFrame second = session.Sample(
            new XuiViewport(100, 100),
            0);
        XuiRenderNode a = first.Nodes.Single(static node => node.Id == "A");
        XuiRenderNode b = first.Nodes.Single(static node => node.Id == "B");

        Assert.AreSame(a.TextColorRuns, b.TextColorRuns);
        Assert.AreEqual(parseCount, session.ColorControlParseCount);
        Assert.AreSame(first, second);
    }

    private static XuiDocument Document(string children) =>
        XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            children +
            "</XuiCanvas>");

    private sealed class LocalizingResolver : IAssetResolver
    {
        public IReadOnlyList<XuiAssetRoot> Roots { get; } = [];

        public IReadOnlyList<XuiDiagnostic> Diagnostics { get; } = [];

        public IReadOnlyList<XuiResolvedFile> Files { get; } = [];

        public ILocalizationCatalog? Localization => null;

        public Task RebuildAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public XuiResolvedFile? ResolveFile(string pathOrName) => null;

        public XuiTextureRegion? ResolveTextureDefinition(
            string imagePath) => null;

        public XuiVisualTemplate? ResolveVisual(string visualId) => null;

        public Task<ResolvedTexture?> ResolveTextureAsync(
            string imagePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedTexture?>(null);

        public ResolvedFont ResolveFont(
            string fontId,
            double requestedSize,
            IReadOnlyDictionary<string, string>? userMappings = null) =>
            new(
                fontId,
                "Segoe UI",
                requestedSize,
                true,
                null,
                []);

        public XuiTextMeasurement MeasureText(
            string fontId,
            string text,
            double requestedSize,
            double maximumWidth,
            bool multiline,
            bool uppercase,
            double characterSpacingAdjust = 0) =>
            new(text.Length, 10, 10, false);

        public string ResolveText(string keyOrLiteral) =>
            keyOrLiteral == "$DYNAMIC$"
                ? "before %COLOR(123456)after"
                : keyOrLiteral;

        public ValueTask<ResolvedBitmapFont?> ResolveBitmapFontAsync(
            string fontId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ResolvedBitmapFont?>(null);
    }
}
