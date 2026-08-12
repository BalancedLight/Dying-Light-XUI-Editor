using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Values;

namespace XuiEditor.Tests;

[TestClass]
public sealed class RotationSerializationTests
{
    [TestMethod]
    [DataRow("0", "0.000000,0.000000,0.000000,1.000000")]
    [DataRow("90", "0.000000,0.000000,0.707107,0.707107")]
    [DataRow("-90", "0.000000,0.000000,-0.707107,0.707107")]
    [DataRow("360", "0.000000,0.000000,0.000000,1.000000")]
    public void ScalarDegreesCanonicalizeAsZQuaternions(
        string authored,
        string expected)
    {
        Assert.IsTrue(XuiRotationCodec.TryCanonicalize(
            authored,
            out string canonical,
            out XuiRotationSourceKind sourceKind));

        Assert.AreEqual(XuiRotationSourceKind.ScalarDegrees, sourceKind);
        Assert.AreEqual(expected, canonical);
    }

    [TestMethod]
    public void EulerDegreesUseUnityCompatibleZxyOrdering()
    {
        Assert.IsTrue(XuiRotationCodec.TryCanonicalize(
            "10,20,30",
            out string canonical,
            out XuiRotationSourceKind sourceKind));

        Assert.AreEqual(XuiRotationSourceKind.EulerDegrees, sourceKind);
        Assert.AreEqual(
            "0.127679,0.144878,0.239298,0.951549",
            canonical);
    }

    [TestMethod]
    public void ValidQuaternionSaveTextIsPreservedAndAuthoringIsCanonical()
    {
        const string authored = "0,0,0.707107,0.707107";

        Assert.IsTrue(XuiRotationCodec.TryNormalizeForSave(
            authored,
            out string saved,
            out XuiRotationSourceKind sourceKind));
        Assert.IsTrue(XuiRotationCodec.TryCanonicalize(
            authored,
            out string committed,
            out _));

        Assert.AreEqual(XuiRotationSourceKind.Quaternion, sourceKind);
        Assert.AreEqual(authored, saved);
        Assert.AreEqual(
            "0.000000,0.000000,0.707107,0.707107",
            committed);
    }

    [TestMethod]
    public void InvalidAndZeroLengthRotationsAreRejected()
    {
        Assert.IsFalse(XuiRotationCodec.TryCanonicalize(
            "NaN",
            out _,
            out _));
        Assert.IsFalse(XuiRotationCodec.TryCanonicalize(
            "0,0,0,0",
            out _,
            out _));
        Assert.IsFalse(XuiRotationCodec.TryCanonicalize(
            "1,2",
            out _,
            out _));
    }

    [TestMethod]
    public void LegacyPreviewMatchesCanonicalQuaternionAroundAuthoredPivot()
    {
        const string prefix =
            "<XuiCanvas><Properties><Width>1280</Width><Height>720</Height>" +
            "</Properties><AdvGroup><Properties><Id>newsfeed</Id>" +
            "<Width>1280</Width><Height>32</Height>" +
            "<Position>552,336,0</Position><Pivot>640,16,0</Pivot>";
        const string suffix = "</Properties></AdvGroup></XuiCanvas>";
        RenderedRotation legacy = RenderSingle(
            prefix + "<Rotation>90</Rotation>" + suffix);
        RenderedRotation canonical = RenderSingle(
            prefix +
            "<Rotation>0.000000,0.000000,0.707107,0.707107</Rotation>" +
            suffix);

        AssertMatrixEqual(
            legacy.Node.LocalTransform,
            canonical.Node.LocalTransform);
        AssertMatrixEqual(
            legacy.Node.WorldTransform,
            canonical.Node.WorldTransform);
        AssertRectEqual(legacy.Node.WorldBounds, canonical.Node.WorldBounds);
        Assert.IsTrue(legacy.DiagnosticCodes.Contains("XUI-LAYOUT015"));
        Assert.IsFalse(canonical.DiagnosticCodes.Contains("XUI-LAYOUT015"));
    }

    [TestMethod]
    public void LegacyTimelineRotationUsesQuaternionInterpolationAndDiagnostic()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>1</Width><Height>1</Height></Properties>" +
            "<MyImage><Properties><Id>I</Id></Properties></MyImage>" +
            "<Timelines><Timeline><Id>I</Id><TimelineProp>Rotation</TimelineProp>" +
            Key(0, "0") + Key(10, "90") +
            "</Timeline></Timelines></XuiCanvas>");

        XuiTimelineSet set = XuiTimelineParser.Parse(document);
        XuiAnimatedValue sampled = TimelineEvaluator.Sample(
            set.Timelines.Single().Tracks.Single(),
            5)!;

        Assert.AreEqual(XuiTimelineValueKind.Quaternion, sampled.Kind);
        Assert.AreEqual(45, sampled.Quaternion.ZRotationDegrees, 0.001);
        Assert.AreEqual(2, set.Diagnostics.Count(static diagnostic =>
            diagnostic.Code == "XUI-TL012"));
    }

    [TestMethod]
    public async Task SaveMigratesStaticAndTimelineRotationsInOneBatch()
    {
        using TestDirectory directory = new();
        string path = directory.File("legacy.xui");
        const string source =
            "<XuiCanvas odd='preserved'><Properties><Width>1</Width><Height>1</Height>" +
            "</Properties><MyImage><Properties><Id>I</Id><Rotation>90</Rotation>" +
            "<Text>untouched</Text></Properties></MyImage><Timelines><Timeline>" +
            "<Id>I</Id><TimelineProp>Rotation</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation>" +
            "<Prop>0,0,-90</Prop></KeyFrame><KeyFrame><Time>10</Time>" +
            "<Interpolation>0</Interpolation><Prop>0,0,90</Prop></KeyFrame>" +
            "</Timeline></Timelines></XuiCanvas>";
        await File.WriteAllTextAsync(path, source, new UTF8Encoding(false));
        XuiDocument document = await XuiDocument.OpenAsync(path);

        XuiSaveResult first = await document.SaveAsync();

        Assert.AreEqual(XuiSaveDisposition.Saved, first.Disposition);
        Assert.AreEqual(3, first.RepairedRotationCount);
        Assert.AreEqual(source, await File.ReadAllTextAsync(path + ".bak"));
        string migrated = await File.ReadAllTextAsync(path);
        StringAssert.Contains(
            migrated,
            "<Rotation>0.000000,0.000000,0.707107,0.707107</Rotation>");
        StringAssert.Contains(
            migrated,
            "<Prop>0.000000,0.000000,-0.707107,0.707107</Prop>");
        StringAssert.Contains(
            migrated,
            "<Prop>0.000000,0.000000,0.707107,0.707107</Prop>");
        StringAssert.Contains(migrated, "odd='preserved'");
        StringAssert.Contains(migrated, "<Text>untouched</Text>");

        XuiSaveResult second = await document.SaveAsync();
        Assert.AreEqual(XuiSaveDisposition.Unchanged, second.Disposition);
        Assert.AreEqual(0, second.RepairedRotationCount);
        Assert.AreEqual(migrated, await File.ReadAllTextAsync(path));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path + ".bak"));
    }

    [TestMethod]
    public async Task InvalidRotationBlocksSaveBeforeReplacingOriginal()
    {
        using TestDirectory directory = new();
        string path = directory.File("invalid.xui");
        const string source =
            "<XuiCanvas><Properties><Rotation>0,0,0,0</Rotation>" +
            "</Properties></XuiCanvas>";
        await File.WriteAllTextAsync(path, source, new UTF8Encoding(false));
        XuiDocument document = await XuiDocument.OpenAsync(path);

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => document.SaveAsync());

        StringAssert.Contains(exception.Message, "Rotation value");
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        Assert.IsFalse(File.Exists(path + ".bak"));
    }

    [TestMethod]
    public void PastedTimelineRotationsAreCanonicalizedWithoutChangingOtherProps()
    {
        XuiDocument formatSource = XuiDocument.FromText("<XuiCanvas />");
        const string raw =
            "<KeyFrame><Time>5</Time><Prop>90</Prop>" +
            "<Prop>leave-me-alone</Prop></KeyFrame>";

        string normalized = XuiRotationMigration.NormalizeKeyFrameXml(
            raw,
            [0],
            formatSource.Format);

        StringAssert.Contains(
            normalized,
            "<Prop>0.000000,0.000000,0.707107,0.707107</Prop>");
        StringAssert.Contains(normalized, "<Prop>leave-me-alone</Prop>");
    }

    private static RenderedRotation RenderSingle(string source)
    {
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            XuiDocument.FromText(source),
            XuiViewport.Default,
            0);
        return new RenderedRotation(
            frame.Nodes.Single(static node => node.Id == "newsfeed"),
            frame.Diagnostics
                .Select(static diagnostic => diagnostic.Code)
                .ToArray());
    }

    private static void AssertMatrixEqual(
        System.Numerics.Matrix3x2 expected,
        System.Numerics.Matrix3x2 actual)
    {
        Assert.AreEqual(expected.M11, actual.M11, 0.00001);
        Assert.AreEqual(expected.M12, actual.M12, 0.00001);
        Assert.AreEqual(expected.M21, actual.M21, 0.00001);
        Assert.AreEqual(expected.M22, actual.M22, 0.00001);
        Assert.AreEqual(expected.M31, actual.M31, 0.00001);
        Assert.AreEqual(expected.M32, actual.M32, 0.00001);
    }

    private static void AssertRectEqual(XuiRect expected, XuiRect actual)
    {
        Assert.AreEqual(expected.X, actual.X, 0.00001);
        Assert.AreEqual(expected.Y, actual.Y, 0.00001);
        Assert.AreEqual(expected.Width, actual.Width, 0.00001);
        Assert.AreEqual(expected.Height, actual.Height, 0.00001);
    }

    private static string Key(int tick, string value) =>
        $"<KeyFrame><Time>{tick}</Time><Interpolation>0</Interpolation>" +
        $"<Prop>{value}</Prop></KeyFrame>";

    private sealed record RenderedRotation(
        XuiRenderNode Node,
        IReadOnlyList<string> DiagnosticCodes);
}
