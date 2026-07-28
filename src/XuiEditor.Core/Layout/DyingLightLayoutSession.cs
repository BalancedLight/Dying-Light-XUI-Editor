using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;

namespace XuiEditor.Core.Layout;

public sealed class DyingLightLayoutSession
{
    private readonly XuiDocument _document;
    private readonly IAssetResolver? _assetResolver;
    private readonly long _documentRevision;
    private readonly long _assetRevision;
    private readonly DyingLightLayoutCompilation _compilation;
    private readonly TimelineAnimationCache _timelineAnimationCache;
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

    public XuiTimelineSet Timelines { get; }

    public XuiTimelineScopeCatalog TimelineScopes { get; }

    internal int CompiledNodeCount => _compilation.NodeCount;

    internal int CompiledVisualCount => _compilation.VisualCount;

    internal int CompiledMaterialProfileCount =>
        _compilation.MaterialProfileCount;

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
            Remember(incremental.Frame, timelineState, context);
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
        Remember(frame, timelineState, context);
        return new XuiRenderSample(
            frame,
            changedKeys,
            FullEvaluationRequired: true);
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

    private void Remember(
        XuiRenderFrame frame,
        XuiTimelineEvaluationState timelineState,
        XuiRenderContext context)
    {
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
