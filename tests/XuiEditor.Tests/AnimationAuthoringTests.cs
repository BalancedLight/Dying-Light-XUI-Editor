using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Documents;

namespace XuiEditor.Tests;

[TestClass]
public sealed class AnimationAuthoringTests
{
    [TestMethod]
    public void BuiltInPresetsExposeRecoveredStockRanges()
    {
        AssertPreset("quick-show-hide", 11, "Show", 0, "EndShow", 5, "Hide", 6, "EndHide", 11);
        AssertPreset("menu-transition", 40, "Idle", 0, "TransIn", 1, "EndTransIn", 20, "TransOut", 21, "EndTransOut", 40);
        AssertPreset("hud-pop", 10, "Show", 0, "EndShow", 8, "Hide", 9, "EndHide", 10);

        XuiAnimationPreset buttons = XuiAnimationPresets.Find("button-states");
        Assert.AreEqual(92, buttons.MaximumTick);
        string joinedStates = string.Join(
            ",",
            buttons.NamedFrames.Select(static frame => frame.Name)
                .Where(static name => !name.StartsWith("End", StringComparison.Ordinal)));
        Assert.AreEqual(
            "Normal,InitFocus,Focus,KillFocus,Press,NormalPress,NormalSel,FocusSel,NormalDisable,FocusDisable",
            joinedStates);
        XuiAnimationTrackTemplate scale = buttons.Tracks.Single(static track => track.PropertyName == "Scale");
        Assert.AreEqual("0.7,0.7,1", scale.Keys.Single(static key => key.Tick == 42).Value);
        Assert.AreEqual("1,1,1", scale.Keys.Single(static key => key.Tick == 50).Value);
        Assert.AreEqual("goto", buttons.NamedFrames.Single(static frame => frame.Name == "EndInitFocus").Command);
        Assert.AreEqual("Focus", buttons.NamedFrames.Single(static frame => frame.Name == "EndInitFocus").CommandParameter);
        XuiAnimationPreset quick = XuiAnimationPresets.Find("quick-show-hide");
        Assert.AreEqual(string.Empty, quick.NamedFrames.Single(static frame => frame.Name == "Show").Command);
        Assert.AreEqual("stop", quick.NamedFrames.Single(static frame => frame.Name == "EndShow").Command);
        XuiAnimationPreset transition = XuiAnimationPresets.Find("menu-transition");
        Assert.AreEqual("goto", transition.NamedFrames.Single(static frame => frame.Name == "EndTransIn").Command);
        Assert.AreEqual("Idle", transition.NamedFrames.Single(static frame => frame.Name == "EndTransIn").CommandParameter);
    }

    [TestMethod]
    public void QuickShowHideIsInsertedLosslesslyAndUndoableAsOneCommand()
    {
        string source = "<XuiCanvas>\r\n  <!--keep-->\r\n  <MyImage><Properties><Id>I</Id><Opacity>0.25</Opacity></Properties></MyImage>\r\n</XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode target = document.Root.Elements("MyImage").Single();
        XuiAnimationAuthoringResult plan = XuiAnimationAuthoringService.Plan(
            document,
            new XuiAnimationAuthoringRequest(
                document.Root.Key,
                [target.Key],
                XuiAnimationPresets.Find("quick-show-hide")));

        Assert.IsFalse(plan.ConflictReport.HasErrors);
        Assert.IsNotNull(plan.Command);
        document.Execute(plan.Command);

        StringAssert.Contains(document.Text, "<!--keep-->");
        Assert.IsTrue(document.Text.StartsWith("<XuiCanvas>\r\n", StringComparison.Ordinal));
        XuiTimelineSet parsed = XuiTimelineParser.Parse(document);
        Assert.AreEqual(2, parsed.Timelines.Count);
        Assert.AreEqual(4, parsed.NamedFrames.Count);
        XuiTrack opacity = parsed.Timelines.Single(timeline => timeline.Tracks[0].PropertyName == "Opacity").Tracks[0];
        Assert.AreEqual(0.4, TimelineEvaluator.Sample(opacity, 2)!.Number, 0.0001);

        document.Undo();
        Assert.AreEqual(source, document.Text);
        Assert.IsFalse(document.History.CanUndo);
        document.Redo();
        Assert.AreEqual(4, XuiTimelineParser.Parse(document).NamedFrames.Count);
    }

    [TestMethod]
    public void IdenticalMarkersAreReusedAndConflictingMarkersAreRejected()
    {
        const string source = "<XuiCanvas><Properties><Id>Root</Id></Properties><MyImage><Properties><Id>I</Id></Properties></MyImage><Timelines><NamedFrames><NamedFrame><Name>P_Show</Name><Time>4</Time><Command></Command><CommandParams></CommandParams></NamedFrame></NamedFrames></Timelines></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode target = document.Root.Elements("MyImage").Single();
        XuiAnimationAuthoringResult reusable = XuiAnimationAuthoringService.Plan(
            document,
            new XuiAnimationAuthoringRequest(
                document.Root.Key,
                [target.Key],
                XuiAnimationPresets.Find("quick-show-hide"),
                StartTick: 4,
                FramePrefix: "P_"));
        Assert.IsFalse(reusable.ConflictReport.HasErrors);
        Assert.IsTrue(reusable.ConflictReport.Conflicts.Any(static conflict => conflict.Severity == XuiAnimationConflictSeverity.Information));

        XuiAnimationAuthoringResult conflicting = XuiAnimationAuthoringService.Plan(
            document,
            new XuiAnimationAuthoringRequest(
                document.Root.Key,
                [target.Key],
                XuiAnimationPresets.Find("quick-show-hide"),
                StartTick: 5,
                FramePrefix: "P_"));
        Assert.IsTrue(conflicting.ConflictReport.HasErrors);
        Assert.IsNull(conflicting.Command);
        Assert.AreEqual(source, document.Text);
    }

    [TestMethod]
    public void SingleTrackKeyMergesAndSamplesSiblingProperties()
    {
        const string source = "<XuiCanvas><MyImage><Properties><Id>I</Id></Properties></MyImage><Timelines><Timeline><Id>I</Id><TimelineProp>Opacity</TimelineProp><TimelineProp>Scale</TimelineProp><KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>0</Prop><Prop>1,1,1</Prop></KeyFrame><KeyFrame><Time>10</Time><Interpolation>0</Interpolation><Prop>1</Prop><Prop>2,2,1</Prop></KeyFrame></Timeline></Timelines></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode target = document.Root.Elements("MyImage").Single();
        XuiAnimationAuthoringResult plan = XuiAnimationAuthoringService.PlanTrackKey(
            document,
            document.Root.Key,
            target.Key,
            "Opacity",
            "0.25",
            5);

        Assert.IsFalse(plan.ConflictReport.HasErrors);
        document.Execute(plan.Command!);
        XuiTimeline timeline = XuiTimelineParser.Parse(document).Timelines.Single();
        XuiTrack opacity = timeline.Tracks.Single(static track => track.PropertyName == "Opacity");
        XuiTrack scale = timeline.Tracks.Single(static track => track.PropertyName == "Scale");
        Assert.AreEqual(0.25, TimelineEvaluator.Sample(opacity, 5)!.Number, 0.0001);
        Assert.AreEqual(1.5, TimelineEvaluator.Sample(scale, 5)!.Vector3.X, 0.0001);
    }

    [TestMethod]
    public void MarkersOnlyCreatesNoMotionAndParentScopeCanTargetSeveralChildren()
    {
        const string source = "<XuiCanvas><AdvGroup><Properties><Id>G</Id></Properties><MyImage><Properties><Id>A</Id></Properties></MyImage><MyImage><Properties><Id>B</Id></Properties></MyImage></AdvGroup></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode group = document.Root.Elements("AdvGroup").Single();
        XuiSyntaxNode[] targets = group.Elements("MyImage").ToArray();
        XuiAnimationAuthoringResult markers = XuiAnimationAuthoringService.Plan(
            document,
            new XuiAnimationAuthoringRequest(
                group.Key,
                [],
                XuiAnimationPresets.Find("quick-show-hide"),
                MarkersOnly: true));
        Assert.IsFalse(markers.ConflictReport.HasErrors);
        document.Execute(markers.Command!);
        Assert.AreEqual(0, XuiTimelineParser.Parse(document).Timelines.Count);
        Assert.AreEqual(4, XuiTimelineParser.Parse(document).NamedFrames.Count);
        document.Undo();

        XuiAnimationAuthoringResult parent = XuiAnimationAuthoringService.Plan(
            document,
            new XuiAnimationAuthoringRequest(
                group.Key,
                targets.Select(static target => target.Key).ToArray(),
                XuiAnimationPresets.Find("menu-transition")));
        Assert.IsFalse(parent.ConflictReport.HasErrors);
        document.Execute(parent.Command!);
        XuiTimelineSet set = XuiTimelineParser.Parse(document);
        Assert.AreEqual(2, set.Timelines.Count);
        Assert.AreEqual(
            "A,B",
            string.Join(
                ",",
                set.Timelines.Select(static timeline => timeline.TargetId)
                    .Order(StringComparer.Ordinal)));
        Assert.IsTrue(set.Timelines.All(timeline => timeline.ScopeKey == group.Key));
    }

    [TestMethod]
    public void InvalidCustomValuesAndDuplicateTracksAreRejectedTransactionally()
    {
        const string source = "<XuiCanvas><MyImage><Properties><Id>I</Id></Properties></MyImage><Timelines><Timeline><Id>I</Id><TimelineProp>Opacity</TimelineProp><KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>1</Prop></KeyFrame></Timeline><Timeline><Id>I</Id><TimelineProp>Opacity</TimelineProp><KeyFrame><Time>5</Time><Interpolation>0</Interpolation><Prop>0</Prop></KeyFrame></Timeline></Timelines></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode target = document.Root.Elements("MyImage").Single();
        XuiAnimationAuthoringResult duplicate = XuiAnimationAuthoringService.PlanTrackKey(
            document,
            document.Root.Key,
            target.Key,
            "Opacity",
            "0.5",
            3);
        Assert.IsTrue(duplicate.ConflictReport.HasErrors);

        XuiAnimationAuthoringResult invalid = XuiAnimationAuthoringService.Plan(
            document,
            new XuiAnimationAuthoringRequest(
                document.Root.Key,
                [target.Key],
                XuiAnimationPresets.Find("custom-property"),
                CustomProperty: "Opacity",
                CustomStartValue: "not-a-number",
                CustomEndValue: "1"));
        Assert.IsTrue(invalid.ConflictReport.HasErrors);
        Assert.IsNull(invalid.Command);
        Assert.AreEqual(source, document.Text);
    }

    [TestMethod]
    public void SelfClosingOwnersAndDuplicateTargetIdsAreRejectedTransactionally()
    {
        const string selfClosingSource =
            "<XuiCanvas><AdvGroup /><MyImage><Properties><Id>I</Id></Properties></MyImage></XuiCanvas>";
        XuiDocument selfClosing = XuiDocument.FromText(selfClosingSource);
        XuiSyntaxNode owner = selfClosing.Root.Elements("AdvGroup").Single();
        XuiSyntaxNode target = selfClosing.Root.Elements("MyImage").Single();
        XuiAnimationAuthoringResult invalidOwner = XuiAnimationAuthoringService.Plan(
            selfClosing,
            new XuiAnimationAuthoringRequest(
                owner.Key,
                [target.Key],
                XuiAnimationPresets.Find("quick-show-hide")));

        Assert.IsTrue(invalidOwner.ConflictReport.HasErrors);
        Assert.IsNull(invalidOwner.Command);
        Assert.AreEqual(selfClosingSource, selfClosing.Text);

        const string duplicateSource =
            "<XuiCanvas><MyImage><Properties><Id>I</Id></Properties></MyImage><MyImage><Properties><Id>I</Id></Properties></MyImage></XuiCanvas>";
        XuiDocument duplicate = XuiDocument.FromText(duplicateSource);
        XuiAnimationAuthoringResult duplicateTarget = XuiAnimationAuthoringService.Plan(
            duplicate,
            new XuiAnimationAuthoringRequest(
                duplicate.Root.Key,
                [duplicate.Root.Elements("MyImage").First().Key],
                XuiAnimationPresets.Find("quick-show-hide")));

        Assert.IsTrue(duplicateTarget.ConflictReport.HasErrors);
        Assert.IsNull(duplicateTarget.Command);
        Assert.AreEqual(duplicateSource, duplicate.Text);
    }

    [TestMethod]
    public void EveryMotionPresetSamplesOnBetweenAndAfterGeneratedKeys()
    {
        foreach (XuiAnimationPreset preset in XuiAnimationPresets.BuiltIn.Where(
                     static preset => preset.Tracks.Count > 0))
        {
            XuiDocument document = XuiDocument.FromText(
                "<XuiCanvas><MyImage><Properties><Id>I</Id><Color>0xff112233</Color></Properties></MyImage></XuiCanvas>");
            XuiSyntaxNode target = document.Root.Elements("MyImage").Single();
            XuiAnimationAuthoringResult plan = XuiAnimationAuthoringService.Plan(
                document,
                new XuiAnimationAuthoringRequest(
                    document.Root.Key,
                    [target.Key],
                    preset,
                    StartTick: 7));
            Assert.IsFalse(
                plan.ConflictReport.HasErrors,
                $"{preset.Name}: {string.Join("; ", plan.ConflictReport.Conflicts.Select(static conflict => conflict.Message))}");
            document.Execute(plan.Command!);
            XuiTimelineSet set = XuiTimelineParser.Parse(document);
            foreach (XuiAnimationTrackTemplate template in preset.Tracks)
            {
                XuiTrack track = set.Timelines
                    .Single(timeline => timeline.Tracks[0].PropertyName == template.PropertyName)
                    .Tracks[0];
                foreach (XuiAnimationKeyTemplate key in template.Keys)
                {
                    string expected = key.Value == XuiAnimationPresets.BaseColorToken
                        ? "0xff112233"
                        : key.Value;
                    Assert.AreEqual(
                        expected,
                        TimelineEvaluator.Sample(track, key.Tick + 7)!.ToXuiString(),
                        ignoreCase: true,
                        culture: CultureInfo.InvariantCulture,
                        message: $"{preset.Name}.{template.PropertyName}@{key.Tick}");
                }

                int first = template.Keys.Min(static key => key.Tick) + 7;
                int last = template.Keys.Max(static key => key.Tick) + 7;
                Assert.IsNotNull(TimelineEvaluator.Sample(track, first));
                Assert.IsNotNull(TimelineEvaluator.Sample(track, last + 20));
                if (last - first > 1)
                {
                    Assert.IsNotNull(TimelineEvaluator.Sample(track, first + ((last - first) / 2)));
                }
            }
        }
    }

    private static void AssertPreset(
        string id,
        int maximumTick,
        params object[] markerPairs)
    {
        XuiAnimationPreset preset = XuiAnimationPresets.Find(id);
        Assert.AreEqual(maximumTick, preset.MaximumTick);
        for (int index = 0; index < markerPairs.Length; index += 2)
        {
            string name = (string)markerPairs[index];
            int tick = (int)markerPairs[index + 1];
            Assert.AreEqual(tick, preset.NamedFrames.Single(frame => frame.Name == name).Tick);
        }
    }
}
