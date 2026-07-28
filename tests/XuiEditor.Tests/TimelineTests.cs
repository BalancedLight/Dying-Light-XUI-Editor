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
            "0.1,0.5,0.8,1", "1,0.2,0.7,0.9", "0xff445566", "4", "icon",
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
        XuiTrack constant = set.Timelines[0].Tracks.Single(static track =>
            track.Property == XuiTimelineProperty.Const1);
        XuiAnimatedValue sampledConstant =
            TimelineEvaluator.Sample(constant, 0)!;
        Assert.AreEqual(XuiTimelineValueKind.Vector4, sampledConstant.Kind);
        Assert.AreEqual(0.9, sampledConstant.Vector4.W, 0.0001);
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
    public void StepTracksUseAnIntermediateKeyValueOnItsExactTick()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>1</Width><Height>1</Height></Properties>" +
            "<MyImage><Properties><Id>I</Id></Properties></MyImage>" +
            "<Timelines><Timeline><Id>I</Id><TimelineProp>Show</TimelineProp>" +
            "<TimelineProp>ImagePath</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation>" +
            "<Prop>false</Prop><Prop>before</Prop></KeyFrame>" +
            "<KeyFrame><Time>5</Time><Interpolation>0</Interpolation>" +
            "<Prop>true</Prop><Prop>exact</Prop></KeyFrame>" +
            "<KeyFrame><Time>10</Time><Interpolation>0</Interpolation>" +
            "<Prop>false</Prop><Prop>after</Prop></KeyFrame>" +
            "</Timeline></Timelines></XuiCanvas>");
        XuiTimeline timeline =
            XuiTimelineParser.Parse(document).Timelines[0];

        Assert.IsFalse(
            TimelineEvaluator.Sample(timeline.Tracks[0], 4)!.Boolean);
        Assert.IsTrue(
            TimelineEvaluator.Sample(timeline.Tracks[0], 5)!.Boolean);
        Assert.AreEqual(
            "exact",
            TimelineEvaluator.Sample(timeline.Tracks[1], 5)!.Text);
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
    public void ConstantTracksAcceptScalarAndVectorFormsAndInterpolate()
    {
        XuiTimeline vectorTimeline = XuiTimelineParser.Parse(
                XuiDocument.FromText(CreateTimelineXml(
                    ["Const0"],
                    ["0,1,2,3"],
                    0,
                    ["4,5,6,7"],
                    10)))
            .Timelines[0];
        XuiTimeline scalarTimeline = XuiTimelineParser.Parse(
                XuiDocument.FromText(CreateTimelineXml(
                    ["Const1"],
                    ["2"],
                    0,
                    ["4"],
                    10)))
            .Timelines[0];

        XuiAnimatedValue vector =
            TimelineEvaluator.Sample(vectorTimeline.Tracks[0], 5)!;
        Assert.AreEqual(XuiTimelineValueKind.Vector4, vector.Kind);
        Assert.AreEqual(2, vector.Vector4.X, 0.0001);
        Assert.AreEqual(5, vector.Vector4.W, 0.0001);
        Assert.AreEqual(
            "2,3,4,5",
            vector.ToXuiString());
        Assert.AreEqual(
            3,
            TimelineEvaluator.Sample(scalarTimeline.Tracks[0], 5)!.Number,
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

    [TestMethod]
    public void ScopeCatalogAndWorkspaceRememberIndependentLocalTicks()
    {
        XuiDocument first = XuiDocument.FromText(
            NestedScopeXml(groupMaximumTick: 10));
        XuiTimelineSet firstSet = XuiTimelineParser.Parse(first);
        XuiTimelineScopeCatalog firstCatalog =
            XuiTimelineScopeCatalog.Build(first, firstSet);
        XuiTimelineScope root = firstCatalog.RootScope!;
        XuiTimelineScope group = firstCatalog.Scopes.Single(scope =>
            scope.OwnerId == "G_Group");
        XuiSyntaxNode groupTarget = first.Root
            .DescendantsAndSelf()
            .Single(node =>
                XuiModelReader.GetId(node, first.Text) == "I_Child");
        XuiTimelineWorkspace workspace = new(firstCatalog);

        Assert.AreEqual("XuiCanvas", root.Owner.Name);
        Assert.AreEqual(root.ScopeKey, workspace.ActiveScope?.ScopeKey);
        Assert.IsTrue(workspace.ResolveSelection([groupTarget], first.Text));
        Assert.AreEqual(group.ScopeKey, workspace.ActiveScope?.ScopeKey);
        Assert.IsTrue(workspace.SetActiveTick(9));
        Assert.AreEqual(9, workspace.ActiveTick);

        Assert.IsTrue(workspace.ResolveSelection([root.Owner], first.Text));
        Assert.IsTrue(workspace.SetActiveTick(3));
        Assert.AreEqual(3, workspace.ActiveTick);
        Assert.IsTrue(workspace.ResolveSelection([groupTarget], first.Text));
        Assert.AreEqual(9, workspace.ActiveTick);
        Assert.AreEqual(3, workspace.EvaluationState.TickFor(root.ScopeKey));
        Assert.AreEqual(9, workspace.EvaluationState.TickFor(group.ScopeKey));

        XuiDocument edited = XuiDocument.FromText(
            NestedScopeXml(groupMaximumTick: 4));
        XuiTimelineScopeCatalog editedCatalog =
            XuiTimelineScopeCatalog.Build(
                edited,
                XuiTimelineParser.Parse(edited));
        workspace.Rebind(editedCatalog);

        Assert.AreEqual(4, workspace.ActiveTick);
        Assert.AreEqual(
            3,
            workspace.TickFor(editedCatalog.RootScope!.ScopeKey));
    }

    [TestMethod]
    public void WorkspaceDefaultsEachScopeToItsEarliestFullyVisiblePose()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<AdvGroup><Properties><Id>Group</Id></Properties>" +
            "<MyImage><Properties><Id>Animated</Id><Show>false</Show></Properties></MyImage>" +
            "<Timelines><Timeline><Id>Animated</Id>" +
            "<TimelineProp>Show</TimelineProp><TimelineProp>Opacity</TimelineProp>" +
            "<TimelineProp>Scale</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation>" +
            "<Prop>false</Prop><Prop>0</Prop><Prop>0,0,0</Prop></KeyFrame>" +
            "<KeyFrame><Time>3</Time><Interpolation>0</Interpolation>" +
            "<Prop>true</Prop><Prop>0</Prop><Prop>0,0,0</Prop></KeyFrame>" +
            "<KeyFrame><Time>10</Time><Interpolation>0</Interpolation>" +
            "<Prop>true</Prop><Prop>1</Prop><Prop>0.5,0.5,0</Prop></KeyFrame>" +
            "<KeyFrame><Time>12</Time><Interpolation>0</Interpolation>" +
            "<Prop>true</Prop><Prop>1</Prop><Prop>1,1,0</Prop></KeyFrame>" +
            "<KeyFrame><Time>20</Time><Interpolation>0</Interpolation>" +
            "<Prop>false</Prop><Prop>0</Prop><Prop>0,0,0</Prop></KeyFrame>" +
            "</Timeline></Timelines></AdvGroup>" +
            "<Timelines><Timeline><Id>Group</Id><TimelineProp>Opacity</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>1</Prop></KeyFrame>" +
            "<KeyFrame><Time>5</Time><Interpolation>0</Interpolation><Prop>0</Prop></KeyFrame>" +
            "</Timeline></Timelines></XuiCanvas>");
        XuiTimelineScopeCatalog catalog = XuiTimelineScopeCatalog.Build(
            document,
            XuiTimelineParser.Parse(document));
        XuiTimelineScope groupScope = catalog.Scopes.Single(scope =>
            scope.OwnerId == "Group");
        XuiSyntaxNode animated = document.Root
            .DescendantsAndSelf()
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Animated");
        XuiTimelineWorkspace workspace = new(catalog);

        Assert.AreEqual(12, groupScope.ComposedTick);
        Assert.AreEqual(0, workspace.ActiveTick);
        Assert.IsTrue(workspace.ResolveSelection([animated], document.Text));
        Assert.AreEqual(12, workspace.ActiveTick);
        Assert.IsTrue(workspace.ActiveTickIsComposed);
        Assert.AreEqual(
            12,
            workspace.EvaluationState.TickFor(groupScope.ScopeKey));
        Assert.AreEqual(0, workspace.RememberedTicks.Count);

        Assert.IsTrue(workspace.ResetActiveTick());
        Assert.AreEqual(0, workspace.ActiveTick);
        Assert.IsFalse(workspace.ActiveTickIsComposed);
        Assert.AreEqual(0, workspace.EvaluationState.TickFor(
            groupScope.ScopeKey));
    }

    [TestMethod]
    public void MixedScopeSelectionDisablesTheActiveWorkspaceScope()
    {
        XuiDocument document = XuiDocument.FromText(
            NestedScopeXml(groupMaximumTick: 10));
        XuiTimelineScopeCatalog catalog = XuiTimelineScopeCatalog.Build(
            document,
            XuiTimelineParser.Parse(document));
        XuiSyntaxNode rootTarget = document.Root
            .DescendantsAndSelf()
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "HUD_DI");
        XuiSyntaxNode groupTarget = document.Root
            .DescendantsAndSelf()
            .Single(node =>
                XuiModelReader.GetId(node, document.Text) == "I_Child");
        XuiTimelineWorkspace workspace = new(catalog);

        Assert.IsTrue(workspace.ResolveSelection(
            [rootTarget, groupTarget],
            document.Text));

        Assert.IsTrue(workspace.HasMixedSelection);
        Assert.IsNull(workspace.ActiveScope);
        Assert.IsFalse(workspace.SetActiveTick(5));
    }

    [TestMethod]
    public void SynchronizedEvaluationStateRetainsTheLegacyTickContract()
    {
        XuiTimelineEvaluationState synchronized =
            XuiTimelineEvaluationState.Synchronized(17);
        XuiTimelineEvaluationState local =
            XuiTimelineEvaluationState.ScopeLocal(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["group"] = 9,
                });

        Assert.AreEqual(17, synchronized.TickFor("root"));
        Assert.AreEqual(17, synchronized.TickFor("group"));
        Assert.AreEqual(0, local.TickFor("root"));
        Assert.AreEqual(9, local.TickFor("group"));
    }

    [TestMethod]
    public void PlaybackStopsAtTheActiveScopesLocalMaximum()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>1</Width><Height>1</Height></Properties>" +
            "<AdvGroup><Properties><Id>Group</Id></Properties>" +
            "<MyImage><Properties><Id>Child</Id></Properties></MyImage>" +
            "<Timelines><Timeline><Id>Child</Id><TimelineProp>Opacity</TimelineProp>" +
            Key(0, "0") + Key(2, "1") +
            "</Timeline></Timelines></AdvGroup>" +
            "<MyImage><Properties><Id>RootChild</Id></Properties></MyImage>" +
            "<Timelines><Timeline><Id>RootChild</Id><TimelineProp>Opacity</TimelineProp>" +
            Key(0, "0") + Key(100, "1") +
            "</Timeline></Timelines></XuiCanvas>");
        XuiTimelineSet set = XuiTimelineParser.Parse(document);
        string groupScope = set.Timelines.Single(timeline =>
            timeline.TargetId == "Child").ScopeKey;

        TimelinePlaybackState state = TimelinePlayback.Advance(
            set,
            groupScope,
            currentTick: 2,
            playing: true,
            loop: false);

        Assert.AreEqual(2, state.Tick);
        Assert.IsFalse(state.IsPlaying);
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

    private static string NestedScopeXml(int groupMaximumTick) =>
        "<XuiCanvas><Properties><Width>1280</Width><Height>720</Height></Properties>" +
        "<AdvGroup><Properties><Id>HUD_DI</Id></Properties>" +
        "<AdvGroup><Properties><Id>G_Group</Id></Properties>" +
        "<MyImage><Properties><Id>I_Child</Id></Properties></MyImage>" +
        "<Timelines><Timeline><Id>I_Child</Id><TimelineProp>Show</TimelineProp>" +
        Key(0, "true") +
        Key(groupMaximumTick, "false") +
        "</Timeline><NamedFrames><NamedFrame><Name>Idle</Name><Time>0</Time>" +
        "</NamedFrame><NamedFrame><Name>End</Name><Time>" +
        groupMaximumTick.ToString(
            System.Globalization.CultureInfo.InvariantCulture) +
        "</Time></NamedFrame></NamedFrames></Timelines></AdvGroup></AdvGroup>" +
        "<Timelines><Timeline><Id>HUD_DI</Id><TimelineProp>Opacity</TimelineProp>" +
        Key(0, "1") +
        Key(5, "0") +
        "</Timeline></Timelines></XuiCanvas>";
}
