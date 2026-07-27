using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Documents;

namespace XuiEditor.Tests;

[TestClass]
public sealed class TimelineTests
{
    [TestMethod]
    public void ParsesEveryObservedDyingLightPropertyAndQuaternionRotation()
    {
        string[] properties =
        [
            "Opacity", "Show", "Scale", "Position", "Color", "TextColor",
            "Width", "Height", "Outline", "TextProgress", "Rotation",
            "Const0", "Const1", "OutlineColor", "Shadow", "ImagePath",
            "DefaultFontColor", "Pivot", "Material",
        ];
        string[] values =
        [
            "0.5", "true", "1,1,1", "2,3,0", "0xff010203", "0xff112233",
            "40", "20", "1", "0.3", "0,0,0.707107,0.707107",
            "2", "3", "0xff445566", "4", "icon",
            "0xff778899", "5,6,0", "menu.mat",
        ];
        XuiDocument document = XuiDocument.FromText(
            CreateTimelineXml(properties, values, 0));

        XuiTimelineSet set = XuiTimelineParser.Parse(document);

        Assert.AreEqual(1, set.Timelines.Count);
        Assert.AreEqual(properties.Length, set.Timelines[0].Tracks.Count);
        Assert.IsFalse(set.Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == Core.Diagnostics.XuiDiagnosticSeverity.Error));
        XuiTrack rotation = set.Timelines[0].Tracks.Single(static track =>
            track.Property == XuiTimelineProperty.Rotation);
        XuiAnimatedValue sampled = TimelineEvaluator.Sample(rotation, 0)!;
        Assert.AreEqual(XuiTimelineValueKind.Quaternion, sampled.Kind);
        Assert.AreEqual(90, sampled.Quaternion.ZRotationDegrees, 0.01);
    }

    [TestMethod]
    public void LinearFixtureSamplesTicksZeroOneElevenTwelveAndTwentyTwo()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>1280</Width><Height>720</Height></Properties>" +
            "<MyImage><Properties><Id>I</Id></Properties></MyImage>" +
            "<Timelines><Timeline><Id>I</Id><TimelineProp>Opacity</TimelineProp>" +
            Key(0, "0") + Key(1, "0") + Key(11, "1") +
            Key(12, "1") + Key(22, "0") +
            "</Timeline></Timelines></XuiCanvas>");
        XuiTrack track = XuiTimelineParser.Parse(document)
            .Timelines[0]
            .Tracks[0];

        Assert.AreEqual(0, TimelineEvaluator.Sample(track, 0)!.Number, 0.0001);
        Assert.AreEqual(0, TimelineEvaluator.Sample(track, 1)!.Number, 0.0001);
        Assert.AreEqual(1, TimelineEvaluator.Sample(track, 11)!.Number, 0.0001);
        Assert.AreEqual(1, TimelineEvaluator.Sample(track, 12)!.Number, 0.0001);
        Assert.AreEqual(0, TimelineEvaluator.Sample(track, 22)!.Number, 0.0001);
        Assert.AreEqual(0.5, TimelineEvaluator.Sample(track, 6)!.Number, 0.0001);
        Assert.AreEqual(0.5, TimelineEvaluator.Sample(track, 17)!.Number, 0.0001);
    }

    [TestMethod]
    public void EasedInterpolationUsesEaseFields()
    {
        string source =
            "<XuiCanvas><Properties><Width>1</Width><Height>1</Height></Properties>" +
            "<MyImage><Properties><Id>I</Id></Properties></MyImage><Timelines>" +
            "<Timeline><Id>I</Id><TimelineProp>Opacity</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>2</Interpolation>" +
            "<EaseIn>2</EaseIn><EaseOut>0</EaseOut><EaseScale>1</EaseScale>" +
            "<Prop>0</Prop></KeyFrame>" +
            Key(10, "10") +
            "</Timeline></Timelines></XuiCanvas>";
        XuiTrack track = XuiTimelineParser.Parse(
                XuiDocument.FromText(source))
            .Timelines[0]
            .Tracks[0];

        double value = TimelineEvaluator.Sample(track, 2)!.Number;
        Assert.AreNotEqual(2, value, 0.001);
        Assert.IsLessThan(2, value);
    }

    [TestMethod]
    public void BooleanAndStringTracksUseStepBehavior()
    {
        XuiDocument document = XuiDocument.FromText(CreateTimelineXml(
            ["Show", "ImagePath", "Shadow"],
            ["false", "before", "true"],
            0,
            ["true", "after", "false"],
            10));
        XuiTimeline timeline = XuiTimelineParser.Parse(document).Timelines[0];

        Assert.IsFalse(TimelineEvaluator.Sample(timeline.Tracks[0], 5)!.Boolean);
        Assert.AreEqual("before", TimelineEvaluator.Sample(timeline.Tracks[1], 5)!.Text);
        Assert.IsTrue(TimelineEvaluator.Sample(timeline.Tracks[2], 5)!.Boolean);
        Assert.IsTrue(TimelineEvaluator.Sample(timeline.Tracks[0], 10)!.Boolean);
        Assert.AreEqual("after", TimelineEvaluator.Sample(timeline.Tracks[1], 10)!.Text);
        Assert.IsFalse(TimelineEvaluator.Sample(timeline.Tracks[2], 10)!.Boolean);
    }

    [TestMethod]
    public void OutlineAcceptsBooleanAndNumericCorpusForms()
    {
        XuiTimeline booleanTimeline = XuiTimelineParser.Parse(
                XuiDocument.FromText(CreateTimelineXml(
                    ["Outline"],
                    ["false"],
                    0,
                    ["true"],
                    10)))
            .Timelines[0];
        XuiTimeline numericTimeline = XuiTimelineParser.Parse(
                XuiDocument.FromText(CreateTimelineXml(
                    ["Outline"],
                    ["0"],
                    0,
                    ["2"],
                    10)))
            .Timelines[0];

        Assert.AreEqual(
            XuiTimelineValueKind.Boolean,
            TimelineEvaluator.Sample(booleanTimeline.Tracks[0], 5)!.Kind);
        Assert.IsFalse(TimelineEvaluator.Sample(booleanTimeline.Tracks[0], 5)!.Boolean);
        Assert.AreEqual(
            1,
            TimelineEvaluator.Sample(numericTimeline.Tracks[0], 5)!.Number,
            0.0001);
    }

    [TestMethod]
    public void UnknownPropertyDoesNotShiftFollowingPropertyValues()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>1</Width><Height>1</Height></Properties>" +
            "<MyImage><Properties><Id>I</Id></Properties></MyImage><Timelines><Timeline>" +
            "<Id>I</Id><TimelineProp>EngineMystery</TimelineProp>" +
            "<TimelineProp>Opacity</TimelineProp><KeyFrame><Time>0</Time>" +
            "<Interpolation>0</Interpolation><Prop>mystery</Prop><Prop>0.75</Prop>" +
            "</KeyFrame></Timeline></Timelines></XuiCanvas>");

        XuiTimelineSet set = XuiTimelineParser.Parse(document);

        Assert.AreEqual(1, set.Timelines[0].Tracks.Count);
        Assert.AreEqual(
            0.75,
            TimelineEvaluator.Sample(set.Timelines[0].Tracks[0], 0)!.Number,
            0.0001);
        Assert.AreEqual(1, set.Timelines[0].Tracks[0].SourcePropertyIndex);
        Assert.IsTrue(set.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-TL002"));
    }

    [TestMethod]
    public void NamedFrameCommandsStopGotoAndDetectCycles()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>1</Width><Height>1</Height></Properties>" +
            "<Timelines><NamedFrames>" +
            "<NamedFrame><Name>Start</Name><Time>0</Time></NamedFrame>" +
            "<NamedFrame><Name>A</Name><Time>1</Time><Command>goto</Command>" +
            "<CommandParams>B</CommandParams></NamedFrame>" +
            "<NamedFrame><Name>B</Name><Time>1</Time><Command>gotoandplay</Command>" +
            "<CommandParams>A</CommandParams></NamedFrame>" +
            "</NamedFrames></Timelines></XuiCanvas>");
        XuiTimelineSet set = XuiTimelineParser.Parse(document);
        string scope = set.NamedFrames[0].ScopeKey;

        TimelinePlaybackState state = TimelinePlayback.Advance(
            set,
            scope,
            0,
            playing: true,
            loop: false);

        Assert.IsFalse(state.IsPlaying);
        Assert.IsTrue(state.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-TL010"));
    }

    private static string CreateTimelineXml(
        IReadOnlyList<string> properties,
        IReadOnlyList<string> firstValues,
        int firstTick,
        IReadOnlyList<string>? secondValues = null,
        int secondTick = 0)
    {
        string propertyXml = string.Concat(properties.Select(
            static property => $"<TimelineProp>{property}</TimelineProp>"));
        string frame1 =
            $"<KeyFrame><Time>{firstTick}</Time><Interpolation>0</Interpolation>" +
            string.Concat(firstValues.Select(static value => $"<Prop>{value}</Prop>")) +
            "</KeyFrame>";
        string frame2 = secondValues is null
            ? string.Empty
            : $"<KeyFrame><Time>{secondTick}</Time><Interpolation>0</Interpolation>" +
              string.Concat(secondValues.Select(static value => $"<Prop>{value}</Prop>")) +
              "</KeyFrame>";
        return
            "<XuiCanvas><Properties><Width>1280</Width><Height>720</Height></Properties>" +
            "<MyImage><Properties><Id>Target</Id></Properties></MyImage>" +
            $"<Timelines><Timeline><Id>Target</Id>{propertyXml}{frame1}{frame2}" +
            "</Timeline></Timelines></XuiCanvas>";
    }

    private static string Key(int tick, string value) =>
        $"<KeyFrame><Time>{tick}</Time><Interpolation>0</Interpolation>" +
        $"<Prop>{value}</Prop></KeyFrame>";
}
