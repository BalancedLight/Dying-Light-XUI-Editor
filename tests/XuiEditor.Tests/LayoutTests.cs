using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Diagnostics;
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

        Assert.AreEqual(XuiPaintKind.Texture, image.PaintKind);
        Assert.AreEqual(Core.Values.XuiColor.White, image.Color);
        Assert.IsTrue(image.IsApproximation);
        Assert.IsTrue(frame.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-LAYOUT009"));
    }

    [TestMethod]
    public void WhiteImageAliasIsSolidColorWithoutTextureLookup()
    {
        XuiDocument document = Document(
            "<IUIAARectangle><Properties><Id>Lower</Id><Width>40</Width><Height>20</Height>" +
            "<ImagePath>white</ImagePath><Color>0x80402010</Color>" +
            "<Opacity>0.5</Opacity><Material>menu_antialias.mat</Material>" +
            "</Properties></IUIAARectangle>" +
            "<MyImage><Properties><Id>Upper</Id><Width>30</Width><Height>10</Height>" +
            "<ImagePath>  White  </ImagePath></Properties></MyImage>");
        CountingAssetResolver resolver = new();

        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            document,
            new XuiViewport(100, 100),
            0,
            resolver);
        XuiRenderNode lower = frame.Nodes.Single(static node =>
            node.Id == "Lower");
        XuiRenderNode upper = frame.Nodes.Single(static node =>
            node.Id == "Upper");

        Assert.AreEqual(XuiRenderKind.Rectangle, lower.Kind);
        Assert.AreEqual(XuiPaintKind.SolidColor, lower.PaintKind);
        Assert.AreEqual(new XuiColor(0x80, 0x40, 0x20, 0x10), lower.Color);
        Assert.AreEqual(0.5, lower.Opacity, 0.001);
        Assert.IsFalse(lower.IsApproximation);
        Assert.AreEqual(XuiPaintKind.SolidColor, upper.PaintKind);
        Assert.AreEqual(XuiColor.White, upper.Color);
        Assert.AreEqual(0, resolver.TextureDefinitionRequests);
        Assert.IsFalse(frame.Diagnostics.Any(static diagnostic =>
            diagnostic.Code is "XUI-LAYOUT004" or "XUI-LAYOUT009"));
        StringAssert.Contains(document.Text, "<ImagePath>  White  </ImagePath>");
    }

    [TestMethod]
    public void PaintClassificationKeepsUnsupportedMaterialsHonest()
    {
        XuiDocument document = Document(
            "<IUIAARectangle><Properties><Id>Variant</Id><Width>40</Width><Height>20</Height>" +
            "<ImagePath>white</ImagePath><Color>0xff223344</Color>" +
            "<Material>menu_antialias_clip.mat</Material>" +
            "</Properties></IUIAARectangle>" +
            "<IUIAARectangle><Properties><Id>Plain</Id><Width>20</Width><Height>20</Height>" +
            "</Properties></IUIAARectangle>" +
            "<MyImage><Properties><Id>EmptyImage</Id><Width>20</Width><Height>20</Height>" +
            "</Properties></MyImage>");

        XuiRenderFrame frame = Frame(document);
        XuiRenderNode variant = frame.Nodes.Single(static node =>
            node.Id == "Variant");
        XuiRenderNode plain = frame.Nodes.Single(static node =>
            node.Id == "Plain");
        XuiRenderNode emptyImage = frame.Nodes.Single(static node =>
            node.Id == "EmptyImage");

        Assert.AreEqual(XuiPaintKind.SolidColor, variant.PaintKind);
        Assert.IsTrue(variant.IsApproximation);
        Assert.IsTrue(frame.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "XUI-LAYOUT004" &&
            diagnostic.NodeKey == variant.Key));
        Assert.AreEqual(XuiPaintKind.SolidColor, plain.PaintKind);
        Assert.AreEqual(XuiPaintKind.None, emptyImage.PaintKind);
    }

    [TestMethod]
    public void MaterialCatalogClassifiesEvidenceBackedFamilies()
    {
        Assert.AreEqual(
            XuiMaterialBehavior.DefaultAlpha,
            XuiMaterialCatalog.Resolve(
                "menu_button_back.mat",
                XuiRenderKind.Image).Behavior);
        Assert.AreEqual(
            XuiMaterialBehavior.Text,
            XuiMaterialCatalog.Resolve(
                "sprite_text_vc_white.mat",
                XuiRenderKind.Text).Behavior);
        Assert.AreEqual(
            XuiMaterialBehavior.Clip,
            XuiMaterialCatalog.Resolve(
                "menu_mask_clip.mat",
                XuiRenderKind.Image).Behavior);
        Assert.AreEqual(
            XuiMaterialBehavior.Clip,
            XuiMaterialCatalog.Resolve(
                "hud_car_fuel_bar.mat",
                XuiRenderKind.Image).Behavior);
        Assert.AreEqual(
            XuiMaterialBehavior.Tint,
            XuiMaterialCatalog.Resolve(
                "hud_colorize.mat",
                XuiRenderKind.Image).Behavior);
        Assert.AreEqual(
            XuiMaterialBehavior.Tint,
            XuiMaterialCatalog.Resolve(
                "menu_gamma.mat",
                XuiRenderKind.Image).Behavior);
        Assert.AreEqual(
            XuiMaterialBehavior.GroupPassThrough,
            XuiMaterialCatalog.Resolve(
                "button_main_group.mat",
                XuiRenderKind.Group).Behavior);
        Assert.AreEqual(
            XuiMaterialBehavior.RuntimeGenerated,
            XuiMaterialCatalog.Resolve(
                "map_area_stroke.mat",
                XuiRenderKind.Shape).Behavior);
        Assert.IsTrue(
            XuiMaterialCatalog.Resolve(
                "menu_viewport.mat",
                XuiRenderKind.Image).SuppressSelfPaint);
    }

    [TestMethod]
    public void ForcedMaskedGroupSubstitutesDescendantMaterialsByNodeKind()
    {
        XuiDocument document = Document(
            "<UIMaskedGroup><Properties><Id>Mask</Id><Width>100</Width><Height>100</Height>" +
            "<ForceMaterials>true</ForceMaterials>" +
            "<ImageMaskMaterial>menu_mask_clip.mat</ImageMaskMaterial>" +
            "<TextMaskMaterial>menu_text_clip.mat</TextMaskMaterial>" +
            "<AARectangleMaskMaterial>menu_antialias_clip.mat</AARectangleMaskMaterial>" +
            "</Properties>" +
            "<MyImage><Properties><Id>Image</Id><Width>10</Width><Height>10</Height>" +
            "<ImagePath>white</ImagePath><Material>sprite.mat</Material></Properties></MyImage>" +
            "<MyText><Properties><Id>Text</Id><Width>20</Width><Height>10</Height>" +
            "<Text>Masked</Text><Material>menu_text.mat</Material></Properties></MyText>" +
            "<IUIAARectangle><Properties><Id>Rect</Id><Width>10</Width><Height>10</Height>" +
            "<ImagePath>white</ImagePath><Material>menu_antialias.mat</Material>" +
            "</Properties></IUIAARectangle></UIMaskedGroup>");

        XuiRenderFrame frame = Frame(document);
        XuiRenderNode image = frame.Nodes.Single(static node =>
            node.Id == "Image");
        XuiRenderNode text = frame.Nodes.Single(static node =>
            node.Id == "Text");
        XuiRenderNode rectangle = frame.Nodes.Single(static node =>
            node.Id == "Rect");

        Assert.AreEqual("menu_mask_clip.mat", image.Material);
        Assert.AreEqual("menu_text_clip.mat", text.Material);
        Assert.AreEqual("menu_antialias_clip.mat", rectangle.Material);
        Assert.AreEqual(XuiMaterialBehavior.Clip, image.MaterialProfile.Behavior);
        Assert.AreEqual(XuiMaterialBehavior.Clip, text.MaterialProfile.Behavior);
        Assert.AreEqual(
            XuiMaterialBehavior.Clip,
            rectangle.MaterialProfile.Behavior);
    }

    [TestMethod]
    public void RuntimeGeneratedShapeStaysTransparentAndDiagnostic()
    {
        XuiDocument document = Document(
            "<IUIShape><Properties><Id>MapArea</Id><Width>80</Width><Height>60</Height>" +
            "<ImagePath>white</ImagePath><Material>map_area_stroke.mat</Material>" +
            "</Properties></IUIShape>");

        XuiRenderFrame frame = Frame(document);
        XuiRenderNode shape = frame.Nodes.Single(static node =>
            node.Id == "MapArea");

        Assert.AreEqual(XuiRenderKind.Shape, shape.Kind);
        Assert.AreEqual(XuiPaintKind.None, shape.PaintKind);
        Assert.IsTrue(shape.MaterialProfile.RequiresRuntimeData);
        Assert.IsTrue(shape.IsApproximation);
        Assert.IsTrue(frame.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-LAYOUT004"));
    }

    [TestMethod]
    public void RepeatedUnsupportedMaterialDiagnosticsAreAggregated()
    {
        XuiDocument document = Document(
            "<MyImage><Properties><Id>A</Id><Width>10</Width><Height>10</Height>" +
            "<ImagePath>white</ImagePath><Material>custom_shader.mat</Material>" +
            "</Properties></MyImage>" +
            "<MyImage><Properties><Id>B</Id><Width>10</Width><Height>10</Height>" +
            "<ImagePath>white</ImagePath><Material>custom_shader.mat</Material>" +
            "</Properties></MyImage>");

        XuiRenderFrame frame = Frame(document);
        XuiDiagnostic[] diagnostics = frame.Diagnostics
            .Where(static diagnostic => diagnostic.Code == "XUI-LAYOUT004")
            .ToArray();

        Assert.HasCount(1, diagnostics);
        StringAssert.Contains(diagnostics[0].Message, "2 affected nodes");
    }

    [TestMethod]
    public void CompiledLayoutSessionRejectsStaleDocumentRevision()
    {
        XuiDocument document = Document(
            "<MyImage><Properties><Id>A</Id><Width>10</Width><Height>10</Height>" +
            "</Properties></MyImage>");
        DyingLightLayoutSession session =
            DyingLightLayoutSession.Compile(document);
        XuiPropertyEntry width = XuiModelReader.GetProperty(
            document.Root,
            document.Text,
            "Width")!;
        document.Execute(XuiCommandFactory.SetElementValue(
            document,
            width.Element,
            "120"));

        Assert.IsFalse(session.IsCurrent(document, assetResolver: null));
        Assert.Throws<InvalidOperationException>(() =>
            session.Sample(new XuiViewport(100, 100), 0));
    }

    [TestMethod]
    public void CompiledLayoutSessionReusesStaticMetadataAcrossTicks()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>Panel</Id></Properties>" +
            "<IUIAARectangle><Properties><Id>Fill</Id><Width>20</Width><Height>10</Height>" +
            "<Material>menu_antialias.mat</Material><ImagePath>white</ImagePath>" +
            "</Properties></IUIAARectangle></AdvGroup>");
        DyingLightLayoutSession session =
            DyingLightLayoutSession.Compile(document);
        int nodeCount = session.CompiledNodeCount;
        int materialCount = session.CompiledMaterialProfileCount;

        XuiRenderFrame first =
            session.Sample(new XuiViewport(1280, 720), 0);
        XuiRenderFrame second =
            session.Sample(new XuiViewport(1280, 720), 12);

        Assert.IsGreaterThanOrEqualTo(3, nodeCount);
        Assert.IsGreaterThanOrEqualTo(2, materialCount);
        Assert.AreEqual(nodeCount, session.CompiledNodeCount);
        Assert.AreEqual(
            materialCount,
            session.CompiledMaterialProfileCount);
        Assert.AreEqual(first.Nodes.Count, second.Nodes.Count);
    }

    [TestMethod]
    public void WhiteSolidPaintRetainsNestedTransformAndClip()
    {
        XuiDocument document = Document(
            "<AdvGroup><Properties><Id>ClipParent</Id><Width>30</Width><Height>20</Height>" +
            "<Position>5,7,0</Position><ClipChildren>true</ClipChildren></Properties>" +
            "<IUIAARectangle><Properties><Id>Fill</Id><Width>40</Width><Height>30</Height>" +
            "<Position>10,4,0</Position><Scale>2,1,1</Scale>" +
            "<ImagePath>white</ImagePath><Color>0x7f123456</Color>" +
            "</Properties></IUIAARectangle></AdvGroup>");

        XuiRenderNode fill = Frame(document).Nodes.Single(static node =>
            node.Id == "Fill");

        Assert.AreEqual(XuiPaintKind.SolidColor, fill.PaintKind);
        Assert.AreEqual(new XuiRect(15, 11, 80, 30), fill.WorldBounds);
        Assert.IsNotNull(fill.ClipBounds);
        Assert.AreEqual(new XuiRect(5, 7, 30, 20), fill.ClipBounds.Value);
        Assert.AreEqual(new XuiColor(0x7f, 0x12, 0x34, 0x56), fill.Color);
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

    private sealed class CountingAssetResolver : IAssetResolver
    {
        public int TextureDefinitionRequests { get; private set; }

        public IReadOnlyList<XuiAssetRoot> Roots { get; } = [];

        public IReadOnlyList<Core.Diagnostics.XuiDiagnostic> Diagnostics { get; } = [];

        public IReadOnlyList<XuiResolvedFile> Files { get; } = [];

        public ILocalizationCatalog? Localization => null;

        public Task RebuildAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public XuiResolvedFile? ResolveFile(string pathOrName) => null;

        public XuiTextureRegion? ResolveTextureDefinition(string imagePath)
        {
            TextureDefinitionRequests++;
            return null;
        }

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
            new(0, 0, 1, false);

        public string ResolveText(string keyOrLiteral) => keyOrLiteral;

        public ValueTask<ResolvedBitmapFont?> ResolveBitmapFontAsync(
            string fontId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ResolvedBitmapFont?>(null);
    }
}
