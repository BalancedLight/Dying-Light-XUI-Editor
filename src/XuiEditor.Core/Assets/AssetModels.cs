using XuiEditor.Core.Animation;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Assets;

public enum XuiAssetRootKind
{
    Workspace,
    LooseMod,
    ExtractedDyingLight,
}

public sealed record XuiAssetRoot(
    string Path,
    XuiAssetRootKind Kind,
    bool IsReadOnly)
{
    public bool EffectiveIsReadOnly =>
        IsReadOnly || Kind == XuiAssetRootKind.ExtractedDyingLight;

    public string FullPath { get; } =
        System.IO.Path.TrimEndingDirectorySeparator(
            System.IO.Path.GetFullPath(Path));
}

public enum XuiTexturePrimitive
{
    Whole,
    Rectangle,
    RectangleWithCorner,
    TileSet,
}

public enum XuiTileRole
{
    CornerTopLeft,
    CornerTopRight,
    CornerBottomLeft,
    CornerBottomRight,
    Top,
    Bottom,
    Left,
    Right,
    Middle,
}

public sealed record XuiTilePart(
    XuiTileRole Role,
    string RegionName,
    int Probability,
    int RotationMode);

public sealed record XuiTextureRegion(
    string Name,
    string TextureFile,
    int TextureWidth,
    int TextureHeight,
    XuiRect SourceRectangle,
    XuiTexturePrimitive Primitive,
    XuiVector2 CornerSize,
    IReadOnlyList<XuiTilePart> TileParts,
    string DefinitionPath);

public sealed record ResolvedTileTexturePart(
    XuiTileRole Role,
    string RegionName,
    int Probability,
    int RotationMode,
    int Width,
    int Height,
    byte[] BgraPixels,
    string SourcePath,
    string ContentHash);

public sealed record ResolvedTexture(
    string Name,
    int Width,
    int Height,
    byte[] BgraPixels,
    XuiTextureRegion Definition,
    string SourcePath,
    string ContentHash,
    bool IsApproximation,
    IReadOnlyList<XuiDiagnostic> Diagnostics)
{
    public IReadOnlyList<ResolvedTileTexturePart> TileParts { get; init; } = [];
}

public sealed record XuiFontDefinition(
    string EngineId,
    string Family,
    double BaseSize,
    int StyleFlags,
    double HeightScale,
    string? TextureAlias);

public sealed record XuiFontStyle(
    string Id,
    string EngineFontId,
    double Scale,
    double Outline,
    double CharacterSpacing,
    double SpecialSignsScale);

public sealed record ResolvedFont(
    string RequestedId,
    string Family,
    double Size,
    bool IsApproximation,
    string? FontFile,
    IReadOnlyList<XuiDiagnostic> Diagnostics);

public sealed record XuiResolvedFile(
    string Path,
    XuiAssetRoot Root,
    string RelativePath);

public sealed record XuiVisualTemplate(
    string Id,
    XuiSyntaxNode Syntax,
    string Source,
    string SourcePath,
    XuiAssetRoot Root,
    XuiTimelineSet Timelines);

public interface IAssetResolver
{
    IReadOnlyList<XuiAssetRoot> Roots { get; }

    IReadOnlyList<XuiDiagnostic> Diagnostics { get; }

    Task RebuildAsync(CancellationToken cancellationToken = default);

    XuiResolvedFile? ResolveFile(string pathOrName);

    XuiTextureRegion? ResolveTextureDefinition(string imagePath);

    XuiVisualTemplate? ResolveVisual(string visualId);

    Task<ResolvedTexture?> ResolveTextureAsync(
        string imagePath,
        CancellationToken cancellationToken = default);

    ResolvedFont ResolveFont(
        string fontId,
        double requestedSize,
        IReadOnlyDictionary<string, string>? userMappings = null);
}
