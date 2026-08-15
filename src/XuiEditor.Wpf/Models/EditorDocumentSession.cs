using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Wpf.Controls;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf.Models;

internal sealed class EditorDocumentSession : INotifyPropertyChanged, IDisposable
{
    public EditorDocumentSession(
        XuiDocument document,
        string? recoverySuggestedPath = null,
        RecoverySnapshot? activeRecovery = null,
        string? recoveryKey = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        RecoverySuggestedPath = recoverySuggestedPath;
        ActiveRecovery = activeRecovery;
        RecoveryKey = recoveryKey ?? Guid.NewGuid().ToString("N");
        Expanded.Add(document.Root.Key);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public XuiDocument Document { get; private set; }

    public string RecoveryKey { get; }

    public string? RecoverySuggestedPath { get; set; }

    public RecoverySnapshot? ActiveRecovery { get; set; }

    public bool RecoveryPending { get; set; }

    public string DisplayName =>
        Document.Path is not null
            ? Path.GetFileName(Document.Path)
            : RecoverySuggestedPath is not null
                ? Path.GetFileName(RecoverySuggestedPath)
                : Document.DisplayName;

    public string Header => Document.IsDirty ? $"● {DisplayName}" : DisplayName;

    public string Location =>
        Document.Path ??
        RecoverySuggestedPath ??
        SourceLocation(Document.Source) ??
        DisplayName;

    public string Identity =>
        Document.Path is not null
            ? $"file:{Path.GetFullPath(Document.Path)}"
            : Document.Source is { } source
                ? $"source:{source.Origin}|{source.VirtualPath}|{source.DisplayName}"
                : $"session:{RecoveryKey}";

    public HashSet<string> Expanded { get; } = new(StringComparer.Ordinal);

    public HashSet<string> SelectedKeys { get; } = new(StringComparer.Ordinal);

    public HashSet<string> HiddenKeys { get; } = new(StringComparer.Ordinal);

    public HashSet<string>? HiddenKeysBeforeIsolation { get; set; }

    public HashSet<string> ForceShownKeys { get; } = new(StringComparer.Ordinal);

    public HashSet<string> LockedKeys { get; } = new(StringComparer.Ordinal);

    public HashSet<string>? ExpansionBeforeFilter { get; set; }

    public bool FilterActive { get; set; }

    public string HierarchyFilter { get; set; } = string.Empty;

    public string? LastHierarchyFilter { get; set; }

    public string? SelectedNamedFrameKey { get; set; }

    public string? RawXmlLoadedNodeKey { get; set; }

    public long RawXmlLoadedRevision { get; set; } = -1;

    public DyingLightAssetResolver? AssetResolver { get; set; }

    public DyingLightXuiAssetCatalog? AssetCatalog { get; set; }

    public DyingLightLayoutSession? LayoutSession { get; set; }

    public HierarchyIndex? HierarchyIndex { get; set; }

    public XuiTimelineSet? TimelineSet { get; set; }

    public XuiTimelineWorkspace? TimelineWorkspace { get; set; }

    public IReadOnlyList<XuiDiagnostic> AllDiagnostics { get; set; } = [];

    public IReadOnlyList<XuiDiagnostic> EvaluationDiagnostics { get; set; } = [];

    public bool EvaluationDiagnosticsInitialized { get; set; }

    public Dictionary<string, IReadOnlyList<XuiDiagnostic>> TextureDiagnostics
        { get; } = new(StringComparer.Ordinal);

    public XuiViewportViewState? ViewportState { get; set; }

    public bool AssetStateStale { get; set; } = true;

    public int AssetBuildVersion { get; set; }

    public void RefreshChrome()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(Identity));
    }

    public bool MatchesPath(string path) =>
        Document.Path is not null &&
        string.Equals(
            Path.GetFullPath(Document.Path),
            Path.GetFullPath(path),
            StringComparison.OrdinalIgnoreCase);

    public XuiDocument ReplaceDocument(XuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        XuiDocument previous = Document;
        Document = document;
        Expanded.Clear();
        Expanded.Add(document.Root.Key);
        SelectedKeys.Clear();
        HiddenKeys.Clear();
        HiddenKeysBeforeIsolation = null;
        ForceShownKeys.Clear();
        LockedKeys.Clear();
        ExpansionBeforeFilter = null;
        FilterActive = false;
        LastHierarchyFilter = null;
        SelectedNamedFrameKey = null;
        RawXmlLoadedNodeKey = null;
        RawXmlLoadedRevision = -1;
        AssetResolver = null;
        AssetCatalog = null;
        LayoutSession = null;
        HierarchyIndex = null;
        TimelineSet = null;
        TimelineWorkspace = null;
        AllDiagnostics = [];
        EvaluationDiagnostics = [];
        EvaluationDiagnosticsInitialized = false;
        TextureDiagnostics.Clear();
        AssetStateStale = true;
        AssetBuildVersion++;
        RecoveryPending = false;
        RefreshChrome();
        return previous;
    }

    public void Dispose()
    {
    }

    private static string? SourceLocation(XuiDocumentSource? source)
    {
        if (source is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(source.VirtualPath)
            ? source.Origin
            : $"{source.Origin} · {source.VirtualPath}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal enum DocumentSaveOutcome
{
    Saved,
    Unchanged,
    Cancelled,
    Failed,
}

internal sealed record SaveAllResult(
    int Saved,
    int Unchanged,
    int Cancelled,
    int Failed,
    int RemainingDirty);
