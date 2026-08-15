using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Schema;

namespace XuiEditor.Tests;

[TestClass]
public sealed class TextStyleCatalogTests
{
    private static readonly (int Value, int Count)[] ExpectedStockProfiles =
    [
        (0x0000, 17),
        (0x0002, 2),
        (0x0100, 174),
        (0x0110, 190),
        (0x0200, 113),
        (0x0210, 17),
        (0x0400, 197),
        (0x0410, 25),
        (0x0414, 2),
        (0x1000, 85),
        (0x1010, 144),
        (0x1100, 88),
        (0x1110, 316),
        (0x1114, 1),
        (0x1200, 106),
        (0x1210, 233),
        (0x1400, 159),
        (0x1410, 334),
    ];

    [TestMethod]
    public void CatalogContainsEveryObservedStockValueAndOccurrenceCount()
    {
        Assert.HasCount(18, XuiTextStyleCatalog.Profiles);
        Assert.AreEqual(
            2203,
            XuiTextStyleCatalog.Profiles.Sum(static profile =>
                profile.StockOccurrenceCount));

        foreach ((int value, int count) in ExpectedStockProfiles)
        {
            Assert.IsTrue(XuiTextStyleCatalog.TryResolve(
                value,
                out XuiTextStyleProfile profile));
            Assert.AreEqual(count, profile.StockOccurrenceCount);
            Assert.IsTrue(profile.IsStockObserved);
            Assert.AreEqual(
                value.ToString(CultureInfo.InvariantCulture),
                profile.CanonicalText);
            Assert.AreEqual($"0x{value:X8}", profile.HexText);
        }

        Assert.IsFalse(XuiTextStyleCatalog.TryResolve(0x0001, out _));
        Assert.AreEqual(
            XuiTextStyleEvidence.DyingLightBinary,
            XuiTextStyleCatalog.Describe(0x0001).Evidence);
        Assert.AreEqual(
            XuiTextStyleEvidence.Unknown,
            XuiTextStyleCatalog.Describe(unchecked((int)0x80000000)).Evidence);
    }

    [TestMethod]
    public void CodecSeparatesCompatibilityFlagsFromUnknownBits()
    {
        XuiDecodedTextStyle decoded = XuiTextStyleCodec.Decode(0xC011);
        Assert.IsTrue(decoded.ScaleAwareGlyphSizing);
        Assert.IsTrue(decoded.Compatibility0010);
        Assert.IsTrue(decoded.Compatibility4000);
        Assert.AreEqual(0x4011, decoded.CompatibilityBits);
        Assert.AreEqual(0x8000, decoded.UnknownBits);
        Assert.AreEqual(0xC011, decoded.UnmappedBits);

        int changed = XuiTextStyleCodec.SetHorizontalAlignment(
            XuiTextStyleCodec.SetFlag(
                decoded.RawValue,
                XuiKnownTextStyle.Bold,
                true),
            XuiTextHorizontalStyle.Center);
        XuiDecodedTextStyle changedDecoded = XuiTextStyleCodec.Decode(changed);
        Assert.AreEqual(decoded.CompatibilityBits, changedDecoded.CompatibilityBits);
        Assert.AreEqual(decoded.UnknownBits, changedDecoded.UnknownBits);
        Assert.IsTrue(changedDecoded.Bold);
        Assert.AreEqual(
            XuiTextHorizontalStyle.Center,
            changedDecoded.HorizontalAlignment);
    }

    [TestMethod]
    public void TextStyleMetadataSelectsTheDedicatedPalette()
    {
        XuiPropertyDefinition definition =
            XuiClassCatalog.Default.FindProperty("TextStyle")!;
        Assert.AreEqual(XuiPropertyType.WholeNumber, definition.Type);
        Assert.AreEqual(
            XuiPropertyEditorKind.TextStylePalette,
            definition.EditorKind);
        Assert.AreEqual(XuiPreviewSupport.Exact, definition.PreviewSupport);
    }

    [TestMethod]
    public void LayoutCarriesScaleAwareStyleWithoutFabricatingOtherFlags()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>200</Width><Height>100</Height>" +
            "</Properties><MyText><Properties><Id>T</Id><Width>100</Width>" +
            "<Height>30</Height><Scale>2,1,1</Scale><Text>test</Text>" +
            "<TextStyle>16401</TextStyle></Properties></MyText></XuiCanvas>");

        XuiRenderNode node = DyingLightLayoutEngine.Evaluate(
                document,
                new XuiViewport(200, 100),
                0)
            .Nodes.Single(static candidate => candidate.Id == "T");
        Assert.AreEqual(16401, node.TextStyleValue);
        Assert.IsTrue(node.ScaleAwareText);
        Assert.IsFalse(node.Bold);
        Assert.IsFalse(node.Italic);
        Assert.IsFalse(node.Underline);
    }

    [TestMethod]
    public void LayoutParsesHexTextStyleAndDiagnosesInvalidValues()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>200</Width><Height>100</Height>" +
            "</Properties><MyText><Properties><Id>Hex</Id><Width>100</Width>" +
            "<Height>30</Height><Scale>2,1,1</Scale><Text>hex</Text>" +
            "<TextStyle>0x00000001</TextStyle></Properties></MyText>" +
            "<MyText><Properties><Id>Invalid</Id><Text>invalid</Text>" +
            "<TextStyle>not-a-mask</TextStyle></Properties></MyText>" +
            "</XuiCanvas>");

        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(200, 100),
            0);
        XuiRenderNode hex = frame.Nodes.Single(static node =>
            node.Id == "Hex");
        XuiRenderNode invalid = frame.Nodes.Single(static node =>
            node.Id == "Invalid");

        Assert.AreEqual(1, hex.TextStyleValue);
        Assert.IsTrue(hex.ScaleAwareText);
        Assert.AreEqual(0, invalid.TextStyleValue);
        Assert.IsTrue(frame.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-LAYOUT005" &&
            diagnostic.Message.Contains(
                "Property TextStyle has invalid integer value 'not-a-mask'",
                StringComparison.Ordinal)));
    }
}
