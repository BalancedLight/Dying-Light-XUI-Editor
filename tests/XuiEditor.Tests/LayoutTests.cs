using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Values;

namespace XuiEditor.Tests;

[TestClass]
public sealed class LayoutTests
{
    [TestMethod]
    public void RightAndBottomAnchorsUseRecoveredMargins()
    {
        XuiRenderNode child = EvaluateChild(
            "<Width>10</Width><Height>20</Height><Position>5,7,0</Position><Anchor>12</Anchor>");

        Assert.AreEqual(85, child.WorldBounds.X, 0.001);
        Assert.AreEqual(73, child.WorldBounds.Y, 0.001);
        Assert.AreEqual(10, child.WorldBounds.Width, 0.001);
        Assert.AreEqual(20, child.WorldBounds.Height, 0.001);
    }

    [TestMethod]
    public void CenterAnchorsMeasureRelativeToElementCenter()
    {
        XuiRenderNode child = EvaluateChild(
            "<Width>20</Width><Height>10</Height><Position>4,6,0</Position><Anchor>48</Anchor>");

        Assert.AreEqual(36, child.WorldBounds.X, 0.001);
        Assert.AreEqual(39, child.WorldBounds.Y, 0.001);
    }

    [TestMethod]
    public void OpposingAnchorsStretchWithoutRewritingDescendants()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Parent</Id><Width>80</Width><Height>70</Height>" +
            "<Position>10,10,0</Position></Properties>" +
            "<MyImage><Properties><Id>Child</Id><Width>20</Width><Height>20</Height>" +
            "<Position>5,6,0</Position><Anchor>15</Anchor></Properties></MyImage>" +
            "</AdvGroup>");

        XuiRenderNode child = Frame(document).Nodes.Single(static node =>
            node.Id == "Child");

        Assert.AreEqual(5, child.Position.X, 0.001);
        Assert.AreEqual(6, child.Position.Y, 0.001);
        Assert.AreEqual(20, child.Size.X, 0.001);
        Assert.AreEqual(20, child.Size.Y, 0.001);
        Assert.AreEqual(15, child.WorldBounds.X, 0.001);
        Assert.AreEqual(16, child.WorldBounds.Y, 0.001);
    }

    [TestMethod]
    public void ParentResizeScalesChildrenUnlessKeepFlagsOptOut()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Parent</Id><Width>100</Width><Height>100</Height>" +
            "</Properties>" +
            "<MyImage><Properties><Id>Scaled</Id><Width>20</Width><Height>10</Height>" +
            "<Position>10,20,0</Position></Properties></MyImage>" +
            "<MyImage><Properties><Id>Kept</Id><Width>20</Width><Height>10</Height>" +
            "<Position>10,20,0</Position><KeepWidthOnParentSizeChange>true</KeepWidthOnParentSizeChange>" +
            "<KeepHeightOnParentSizeChange>true</KeepHeightOnParentSizeChange>" +
            "<KeepPosXOnParentSizeChange>true</KeepPosXOnParentSizeChange>" +
            "<KeepPosYOnParentSizeChange>true</KeepPosYOnParentSizeChange>" +
            "</Properties></MyImage></AdvGroup>" +
            "<Timelines><Timeline><Id>Parent</Id><TimelineProp>Width</TimelineProp>" +
            "<TimelineProp>Height</TimelineProp><KeyFrame><Time>0</Time>" +
            "<Interpolation>0</Interpolation><Prop>200</Prop><Prop>50</Prop>" +
            "</KeyFrame></Timeline></Timelines>");

        XuiRenderFrame frame = Frame(document);
        XuiRenderNode scaled = frame.Nodes.Single(static node =>
            node.Id == "Scaled");
        XuiRenderNode kept = frame.Nodes.Single(static node =>
            node.Id == "Kept");

        Assert.AreEqual(new Core.Values.XuiVector2(40, 5), scaled.Size);
        Assert.AreEqual(20, scaled.Position.X, 0.001);
        Assert.AreEqual(10, scaled.Position.Y, 0.001);
        Assert.AreEqual(new Core.Values.XuiVector2(20, 10), kept.Size);
        Assert.AreEqual(10, kept.Position.X, 0.001);
        Assert.AreEqual(20, kept.Position.Y, 0.001);
    }

    [TestMethod]
    public void HoldAspectRatioUsesSelectedParentAxisAndPreservesPivotMode()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Parent</Id><Width>100</Width><Height>100</Height>" +
            "</Properties><MyImage><Properties><Id>Child</Id><Width>10</Width>" +
            "<Height>10</Height><Position>10,10,0</Position><Pivot>5,5,0</Pivot>" +
            "<HoldAspectRatio>true</HoldAspectRatio><HoldAspectRatioX>true</HoldAspectRatioX>" +
            "<HoldAspectPivotPosition>true</HoldAspectPivotPosition>" +
            "</Properties></MyImage></AdvGroup>" +
            "<Timelines><Timeline><Id>Parent</Id><TimelineProp>Width</TimelineProp>" +
            "<TimelineProp>Height</TimelineProp><KeyFrame><Time>0</Time>" +
            "<Interpolation>0</Interpolation><Prop>200</Prop><Prop>300</Prop>" +
            "</KeyFrame></Timeline></Timelines>");

        XuiRenderNode child = Frame(document).Nodes.Single(static node =>
            node.Id == "Child");

        Assert.AreEqual(new Core.Values.XuiVector2(20, 20), child.Size);
        Assert.AreEqual(20, child.Position.X, 0.001);
        Assert.AreEqual(30, child.Position.Y, 0.001);
        Assert.AreEqual(10, child.Pivot.X, 0.001);
        Assert.AreEqual(10, child.Pivot.Y, 0.001);
    }

    [TestMethod]
    public void ResolutionKeepFlagsCompensateTheViewportTransform()
    {
        XuiDocument document = Document(
            "<MyImage><Properties><Id>Kept</Id><Width>20</Width><Height>20</Height>" +
            "<Position>10,10,0</Position><KeepWidthOnResolutionChange>true</KeepWidthOnResolutionChange>" +
            "<KeepPosXOnResolutionChange>true</KeepPosXOnResolutionChange>" +
            "</Properties></MyImage>");

        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(200, 300, PreserveAspect: false),
            0);
        XuiRenderNode kept = frame.Nodes.Single(static node =>
            node.Id == "Kept");

        Assert.AreEqual(10, kept.Size.X, 0.001);
        Assert.AreEqual(5, kept.Position.X, 0.001);
        Assert.AreEqual(2, frame.ViewportTransform.M11, 0.001);
        Assert.AreEqual(3, frame.ViewportTransform.M22, 0.001);
        Assert.IsTrue(frame.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-LAYOUT010"));
    }

    [TestMethod]
    public void PivotScaleAndNestedTransformsProduceGoldenBounds()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Parent</Id><Width>50</Width><Height>50</Height>" +
            "<Position>10,20,0</Position></Properties>" +
            "<MyImage><Properties><Id>Child</Id><Width>10</Width><Height>10</Height>" +
            "<Position>5,5,0</Position><Pivot>5,5,0</Pivot><Scale>2,2,1</Scale>" +
            "</Properties></MyImage></AdvGroup>");

        XuiRenderNode child = Frame(document).Nodes.Single(static node =>
            node.Id == "Child");

        Assert.AreEqual(10, child.WorldBounds.X, 0.01);
        Assert.AreEqual(20, child.WorldBounds.Y, 0.01);
        Assert.AreEqual(20, child.WorldBounds.Width, 0.01);
        Assert.AreEqual(20, child.WorldBounds.Height, 0.01);
    }

    [TestMethod]
    public void ClipChildrenPropagatesAxisAlignedClip()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Parent</Id><Width>20</Width><Height>20</Height>" +
            "<Position>10,10,0</Position><ClipChildren>true</ClipChildren></Properties>" +
            "<MyImage><Properties><Id>Child</Id><Width>30</Width><Height>30</Height>" +
            "<Position>10,10,0</Position></Properties></MyImage></AdvGroup>");

        XuiRenderNode child = Frame(document).Nodes.Single(static node =>
            node.Id == "Child");

        Assert.IsNotNull(child.ClipBounds);
        Assert.AreEqual(10, child.ClipBounds.Value.X, 0.001);
        Assert.AreEqual(20, child.ClipBounds.Value.Width, 0.001);
    }

    [TestMethod]
    public void AlphaMasksAreClippedAndReportedAsAnExplicitApproximation()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Mask</Id><Width>20</Width><Height>20</Height>" +
            "<UseMask>true</UseMask></Properties>" +
            "<MyImage><Properties><Id>Child</Id><Width>30</Width><Height>30</Height>" +
            "</Properties></MyImage></AdvGroup>");

        XuiRenderFrame frame = Frame(document);
        XuiRenderNode child = frame.Nodes.Single(static node =>
            node.Id == "Child");

        Assert.IsNotNull(child.ClipBounds);
        Assert.IsTrue(frame.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-LAYOUT011"));
    }

    [TestMethod]
    public void TimelineSampleFlowsThroughOpacityAndPosition()
    {
        XuiDocument document = Document(
            "<MyImage><Properties><Id>Child</Id><Width>10</Width><Height>10</Height>" +
            "<Position>0,0,0</Position><Opacity>0</Opacity></Properties></MyImage>" +
            "<Timelines><Timeline><Id>Child</Id><TimelineProp>Opacity</TimelineProp>" +
            "<TimelineProp>Position</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation>" +
            "<Prop>0</Prop><Prop>0,0,0</Prop></KeyFrame>" +
            "<KeyFrame><Time>10</Time><Interpolation>0</Interpolation>" +
            "<Prop>1</Prop><Prop>20,10,0</Prop></KeyFrame>" +
            "</Timeline></Timelines>");

        XuiRenderNode child = DyingLightLayoutEngine.Evaluate(
                document,
                XuiViewport.Default,
                5)
            .Nodes.Single(static node => node.Id == "Child");

        Assert.AreEqual(0.5, child.Opacity, 0.001);
        Assert.AreEqual(10, child.WorldBounds.X, 0.001);
        Assert.AreEqual(5, child.WorldBounds.Y, 0.001);
    }

    [TestMethod]
    public void TimelineScopesRespectRecursionBarriersAndNearestSupervisor()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Boundary</Id><Width>50</Width><Height>50</Height>" +
            "<DisableTimelineRecursion>true</DisableTimelineRecursion></Properties>" +
            "<MyImage><Properties><Id>Child</Id><Width>10</Width><Height>10</Height>" +
            "<Opacity>1</Opacity></Properties></MyImage>" +
            "<Timelines><Timeline><Id>Child</Id><TimelineProp>Opacity</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>0.7</Prop>" +
            "</KeyFrame></Timeline></Timelines></AdvGroup>" +
            "<Timelines><Timeline><Id>Child</Id><TimelineProp>Opacity</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>0.2</Prop>" +
            "</KeyFrame></Timeline></Timelines>");

        XuiRenderNode child = Frame(document).Nodes.Single(static node =>
            node.Id == "Child");

        Assert.AreEqual(0.7, child.Opacity, 0.001);
    }

    [TestMethod]
    public void TimelineRecursionBarrierBlocksOuterSupervisor()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Boundary</Id><Width>50</Width><Height>50</Height>" +
            "<DisableTimelineRecursion>true</DisableTimelineRecursion></Properties>" +
            "<MyImage><Properties><Id>Child</Id><Width>10</Width><Height>10</Height>" +
            "<Opacity>1</Opacity></Properties></MyImage></AdvGroup>" +
            "<Timelines><Timeline><Id>Child</Id><TimelineProp>Opacity</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>0.2</Prop>" +
            "</KeyFrame></Timeline></Timelines>");

        XuiRenderNode child = Frame(document).Nodes.Single(static node =>
            node.Id == "Child");

        Assert.AreEqual(1, child.Opacity, 0.001);
    }

    [TestMethod]
    public void TextRenderingModelPreservesAuthoredTypographyAndAlignment()
    {
        XuiDocument document = Document(
            "<MyText><Properties><Id>Styled</Id><Width>200</Width><Height>80</Height>" +
            "<Text>Hello world</Text><PointSize>32</PointSize><Uppercase>true</Uppercase>" +
            "<MultiLine>true</MultiLine><TextStyle>5134</TextStyle>" +
            "<ContentHorizontalAlign>right</ContentHorizontalAlign>" +
            "<ContentVerticalAlign>middle</ContentVerticalAlign>" +
            "<ContentHorizontalBorder>7</ContentHorizontalBorder>" +
            "<ContentVerticalBorder>3</ContentVerticalBorder>" +
            "<Outline>true</Outline><OutlineSize>2</OutlineSize>" +
            "<OutlineColor>0x80112233</OutlineColor><Shadow>true</Shadow>" +
            "<ShadowOffset>4</ShadowOffset><ShadowColor>0x90445566</ShadowColor>" +
            "</Properties></MyText>" +
            "<MyHtml><Properties><Id>Html</Id><Width>100</Width><Height>20</Height>" +
            "<SourceString>Fallback source</SourceString></Properties></MyHtml>");

        XuiRenderFrame frame = Frame(document);
        XuiRenderNode styled = frame.Nodes.Single(static node =>
            node.Id == "Styled");
        XuiRenderNode html = frame.Nodes.Single(static node =>
            node.Id == "Html");

        Assert.AreEqual(32, styled.PointSize, 0.001);
        Assert.IsTrue(styled.Uppercase);
        Assert.IsTrue(styled.MultiLine);
        Assert.IsTrue(styled.Bold);
        Assert.IsTrue(styled.Italic);
        Assert.IsTrue(styled.Underline);
        Assert.AreEqual(
            XuiTextHorizontalAlignment.Right,
            styled.HorizontalTextAlignment);
        Assert.AreEqual(
            XuiTextVerticalAlignment.Middle,
            styled.VerticalTextAlignment);
        Assert.AreEqual(new XuiVector2(7, 3), styled.TextBorder);
        Assert.IsTrue(styled.Outline);
        Assert.AreEqual(2, styled.OutlineSize, 0.001);
        Assert.AreEqual(0x80112233u, styled.OutlineColor.Argb);
        Assert.IsTrue(styled.Shadow);
        Assert.AreEqual(4, styled.ShadowOffset, 0.001);
        Assert.AreEqual(0x90445566u, styled.ShadowColor.Argb);
        Assert.AreEqual("Fallback source", html.Text);
    }

    [TestMethod]
    public void DuplicatePropertiesUseLastValueAndStayDiagnosticSafe()
    {
        XuiRenderNode child = EvaluateChild(
            "<Width>10</Width><Width>14</Width><Height>8</Height>");
        Assert.AreEqual(14, child.Size.X, 0.001);
    }

    [TestMethod]
    public void StackPanelUsesEngineReverseOrderMarginsAndContentHeight()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Stack</Id><Width>100</Width><Height>100</Height>" +
            "<ClassOverride>UIStackPanel</ClassOverride>" +
            "<AutoSizeToContentY>true</AutoSizeToContentY></Properties>" +
            "<MyImage><Properties><Id>A</Id><Width>20</Width><Height>10</Height>" +
            "<MarginBottom>2</MarginBottom></Properties></MyImage>" +
            "<MyImage><Properties><Id>B</Id><Width>20</Width><Height>20</Height>" +
            "<MarginTop>3</MarginTop></Properties></MyImage></AdvGroup>");

        XuiRenderFrame frame = Frame(document);
        XuiRenderNode stack = frame.Nodes.Single(static node =>
            node.Id == "Stack");
        XuiRenderNode first = frame.Nodes.Single(static node => node.Id == "A");
        XuiRenderNode second = frame.Nodes.Single(static node => node.Id == "B");

        Assert.AreEqual(35, stack.Size.Y, 0.001);
        Assert.AreEqual(23, first.Position.Y, 0.001);
        Assert.AreEqual(3, second.Position.Y, 0.001);
        Assert.AreEqual(10, first.Size.Y, 0.001);
        Assert.AreEqual(20, second.Size.Y, 0.001);
    }

    [TestMethod]
    public void WrapPanelUsesDeclarationOrderMarginsAndWrapsRows()
    {
        XuiDocument document = Document(
            "<UIWrapPanel><Properties><Id>Wrap</Id><Width>50</Width><Height>100</Height>" +
            "<AutoSizeToContentY>true</AutoSizeToContentY></Properties>" +
            "<MyImage><Properties><Id>A</Id><Width>30</Width><Height>10</Height>" +
            "<MarginRight>5</MarginRight></Properties></MyImage>" +
            "<MyImage><Properties><Id>B</Id><Width>25</Width><Height>8</Height>" +
            "<MarginLeft>2</MarginLeft><MarginTop>1</MarginTop>" +
            "<MarginBottom>1</MarginBottom></Properties></MyImage></UIWrapPanel>");

        XuiRenderFrame frame = Frame(document);
        XuiRenderNode wrap = frame.Nodes.Single(static node =>
            node.Id == "Wrap");
        XuiRenderNode first = frame.Nodes.Single(static node => node.Id == "A");
        XuiRenderNode second = frame.Nodes.Single(static node => node.Id == "B");

        Assert.AreEqual(0, first.Position.X, 0.001);
        Assert.AreEqual(0, first.Position.Y, 0.001);
        Assert.AreEqual(2, second.Position.X, 0.001);
        Assert.AreEqual(11, second.Position.Y, 0.001);
        Assert.AreEqual(20, wrap.Size.Y, 0.001);
    }

    [TestMethod]
    public void RuntimeScenarioCanRevealAndPopulateHudPlaceholders()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>HiddenParent</Id><Opacity>0</Opacity>" +
            "<Show>false</Show></Properties><MyText><Properties><Id>T_Timer</Id>" +
            "<Width>100</Width><Height>20</Height><Opacity>0</Opacity>" +
            "<Show>false</Show><Text>00:00</Text></Properties></MyText></AdvGroup>");
        XuiPreviewScenario scenario = new(
            "timer",
            "Timer",
            string.Empty,
            [new XuiPreviewProperty("T_Timer", "Text", "00:23.6")],
            new HashSet<string>(["T_Timer"], StringComparer.Ordinal));

        XuiRenderNode timer = DyingLightLayoutEngine.Evaluate(
                document,
                new XuiViewport(100, 100),
                0,
                renderContext: new XuiRenderContext(scenario))
            .Nodes.Single(static node => node.Id == "T_Timer");

        Assert.IsTrue(timer.IsShown);
        Assert.AreEqual(1, timer.Opacity, 0.001);
        Assert.AreEqual("00:23.6", timer.Text);
    }

    [TestMethod]
    public void UnknownControlIsTransparentApproximationNotPreviewText()
    {
        XuiDocument document = Document(
            "<EngineRuntimeMystery><Properties><Id>Mystery</Id><Width>30</Width>" +
            "<Height>40</Height></Properties></EngineRuntimeMystery>");
        XuiRenderFrame frame = Frame(document);
        XuiRenderNode unknown = frame.Nodes.Single(static node => node.Id == "Mystery");
        Assert.AreEqual(XuiRenderKind.Unknown, unknown.Kind);
        Assert.IsTrue(unknown.IsApproximation);
        Assert.IsTrue(frame.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-LAYOUT003"));
    }

    [TestMethod]
    public void ImagesWithoutResourcesStayTransparentAndDiagnostic()
    {
        XuiDocument document = Document(
            "<MyImage><Properties><Id>Missing</Id><Width>100</Width><Height>50</Height>" +
            "<ImagePath>not_in_the_game</ImagePath></Properties></MyImage>");
        DyingLightAssetResolver resolver = new([]);

        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(100, 100),
            0,
            resolver);
        XuiRenderNode image = frame.Nodes.Single(static node =>
            node.Id == "Missing");

        Assert.AreEqual(Core.Values.XuiColor.Transparent, image.Color);
        Assert.IsTrue(image.IsApproximation);
        Assert.IsTrue(frame.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-LAYOUT009"));
    }

    [TestMethod]
    public async Task VisualLibraryExpandsTemplateAndBindsPresenterText()
    {
        using TestDirectory directory = new();
        string root = directory.File("assets");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "skin.xui"),
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<XuiVisual><Properties><Id>ButtonV</Id><Width>30</Width><Height>12</Height></Properties>" +
            "<MyImage><Properties><Id>Background</Id><Width>30</Width><Height>12</Height>" +
            "<ImagePath>white</ImagePath></Properties></MyImage>" +
            "<MyTextPresenter><Properties><Id>Label</Id><Width>30</Width><Height>12</Height>" +
            "<Text>design-time placeholder</Text></Properties></MyTextPresenter>" +
            "<Timelines><Timeline><Id>Background</Id><TimelineProp>Opacity</TimelineProp>" +
            "<KeyFrame><Time>0</Time><Interpolation>0</Interpolation><Prop>0.25</Prop></KeyFrame>" +
            "</Timeline></Timelines></XuiVisual></XuiCanvas>");
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));
        await resolver.RebuildAsync();
        XuiDocument document = Document(
            "<AdvButton><Properties><Id>Button</Id><Visual>ButtonV</Visual>" +
            "<Text>PLAY</Text></Properties></AdvButton>");

        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(100, 100),
            0,
            resolver);

        XuiRenderNode button = frame.Nodes.Single(static node => node.Id == "Button");
        XuiRenderNode background = frame.Nodes.Single(static node =>
            node.Id == "Background");
        XuiRenderNode label = frame.Nodes.Single(static node => node.Id == "Label");
        Assert.AreEqual(new Core.Values.XuiVector2(30, 12), button.Size);
        Assert.IsTrue(button.VisualResolved);
        Assert.AreEqual(0.25, background.Opacity, 0.001);
        Assert.AreEqual("PLAY", label.Text);
        Assert.IsTrue(label.IsVisualTemplatePart);
        Assert.AreEqual(button.Key, label.SelectionKey);
    }

    [TestMethod]
    public async Task VisualTemplateChildrenScaleToTheInstanceSize()
    {
        using TestDirectory directory = new();
        string root = directory.File("assets");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "skin.xui"),
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            "<XuiVisual><Properties><Id>PanelV</Id><Width>100</Width><Height>50</Height>" +
            "</Properties><MyImage><Properties><Id>Scaled</Id><Width>20</Width>" +
            "<Height>10</Height><Position>10,5,0</Position></Properties></MyImage>" +
            "<MyImage><Properties><Id>Kept</Id><Width>20</Width><Height>10</Height>" +
            "<Position>10,5,0</Position><KeepWidthOnParentSizeChange>true</KeepWidthOnParentSizeChange>" +
            "<KeepHeightOnParentSizeChange>true</KeepHeightOnParentSizeChange>" +
            "</Properties></MyImage></XuiVisual></XuiCanvas>");
        DyingLightAssetResolver resolver = new(
        [
            new XuiAssetRoot(root, XuiAssetRootKind.Workspace, false),
        ],
            directory.File("cache"));
        await resolver.RebuildAsync();
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Panel</Id><Width>200</Width><Height>100</Height>" +
            "<Visual>PanelV</Visual></Properties></AdvGroup>");

        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            XuiViewport.Default,
            0,
            resolver);
        XuiRenderNode scaled = frame.Nodes.Single(static node =>
            node.Id == "Scaled");
        XuiRenderNode kept = frame.Nodes.Single(static node =>
            node.Id == "Kept");

        Assert.AreEqual(new Core.Values.XuiVector2(40, 20), scaled.Size);
        Assert.AreEqual(20, scaled.Position.X, 0.001);
        Assert.AreEqual(10, scaled.Position.Y, 0.001);
        Assert.AreEqual(new Core.Values.XuiVector2(20, 10), kept.Size);
        Assert.AreEqual(20, kept.Position.X, 0.001);
        Assert.AreEqual(10, kept.Position.Y, 0.001);
    }

    private static XuiRenderNode EvaluateChild(string properties) =>
        Frame(Document(
                $"<MyImage><Properties><Id>Child</Id>{properties}</Properties></MyImage>"))
            .Nodes.Single(static node => node.Id == "Child");

    private static XuiDocument Document(string children) =>
        XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height></Properties>" +
            children +
            "</XuiCanvas>");

    private static XuiRenderFrame Frame(XuiDocument document) =>
        DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(100, 100),
            0);
}
