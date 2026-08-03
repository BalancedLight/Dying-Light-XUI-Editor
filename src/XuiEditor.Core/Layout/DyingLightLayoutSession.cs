using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Layout;

public sealed class DyingLightLayoutSession
{
    private readonly XuiDocument _document;
    private readonly IAssetResolver? _assetResolver;
    private long _documentRevision;
    private readonly long _assetRevision;
    private readonly DyingLightLayoutCompilation _compilation;
    private TimelineAnimationCache _timelineAnimationCache;
    private readonly Dictionary<string, int> _renderNodeIndexByKey =
        new(StringComparer.Ordinal);
    private XuiRenderFrame? _previousFrame;
    private XuiTimelineEvaluationState? _previousTimelineState;
    private XuiRenderContext? _previousRenderContext;

    private DyingLightLayoutSession(
        XuiDocument document,
        IAssetResolver? assetResolver)
    {
        _document = document;
        _assetResolver = assetResolver;
        _documentRevision = document.Revision;
        _assetRevision = assetResolver?.Revision ?? 0;
        Timelines = XuiTimelineParser.Parse(document);
        TimelineScopes = XuiTimelineScopeCatalog.Build(
            document,
            Timelines);
        _timelineAnimationCache =
            new TimelineAnimationCache(TimelineScopes);
        _compilation = new DyingLightLayoutCompilation(
            document,
            assetResolver);
    }

    public XuiTimelineSet Timelines { get; private set; }

    public XuiTimelineScopeCatalog TimelineScopes { get; private set; }

    internal int CompiledNodeCount => _compilation.NodeCount;

    internal int CompiledVisualCount => _compilation.VisualCount;

    internal int CompiledMaterialProfileCount =>
        _compilation.MaterialProfileCount;

    internal int ColorControlParseCount =>
        _compilation.ColorControlParseCount;

    internal int TimelineScopeEvaluationCount =>
        _timelineAnimationCache.ScopeEvaluationCount;

    public static DyingLightLayoutSession Compile(
        XuiDocument document,
        IAssetResolver? assetResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DyingLightLayoutSession(document, assetResolver);
    }

    public bool IsCurrent(
        XuiDocument document,
        IAssetResolver? assetResolver) =>
        ReferenceEquals(_document, document) &&
        ReferenceEquals(_assetResolver, assetResolver) &&
        _documentRevision == document.Revision &&
        _assetRevision == (assetResolver?.Revision ?? 0);

    public bool TryRebindAnimationMetadata(
        XuiDocument document,
        IAssetResolver? assetResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!ReferenceEquals(_document, document) ||
            !ReferenceEquals(_assetResolver, assetResolver) ||
            _assetRevision != (assetResolver?.Revision ?? 0) ||
            !_compilation.TryRebindAfterAnimationEdit(document))
        {
            return false;
        }

        Timelines = XuiTimelineParser.Parse(document);
        TimelineScopes = XuiTimelineScopeCatalog.Build(document, Timelines);
        _timelineAnimationCache = new TimelineAnimationCache(TimelineScopes);
        _documentRevision = document.Revision;
        _previousFrame = null;
        _previousTimelineState = null;
        _previousRenderContext = null;
        _renderNodeIndexByKey.Clear();
        return true;
    }

    public XuiRenderFrame Sample(
        XuiViewport viewport,
        int tick,
        XuiRenderContext? renderContext = null) =>
        Sample(
            viewport,
            XuiTimelineEvaluationState.Synchronized(tick),
            renderContext);

    public XuiRenderFrame Sample(
        XuiViewport viewport,
        XuiTimelineEvaluationState timelineState,
        XuiRenderContext? renderContext = null) =>
        SampleWithChanges(viewport, timelineState, renderContext).Frame;

    public XuiRenderSample SampleWithChanges(
        XuiViewport viewport,
        int tick,
        XuiRenderContext? renderContext = null) =>
        SampleWithChanges(
            viewport,
            XuiTimelineEvaluationState.Synchronized(tick),
            renderContext);

    public XuiRenderSample SampleWithChanges(
        XuiViewport viewport,
        XuiTimelineEvaluationState timelineState,
        XuiRenderContext? renderContext = null)
    {
        ArgumentNullException.ThrowIfNull(timelineState);
        if (!IsCurrent(_document, _assetResolver))
        {
            throw new InvalidOperationException(
                "The compiled layout session is stale. Compile a new session after document or asset changes.");
        }

        XuiRenderContext context = EffectiveContext(renderContext);
        if (_previousFrame is not null &&
            _previousTimelineState is not null &&
            _previousFrame.Viewport == viewport &&
            SameContext(_previousRenderContext, context) &&
            IncrementalTimelineFrameEvaluator.TrySample(
                _previousFrame,
                _previousTimelineState,
                timelineState,
                TimelineScopes,
                _timelineAnimationCache,
                _compilation,
                context,
                out XuiRenderSample? incremental))
        {
            Remember(
                incremental.Frame,
                timelineState,
                context,
                rebuildRenderIndex: false);
            return incremental;
        }

        XuiRenderFrame frame = DyingLightLayoutEngine.EvaluateCompiled(
            _document,
            viewport,
            timelineState,
            Timelines,
            _timelineAnimationCache,
            _compilation,
            _assetResolver,
            context);
        IReadOnlyList<string> changedKeys =
            ChangedKeys(_previousFrame, frame);
        Remember(
            frame,
            timelineState,
            context,
            rebuildRenderIndex: true);
        return new XuiRenderSample(
            frame,
            changedKeys,
            FullEvaluationRequired: true);
    }

    public XuiPreviewStateExplanation ExplainPreviewState(
        string nodeKey,
        XuiRenderFrame frame,
        XuiTimelineEvaluationState timelineState,
        XuiRenderContext? renderContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(timelineState);

        XuiSyntaxNode? syntax = _document.SyntaxTree.FindByKey(nodeKey);
        if (syntax is null)
        {
            return new XuiPreviewStateExplanation(
                false,
                XuiPreviewStateReason.NotRendered,
                "This element no longer exists in the document.",
                nodeKey);
        }

        XuiRenderNode? renderNode = FindRenderNode(frame, nodeKey);
        if (renderNode is null)
        {
            return new XuiPreviewStateExplanation(
                false,
                XuiPreviewStateReason.NotRendered,
                "This element did not produce a preview node. It may be structural or runtime-generated.",
                nodeKey);
        }

        XuiRenderContext context = EffectiveContext(renderContext);
        CompiledXuiNode compiled = _compilation.Node(
            syntax,
            _document.Text);
        string id = compiled.Id;
        string display = DisplayName(renderNode);
        if (context.IsForceShown(id, syntax.Key) &&
            renderNode.IsShown &&
            renderNode.Opacity > 0.000001)
        {
            return new XuiPreviewStateExplanation(
                true,
                XuiPreviewStateReason.ForceShown,
                $"{display} is force-shown in the editor. Authored and animated visibility are temporarily overridden.",
                syntax.Key);
        }

        IReadOnlyDictionary<string, string>? runtime =
            context.PropertiesFor(id, syntax.Key);
        IReadOnlyDictionary<string, XuiAnimatedValue>? animated =
            _timelineAnimationCache.ForNode(
                timelineState,
                id,
                syntax.Key,
                _compilation.TimelineRecursionBarrier(syntax.Key));
        XuiTimelineScope? scope =
            TimelineScopes.ResolveForNode(syntax, _document.Text);
        int? scopeTick = scope is null
            ? null
            : timelineState.TickFor(scope.ScopeKey);

        if (!renderNode.LocalIsShown)
        {
            if (context.IsForceHidden(id, syntax.Key))
            {
                return Hidden(
                    XuiPreviewStateReason.ForceHidden,
                    $"{display} is hidden by an editor preview override.",
                    syntax.Key,
                    scope,
                    scopeTick);
            }

            if (TryRuntimeBoolean(runtime, "Show", out bool runtimeShow) &&
                !runtimeShow)
            {
                return Hidden(
                    XuiPreviewStateReason.RuntimeHidden,
                    $"{display} is hidden by the selected preview or controller runtime state.",
                    syntax.Key,
                    scope,
                    scopeTick);
            }

            if (TryAnimatedBoolean(animated, "Show", out bool animatedShow) &&
                !animatedShow)
            {
                return Hidden(
                    XuiPreviewStateReason.AnimatedHidden,
                    $"{display} is hidden by its Show animation at tick {scopeTick ?? 0}.",
                    syntax.Key,
                    scope,
                    scopeTick);
            }

            if (compiled.Properties.TryGetValue(
                    "Show",
                    out string? authoredShowRaw) &&
                XuiValueParser.TryBoolean(
                    authoredShowRaw,
                    out bool authoredShow) &&
                !authoredShow)
            {
                return Hidden(
                    XuiPreviewStateReason.AuthoredHidden,
                    $"{display} has authored Show=false.",
                    syntax.Key,
                    scope,
                    scopeTick);
            }

            return Hidden(
                XuiPreviewStateReason.RuntimeHidden,
                $"{display} is hidden by its effective visual or controller state.",
                syntax.Key,
                scope,
                scopeTick);
        }

        if (renderNode.LocalOpacity <= 0.000001)
        {
            string cause;
            if (TryRuntimeNumber(runtime, "Opacity", out double runtimeOpacity) &&
                runtimeOpacity <= 0.000001)
            {
                cause =
                    $"{display} has zero opacity in the selected preview or controller state.";
            }
            else if (TryAnimatedNumber(
                         animated,
                         "Opacity",
                         out double animatedOpacity) &&
                     animatedOpacity <= 0.000001)
            {
                cause =
                    $"{display} has zero animated opacity at tick {scopeTick ?? 0}.";
            }
            else
            {
                cause = $"{display} has zero effective opacity.";
            }

            return Hidden(
                XuiPreviewStateReason.ZeroOpacity,
                cause,
                syntax.Key,
                scope,
                scopeTick);
        }

        if (!renderNode.IsShown || renderNode.Opacity <= 0.000001)
        {
            XuiRenderNode? ancestor = renderNode.ParentKey is string parentKey
                ? FindRenderNode(frame, parentKey)
                : null;
            while (ancestor is not null)
            {
                if (!ancestor.LocalIsShown)
                {
                    return Hidden(
                        XuiPreviewStateReason.AncestorHidden,
                        $"{display} is hidden because ancestor {DisplayName(ancestor)} is hidden.",
                        ancestor.SelectionKey,
                        scope,
                        scopeTick);
                }

                if (ancestor.LocalOpacity <= 0.000001)
                {
                    return Hidden(
                        XuiPreviewStateReason.AncestorOpacity,
                        $"{display} is transparent because ancestor {DisplayName(ancestor)} has zero opacity.",
                        ancestor.SelectionKey,
                        scope,
                        scopeTick);
                }

                ancestor = ancestor.ParentKey is string nextKey
                    ? FindRenderNode(frame, nextKey)
                    : null;
            }
        }

        if (renderNode.ClipBounds is XuiRect clip &&
            !Intersects(renderNode.WorldBounds, clip))
        {
            return Hidden(
                XuiPreviewStateReason.Clipped,
                $"{display} is completely outside its effective clip.",
                syntax.Key,
                scope,
                scopeTick);
        }

        XuiRect canvas = new(
            0,
            0,
            frame.DesignSize.X,
            frame.DesignSize.Y);
        if (!Intersects(renderNode.WorldBounds, canvas))
        {
            return Hidden(
                XuiPreviewStateReason.OutsideCanvas,
                $"{display} is outside the authored canvas.",
                syntax.Key,
                scope,
                scopeTick);
        }

        return new XuiPreviewStateExplanation(
            true,
            XuiPreviewStateReason.Visible,
            $"{display} is visible in the composed preview.",
            syntax.Key,
            scope?.ScopeKey,
            scopeTick);
    }

    private XuiRenderContext EffectiveContext(
        XuiRenderContext? renderContext)
    {
        XuiRenderContext context =
            renderContext ?? new XuiRenderContext();
        return context.ControllerRuntimeProfile is null &&
               context.ApplyCommonControllerProfile &&
               _compilation.ControllerRuntimeProfile is
                   XuiControllerRuntimeProfile controllerProfile
            ? context with
            {
                ControllerRuntimeProfile = controllerProfile,
            }
            : context;
    }

    private static XuiPreviewStateExplanation Hidden(
        XuiPreviewStateReason reason,
        string summary,
        string responsibleKey,
        XuiTimelineScope? scope,
        int? scopeTick) =>
        new(
            false,
            reason,
            summary,
            responsibleKey,
            scope?.ScopeKey,
            scopeTick);

    private static string DisplayName(XuiRenderNode node) =>
        string.IsNullOrWhiteSpace(node.Id)
            ? node.ElementName
            : node.Id;

    private static bool TryRuntimeBoolean(
        IReadOnlyDictionary<string, string>? values,
        string property,
        out bool value)
    {
        value = false;
        return values?.TryGetValue(property, out string? raw) == true &&
               XuiValueParser.TryBoolean(raw, out value);
    }

    private static bool TryRuntimeNumber(
        IReadOnlyDictionary<string, string>? values,
        string property,
        out double value)
    {
        value = 0;
        return values?.TryGetValue(property, out string? raw) == true &&
               XuiValueParser.TryNumber(raw, out value);
    }

    private static bool TryAnimatedBoolean(
        IReadOnlyDictionary<string, XuiAnimatedValue>? values,
        string property,
        out bool value)
    {
        value = false;
        if (values?.TryGetValue(
                property,
                out XuiAnimatedValue? animated) != true ||
            animated is null ||
            animated.Kind != XuiTimelineValueKind.Boolean)
        {
            return false;
        }

        value = animated.Boolean;
        return true;
    }

    private static bool TryAnimatedNumber(
        IReadOnlyDictionary<string, XuiAnimatedValue>? values,
        string property,
        out double value)
    {
        value = 0;
        if (values?.TryGetValue(
                property,
                out XuiAnimatedValue? animated) != true ||
            animated is null ||
            animated.Kind != XuiTimelineValueKind.Number)
        {
            return false;
        }

        value = animated.Number;
        return true;
    }

    private static bool Intersects(XuiRect left, XuiRect right) =>
        left.Width > 0 &&
        left.Height > 0 &&
        right.Width > 0 &&
        right.Height > 0 &&
        left.X < right.Right &&
        left.Right > right.X &&
        left.Y < right.Bottom &&
        left.Bottom > right.Y;

    private void Remember(
        XuiRenderFrame frame,
        XuiTimelineEvaluationState timelineState,
        XuiRenderContext context,
        bool rebuildRenderIndex)
    {
        if (rebuildRenderIndex)
        {
            _renderNodeIndexByKey.Clear();
            for (int index = 0; index < frame.Nodes.Count; index++)
            {
                _renderNodeIndexByKey.TryAdd(
                    frame.Nodes[index].Key,
                    index);
            }
        }

        _previousFrame = frame;
        _previousTimelineState = timelineState;
        _previousRenderContext = context with
        {
            ForceShownTargets = context.ForceShownTargets is null
                ? null
                : new HashSet<string>(
                    context.ForceShownTargets,
                    StringComparer.Ordinal),
            ForceHiddenTargets = context.ForceHiddenTargets is null
                ? null
                : new HashSet<string>(
                    context.ForceHiddenTargets,
                    StringComparer.Ordinal),
        };
    }

    private XuiRenderNode? FindRenderNode(
        XuiRenderFrame frame,
        string key)
    {
        if (_renderNodeIndexByKey.TryGetValue(key, out int index) &&
            index >= 0 &&
            index < frame.Nodes.Count)
        {
            XuiRenderNode candidate = frame.Nodes[index];
            if (candidate.Key.Equals(key, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ChangedKeys(
        XuiRenderFrame? previous,
        XuiRenderFrame current)
    {
        if (previous is null ||
            previous.Nodes.Count != current.Nodes.Count)
        {
            return current.Nodes
                .Select(static node => node.Key)
                .ToArray();
        }

        List<string> changed = [];
        for (int index = 0; index < current.Nodes.Count; index++)
        {
            XuiRenderNode oldNode = previous.Nodes[index];
            XuiRenderNode newNode = current.Nodes[index];
            if (!oldNode.Key.Equals(
                    newNode.Key,
                    StringComparison.Ordinal) ||
                !Equals(oldNode, newNode))
            {
                changed.Add(newNode.Key);
            }
        }

        return changed;
    }

    private static bool SameContext(
        XuiRenderContext? previous,
        XuiRenderContext current) =>
        previous is not null &&
        Equals(previous.Scenario, current.Scenario) &&
        previous.ResolveLocalization == current.ResolveLocalization &&
        previous.ApplyCommonControllerProfile ==
        current.ApplyCommonControllerProfile &&
        Equals(
            previous.ControllerRuntimeProfile,
            current.ControllerRuntimeProfile) &&
        SetEquals(
            previous.ForceShownTargets,
            current.ForceShownTargets) &&
        SetEquals(
            previous.ForceHiddenTargets,
            current.ForceHiddenTargets);

    private static bool SetEquals(
        IReadOnlySet<string>? left,
        IReadOnlySet<string>? right)
    {
        if (left is null || left.Count == 0)
        {
            return right is null || right.Count == 0;
        }

        return right is not null && left.SetEquals(right);
    }
}
