using System.Globalization;
using System.Numerics;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Layout;

public sealed class DyingLightLayoutEngine
{
    private const int MaximumRenderNodes = 500_000;
    private const int MaximumVisualNesting = 64;

    public static XuiRenderFrame Evaluate(
        XuiDocument document,
        XuiViewport viewport,
        int tick,
        IAssetResolver? assetResolver = null,
        XuiRenderContext? renderContext = null) =>
        DyingLightLayoutSession
            .Compile(document, assetResolver)
            .Sample(viewport, tick, renderContext);

    public static XuiRenderFrame Evaluate(
        XuiDocument document,
        XuiViewport viewport,
        XuiTimelineEvaluationState timelineState,
        IAssetResolver? assetResolver = null,
        XuiRenderContext? renderContext = null) =>
        DyingLightLayoutSession
            .Compile(document, assetResolver)
            .Sample(viewport, timelineState, renderContext);

    internal static XuiRenderFrame EvaluateCompiled(
        XuiDocument document,
        XuiViewport viewport,
        XuiTimelineEvaluationState timelineState,
        XuiTimelineSet timelineSet,
        TimelineAnimationCache timelineAnimationCache,
        DyingLightLayoutCompilation compilation,
        IAssetResolver? assetResolver = null,
        XuiRenderContext? renderContext = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(timelineState);
        ArgumentNullException.ThrowIfNull(timelineSet);
        if (viewport.Width <= 0 ||
            viewport.Height <= 0 ||
            viewport.DpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewport),
                "Viewport dimensions and DPI scale must be positive.");
        }

        List<XuiDiagnostic> diagnostics = [];
        diagnostics.AddRange(timelineSet.Diagnostics);
        AnimationOverrides animation = new(
            timelineAnimationCache,
            timelineState);

        XuiRenderContext context = renderContext ?? new XuiRenderContext();
        if (context.ControllerRuntimeProfile is null &&
            context.ApplyCommonControllerProfile &&
            compilation.ControllerRuntimeProfile is
                XuiControllerRuntimeProfile controllerProfile)
        {
            context = context with
            {
                ControllerRuntimeProfile = controllerProfile,
            };
        }

        if (context.ControllerRuntimeProfile is
                XuiControllerRuntimeProfile activeControllerProfile &&
            activeControllerProfile.HiddenTargets.Any(target =>
                !compilation.IsTargetForceShown(target, context)))
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT014",
                XuiDiagnosticSeverity.Info,
                activeControllerProfile.Description,
                document.Root.Span,
                document.Root.Key));
        }

        CompiledXuiNode compiledRoot = compilation.Node(
            document.Root,
            document.Text);
        PropertyBag rootProperties = new(
            document.Root,
            compiledRoot.Properties,
            overrides: null,
            runtimeOverrides: context.PropertiesFor(
                compiledRoot.Id,
                document.Root.Key));
        double designWidth = rootProperties.Number("Width", 1280, diagnostics);
        double designHeight = rootProperties.Number("Height", 720, diagnostics);
        if (designWidth <= 0 || designHeight <= 0)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT001",
                XuiDiagnosticSeverity.Error,
                "The canvas design size is invalid; 1280×720 is used.",
                document.Root.Span,
                document.Root.Key));
            designWidth = 1280;
            designHeight = 720;
        }

        Matrix3x2 viewportTransform = CreateViewportTransform(
            new XuiVector2(designWidth, designHeight),
            viewport);
        ResolutionContext resolution = new(
            new XuiVector2(designWidth, designHeight),
            viewport);
        List<XuiRenderNode> renderNodes = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        int declarationOrder = 0;

        EvaluateNode(
            document.Root,
            parent: null,
            document.Text,
            compilation,
            animation,
            diagnostics,
            renderNodes,
            ids,
            assetResolver,
            context,
            keyPrefix: string.Empty,
            selectionKey: null,
            isVisualTemplatePart: false,
            visualBindings: null,
            visualStack: [],
            timelineRecursionBarrier: null,
            forcedMaterials: null,
            resolution,
            authoredParentSizeOverride: null,
            arrangedPositionOverride: null,
            ref declarationOrder);

        return new XuiRenderFrame(
            new XuiVector2(designWidth, designHeight),
            viewport,
            viewportTransform,
            renderNodes,
            AggregateDiagnostics(diagnostics));
    }

    private static void EvaluateNode(
        XuiSyntaxNode syntax,
        XuiRenderNode? parent,
        string source,
        DyingLightLayoutCompilation compilation,
        AnimationOverrides animation,
        List<XuiDiagnostic> diagnostics,
        List<XuiRenderNode> result,
        HashSet<string> ids,
        IAssetResolver? assetResolver,
        XuiRenderContext renderContext,
        string keyPrefix,
        string? selectionKey,
        bool isVisualTemplatePart,
        VisualInstanceBindings? visualBindings,
        HashSet<string> visualStack,
        string? timelineRecursionBarrier,
        ForcedMaterialSet? forcedMaterials,
        ResolutionContext resolution,
        XuiVector2? authoredParentSizeOverride,
        XuiVector3? arrangedPositionOverride,
        ref int declarationOrder)
    {
        if (result.Count >= MaximumRenderNodes)
        {
            throw new InvalidDataException(
                $"The XUI render tree exceeds the {MaximumRenderNodes:N0}-node safety limit.");
        }

        CompiledXuiNode compiledNode = compilation.Node(syntax, source);
        string id = compiledNode.Id;
        IReadOnlyDictionary<string, XuiAnimatedValue>? overrides =
            animation.ForNode(
                id,
                syntax.Key,
                timelineRecursionBarrier);
        IReadOnlyDictionary<string, string>? runtimeOverrides =
            renderContext.PropertiesFor(id, syntax.Key);
        PropertyBag properties = new(
            syntax,
            compiledNode.Properties,
            overrides,
            runtimeOverrides);
        PropertyBag authoredProperties = new(
            syntax,
            compiledNode.Properties,
            overrides: null,
            runtimeOverrides: null);
        VisualGeometryOverride? visualGeometry =
            visualBindings?.ResolveGeometry(id, properties);
        string effectiveKey = keyPrefix.Length == 0
            ? syntax.Key
            : keyPrefix + syntax.Key;
        string effectiveSelectionKey = selectionKey ?? effectiveKey;
        string visualId = properties.Text("Visual").Trim();
        XuiVisualTemplate? visualTemplate =
            compilation.ResolveVisual(visualId);
        PropertyBag? visualRootProperties = visualTemplate is null
            ? null
            : new PropertyBag(
                visualTemplate.Syntax,
                compilation.Node(
                    visualTemplate.Syntax,
                    visualTemplate.Source).Properties,
                overrides: null,
                runtimeOverrides: null);

        string qualifiedId = keyPrefix + '\u001f' + id;
        if (id.Length > 0 && !ids.Add(qualifiedId))
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT002",
                XuiDiagnosticSeverity.Warning,
                $"Duplicate Id '{id}' makes timeline and navigation targets ambiguous.",
                syntax.Span,
                syntax.Key));
        }

        double defaultWidth = parent is null
            ? 1280
            : visualRootProperties?.Number("Width", 0, diagnostics) ?? 0;
        double defaultHeight = parent is null
            ? 720
            : visualRootProperties?.Number("Height", 0, diagnostics) ?? 0;
        double authoredWidth = authoredProperties.Number(
            "Width",
            defaultWidth,
            diagnostics);
        double authoredHeight = authoredProperties.Number(
            "Height",
            defaultHeight,
            diagnostics);
        double width = properties.Number(
            "Width",
            authoredWidth,
            diagnostics);
        double height = properties.Number(
            "Height",
            authoredHeight,
            diagnostics);
        XuiVector3 position = properties.Vector3("Position", default, diagnostics);
        if (arrangedPositionOverride is XuiVector3 arrangedPosition)
        {
            position = arrangedPosition;
        }

        if (visualGeometry is VisualGeometryOverride geometry)
        {
            width = geometry.Width ?? width;
            height = geometry.Height ?? height;
            position = position with
            {
                X = geometry.X ?? position.X,
                Y = geometry.Y ?? position.Y,
            };
        }

        XuiVector3 pivot = properties.Vector3("Pivot", default, diagnostics);
        XuiVector3 scale = properties.Vector3(
            "Scale",
            new XuiVector3(1, 1, 1),
            diagnostics);
        if (scale.X == 0 && scale.Y == 0)
        {
            scale = new XuiVector3(1, 1, scale.Z);
        }

        double rotationDegrees = properties.RotationDegrees(diagnostics);
        double opacity = Math.Clamp(properties.Number("Opacity", 1, diagnostics), 0, 1);
        bool shown = properties.Boolean("Show", true, diagnostics);
        bool forceShown = renderContext.IsForceShown(id, syntax.Key);
        bool forceHidden = renderContext.IsForceHidden(id, syntax.Key);
        if (forceShown)
        {
            shown = true;
            opacity = 1;
        }
        else if (forceHidden)
        {
            shown = false;
        }
        else if (visualBindings is not null)
        {
            shown = visualBindings.ResolveVisibility(id, shown);
        }

        int anchorValue = properties.Integer("Anchor", 0, diagnostics);
        XuiAnchor anchor = visualGeometry?.Anchor ??
                           (XuiAnchor)(anchorValue & 0x3f);
        XuiVector2 parentSize = parent?.Size ?? new XuiVector2(width, height);
        XuiVector2 authoredParentSize = authoredParentSizeOverride ??
                                        parent?.AuthoredSize ??
                                        parentSize;

        if (parent is not null &&
            visualGeometry?.BypassParentSizeChange != true)
        {
            ApplyParentSizeChange(
                anchor,
                properties,
                parentSize,
                authoredParentSize,
                ref position,
                ref pivot,
                ref width,
                ref height,
                diagnostics);
        }

        bool resolutionApproximation = ApplyResolutionChange(
            properties,
            resolution,
            ref position,
            ref width,
            ref height,
            ref scale,
            diagnostics);

        XuiRenderKind kind = Classify(compiledNode, properties);
        string authoredMaterial = properties.Text("Material").Trim();
        string material =
            forcedMaterials?.MaterialFor(kind, syntax.Name) ??
            authoredMaterial;
        XuiMaterialProfile materialProfile =
            forcedMaterials is null &&
            kind == compiledNode.Kind &&
            string.Equals(
                material,
                compiledNode.Material,
                StringComparison.Ordinal)
                ? compiledNode.MaterialProfile
                : compilation.ResolveMaterial(material, kind);
        string authoredText = properties.Text(
            "Text",
            properties.Text("SourceString"));
        string text = IsTextPresenter(syntax.Name, properties) &&
                      visualBindings is not null
            ? visualBindings.ResolveText(id, properties, authoredText)
            : authoredText;
        if (renderContext.ResolveLocalization && assetResolver is not null)
        {
            text = assetResolver.ResolveText(text);
        }

        string imagePath = IsImagePresenter(syntax.Name, properties) &&
                           visualBindings is not null &&
                           IsPrimaryDataAssociation(properties)
            ? visualBindings.ImagePath
            : properties.Text("ImagePath");
        XuiPaintKind paintKind = ClassifyPaint(
            kind,
            imagePath,
            materialProfile);
        string fontId = properties.Text(
            "Font",
            properties.Text("DefaultFont")).Trim();
        double pointSize = kind == XuiRenderKind.Text
            ? Math.Max(0, properties.Number("PointSize", 0, diagnostics))
            : 0;
        bool uppercase = kind == XuiRenderKind.Text &&
                         properties.Boolean(
                             "Uppercase",
                             false,
                             diagnostics);
        bool colorControlSequenceEnabled =
            properties.Boolean(
                "ColorControlSequenceEnabled",
                false,
                diagnostics);
        XuiColorControlParseResult colorControlText =
            compilation.ResolveColorControlText(
                text,
                kind == XuiRenderKind.Text &&
                colorControlSequenceEnabled);
        bool routesToTextPresenter =
            kind != XuiRenderKind.Text &&
            colorControlText.HasMarkup &&
            visualTemplate is not null &&
            FindVisualPresenter(
                visualTemplate,
                compilation,
                static (_, _) => true) is not null;
        AddColorControlDiagnostics(
            syntax,
            kind,
            colorControlSequenceEnabled,
            routesToTextPresenter,
            colorControlText,
            diagnostics);
        if (kind == XuiRenderKind.Text)
        {
            text = colorControlText.DisplayText;
        }

        bool multiLine = kind == XuiRenderKind.Text &&
                         (properties.Boolean(
                              "MultiLine",
                              false,
                              diagnostics) ||
                          text.Contains('\n'));
        XuiVector2 textBorder = kind == XuiRenderKind.Text
            ? new XuiVector2(
                Math.Max(
                    0,
                    properties.Number(
                        "ContentHorizontalBorder",
                        0,
                        diagnostics)),
                Math.Max(
                    0,
                    properties.Number(
                        "ContentVerticalBorder",
                        0,
                        diagnostics)))
            : default;
        double characterSpacingAdjust = kind == XuiRenderKind.Text
            ? properties.Number(
                "CharacterSpacingAdjust",
                0,
                diagnostics)
            : 0;
        if (kind == XuiRenderKind.Text && assetResolver is not null)
        {
            bool autoSizeToText = properties.Boolean(
                "AutoSizeToText",
                false,
                diagnostics);
            bool autoWidth =
                autoSizeToText ||
                properties.Boolean(
                    "AutoAdjustWidth",
                    false,
                    diagnostics);
            bool autoHeight =
                autoSizeToText ||
                properties.Boolean(
                    "AutoAdjustHeight",
                    false,
                    diagnostics) ||
                properties.Boolean(
                    "MultilineAutoSizeHeight",
                    false,
                    diagnostics);
            if (autoWidth || autoHeight)
            {
                double maximumTextWidth = multiLine && !autoWidth
                    ? Math.Max(1, width - (textBorder.X * 2))
                    : 0;
                XuiTextMeasurement measurement = assetResolver.MeasureText(
                    fontId,
                    text,
                    pointSize,
                    maximumTextWidth,
                    multiLine,
                    uppercase,
                    characterSpacingAdjust);
                if (autoWidth)
                {
                    width = measurement.Width + (textBorder.X * 2);
                }

                if (autoHeight)
                {
                    height = measurement.Height + (textBorder.Y * 2);
                }
            }
        }

        if (kind == XuiRenderKind.Control &&
            visualTemplate is not null &&
            assetResolver is not null &&
            ShouldAutoAdjustControlWidth(properties, diagnostics))
        {
            ApplyControlAutoWidth(
                syntax,
                properties,
                visualTemplate,
                compilation,
                assetResolver,
                text,
                arrangedPositionOverride is null,
                ref position,
                ref width,
                diagnostics);
        }

        IReadOnlyList<XuiSyntaxNode> visualChildren =
            compiledNode.VisualChildren;
        XuiVector2? parentToTextSize = MeasureParentToTextChildren(
            visualChildren,
            source,
            compilation,
            animation,
            assetResolver,
            renderContext,
            properties.Boolean(
                "DisableTimelineRecursion",
                false,
                diagnostics)
                ? syntax.Key
                : timelineRecursionBarrier,
            diagnostics);
        if (parentToTextSize is XuiVector2 measuredParentSize)
        {
            width = measuredParentSize.X;
            height = measuredParentSize.Y;
        }

        PanelArrangement? childArrangement = ArrangePanelChildren(
            syntax.Name,
            properties,
            visualChildren,
            source,
            compilation,
            animation,
            renderContext,
            assetResolver,
            childTimelineBarrier: properties.Boolean(
                "DisableTimelineRecursion",
                false,
                diagnostics)
                ? syntax.Key
                : timelineRecursionBarrier,
            panelSize: new XuiVector2(width, height),
            diagnostics: diagnostics);
        if (childArrangement is not null)
        {
            if (properties.Boolean(
                    "AutoSizeToContentX",
                    false,
                    diagnostics))
            {
                width = childArrangement.ContentSize.X;
            }

            if (properties.Boolean(
                    "AutoSizeToContentY",
                    false,
                    diagnostics))
            {
                height = childArrangement.ContentSize.Y;
            }
        }

        if (properties.Boolean("RoundPosition", false, diagnostics))
        {
            position = position with
            {
                X = Math.Floor(position.X),
                Y = Math.Floor(position.Y),
            };
        }

        Matrix3x2 localTransform = CreateLocalTransform(
            position,
            pivot,
            scale,
            rotationDegrees);
        Matrix3x2 worldTransform = parent is null
            ? localTransform
            : localTransform * parent.WorldTransform;
        XuiRect localBounds = new(0, 0, Math.Max(0, width), Math.Max(0, height));
        XuiRect worldBounds = TransformBounds(localBounds, worldTransform);
        XuiRect? clipBounds = ParentClip(parent);
        bool clipChildren = properties.Boolean("ClipChildren", false, diagnostics);
        bool useMask = properties.Boolean("UseMask", false, diagnostics);
        bool materialApproximation =
            material.Length > 0 &&
            materialProfile.IsApproximation;

        bool approximation = kind == XuiRenderKind.Unknown ||
                             materialApproximation ||
                             (visualId.Length > 0 && visualTemplate is null) ||
                             resolutionApproximation;
        if (kind == XuiRenderKind.Unknown)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT003",
                XuiDiagnosticSeverity.Info,
                $"'{syntax.Name}' is engine-only or unknown; only its bounds and children are evaluated.",
                syntax.Span,
                syntax.Key));
        }

        if (materialApproximation)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT004",
                XuiDiagnosticSeverity.Info,
                $"Material '{material}' uses a static editor approximation. " +
                materialProfile.Description,
                syntax.Span,
                syntax.Key));
        }

        if (visualId.Length > 0 && visualTemplate is null)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT006",
                XuiDiagnosticSeverity.Warning,
                $"Visual template '{visualId}' was not found in the configured asset roots.",
                syntax.Span,
                syntax.Key));
        }

        if (useMask)
        {
            approximation = true;
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT011",
                XuiDiagnosticSeverity.Info,
                "This mask is represented by its transformed clip bounds; alpha-channel mask sampling remains an explicit editor approximation.",
                syntax.Span,
                syntax.Key));
        }

        if (kind == XuiRenderKind.Text &&
            fontId.Length > 0 &&
            assetResolver is not null)
        {
            ResolvedFont resolvedFont = assetResolver.ResolveFont(fontId, 0);
            foreach (XuiDiagnostic fontDiagnostic in resolvedFont.Diagnostics)
            {
                diagnostics.Add(fontDiagnostic with
                {
                    Span = syntax.Span,
                    NodeKey = syntax.Key,
                });
            }
        }

        XuiColor defaultColor = kind switch
        {
            XuiRenderKind.Text => properties.Color(
                "DefaultFontColor",
                XuiColor.White,
                diagnostics),
            _ => XuiColor.White,
        };
        XuiColor color = properties.Color(
            kind == XuiRenderKind.Text ? "TextColor" : "Color",
            defaultColor,
            diagnostics);
        int textStyle = kind == XuiRenderKind.Text
            ? properties.Integer("TextStyle", 0, diagnostics)
            : 0;
        XuiTextHorizontalAlignment horizontalTextAlignment =
            kind == XuiRenderKind.Text
                ? ParseHorizontalTextAlignment(
                    properties,
                    textStyle,
                    diagnostics)
                : XuiTextHorizontalAlignment.Left;
        XuiTextVerticalAlignment verticalTextAlignment =
            kind == XuiRenderKind.Text
                ? ParseVerticalTextAlignment(
                    properties,
                    textStyle,
                    diagnostics)
                : XuiTextVerticalAlignment.Top;
        bool outline = kind == XuiRenderKind.Text &&
                       properties.NumberOrBoolean(
                           "Outline",
                           0,
                           diagnostics) > 0;
        double outlineSize = outline
            ? Math.Max(
                0.5,
                properties.Number("OutlineSize", 1, diagnostics))
            : 0;
        XuiColor outlineColor = kind == XuiRenderKind.Text
            ? properties.Color(
                "OutlineColor",
                new XuiColor(160, 0, 0, 0),
                diagnostics)
            : XuiColor.Transparent;
        bool shadow = kind == XuiRenderKind.Text &&
                      properties.NumberOrBoolean(
                          "Shadow",
                          0,
                          diagnostics) > 0;
        double shadowOffset = shadow
            ? properties.Number("ShadowOffset", 1, diagnostics)
            : 0;
        XuiColor shadowColor = kind == XuiRenderKind.Text
            ? properties.Color(
                "ShadowColor",
                properties.Color(
                    "DropShadowColor",
                    new XuiColor(160, 0, 0, 0),
                    diagnostics),
                diagnostics)
            : XuiColor.Transparent;
        if (paintKind == XuiPaintKind.Texture &&
            assetResolver is not null &&
            assetResolver.ResolveTextureDefinition(imagePath) is null)
        {
            approximation = true;
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT009",
                XuiDiagnosticSeverity.Warning,
                $"Image '{imagePath}' was not found in the configured texture definitions; it is transparent in the preview.",
                syntax.Span,
                syntax.Key));
        }

        XuiRenderNode renderNode = new(
            effectiveKey,
            parent?.Key,
            id,
            syntax.Name,
            kind,
            parent is null ? 0 : parent.Depth + 1,
            declarationOrder++,
            new XuiVector2(width, height),
            position,
            pivot,
            scale,
            rotationDegrees,
            forceShown
                ? 1
                : opacity * (parent?.Opacity ?? 1),
            forceShown ||
            (shown && (parent?.IsShown ?? true)),
            localTransform,
            worldTransform,
            localBounds,
            worldBounds,
            clipBounds,
            text,
            imagePath,
            material,
            fontId,
            color,
            visualId,
            properties.Text("ClassOverride"),
            approximation,
            effectiveSelectionKey,
            isVisualTemplatePart,
            visualTemplate is not null)
        {
            AuthoredSize = new XuiVector2(authoredWidth, authoredHeight),
            LocalOpacity = forceShown ? 1 : opacity,
            LocalIsShown = forceShown || shown,
            ForceShown = forceShown,
            EstablishesClip = clipChildren || useMask,
            PaintKind = paintKind,
            MaterialProfile = materialProfile,
            PointSize = pointSize,
            Uppercase = uppercase,
            MultiLine = multiLine,
            Bold = (textStyle & 4) != 0,
            Italic = (textStyle & 2) != 0,
            Underline = (textStyle & 8) != 0,
            HorizontalTextAlignment = horizontalTextAlignment,
            VerticalTextAlignment = verticalTextAlignment,
            TextBorder = textBorder,
            CharacterSpacingAdjust = characterSpacingAdjust,
            ColorControlSequenceEnabled =
                kind == XuiRenderKind.Text &&
                colorControlSequenceEnabled,
            TextColorRuns = kind == XuiRenderKind.Text
                ? colorControlText.ColorRuns
                : [],
            Outline = outline,
            OutlineSize = outlineSize,
            OutlineColor = outlineColor,
            Shadow = shadow,
            ShadowOffset = shadowOffset,
            ShadowColor = shadowColor,
        };
        result.Add(renderNode);

        XuiRenderNode childParent = renderNode;
        if (clipChildren || useMask)
        {
            childParent = renderNode with
            {
                ClipBounds = Intersect(clipBounds, worldBounds),
            };
            result[^1] = childParent;
        }

        ForcedMaterialSet? childForcedMaterials = forcedMaterials;
        if (properties.Boolean("ForceMaterials", false, diagnostics))
        {
            childForcedMaterials = new ForcedMaterialSet(
                properties.Text(
                    "ImageMaskMaterial",
                    "menu_mask_clip.mat"),
                properties.Text(
                    "TextMaskMaterial",
                    "menu_text_clip.mat"),
                properties.Text(
                    "AARectangleMaskMaterial",
                    "menu_antialias_clip.mat"));
        }

        if (visualTemplate is not null)
        {
            VisualInstanceBindings bindings = CreateVisualBindings(
                renderNode,
                properties,
                visualTemplate,
                compilation,
                assetResolver!,
                diagnostics);
            ExpandVisualTemplate(
                visualTemplate,
                childParent,
                renderNode,
                bindings,
                assetResolver!,
                compilation,
                renderContext,
                resolution,
                diagnostics,
                result,
                ids,
                visualStack,
                childForcedMaterials,
                ref declarationOrder);
        }

        string? childTimelineBarrier = properties.Boolean(
            "DisableTimelineRecursion",
            false,
            diagnostics)
            ? syntax.Key
            : timelineRecursionBarrier;
        foreach (XuiSyntaxNode child in visualChildren)
        {
            XuiVector3? arrangedChildPosition =
                childArrangement?.Positions.GetValueOrDefault(child.Key);
            EvaluateNode(
                child,
                childParent,
                source,
                compilation,
                animation,
                diagnostics,
                result,
                ids,
                assetResolver,
                renderContext,
                keyPrefix,
                selectionKey,
                isVisualTemplatePart,
                visualBindings,
                visualStack,
                childTimelineBarrier,
                childForcedMaterials,
                resolution,
                authoredParentSizeOverride:
                    childArrangement is null ? null : childParent.Size,
                arrangedPositionOverride: arrangedChildPosition,
                ref declarationOrder);
        }
    }

    private static void ExpandVisualTemplate(
        XuiVisualTemplate visualTemplate,
        XuiRenderNode parent,
        XuiRenderNode instance,
        VisualInstanceBindings bindings,
        IAssetResolver assetResolver,
        DyingLightLayoutCompilation compilation,
        XuiRenderContext renderContext,
        ResolutionContext resolution,
        List<XuiDiagnostic> diagnostics,
        List<XuiRenderNode> result,
        HashSet<string> ids,
        HashSet<string> visualStack,
        ForcedMaterialSet? forcedMaterials,
        ref int declarationOrder)
    {
        if (visualStack.Count >= MaximumVisualNesting)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT007",
                XuiDiagnosticSeverity.Error,
                $"Visual template nesting exceeded {MaximumVisualNesting} levels at '{visualTemplate.Id}'.",
                visualTemplate.Syntax.Span,
                visualTemplate.Syntax.Key));
            return;
        }

        if (!visualStack.Add(visualTemplate.Id))
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT008",
                XuiDiagnosticSeverity.Error,
                $"Visual template cycle detected at '{visualTemplate.Id}'.",
                visualTemplate.Syntax.Span,
                visualTemplate.Syntax.Key));
            return;
        }

        try
        {
            AnimationOverrides animation =
                compilation.ResolveVisualAnimation(visualTemplate);
            string prefix =
                $"{instance.Key}::$visual[{visualTemplate.Id}]";
            PropertyBag visualRootProperties = new(
                visualTemplate.Syntax,
                compilation.Node(
                    visualTemplate.Syntax,
                    visualTemplate.Source).Properties,
                overrides: null,
                runtimeOverrides: null);
            XuiVector2 visualRootSize = new(
                visualRootProperties.Number(
                    "Width",
                    instance.AuthoredSize.X,
                    diagnostics),
                visualRootProperties.Number(
                    "Height",
                    instance.AuthoredSize.Y,
                    diagnostics));
            IReadOnlyList<XuiSyntaxNode> visualChildren =
                compilation.Node(
                    visualTemplate.Syntax,
                    visualTemplate.Source).VisualChildren;
            PanelArrangement? arrangement = ArrangePanelChildren(
                visualTemplate.Syntax.Name,
                visualRootProperties,
                visualChildren,
                visualTemplate.Source,
                compilation,
                animation,
                renderContext,
                assetResolver,
                childTimelineBarrier: null,
                panelSize: visualRootSize,
                diagnostics: diagnostics);
            foreach (XuiSyntaxNode child in visualChildren)
            {
                EvaluateNode(
                    child,
                    parent,
                    visualTemplate.Source,
                    compilation,
                    animation,
                    diagnostics,
                    result,
                    ids,
                    assetResolver,
                    renderContext,
                    prefix,
                    instance.SelectionKey,
                    isVisualTemplatePart: true,
                    bindings,
                    visualStack,
                    timelineRecursionBarrier: null,
                    forcedMaterials,
                    resolution,
                    authoredParentSizeOverride: visualRootSize,
                    arrangedPositionOverride:
                        arrangement?.Positions.GetValueOrDefault(child.Key),
                    ref declarationOrder);
            }
        }
        finally
        {
            visualStack.Remove(visualTemplate.Id);
        }
    }

    private static XuiVector2? MeasureParentToTextChildren(
        IReadOnlyList<XuiSyntaxNode> children,
        string source,
        DyingLightLayoutCompilation compilation,
        AnimationOverrides animation,
        IAssetResolver? assetResolver,
        XuiRenderContext renderContext,
        string? childTimelineBarrier,
        List<XuiDiagnostic> diagnostics)
    {
        if (assetResolver is null)
        {
            return null;
        }

        bool found = false;
        double width = 0;
        double height = 0;
        foreach (XuiSyntaxNode child in children)
        {
            CompiledXuiNode compiledChild =
                compilation.Node(child, source);
            string id = compiledChild.Id;
            PropertyBag properties = new(
                child,
                compiledChild.Properties,
                animation.ForNode(id, child.Key, childTimelineBarrier),
                renderContext.PropertiesFor(id, child.Key));
            if (!properties.Boolean(
                    "AutoSizeParentToText",
                    false,
                    diagnostics) ||
                Classify(compiledChild, properties) != XuiRenderKind.Text)
            {
                continue;
            }

            found = true;
            string text = properties.Text(
                "Text",
                properties.Text("SourceString"));
            if (renderContext.ResolveLocalization)
            {
                text = assetResolver.ResolveText(text);
            }

            text = compilation.ResolveColorControlText(
                text,
                properties.Boolean(
                    "ColorControlSequenceEnabled",
                    false,
                    diagnostics)).DisplayText;

            string fontId = properties.Text(
                "Font",
                properties.Text("DefaultFont")).Trim();
            double pointSize = Math.Max(
                0,
                properties.Number("PointSize", 0, diagnostics));
            bool uppercase = properties.Boolean(
                "Uppercase",
                false,
                diagnostics);
            bool multiline =
                properties.Boolean("MultiLine", false, diagnostics) ||
                text.Contains('\n');
            double horizontalBorder = Math.Max(
                0,
                properties.Number(
                    "ContentHorizontalBorder",
                    0,
                    diagnostics));
            double verticalBorder = Math.Max(
                0,
                properties.Number(
                    "ContentVerticalBorder",
                    0,
                    diagnostics));
            bool autoWidth =
                properties.Boolean(
                    "AutoSizeToText",
                    false,
                    diagnostics) ||
                properties.Boolean(
                    "AutoAdjustWidth",
                    false,
                    diagnostics);
            double childWidth = Math.Max(
                0,
                properties.Number("Width", 0, diagnostics));
            double maximumTextWidth = multiline && !autoWidth
                ? Math.Max(1, childWidth - (horizontalBorder * 2))
                : 0;
            XuiTextMeasurement measurement = assetResolver.MeasureText(
                fontId,
                text,
                pointSize,
                maximumTextWidth,
                multiline,
                uppercase,
                properties.Number(
                    "CharacterSpacingAdjust",
                    0,
                    diagnostics));
            XuiVector3 position = properties.Vector3(
                "Position",
                default,
                diagnostics);
            XuiVector3 scale = properties.Vector3(
                "Scale",
                new XuiVector3(1, 1, 1),
                diagnostics);
            width = Math.Max(
                width,
                position.X +
                ((measurement.Width + (horizontalBorder * 2)) *
                 Math.Abs(scale.X)));
            height = Math.Max(
                height,
                position.Y +
                ((measurement.Height + (verticalBorder * 2)) *
                 Math.Abs(scale.Y)));
        }

        return found
            ? new XuiVector2(Math.Max(0, width), Math.Max(0, height))
            : null;
    }

    private static PanelArrangement? ArrangePanelChildren(
        string elementName,
        PropertyBag parentProperties,
        IReadOnlyList<XuiSyntaxNode> children,
        string source,
        DyingLightLayoutCompilation compilation,
        AnimationOverrides animation,
        XuiRenderContext renderContext,
        IAssetResolver? assetResolver,
        string? childTimelineBarrier,
        XuiVector2 panelSize,
        List<XuiDiagnostic> diagnostics)
    {
        string classOverride = parentProperties.Text("ClassOverride").Trim();
        string effectiveClass = classOverride.Length > 0
            ? classOverride
            : elementName;
        bool isStack = effectiveClass.Contains(
            "UIStackPanel",
            StringComparison.OrdinalIgnoreCase);
        bool isWrap = effectiveClass.Contains(
            "UIWrapPanel",
            StringComparison.OrdinalIgnoreCase);
        bool isVerticalGroup = effectiveClass.Contains(
            "UIVerticalGroup",
            StringComparison.OrdinalIgnoreCase);
        if (!isStack && !isWrap && !isVerticalGroup)
        {
            return null;
        }

        bool skipInvisible = parentProperties.Boolean(
            "SkipInvisible",
            true,
            diagnostics);
        List<PanelChild> panelChildren = [];
        foreach (XuiSyntaxNode child in children)
        {
            CompiledXuiNode compiledChild =
                compilation.Node(child, source);
            string id = compiledChild.Id;
            PropertyBag properties = new(
                child,
                compiledChild.Properties,
                animation.ForNode(id, child.Key, childTimelineBarrier),
                renderContext.PropertiesFor(id, child.Key));
            bool shown = properties.Boolean("Show", true, diagnostics);
            bool forceShown = renderContext.IsForceShown(id, child.Key);
            if (forceShown)
            {
                shown = true;
            }
            else if (renderContext.IsForceHidden(id, child.Key))
            {
                shown = false;
            }

            double opacity = Math.Clamp(
                properties.Number("Opacity", 1, diagnostics),
                0,
                1);
            if (forceShown)
            {
                opacity = 1;
            }
            XuiVector3 position = properties.Vector3(
                "Position",
                default,
                diagnostics);
            XuiVector3 scale = properties.Vector3(
                "Scale",
                new XuiVector3(1, 1, 1),
                diagnostics);
            if (scale.X == 0 && scale.Y == 0)
            {
                scale = new XuiVector3(1, 1, scale.Z);
            }

            double childWidth = Math.Max(
                0,
                properties.Number("Width", 0, diagnostics));
            double childHeight = Math.Max(
                0,
                properties.Number("Height", 0, diagnostics));
            XuiRenderKind childKind = Classify(compiledChild, properties);
            if (assetResolver is not null &&
                childKind == XuiRenderKind.Text)
            {
                string text = properties.Text(
                    "Text",
                    properties.Text("SourceString"));
                if (renderContext.ResolveLocalization)
                {
                    text = assetResolver.ResolveText(text);
                }

                text = compilation.ResolveColorControlText(
                    text,
                    properties.Boolean(
                        "ColorControlSequenceEnabled",
                        false,
                        diagnostics)).DisplayText;

                bool uppercase = properties.Boolean(
                    "Uppercase",
                    false,
                    diagnostics);
                bool multiline =
                    properties.Boolean(
                        "MultiLine",
                        false,
                        diagnostics) ||
                    text.Contains('\n');
                bool autoSizeToText = properties.Boolean(
                    "AutoSizeToText",
                    false,
                    diagnostics);
                bool autoWidth =
                    autoSizeToText ||
                    properties.Boolean(
                        "AutoAdjustWidth",
                        false,
                        diagnostics);
                bool autoHeight =
                    autoSizeToText ||
                    properties.Boolean(
                        "AutoAdjustHeight",
                        false,
                        diagnostics) ||
                    properties.Boolean(
                        "MultilineAutoSizeHeight",
                        false,
                        diagnostics);
                if (autoWidth || autoHeight)
                {
                    double horizontalBorder = Math.Max(
                        0,
                        properties.Number(
                            "ContentHorizontalBorder",
                            0,
                            diagnostics));
                    double verticalBorder = Math.Max(
                        0,
                        properties.Number(
                            "ContentVerticalBorder",
                            0,
                            diagnostics));
                    XuiTextMeasurement measurement =
                        assetResolver.MeasureText(
                            properties.Text(
                                "Font",
                                properties.Text("DefaultFont")).Trim(),
                            text,
                            Math.Max(
                                0,
                                properties.Number(
                                    "PointSize",
                                    0,
                                    diagnostics)),
                            multiline && !autoWidth
                                ? Math.Max(
                                    1,
                                    childWidth -
                                    (horizontalBorder * 2))
                                : 0,
                            multiline,
                            uppercase,
                            properties.Number(
                                "CharacterSpacingAdjust",
                                0,
                                diagnostics));
                    if (autoWidth)
                    {
                        childWidth =
                            measurement.Width +
                            (horizontalBorder * 2);
                    }

                    if (autoHeight)
                    {
                        childHeight =
                            measurement.Height +
                            (verticalBorder * 2);
                    }
                }
            }
            else if (assetResolver is not null &&
                     childKind == XuiRenderKind.Control &&
                     ShouldAutoAdjustControlWidth(
                         properties,
                         diagnostics))
            {
                XuiVisualTemplate? visualTemplate =
                    compilation.ResolveVisual(
                        properties.Text("Visual").Trim());
                if (visualTemplate is not null)
                {
                    string text = properties.Text(
                        "Text",
                        properties.Text("SourceString"));
                    if (renderContext.ResolveLocalization)
                    {
                        text = assetResolver.ResolveText(text);
                    }

                    if (TryMeasureControlAutoWidth(
                            child,
                            properties,
                            visualTemplate,
                            compilation,
                            assetResolver,
                            text,
                            diagnostics,
                            out ControlAutoWidthMeasurement measurement) &&
                        (!properties.Boolean(
                             "OnlyWhenTextIsWider",
                             false,
                             diagnostics) ||
                         measurement.DesiredWidth > childWidth))
                    {
                        childWidth = measurement.DesiredWidth;
                    }
                }
            }

            panelChildren.Add(new PanelChild(
                child.Key,
                position,
                childWidth,
                childHeight,
                Math.Abs(scale.X),
                Math.Abs(scale.Y),
                properties.Number("MarginLeft", 0, diagnostics),
                properties.Number("MarginTop", 0, diagnostics),
                properties.Number("MarginRight", 0, diagnostics),
                properties.Number("MarginBottom", 0, diagnostics),
                shown,
                opacity,
                (XuiAnchor)(properties.Integer(
                    "Anchor",
                    0,
                    diagnostics) & 0x3f)));
        }

        if (isVerticalGroup)
        {
            return ArrangeVerticalGroup(
                panelChildren,
                panelSize,
                skipInvisible);
        }

        return isStack
            ? ArrangeStackPanel(
                parentProperties,
                panelChildren,
                panelSize,
                skipInvisible,
                diagnostics)
            : ArrangeWrapPanel(
                parentProperties,
                panelChildren,
                panelSize,
                skipInvisible,
                diagnostics);
    }

    private static PanelArrangement ArrangeVerticalGroup(
        IReadOnlyList<PanelChild> children,
        XuiVector2 panelSize,
        bool skipInvisible)
    {
        // Despite its name, Dying Light's UIVerticalGroup is the horizontal
        // command strip used along the bottom of menus. Its recovered
        // ArrangeItems implementation keeps independent cursors for ordinary
        // and right-anchored children.
        const double ItemSpacing = 15;
        Dictionary<string, XuiVector3> positions =
            new(StringComparer.Ordinal);
        double leftCursor = 0;
        double rightCursor = 0;
        double contentHeight = 0;
        int visibleCount = 0;
        foreach (PanelChild child in children)
        {
            bool anchoredRight =
                child.Anchor.HasFlag(XuiAnchor.Right);
            double scaledWidth = child.Width * child.ScaleX;
            double x = anchoredRight
                ? panelSize.X - rightCursor - scaledWidth
                : leftCursor;
            positions[child.Key] = child.Position with { X = x };

            if (skipInvisible && !child.IsShown)
            {
                continue;
            }

            double advance =
                scaledWidth + ItemSpacing + child.MarginRight;
            if (anchoredRight)
            {
                rightCursor += advance;
            }
            else
            {
                leftCursor += advance;
            }

            visibleCount++;
            contentHeight = Math.Max(
                contentHeight,
                child.Position.Y +
                child.MarginTop +
                (child.Height * child.ScaleY) +
                child.MarginBottom);
        }

        double contentWidth = Math.Max(
            0,
            leftCursor + rightCursor -
            (visibleCount > 0 ? ItemSpacing : 0));
        return new PanelArrangement(
            positions,
            new XuiVector2(contentWidth, Math.Max(0, contentHeight)));
    }

    private static PanelArrangement ArrangeStackPanel(
        PropertyBag properties,
        IReadOnlyList<PanelChild> children,
        XuiVector2 panelSize,
        bool skipInvisible,
        List<XuiDiagnostic> diagnostics)
    {
        bool useOpacity = properties.Boolean(
            "UseOpacityForArrangeItems",
            properties.Boolean("UseOpacity", true, diagnostics),
            diagnostics);
        bool useLeftMargins = properties.Boolean(
            "UseLeftMargins",
            false,
            diagnostics);
        bool wrap = properties.Boolean("Wrap", false, diagnostics);
        bool inverseOrder = properties.Boolean(
            "InverseOrder",
            false,
            diagnostics);
        IEnumerable<PanelChild> ordered = inverseOrder
            ? children
            : children.Reverse();
        Dictionary<string, XuiVector3> positions =
            new(StringComparer.Ordinal);
        double cursorX = 0;
        double cursorY = 0;
        double columnWidth = 0;
        double contentWidth = 0;
        double contentHeight = 0;
        foreach (PanelChild child in ordered)
        {
            if ((skipInvisible && !child.IsShown) ||
                (useOpacity && child.Opacity < 0.01))
            {
                continue;
            }

            double scaledWidth = child.Width * child.ScaleX;
            double scaledHeight = child.Height * child.ScaleY;
            double requiredHeight =
                child.MarginTop + scaledHeight + child.MarginBottom;
            if (wrap &&
                cursorY > 0 &&
                panelSize.Y > 0 &&
                cursorY + requiredHeight > panelSize.Y)
            {
                cursorX += columnWidth;
                cursorY = 0;
                columnWidth = 0;
            }

            double x = wrap
                ? cursorX + (useLeftMargins ? child.MarginLeft : 0)
                : useLeftMargins
                    ? child.MarginLeft
                    : child.Position.X;
            double y = cursorY + child.MarginTop;
            positions[child.Key] =
                new XuiVector3(x, y, child.Position.Z);
            cursorY = y + scaledHeight + child.MarginBottom;
            columnWidth = Math.Max(
                columnWidth,
                child.MarginLeft + scaledWidth + child.MarginRight);
            contentWidth = Math.Max(
                contentWidth,
                x + scaledWidth + child.MarginRight);
            contentHeight = Math.Max(contentHeight, cursorY);
        }

        contentWidth = Math.Max(contentWidth, cursorX + columnWidth);
        return new PanelArrangement(
            positions,
            new XuiVector2(
                Math.Max(0, contentWidth),
                Math.Max(0, contentHeight)));
    }

    private static PanelArrangement ArrangeWrapPanel(
        PropertyBag properties,
        IReadOnlyList<PanelChild> children,
        XuiVector2 panelSize,
        bool skipInvisible,
        List<XuiDiagnostic> diagnostics)
    {
        bool alignToRight = properties.Boolean(
            "AlignToRight",
            false,
            diagnostics);
        bool wrap = properties.Boolean("Wrap", true, diagnostics);
        bool invertOrder = properties.Boolean(
            "InvertOrder",
            false,
            diagnostics);
        IEnumerable<PanelChild> ordered = invertOrder
            ? children.Reverse()
            : children;
        List<WrapChildPlacement> placements = [];
        double cursorX = 0;
        double cursorY = 0;
        double rowHeight = 0;
        int row = 0;
        foreach (PanelChild child in ordered)
        {
            if (skipInvisible && !child.IsShown)
            {
                continue;
            }

            double scaledWidth = child.Width * child.ScaleX;
            double scaledHeight = child.Height * child.ScaleY;
            double requiredWidth =
                child.MarginLeft + scaledWidth + child.MarginRight;
            if (wrap &&
                cursorX > 0 &&
                panelSize.X > 0 &&
                cursorX + requiredWidth > panelSize.X)
            {
                cursorY += rowHeight;
                cursorX = 0;
                rowHeight = 0;
                row++;
            }

            double x = cursorX + child.MarginLeft;
            double y = cursorY + child.MarginTop;
            placements.Add(new WrapChildPlacement(
                child.Key,
                new XuiVector3(x, y, child.Position.Z),
                row,
                scaledWidth,
                scaledHeight,
                child.MarginRight,
                child.MarginBottom));
            cursorX = x + scaledWidth + child.MarginRight;
            rowHeight = Math.Max(
                rowHeight,
                child.MarginTop + scaledHeight + child.MarginBottom);
        }

        Dictionary<string, XuiVector3> positions =
            new(StringComparer.Ordinal);
        if (alignToRight && panelSize.X > 0)
        {
            foreach (IGrouping<int, WrapChildPlacement> rowGroup in
                     placements.GroupBy(static placement => placement.Row))
            {
                double rowWidth = rowGroup.Max(static placement =>
                    placement.Position.X +
                    placement.Width +
                    placement.MarginRight);
                double shift = Math.Max(0, panelSize.X - rowWidth);
                foreach (WrapChildPlacement placement in rowGroup)
                {
                    positions[placement.Key] = placement.Position with
                    {
                        X = placement.Position.X + shift,
                    };
                }
            }
        }
        else
        {
            foreach (WrapChildPlacement placement in placements)
            {
                positions[placement.Key] = placement.Position;
            }
        }

        double contentWidth = placements.Count == 0
            ? 0
            : placements.Max(static placement =>
                placement.Position.X +
                placement.Width +
                placement.MarginRight);
        double contentHeight = placements.Count == 0
            ? 0
            : placements.Max(static placement =>
                placement.Position.Y +
                placement.Height +
                placement.MarginBottom);
        return new PanelArrangement(
            positions,
            new XuiVector2(
                Math.Max(0, contentWidth),
                Math.Max(0, contentHeight)));
    }

    private static void ApplyParentSizeChange(
        XuiAnchor anchor,
        PropertyBag properties,
        XuiVector2 parentSize,
        XuiVector2 authoredParentSize,
        ref XuiVector3 position,
        ref XuiVector3 pivot,
        ref double width,
        ref double height,
        List<XuiDiagnostic> diagnostics)
    {
        if (authoredParentSize.X <= 0 ||
            authoredParentSize.Y <= 0 ||
            parentSize.X <= 0 ||
            parentSize.Y <= 0)
        {
            return;
        }

        double xRatio = parentSize.X / authoredParentSize.X;
        double yRatio = parentSize.Y / authoredParentSize.Y;
        if (Math.Abs(xRatio - 1) <= 0.000001 &&
            Math.Abs(yRatio - 1) <= 0.000001)
        {
            return;
        }

        bool holdAspect = properties.Boolean(
            "HoldAspectRatio",
            false,
            diagnostics);
        bool holdAspectX = properties.Boolean(
            "HoldAspectRatioX",
            false,
            diagnostics);
        double uniform = holdAspectX ? xRatio : yRatio;
        double widthRatio = holdAspect ? uniform : xRatio;
        double heightRatio = holdAspect ? uniform : yRatio;

        bool keepWidth =
            properties.Boolean("KeepWidth", false, diagnostics) ||
            properties.Boolean(
                "KeepWidthOnParentSizeChange",
                false,
                diagnostics);
        bool keepHeight =
            properties.Boolean("KeepHeight", false, diagnostics) ||
            properties.Boolean(
                "KeepHeightOnParentSizeChange",
                false,
                diagnostics);
        bool keepPositionX =
            properties.Boolean("KeepPosX", false, diagnostics) ||
            properties.Boolean(
                "KeepPosXOnParentSizeChange",
                false,
                diagnostics);
        bool keepPositionY =
            properties.Boolean("KeepPosY", false, diagnostics) ||
            properties.Boolean(
                "KeepPosYOnParentSizeChange",
                false,
                diagnostics);

        double parentDeltaX = parentSize.X - authoredParentSize.X;
        double parentDeltaY = parentSize.Y - authoredParentSize.Y;
        bool left = anchor.HasFlag(XuiAnchor.Left);
        bool right = anchor.HasFlag(XuiAnchor.Right);
        bool top = anchor.HasFlag(XuiAnchor.Top);
        bool bottom = anchor.HasFlag(XuiAnchor.Bottom);

        if (left && right)
        {
            if (!keepWidth)
            {
                double rightMargin = properties.Number(
                    "AnchorXRight",
                    Math.Max(
                        0,
                        authoredParentSize.X - position.X - width),
                    diagnostics);
                width = Math.Max(
                    0,
                    parentSize.X - position.X - rightMargin);
            }
        }
        else if (anchor.HasFlag(XuiAnchor.CenterX))
        {
            if (!keepPositionX)
            {
                position = position with
                {
                    X = position.X + (parentDeltaX * 0.5),
                };
            }
        }
        else if (right)
        {
            if (!keepPositionX)
            {
                position = position with
                {
                    X = position.X + parentDeltaX,
                };
            }
        }
        else if (!left)
        {
            if (!keepWidth)
            {
                width *= widthRatio;
            }

            if (!keepPositionX)
            {
                position = position with
                {
                    X = position.X * xRatio,
                };
            }
        }

        if (top && bottom)
        {
            if (!keepHeight)
            {
                double bottomMargin = properties.Number(
                    "AnchorYBottom",
                    Math.Max(
                        0,
                        authoredParentSize.Y - position.Y - height),
                    diagnostics);
                height = Math.Max(
                    0,
                    parentSize.Y - position.Y - bottomMargin);
            }
        }
        else if (anchor.HasFlag(XuiAnchor.CenterY))
        {
            if (!keepPositionY)
            {
                position = position with
                {
                    Y = position.Y + (parentDeltaY * 0.5),
                };
            }
        }
        else if (bottom)
        {
            if (!keepPositionY)
            {
                position = position with
                {
                    Y = position.Y + parentDeltaY,
                };
            }
        }
        else if (!top)
        {
            if (!keepHeight)
            {
                height *= heightRatio;
            }

            if (!keepPositionY)
            {
                position = position with
                {
                    Y = position.Y * yRatio,
                };
            }
        }

        bool holdPivot = holdAspect &&
                         properties.Boolean(
                             "HoldAspectPivotPosition",
                             false,
                             diagnostics);
        pivot = pivot with
        {
            X = pivot.X * (holdPivot ? uniform : xRatio),
            Y = pivot.Y * (holdPivot ? uniform : yRatio),
        };
    }

    private static bool ApplyResolutionChange(
        PropertyBag properties,
        ResolutionContext resolution,
        ref XuiVector3 position,
        ref double width,
        ref double height,
        ref XuiVector3 scale,
        List<XuiDiagnostic> diagnostics)
    {
        if (!resolution.HasChange)
        {
            return false;
        }

        bool keepPositionX = properties.Boolean(
            "KeepPosXOnResolutionChange",
            false,
            diagnostics);
        bool keepPositionY = properties.Boolean(
            "KeepPosYOnResolutionChange",
            false,
            diagnostics);
        bool keepWidth = properties.Boolean(
            "KeepWidthOnResolutionChange",
            false,
            diagnostics);
        bool keepHeight = properties.Boolean(
            "KeepHeightOnResolutionChange",
            false,
            diagnostics);
        bool scaleWidth = properties.Boolean(
            "ScaleWidthByResolution",
            false,
            diagnostics);
        bool scaleHeight = properties.Boolean(
            "ScaleHeightByResolution",
            false,
            diagnostics);
        bool holdAspect = properties.Boolean(
            "HoldAspectRatio",
            false,
            diagnostics);

        if (keepPositionX && resolution.XScale > 0)
        {
            position = position with
            {
                X = position.X / resolution.XScale,
            };
        }

        if (keepPositionY && resolution.YScale > 0)
        {
            position = position with
            {
                Y = position.Y / resolution.YScale,
            };
        }

        if (keepWidth && resolution.XScale > 0)
        {
            width /= resolution.XScale;
        }
        else if (scaleWidth)
        {
            width *= resolution.HorizontalAspectScale;
        }

        if (keepHeight && resolution.YScale > 0)
        {
            height /= resolution.YScale;
        }
        else if (scaleHeight)
        {
            height *= resolution.VerticalAspectScale;
        }

        if (holdAspect &&
            !resolution.Viewport.PreserveAspect &&
            resolution.XScale > 0 &&
            resolution.YScale > 0)
        {
            bool useX = properties.Boolean(
                "HoldAspectRatioX",
                false,
                diagnostics);
            scale = useX
                ? scale with
                {
                    Y = scale.Y * resolution.XScale / resolution.YScale,
                }
                : scale with
                {
                    X = scale.X * resolution.YScale / resolution.XScale,
                };
        }

        bool usedResolutionFlag =
            keepPositionX ||
            keepPositionY ||
            keepWidth ||
            keepHeight ||
            scaleWidth ||
            scaleHeight ||
            holdAspect;
        if (usedResolutionFlag)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT010",
                XuiDiagnosticSeverity.Info,
                "Resolution-change flags are evaluated against the authored canvas and target viewport; proprietary safe-area projection remains an explicit approximation.",
                properties.Syntax.Span,
                properties.Syntax.Key));
        }

        return usedResolutionFlag;
    }

    internal static Matrix3x2 CreateLocalTransform(
        XuiVector3 position,
        XuiVector3 pivot,
        XuiVector3 scale,
        double rotationDegrees)
    {
        float radians = (float)(rotationDegrees * Math.PI / 180);
        return
            Matrix3x2.CreateTranslation((float)-pivot.X, (float)-pivot.Y) *
            Matrix3x2.CreateScale((float)scale.X, (float)scale.Y) *
            Matrix3x2.CreateRotation(radians) *
            Matrix3x2.CreateTranslation(
                (float)(position.X + pivot.X),
                (float)(position.Y + pivot.Y));
    }

    private static Matrix3x2 CreateViewportTransform(
        XuiVector2 designSize,
        XuiViewport viewport)
    {
        double xScale = viewport.Width / designSize.X;
        double yScale = viewport.Height / designSize.Y;
        if (!viewport.PreserveAspect)
        {
            return Matrix3x2.CreateScale((float)xScale, (float)yScale);
        }

        double scale = Math.Min(xScale, yScale);
        double x = (viewport.Width - (designSize.X * scale)) * 0.5;
        double y = (viewport.Height - (designSize.Y * scale)) * 0.5;
        return Matrix3x2.CreateScale((float)scale) *
               Matrix3x2.CreateTranslation((float)x, (float)y);
    }

    internal static XuiRect TransformBounds(
        XuiRect bounds,
        Matrix3x2 transform)
    {
        Span<XuiVector2> points =
        [
            Transform(new XuiVector2(bounds.X, bounds.Y), transform),
            Transform(new XuiVector2(bounds.Right, bounds.Y), transform),
            Transform(new XuiVector2(bounds.Right, bounds.Bottom), transform),
            Transform(new XuiVector2(bounds.X, bounds.Bottom), transform),
        ];
        return XuiRect.FromPoints(points);
    }

    private static XuiVector2 Transform(
        XuiVector2 point,
        Matrix3x2 transform)
    {
        Vector2 result = Vector2.Transform(
            new Vector2((float)point.X, (float)point.Y),
            transform);
        return new XuiVector2(result.X, result.Y);
    }

    private static XuiTextHorizontalAlignment ParseHorizontalTextAlignment(
        PropertyBag properties,
        int textStyle,
        ICollection<XuiDiagnostic> diagnostics)
    {
        string raw = properties.Text(
                "ContentHorizontalAlign",
                properties.Text("DefaultHorizontalAlign"))
            .Trim();
        if (raw.Length > 0)
        {
            return raw.ToLowerInvariant() switch
            {
                "left" or "0" => XuiTextHorizontalAlignment.Left,
                "center" or "1" => XuiTextHorizontalAlignment.Center,
                "right" or "2" => XuiTextHorizontalAlignment.Right,
                "justify" or "3" => XuiTextHorizontalAlignment.Justify,
                _ => InvalidHorizontalAlignment(
                    properties,
                    raw,
                    diagnostics),
            };
        }

        if ((textStyle & 0x400) != 0)
        {
            return XuiTextHorizontalAlignment.Center;
        }

        return (textStyle & 0x200) != 0
            ? XuiTextHorizontalAlignment.Right
            : XuiTextHorizontalAlignment.Left;
    }

    private static XuiTextVerticalAlignment ParseVerticalTextAlignment(
        PropertyBag properties,
        int textStyle,
        ICollection<XuiDiagnostic> diagnostics)
    {
        string raw = properties.Text(
                "ContentVerticalAlign",
                properties.Text("DefaultVerticalAlign"))
            .Trim();
        if (raw.Length > 0)
        {
            return raw.ToLowerInvariant() switch
            {
                "top" or "0" => XuiTextVerticalAlignment.Top,
                "middle" or "1" => XuiTextVerticalAlignment.Middle,
                "bottom" or "2" => XuiTextVerticalAlignment.Bottom,
                _ => InvalidVerticalAlignment(
                    properties,
                    raw,
                    diagnostics),
            };
        }

        if (properties.Boolean(
                "VerticalAlignDown",
                false,
                diagnostics))
        {
            return XuiTextVerticalAlignment.Bottom;
        }

        return (textStyle & 0x1000) != 0
            ? XuiTextVerticalAlignment.Middle
            : XuiTextVerticalAlignment.Top;
    }

    private static XuiTextHorizontalAlignment InvalidHorizontalAlignment(
        PropertyBag properties,
        string raw,
        ICollection<XuiDiagnostic> diagnostics)
    {
        diagnostics.Add(new XuiDiagnostic(
            "XUI-LAYOUT012",
            XuiDiagnosticSeverity.Warning,
            $"Unknown horizontal text alignment '{raw}'; left alignment is used.",
            properties.Syntax.Span,
            properties.Syntax.Key));
        return XuiTextHorizontalAlignment.Left;
    }

    private static XuiTextVerticalAlignment InvalidVerticalAlignment(
        PropertyBag properties,
        string raw,
        ICollection<XuiDiagnostic> diagnostics)
    {
        diagnostics.Add(new XuiDiagnostic(
            "XUI-LAYOUT012",
            XuiDiagnosticSeverity.Warning,
            $"Unknown vertical text alignment '{raw}'; top alignment is used.",
            properties.Syntax.Span,
            properties.Syntax.Key));
        return XuiTextVerticalAlignment.Top;
    }

    private static XuiRect? ParentClip(XuiRenderNode? parent) =>
        parent?.ClipBounds;

    internal static XuiRect? Intersect(XuiRect? existing, XuiRect added)
    {
        if (existing is null)
        {
            return added;
        }

        double left = Math.Max(existing.Value.X, added.X);
        double top = Math.Max(existing.Value.Y, added.Y);
        double right = Math.Min(existing.Value.Right, added.Right);
        double bottom = Math.Min(existing.Value.Bottom, added.Bottom);
        return right <= left || bottom <= top
            ? new XuiRect(left, top, 0, 0)
            : new XuiRect(left, top, right - left, bottom - top);
    }

    private static XuiRenderKind Classify(
        CompiledXuiNode compiled,
        PropertyBag properties)
    {
        string classOverride = properties.Text("ClassOverride");
        string visual = properties.Text("Visual").Trim();
        if (string.Equals(
                classOverride,
                compiled.ClassOverride,
                StringComparison.Ordinal) &&
            string.Equals(
                visual,
                compiled.Visual,
                StringComparison.Ordinal))
        {
            return compiled.Kind;
        }

        return DyingLightLayoutCompilation.Classify(
            compiled.SyntaxName,
            classOverride,
            visual);
    }

    private static XuiPaintKind ClassifyPaint(
        XuiRenderKind kind,
        string imagePath,
        XuiMaterialProfile materialProfile)
    {
        if (kind is not XuiRenderKind.Image and
            not XuiRenderKind.Rectangle and
            not XuiRenderKind.Shape)
        {
            return XuiPaintKind.None;
        }

        if (materialProfile.SuppressSelfPaint ||
            kind == XuiRenderKind.Shape &&
            materialProfile.RequiresRuntimeData)
        {
            return XuiPaintKind.None;
        }

        string normalizedPath = imagePath.Trim();
        if (normalizedPath.Equals("white", StringComparison.OrdinalIgnoreCase))
        {
            return XuiPaintKind.SolidColor;
        }

        if (normalizedPath.Length > 0)
        {
            return XuiPaintKind.Texture;
        }

        return kind is XuiRenderKind.Rectangle or XuiRenderKind.Shape
            ? XuiPaintKind.SolidColor
            : XuiPaintKind.None;
    }

    private static bool IsTextPresenter(
        string name,
        PropertyBag properties)
    {
        string combined = name + " " + properties.Text("ClassOverride");
        return combined.Contains("Text", StringComparison.OrdinalIgnoreCase) &&
               combined.Contains("Presenter", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImagePresenter(
        string name,
        PropertyBag properties)
    {
        string combined = name + " " + properties.Text("ClassOverride");
        return combined.Contains("Image", StringComparison.OrdinalIgnoreCase) &&
               combined.Contains("Presenter", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrimaryDataAssociation(PropertyBag properties)
    {
        string raw = properties.Text("DataAssociation").Trim();
        return raw.Length == 0 ||
               XuiValueParser.TryInteger(raw, out int association) &&
               association == 0;
    }

    private static VisualInstanceBindings CreateVisualBindings(
        XuiRenderNode instance,
        PropertyBag properties,
        XuiVisualTemplate visualTemplate,
        DyingLightLayoutCompilation compilation,
        IAssetResolver assetResolver,
        List<XuiDiagnostic> diagnostics)
    {
        bool isButton =
            instance.ElementName.Contains(
                "Button",
                StringComparison.OrdinalIgnoreCase) ||
            instance.ClassOverride.Contains(
                "Button",
                StringComparison.OrdinalIgnoreCase) ||
            instance.Visual.Contains(
                "Button",
                StringComparison.OrdinalIgnoreCase);
        int pressKey = 22528;
        string rawPressKey = properties.Text("PressKey").Trim();
        if (rawPressKey.Length > 0 &&
            XuiValueParser.TryInteger(rawPressKey, out int parsedPressKey))
        {
            pressKey = parsedPressKey;
        }

        (string keyboard, string gamepad) =
            ButtonHintForPressKey(pressKey);
        ButtonVisualLayout? buttonLayout = null;
        if (isButton &&
            ShouldAutoAdjustControlWidth(properties, diagnostics) &&
            TryMeasureControlAutoWidth(
                properties.Syntax,
                properties,
                visualTemplate,
                compilation,
                assetResolver,
                instance.Text,
                diagnostics,
                out ControlAutoWidthMeasurement measurement) &&
            measurement.ButtonLayout is
                XuiButtonLayoutResult measuredButtonLayout &&
            (!properties.Boolean(
                 "OnlyWhenTextIsWider",
                 false,
                 diagnostics) ||
             measurement.DesiredWidth > instance.AuthoredSize.X))
        {
            bool keyboardScheme =
                assetResolver.InputGlyphScheme ==
                XuiInputGlyphScheme.KeyboardAndMouse;
            string activeHintId = keyboardScheme
                ? "T_HintPC"
                : "T_HintConsoles";
            PropertyBag? activeHint = FindVisualPresenter(
                visualTemplate,
                compilation,
                (id, _) => id.Equals(
                    activeHintId,
                    StringComparison.OrdinalIgnoreCase));
            XuiVector3 activeHintPosition = activeHint?.Vector3(
                "Position",
                default,
                diagnostics) ?? default;
            buttonLayout = new ButtonVisualLayout(
                instance.Size.X,
                measuredButtonLayout,
                activeHintId,
                activeHintPosition,
                ResolutionScale: 1);
        }

        return new VisualInstanceBindings(
            instance.Text,
            instance.ImagePath,
            isButton,
            assetResolver.InputGlyphScheme,
            keyboard,
            gamepad,
            KeyboardHintUsesSeparateBackground(pressKey),
            buttonLayout);
    }

    private static (string Keyboard, string Gamepad)
        ButtonHintForPressKey(int pressKey) =>
        pressKey switch
        {
            3840 or 22528 or 22592 =>
                ("&[PC_ENTER]&", "&[A]&"),
            3841 or 22529 or 22593 =>
                ("&[PC_ESC]&", "&[B]&"),
            22530 => ("F", "&[X]&"),
            22531 => ("C", "&[Y]&"),
            22532 => ("Q", "&[LB]&"),
            22533 => ("E", "&[RB]&"),
            22534 => ("&[PC_BACK]&", "&[Back]&"),
            22535 => ("&[PC_START]&", "&[Start]&"),
            _ => ($"Key {pressKey}", string.Empty),
        };

    private static bool KeyboardHintUsesSeparateBackground(int pressKey) =>
        pressKey is not (
            3840 or
            3841 or
            22528 or
            22529 or
            22592 or
            22593);

    private static bool ShouldAutoAdjustControlWidth(
        PropertyBag properties,
        List<XuiDiagnostic> diagnostics)
    {
        XuiButtonLayoutProfile? profile =
            XuiButtonLayoutProfile.Resolve(
                properties.Text("ClassOverride"),
                properties.Text("Visual"));
        return profile is { RequiresAutoAdjustWidth: false } ||
               properties.Boolean(
                   "AutoAdjustWidth",
                   false,
                   diagnostics);
    }

    private static void ApplyControlAutoWidth(
        XuiSyntaxNode syntax,
        PropertyBag properties,
        XuiVisualTemplate visualTemplate,
        DyingLightLayoutCompilation compilation,
        IAssetResolver assetResolver,
        string text,
        bool adjustPosition,
        ref XuiVector3 position,
        ref double width,
        List<XuiDiagnostic> diagnostics)
    {
        if (!TryMeasureControlAutoWidth(
                syntax,
                properties,
                visualTemplate,
                compilation,
                assetResolver,
                text,
                diagnostics,
                out ControlAutoWidthMeasurement measurement))
        {
            return;
        }

        double desiredWidth = measurement.DesiredWidth;
        if (properties.Boolean(
                "OnlyWhenTextIsWider",
                false,
                diagnostics) &&
            desiredWidth <= width)
        {
            return;
        }

        if (adjustPosition)
        {
            double difference = desiredWidth - width;
            bool adjustToLeft = properties.Boolean(
                "AdjustToLeft",
                false,
                diagnostics);
            bool adjustToRight = properties.Boolean(
                "AdjustToRight",
                false,
                diagnostics);
            if (adjustToLeft)
            {
                position = position with
                {
                    X = position.X - difference,
                };
            }
            else if (!adjustToRight)
            {
                XuiAnchor anchor = (XuiAnchor)(
                    properties.Integer("Anchor", 0, diagnostics) & 0x3f);
                bool rightOnly =
                    anchor.HasFlag(XuiAnchor.Right) &&
                    !anchor.HasFlag(XuiAnchor.Left);
                if (rightOnly)
                {
                    position = position with
                    {
                        X = position.X - difference,
                    };
                }
                else if (anchor.HasFlag(XuiAnchor.CenterX))
                {
                    position = position with
                    {
                        X = position.X - (difference * 0.5),
                    };
                }
            }
        }

        width = desiredWidth;
    }

    private static bool TryMeasureControlAutoWidth(
        XuiSyntaxNode syntax,
        PropertyBag properties,
        XuiVisualTemplate visualTemplate,
        DyingLightLayoutCompilation compilation,
        IAssetResolver assetResolver,
        string text,
        List<XuiDiagnostic> diagnostics,
        out ControlAutoWidthMeasurement measurement)
    {
        measurement = default;
        PropertyBag? primaryPresenter = FindVisualPresenter(
            visualTemplate,
            compilation,
            static (_, presenterProperties) =>
                IsPrimaryDataAssociation(presenterProperties));
        if (primaryPresenter is null)
        {
            return false;
        }

        XuiTextMeasurement textMeasurement = MeasurePresenterText(
            primaryPresenter,
            compilation,
            assetResolver,
            text,
            diagnostics);
        XuiButtonLayoutProfile? buttonProfile =
            XuiButtonLayoutProfile.Resolve(
                properties.Text("ClassOverride"),
                properties.Text("Visual"));
        double desiredWidth;
        double labelBlockWidth;
        double hintBlockWidth;
        XuiButtonLayoutResult? buttonLayout = null;
        if (buttonProfile is not null)
        {
            int pressKey = properties.Integer(
                "PressKey",
                22528,
                diagnostics);
            (string keyboardHint, string gamepadHint) =
                ButtonHintForPressKey(pressKey);
            bool keyboard =
                assetResolver.InputGlyphScheme ==
                XuiInputGlyphScheme.KeyboardAndMouse;
            string hintId = keyboard
                ? "T_HintPC"
                : "T_HintConsoles";
            PropertyBag? hintPresenter = FindVisualPresenter(
                visualTemplate,
                compilation,
                (id, _) => id.Equals(
                    hintId,
                    StringComparison.OrdinalIgnoreCase));
            string hintText = keyboard
                ? assetResolver.ResolveText(keyboardHint)
                : assetResolver.ResolveText(gamepadHint);
            double hintWidth = hintPresenter is null ||
                               hintText.Length == 0
                ? 0
                : MeasurePresenterText(
                    hintPresenter,
                    compilation,
                    assetResolver,
                    hintText,
                    diagnostics).Width;
            buttonLayout = buttonProfile.Measure(
                assetResolver.InputGlyphScheme,
                textMeasurement.Width,
                hintWidth);
            labelBlockWidth = buttonLayout.LabelBlockWidth;
            hintBlockWidth = buttonLayout.HintBlockWidth;
            desiredWidth = buttonLayout.TotalWidth;
        }
        else
        {
            double widthAdjust = Math.Max(
                0,
                properties.Number(
                    "WidthAdjust",
                    0,
                    diagnostics));
            labelBlockWidth =
                textMeasurement.Width +
                (widthAdjust * 2);
            hintBlockWidth = 0;
            desiredWidth = labelBlockWidth;
        }

        desiredWidth = Math.Max(0, desiredWidth);
        if (double.IsFinite(desiredWidth))
        {
            measurement = new ControlAutoWidthMeasurement(
                desiredWidth,
                Math.Max(0, labelBlockWidth),
                Math.Max(0, hintBlockWidth),
                buttonLayout);
            return true;
        }

        diagnostics.Add(new XuiDiagnostic(
            "XUI-LAYOUT013",
            XuiDiagnosticSeverity.Warning,
            "Button auto-width produced a non-finite value; the authored geometry is retained.",
            syntax.Span,
            syntax.Key));
        measurement = default;
        return false;
    }

    private static PropertyBag? FindVisualPresenter(
        XuiVisualTemplate visualTemplate,
        DyingLightLayoutCompilation compilation,
        Func<string, PropertyBag, bool> predicate)
    {
        Stack<XuiSyntaxNode> pending = new();
        IReadOnlyList<XuiSyntaxNode> rootChildren =
            compilation.Node(
                visualTemplate.Syntax,
                visualTemplate.Source).VisualChildren;
        for (int index = rootChildren.Count - 1; index >= 0; index--)
        {
            pending.Push(rootChildren[index]);
        }

        while (pending.Count > 0)
        {
            XuiSyntaxNode syntax = pending.Pop();
            CompiledXuiNode compiled =
                compilation.Node(syntax, visualTemplate.Source);
            PropertyBag presenterProperties = new(
                syntax,
                compiled.Properties,
                overrides: null,
                runtimeOverrides: null);
            if (Classify(compiled, presenterProperties) ==
                    XuiRenderKind.Text &&
                IsTextPresenter(syntax.Name, presenterProperties) &&
                predicate(compiled.Id, presenterProperties))
            {
                return presenterProperties;
            }

            for (int index = compiled.VisualChildren.Count - 1;
                 index >= 0;
                 index--)
            {
                pending.Push(compiled.VisualChildren[index]);
            }
        }

        return null;
    }

    private static XuiTextMeasurement MeasurePresenterText(
        PropertyBag presenter,
        DyingLightLayoutCompilation compilation,
        IAssetResolver assetResolver,
        string text,
        List<XuiDiagnostic> diagnostics)
    {
        string font = presenter.Text(
            "Font",
            presenter.Text("DefaultFont")).Trim();
        double pointSize = Math.Max(
            0,
            presenter.Number("PointSize", 0, diagnostics));
        bool uppercase = presenter.Boolean(
            "Uppercase",
            false,
            diagnostics);
        text = compilation.ResolveColorControlText(
            text,
            presenter.Boolean(
                "ColorControlSequenceEnabled",
                false,
                diagnostics)).DisplayText;
        double spacing = presenter.Number(
            "CharacterSpacingAdjust",
            0,
            diagnostics);
        return assetResolver.MeasureText(
            font,
            text,
            pointSize,
            maximumWidth: 0,
            multiline: false,
            uppercase: uppercase,
            characterSpacingAdjust: spacing);
    }

    private sealed record PanelArrangement(
        IReadOnlyDictionary<string, XuiVector3> Positions,
        XuiVector2 ContentSize);

    private sealed record PanelChild(
        string Key,
        XuiVector3 Position,
        double Width,
        double Height,
        double ScaleX,
        double ScaleY,
        double MarginLeft,
        double MarginTop,
        double MarginRight,
        double MarginBottom,
        bool IsShown,
        double Opacity,
        XuiAnchor Anchor);

    private sealed record WrapChildPlacement(
        string Key,
        XuiVector3 Position,
        int Row,
        double Width,
        double Height,
        double MarginRight,
        double MarginBottom);

    private readonly record struct ControlAutoWidthMeasurement(
        double DesiredWidth,
        double LabelBlockWidth,
        double HintBlockWidth,
        XuiButtonLayoutResult? ButtonLayout);

    private sealed record ButtonVisualLayout(
        double TotalWidth,
        XuiButtonLayoutResult Measurement,
        string ActiveHintId,
        XuiVector3 ActiveHintPosition,
        double ResolutionScale);

    private readonly record struct VisualGeometryOverride(
        double? X,
        double? Y,
        double? Width,
        double? Height,
        XuiAnchor? Anchor,
        bool BypassParentSizeChange);

    private sealed record VisualInstanceBindings(
        string Text,
        string ImagePath,
        bool IsButton,
        XuiInputGlyphScheme InputGlyphScheme,
        string KeyboardHint,
        string GamepadHint,
        bool KeyboardHintHasSeparateBackground,
        ButtonVisualLayout? ButtonLayout)
    {
        public string ResolveText(
            string id,
            PropertyBag properties,
            string authoredText)
        {
            if (IsPrimaryDataAssociation(properties))
            {
                return Text;
            }

            if (!IsButton ||
                !XuiValueParser.TryInteger(
                    properties.Text("DataAssociation"),
                    out int association) ||
                association != 1)
            {
                return authoredText;
            }

            if (id.Equals(
                    "T_HintPC",
                    StringComparison.OrdinalIgnoreCase))
            {
                return KeyboardHint;
            }

            if (id.Equals(
                    "T_HintConsoles",
                    StringComparison.OrdinalIgnoreCase))
            {
                return GamepadHint;
            }

            return authoredText;
        }

        public bool ResolveVisibility(
            string id,
            bool authoredVisibility)
        {
            if (!IsButton)
            {
                return authoredVisibility;
            }

            bool keyboard =
                InputGlyphScheme ==
                XuiInputGlyphScheme.KeyboardAndMouse;
            if (id.Equals(
                    "T_HintPC",
                    StringComparison.OrdinalIgnoreCase))
            {
                return keyboard;
            }

            if (id.Equals(
                    "I_IconBg",
                    StringComparison.OrdinalIgnoreCase))
            {
                // UIButtonWithHints::GetHint marks Enter/Esc as self-framed
                // keyboard glyphs. UpdateHint consequently hides I_IconBg
                // for those keys while retaining it behind ordinary labels.
                return keyboard &&
                       KeyboardHintHasSeparateBackground;
            }

            if (id.Equals(
                    "T_HintConsoles",
                    StringComparison.OrdinalIgnoreCase))
            {
                return !keyboard;
            }

            return authoredVisibility;
        }

        public VisualGeometryOverride? ResolveGeometry(
            string id,
            PropertyBag properties)
        {
            if (ButtonLayout is not ButtonVisualLayout layout)
            {
                return null;
            }

            if (id.Equals(
                    "bg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new VisualGeometryOverride(
                    null,
                    null,
                    layout.TotalWidth,
                    null,
                    null,
                    true);
            }

            if (id.Equals(
                    "TextPrezenter",
                    StringComparison.OrdinalIgnoreCase) ||
                id.Equals(
                    "TextPresenter",
                    StringComparison.OrdinalIgnoreCase) ||
                id.Equals(
                    "I_BgText",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new VisualGeometryOverride(
                    null,
                    null,
                    layout.Measurement.LabelBlockWidth,
                    null,
                    null,
                    true);
            }

            if (id.Equals(
                    layout.ActiveHintId,
                    StringComparison.OrdinalIgnoreCase))
            {
                bool keyboard =
                    layout.Measurement.ActiveHintScheme ==
                    XuiInputGlyphScheme.KeyboardAndMouse;
                return new VisualGeometryOverride(
                    keyboard ? null : 0,
                    null,
                    layout.Measurement.HintElementWidth,
                    null,
                    null,
                    true);
            }

            if (id.Equals(
                    "I_IconBg",
                    StringComparison.OrdinalIgnoreCase))
            {
                double factor =
                    layout.Measurement.HintBackgroundScale;
                return new VisualGeometryOverride(
                    layout.ActiveHintPosition.X -
                    (5.5 * factor * layout.ResolutionScale),
                    layout.ActiveHintPosition.Y -
                    (2 * factor * layout.ResolutionScale),
                    layout.Measurement.MeasuredHintWidth +
                    (21.8 * factor * layout.ResolutionScale),
                    null,
                    null,
                    true);
            }

            if (id.Equals(
                    "I_BgHint",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new VisualGeometryOverride(
                    null,
                    null,
                    layout.Measurement.HintBlockWidth,
                    null,
                    null,
                    true);
            }

            return null;
        }
    }

    private sealed record ForcedMaterialSet(
        string ImageMaterial,
        string TextMaterial,
        string AARectangleMaterial)
    {
        public string? MaterialFor(
            XuiRenderKind kind,
            string elementName) =>
            kind switch
            {
                XuiRenderKind.Text => TextMaterial,
                XuiRenderKind.Rectangle
                    when elementName.Contains(
                        "AARectangle",
                        StringComparison.OrdinalIgnoreCase) =>
                    AARectangleMaterial,
                XuiRenderKind.Image or XuiRenderKind.Shape => ImageMaterial,
                _ => null,
            };
    }

    private static void AddColorControlDiagnostics(
        XuiSyntaxNode syntax,
        XuiRenderKind kind,
        bool enabled,
        bool routesToTextPresenter,
        XuiColorControlParseResult parsed,
        List<XuiDiagnostic> diagnostics)
    {
        if (!parsed.HasMarkup)
        {
            return;
        }

        if (kind != XuiRenderKind.Text && !routesToTextPresenter)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-TEXT003",
                XuiDiagnosticSeverity.Warning,
                "Color markup is attached to an element that does not resolve to an IUIText node, so Dying Light will display or ignore it as ordinary data.",
                syntax.Span,
                syntax.Key));
        }

        if (parsed.HasMalformedSequences)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-TEXT002",
                XuiDiagnosticSeverity.Warning,
                "Malformed or unsupported color markup is displayed literally. Accepted forms are %COLOR(RRGGBB) and %COLOR(reset); the COLOR prefix is case-sensitive.",
                syntax.Span,
                syntax.Key));
        }

        if (kind == XuiRenderKind.Text &&
            parsed.HasValidSequences &&
            !enabled)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-TEXT001",
                XuiDiagnosticSeverity.Warning,
                "Color markup is present, but ColorControlSequenceEnabled is false. Dying Light will display these tags literally unless the controller enables them at runtime.",
                syntax.Span,
                syntax.Key));
        }
    }

    private static List<XuiDiagnostic> AggregateDiagnostics(
        IReadOnlyList<XuiDiagnostic> diagnostics)
    {
        Dictionary<(string Code, string Message), List<XuiDiagnostic>>
            materialGroups = [];
        List<XuiDiagnostic> result = [];
        foreach (XuiDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Code != "XUI-LAYOUT004")
            {
                result.Add(diagnostic);
                continue;
            }

            (string, string) key = (diagnostic.Code, diagnostic.Message);
            if (!materialGroups.TryGetValue(
                    key,
                    out List<XuiDiagnostic>? group))
            {
                group = [];
                materialGroups.Add(key, group);
            }

            group.Add(diagnostic);
        }

        foreach (List<XuiDiagnostic> group in materialGroups.Values)
        {
            XuiDiagnostic first = group[0];
            result.Add(group.Count == 1
                ? first
                : first with
                {
                    Message =
                        $"{first.Message} ({group.Count:N0} affected nodes; first shown.)",
                });
        }

        return result;
    }

    private readonly record struct ResolutionContext(
        XuiVector2 DesignSize,
        XuiViewport Viewport)
    {
        public double XScale => Viewport.Width / DesignSize.X;

        public double YScale => Viewport.Height / DesignSize.Y;

        public double HorizontalAspectScale =>
            YScale <= 0 ? 1 : XScale / YScale;

        public double VerticalAspectScale =>
            XScale <= 0 ? 1 : YScale / XScale;

        public bool HasChange =>
            Math.Abs(XScale - 1) > 0.000001 ||
            Math.Abs(YScale - 1) > 0.000001;
    }

    private sealed class PropertyBag
    {
        private readonly IReadOnlyDictionary<string, string> _values;
        private readonly IReadOnlyDictionary<string, XuiAnimatedValue>? _overrides;
        private readonly IReadOnlyDictionary<string, string>? _runtimeOverrides;
        private readonly XuiSyntaxNode _syntax;

        public PropertyBag(
            XuiSyntaxNode syntax,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<string, XuiAnimatedValue>? overrides,
            IReadOnlyDictionary<string, string>? runtimeOverrides)
        {
            _syntax = syntax;
            _overrides = overrides;
            _runtimeOverrides = runtimeOverrides;
            _values = values;
        }

        public XuiSyntaxNode Syntax => _syntax;

        public string Text(string name, string fallback = "")
        {
            if (TryRuntimeValue(name, out string? runtime))
            {
                return runtime;
            }

            if (_overrides is not null &&
                _overrides.TryGetValue(name, out XuiAnimatedValue? animated))
            {
                return animated.Text;
            }

            return _values.GetValueOrDefault(name, fallback);
        }

        public double Number(
            string name,
            double fallback,
            ICollection<XuiDiagnostic> diagnostics)
        {
            if (TryRuntimeValue(name, out string? runtime))
            {
                if (XuiValueParser.TryNumber(runtime, out double runtimeResult))
                {
                    return runtimeResult;
                }

                Invalid(name, runtime, "number", diagnostics);
                return fallback;
            }

            if (_overrides is not null &&
                _overrides.TryGetValue(name, out XuiAnimatedValue? animated) &&
                animated.Kind == XuiTimelineValueKind.Number)
            {
                return animated.Number;
            }

            string value = Text(name);
            if (value.Length == 0)
            {
                return fallback;
            }

            if (XuiValueParser.TryNumber(value, out double result))
            {
                return result;
            }

            Invalid(name, value, "number", diagnostics);
            return fallback;
        }

        public int Integer(
            string name,
            int fallback,
            ICollection<XuiDiagnostic> diagnostics)
        {
            string value = Text(name);
            if (value.Length == 0)
            {
                return fallback;
            }

            if (XuiValueParser.TryInteger(value, out int result))
            {
                return result;
            }

            Invalid(name, value, "integer", diagnostics);
            return fallback;
        }

        public bool Boolean(
            string name,
            bool fallback,
            ICollection<XuiDiagnostic> diagnostics)
        {
            if (TryRuntimeValue(name, out string? runtime))
            {
                if (XuiValueParser.TryBoolean(runtime, out bool runtimeResult))
                {
                    return runtimeResult;
                }

                Invalid(name, runtime, "boolean", diagnostics);
                return fallback;
            }

            if (_overrides is not null &&
                _overrides.TryGetValue(name, out XuiAnimatedValue? animated) &&
                animated.Kind == XuiTimelineValueKind.Boolean)
            {
                return animated.Boolean;
            }

            string value = Text(name);
            if (value.Length == 0)
            {
                return fallback;
            }

            if (XuiValueParser.TryBoolean(value, out bool result))
            {
                return result;
            }

            Invalid(name, value, "boolean", diagnostics);
            return fallback;
        }

        public double NumberOrBoolean(
            string name,
            double fallback,
            ICollection<XuiDiagnostic> diagnostics)
        {
            if (TryRuntimeValue(name, out string? runtime))
            {
                if (XuiValueParser.TryBoolean(runtime, out bool runtimeBoolean))
                {
                    return runtimeBoolean ? 1 : 0;
                }

                if (XuiValueParser.TryNumber(runtime, out double runtimeNumber))
                {
                    return runtimeNumber;
                }

                Invalid(name, runtime, "boolean or number", diagnostics);
                return fallback;
            }

            if (_overrides is not null &&
                _overrides.TryGetValue(name, out XuiAnimatedValue? animated))
            {
                if (animated.Kind == XuiTimelineValueKind.Number)
                {
                    return animated.Number;
                }

                if (animated.Kind == XuiTimelineValueKind.Boolean)
                {
                    return animated.Boolean ? 1 : 0;
                }
            }

            string value = Text(name);
            if (value.Length == 0)
            {
                return fallback;
            }

            if (XuiValueParser.TryBoolean(value, out bool boolean))
            {
                return boolean ? 1 : 0;
            }

            if (XuiValueParser.TryNumber(value, out double number))
            {
                return number;
            }

            Invalid(name, value, "boolean or number", diagnostics);
            return fallback;
        }

        public XuiVector3 Vector3(
            string name,
            XuiVector3 fallback,
            ICollection<XuiDiagnostic> diagnostics)
        {
            if (TryRuntimeValue(name, out string? runtime))
            {
                if (XuiValueParser.TryVector3(
                        runtime,
                        out XuiVector3 runtimeResult))
                {
                    return runtimeResult;
                }

                if (XuiValueParser.TryVector2(
                        runtime,
                        out XuiVector2 runtimeResult2))
                {
                    return new XuiVector3(
                        runtimeResult2.X,
                        runtimeResult2.Y,
                        fallback.Z);
                }

                Invalid(name, runtime, "2D or 3D vector", diagnostics);
                return fallback;
            }

            if (_overrides is not null &&
                _overrides.TryGetValue(name, out XuiAnimatedValue? animated))
            {
                if (animated.Kind == XuiTimelineValueKind.Vector3)
                {
                    return animated.Vector3;
                }

                if (animated.Kind == XuiTimelineValueKind.Vector2)
                {
                    return new XuiVector3(
                        animated.Vector2.X,
                        animated.Vector2.Y,
                        fallback.Z);
                }
            }

            string value = Text(name);
            if (value.Length == 0)
            {
                return fallback;
            }

            if (XuiValueParser.TryVector3(value, out XuiVector3 result))
            {
                return result;
            }

            if (XuiValueParser.TryVector2(value, out XuiVector2 result2))
            {
                return new XuiVector3(result2.X, result2.Y, fallback.Z);
            }

            Invalid(name, value, "2D or 3D vector", diagnostics);
            return fallback;
        }

        public XuiColor Color(
            string name,
            XuiColor fallback,
            ICollection<XuiDiagnostic> diagnostics)
        {
            if (TryRuntimeValue(name, out string? runtime))
            {
                if (XuiValueParser.TryColor(runtime, out XuiColor runtimeResult))
                {
                    return runtimeResult;
                }

                Invalid(name, runtime, "ARGB color", diagnostics);
                return fallback;
            }

            if (_overrides is not null &&
                _overrides.TryGetValue(name, out XuiAnimatedValue? animated) &&
                animated.Kind == XuiTimelineValueKind.Color)
            {
                return animated.Color;
            }

            string value = Text(name);
            if (value.Length == 0)
            {
                return fallback;
            }

            if (XuiValueParser.TryColor(value, out XuiColor result))
            {
                return result;
            }

            Invalid(name, value, "ARGB color", diagnostics);
            return fallback;
        }

        public double RotationDegrees(ICollection<XuiDiagnostic> diagnostics)
        {
            if (TryRuntimeValue("Rotation", out string? runtime))
            {
                return RotationDegreesFromText(runtime, diagnostics);
            }

            if (_overrides is not null &&
                _overrides.TryGetValue("Rotation", out XuiAnimatedValue? animated))
            {
                return animated.Kind switch
                {
                    XuiTimelineValueKind.Number => animated.Number,
                    XuiTimelineValueKind.Vector3 => animated.Vector3.Z,
                    XuiTimelineValueKind.Quaternion =>
                        animated.Quaternion.ZRotationDegrees,
                    _ => 0,
                };
            }

            string value = Text("Rotation");
            if (value.Length == 0)
            {
                return 0;
            }

            return RotationDegreesFromText(value, diagnostics);
        }

        private double RotationDegreesFromText(
            string value,
            ICollection<XuiDiagnostic> diagnostics)
        {
            if (XuiValueParser.TryQuaternion(value, out XuiQuaternion quaternion))
            {
                return quaternion.ZRotationDegrees;
            }

            if (XuiValueParser.TryVector3(value, out XuiVector3 rotation))
            {
                return rotation.Z;
            }

            if (XuiValueParser.TryNumber(value, out double degrees))
            {
                return degrees;
            }

            Invalid("Rotation", value, "quaternion or angle", diagnostics);
            return 0;
        }

        private bool TryRuntimeValue(
            string name,
            out string value)
        {
            if (_runtimeOverrides is not null &&
                _runtimeOverrides.TryGetValue(name, out string? runtime))
            {
                value = runtime;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private void Invalid(
            string name,
            string value,
            string expected,
            ICollection<XuiDiagnostic> diagnostics) =>
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LAYOUT005",
                XuiDiagnosticSeverity.Error,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Property {0} has invalid {1} value '{2}'. The raw value is preserved.",
                    name,
                    expected,
                    value),
                _syntax.Span,
                _syntax.Key));
    }
}
