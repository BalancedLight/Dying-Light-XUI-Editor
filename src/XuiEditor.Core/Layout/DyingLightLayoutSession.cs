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

    private DyingLightLayoutSession(
        XuiDocument document,
        IAssetResolver? assetResolver)
    {
        _document = document;
        _assetResolver = assetResolver;
        _documentRevision = document.Revision;
        _assetRevision = assetResolver?.Revision ?? 0;
        Timelines = XuiTimelineParser.Parse(document);
        _compilation = new DyingLightLayoutCompilation(
            document,
            assetResolver);
    }

    public XuiTimelineSet Timelines { get; }

    internal int CompiledNodeCount => _compilation.NodeCount;

    internal int CompiledVisualCount => _compilation.VisualCount;

    internal int CompiledMaterialProfileCount =>
        _compilation.MaterialProfileCount;

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
        XuiRenderContext? renderContext = null)
    {
        if (!IsCurrent(_document, _assetResolver))
        {
            throw new InvalidOperationException(
                "The compiled layout session is stale. Compile a new session after document or asset changes.");
        }

        return DyingLightLayoutEngine.EvaluateCompiled(
            _document,
            viewport,
            tick,
            Timelines,
            _compilation,
            _assetResolver,
            renderContext);
    }
}
