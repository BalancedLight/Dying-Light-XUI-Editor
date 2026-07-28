using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Values;
using XuiEditor.Wpf.Controls;
using XuiEditor.Wpf.Models;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class MainWindow : Window, IDisposable
{
    private const string MixedValue = "— mixed —";
    private const int AutomaticRawXmlCharacterLimit = 256 * 1024;
    private static readonly HashSet<string> KnownProperties = new(
        StringComparer.Ordinal)
    {
        "Id", "Width", "Height", "Position", "Anchor", "Pivot", "Scale",
        "Rotation", "Opacity", "Show", "Color", "TextColor", "OutlineColor",
        "DefaultFontColor", "ImagePath", "Material", "Text", "Font",
        "DefaultFont", "PointSize", "TextStyle", "Visual", "ClassOverride",
        "NavUp", "NavDown", "NavLeft", "NavRight", "NavTabForward",
        "NavTabBackward", "ClipChildren", "UseMask", "MaskSource",
        "ForceMaterials", "ImageMaskMaterial", "TextMaskMaterial",
        "AARectangleMaskMaterial",
        "KeepPosX", "KeepPosY", "KeepWidth", "KeepHeight",
        "KeepPosXOnParentSizeChange", "KeepPosYOnParentSizeChange",
        "KeepWidthOnParentSizeChange", "KeepHeightOnParentSizeChange",
        "KeepPosXOnResolutionChange", "KeepPosYOnResolutionChange",
        "KeepWidthOnResolutionChange", "KeepHeightOnResolutionChange",
        "HoldAspectRatio", "HoldAspectRatioX", "HoldAspectPivotPosition",
        "RoundPosition", "DisableTimelineRecursion", "UseScreenTransform",
        "ScaleWidthByResolution", "ScaleHeightByResolution",
        "Outline", "TextProgress", "Const0",
        "Const1", "Shadow", "DataAssociation", "ContentVerticalAlign",
        "ContentHorizontalAlign", "MarginLeft", "MarginTop", "MarginRight",
        "MarginBottom", "SizeMode", "Uppercase", "AutoAdjustWidth",
        "AutoAdjustHeight", "ClipMaskChannel",
        "ContentVerticalAlign", "DefaultHorizontalAlign",
        "DefaultVerticalAlign", "ContentHorizontalBorder",
        "ContentVerticalBorder", "SourceString", "MultiLine",
        "VerticalAlignDown", "OutlineSize", "ShadowColor",
        "DropShadowColor", "ShadowOffset", "Bold", "Italic", "Underline",
        "Strike", "CharacterSpacingAdjust", "LineSpacingAdjust",
        "AutoSizeToText", "AutoSizeParentToText",
        "MultilineAutoSizeHeight", "ClipText",
        "ColorControlSequenceEnabled",
    };
    private static readonly HashSet<string> BooleanProperties = new(
        StringComparer.Ordinal)
    {
        "Show", "ClipChildren", "UseMask", "ForceMaterials",
        "KeepPosX", "KeepPosY",
        "KeepWidth", "KeepHeight", "KeepPosXOnParentSizeChange",
        "KeepPosYOnParentSizeChange", "KeepWidthOnParentSizeChange",
        "KeepHeightOnParentSizeChange", "KeepPosXOnResolutionChange",
        "KeepPosYOnResolutionChange", "KeepWidthOnResolutionChange",
        "KeepHeightOnResolutionChange", "HoldAspectRatio",
        "HoldAspectRatioX", "HoldAspectPivotPosition", "RoundPosition",
        "DisableTimelineRecursion", "UseScreenTransform",
        "ScaleWidthByResolution", "ScaleHeightByResolution",
        "Uppercase", "AutoAdjustWidth",
        "AutoAdjustHeight", "MultiLine", "VerticalAlignDown", "Bold",
        "Italic", "Underline", "Strike", "AutoSizeToText",
        "AutoSizeParentToText", "MultilineAutoSizeHeight", "ClipText",
        "ColorControlSequenceEnabled",
    };
    private static readonly HashSet<string> NumberProperties = new(
        StringComparer.Ordinal)
    {
        "Width", "Height", "Opacity", "PointSize", "Outline",
        "TextProgress", "Const0", "Const1", "Shadow", "MarginLeft",
        "MarginTop", "MarginRight", "MarginBottom", "OutlineSize",
        "ShadowOffset", "ContentHorizontalBorder", "ContentVerticalBorder",
        "CharacterSpacingAdjust", "LineSpacingAdjust",
    };
    private static readonly HashSet<string> ColorProperties = new(
        StringComparer.Ordinal)
    {
        "Color", "TextColor", "OutlineColor", "DefaultFontColor",
        "ShadowColor", "DropShadowColor",
    };
    private readonly EditorSettings _settings;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenKeys = new(StringComparer.Ordinal);
    private HashSet<string>? _hiddenKeysBeforeIsolation;
    private readonly HashSet<string> _forceShownKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _lockedKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HierarchyRow> _visibleHierarchyRows =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<XuiDiagnostic>>
        _textureDiagnostics = new(StringComparer.Ordinal);
    private IReadOnlyList<XuiDiagnostic> _evaluationDiagnostics = [];
    private bool _evaluationDiagnosticsInitialized;
    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _recoveryTimer;
    private readonly DispatcherTimer _hierarchySearchTimer;
    private readonly Stopwatch _playbackClock = new();
    private XuiDocument? _document;
    private DyingLightInstallIndex? _installIndex;
    private DyingLightAssetResolver? _assetResolver;
    private DyingLightLayoutSession? _layoutSession;
    private HierarchyIndex? _hierarchyIndex;
    private string? _lastHierarchyFilter;
    private XuiTimelineSet? _timelineSet;
    private XuiTimelineWorkspace? _timelineWorkspace;
    private FileSystemWatcher? _watcher;
    private bool _syncingSelection;
    private bool _updatingTick;
    private bool _updatingTimelineEditors;
    private bool _suppressRefresh;
    private bool _refreshPending;
    private bool _filterActive;
    private HashSet<string>? _expansionBeforeFilter;
    private bool _isPlaying;
    private double _playbackRemainder;
    private long _layoutEvaluationCount;
    private string? _copiedKeyFrameXml;
    private string? _selectedNamedFrameKey;
    private string? _rawXmlLoadedNodeKey;
    private long _rawXmlLoadedRevision = -1;
    private DateTime _ignoreWatcherUntilUtc;
    private bool _allowClose;
    private string? _recoverySuggestedPath;
    private RecoverySnapshot? _activeRecovery;
    private bool _disposed;

    private int CurrentTimelineTick =>
        _timelineWorkspace?.ActiveTick ?? 0;

    public MainWindow()
    {
        _settings = EditorSettingsStore.Load();
        HierarchyRows = [];
        InspectorProperties = [];
        FilteredDiagnostics = [];
        InitializeComponent();
        DataContext = this;
        PreviewScenarioCombo.ItemsSource = XuiPreviewScenarioCatalog.Defaults;
        PreviewScenarioCombo.SelectedItem =
            XuiPreviewScenarioCatalog.Defaults.FirstOrDefault(scenario =>
                scenario.Id.Equals(
                    _settings.PreviewScenarioId,
                    StringComparison.Ordinal)) ??
            XuiPreviewScenario.Empty;
        ReferenceOpacitySlider.Value = _settings.ReferenceOverlayOpacity;

        Viewport.TextureDiagnosticsAvailable +=
            Viewport_TextureDiagnosticsAvailable;

        _playbackTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            PlaybackTimer_Tick,
            Dispatcher)
        {
            IsEnabled = false,
        };
        _recoveryTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            RecoveryTimer_Tick,
            Dispatcher)
        {
            IsEnabled = false,
        };
        _hierarchySearchTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(150),
            DispatcherPriority.Background,
            (timer, _) =>
            {
                ((DispatcherTimer)timer!).Stop();
                ApplyHierarchyFilter();
            },
            Dispatcher)
        {
            IsEnabled = false,
        };
    }

    public BatchObservableCollection<HierarchyRow> HierarchyRows { get; }

    public BatchObservableCollection<InspectorPropertyRow> InspectorProperties { get; }

    public BatchObservableCollection<XuiDiagnostic> FilteredDiagnostics { get; }

    internal IReadOnlyCollection<string> ExpandedKeysForTesting => _expanded;

    internal IReadOnlyCollection<string> SelectedKeysForTesting => _selectedKeys;

    internal XuiViewportControl ViewportForTesting => Viewport;

    internal TimelineEditorControl TimelineForTesting => TimelineEditor;

    internal XuiTimelineWorkspace? TimelineWorkspaceForTesting =>
        _timelineWorkspace;

    internal string TimelineScopeLabelForTesting =>
        TimelineScopeText.Text;

    internal int NamedFrameCountForTesting =>
        NamedFrameComboBox.Items.Count;

    internal bool TimelineEditingEnabledForTesting =>
        TimelineTransportPanel.IsEnabled &&
        TimelineEditPanel.IsEnabled &&
        TimelineEditor.IsEnabled;

    internal long LayoutEvaluationCountForTesting =>
        _layoutEvaluationCount;

    internal bool RawXmlMaterializedForTesting =>
        _rawXmlLoadedNodeKey is not null;

    internal string RawXmlStatusForTesting => RawXmlStatusText.Text;

    internal string PreviewStateForTesting => PreviewStateText.Text;

    internal HierarchyRow? HierarchyRowForTesting(string nodeKey) =>
        _hierarchyIndex?.FindRow(nodeKey);

    internal XuiRenderContext PreviewRenderContextForTesting =>
        BuildRenderContext();

    internal void SetPreviewScenarioForTesting(string scenarioId)
    {
        PreviewScenarioCombo.SelectedItem =
            XuiPreviewScenarioCatalog.Defaults.Single(scenario =>
                scenario.Id.Equals(scenarioId, StringComparison.Ordinal));
    }

    internal ListBox HierarchyListForTesting => HierarchyList;

    internal (double Hierarchy, double Inspector, double Timeline)
        PaneSizesForTesting =>
        (
            HierarchyColumn.ActualWidth,
            InspectorColumn.ActualWidth,
            TimelineRow.ActualHeight
        );

    internal void AttachDocumentForTesting(XuiDocument document)
    {
        AttachDocument(document);
        RefreshAll();
    }

    internal void SetAssetResolverForTesting(
        DyingLightAssetResolver resolver)
    {
        _assetResolver = resolver ??
                         throw new ArgumentNullException(nameof(resolver));
        _layoutSession = null;
        Viewport.SetAssetResolver(resolver);
        RefreshEvaluation();
    }

    internal void SetHierarchyExpansionForTesting(string nodeKey, bool expanded)
    {
        if (expanded)
        {
            _expanded.Add(nodeKey);
        }
        else
        {
            _expanded.Remove(nodeKey);
        }

        BuildHierarchy();
    }

    internal void SetHierarchyFilterForTesting(string filter)
    {
        HierarchySearch.Text = filter;
        _hierarchySearchTimer.Stop();
        ApplyHierarchyFilter();
    }

    internal void SetEditorHiddenForTesting(string nodeKey, bool hidden)
    {
        _hiddenKeysBeforeIsolation = null;
        if (hidden)
        {
            _hiddenKeys.Add(nodeKey);
        }
        else
        {
            _hiddenKeys.Remove(nodeKey);
        }

        _hierarchyIndex?.UpdateEditorStates(_hiddenKeys, _lockedKeys);
        Viewport.SetHiddenKeys(EditorHiddenKeys());
        Viewport.SetLockedKeys(EditorLockedKeys());
    }

    internal void IsolateHierarchyForTesting(string nodeKey) =>
        IsolateHierarchy(nodeKey);

    internal void RestoreHierarchyIsolationForTesting() =>
        RestoreHierarchyIsolation();

    internal void SetEditorLockedForTesting(string nodeKey, bool locked)
    {
        if (locked)
        {
            _lockedKeys.Add(nodeKey);
        }
        else
        {
            _lockedKeys.Remove(nodeKey);
        }

        _hierarchyIndex?.UpdateEditorStates(_hiddenKeys, _lockedKeys);
        Viewport.SetLockedKeys(EditorLockedKeys());
    }

    internal void SetRawXmlExpandedForTesting(
        bool expanded,
        bool loadLarge = false)
    {
        RawXmlExpander.IsExpanded = expanded;
        if (expanded)
        {
            RefreshRawXmlEditor(allowLarge: loadLarge);
        }
    }

    internal void SelectNodeKeysForTesting(IEnumerable<string> nodeKeys)
    {
        _selectedKeys.Clear();
        _selectedKeys.UnionWith(nodeKeys);
        if (EnsureSelectedAncestorsExpanded())
        {
            BuildHierarchy();
        }

        SelectRowsFromKeys();
        UpdateSelectionSurfaces();
    }

    internal void SetInspectorBooleanForTesting(
        string propertyName,
        bool value)
    {
        InspectorPropertyRow row =
            InspectorProperties.Single(property =>
                property.Name.Equals(
                    propertyName,
                    StringComparison.Ordinal));
        row.BooleanValue = value;
        CommitInspectorValue(row);
    }

    internal void SetAllInScopeForTesting(bool enabled)
    {
        AllTracksToggle.IsChecked = enabled;
        UpdateTimelineData();
    }

    internal void SetTimelineTickForTesting(int tick) =>
        SetCurrentTick(tick);

    internal void GoToNamedFrameForTesting(string name)
    {
        NamedFrameComboBox.SelectedItem =
            (_timelineWorkspace?.ActiveScope?.NamedFrames ?? [])
            .Single(frame =>
                frame.Name.Equals(name, StringComparison.Ordinal));
        GoToNamedFrame_Click(this, new RoutedEventArgs());
    }

    internal void CommitTransformForTesting(
        XuiTransformCommittedEventArgs eventArgs) =>
        Viewport_TransformCommitted(this, eventArgs);

    internal void AddChildForTesting(
        string parentKey,
        XuiElementCreationRequest request)
    {
        if (_document?.SyntaxTree.FindByKey(parentKey) is
            not XuiSyntaxNode parent)
        {
            throw new InvalidOperationException(
                "The requested parent does not exist.");
        }

        InsertVisualChild(parent, request);
    }

    internal void ApplyTextureDiagnosticsForTesting(
        string imagePath,
        IReadOnlyList<XuiDiagnostic> diagnostics) =>
        Viewport_TextureDiagnosticsAvailable(
            this,
            new XuiTextureDiagnosticsEventArgs(
                imagePath,
                diagnostics));

    private async void Window_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);
        HierarchyColumn.Width = new GridLength(
            Math.Max(180, _settings.HierarchyWidth));
        InspectorColumn.Width = new GridLength(
            Math.Max(240, _settings.InspectorWidth));
        TimelineRow.Height = new GridLength(
            Math.Max(150, _settings.TimelineHeight));
        GridMenuItem.IsChecked = _settings.ShowGrid;
        SafeAreaMenuItem.IsChecked = _settings.ShowSafeArea;
        BoundsMenuItem.IsChecked = _settings.ShowUnknownBounds;
        SnapMenuItem.IsChecked = _settings.SnapEnabled;
        ToolbarSnap.IsChecked = _settings.SnapEnabled;
        ApplyViewportSettings();
        RebuildRecentFilesMenu();
        await EnsureInstallIndexAsync(showErrors: false).ConfigureAwait(true);

        string? commandLineFile = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(static argument =>
                argument.EndsWith(".xui", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(argument));
        if (commandLineFile is not null)
        {
            await OpenDocumentAsync(commandLineFile).ConfigureAwait(true);
            return;
        }

        IReadOnlyList<RecoverySnapshot> snapshots = RecoveryService.Find();
        if (snapshots.Count > 0)
        {
            RecoverySnapshot latest = snapshots[0];
            MessageBoxResult recover = MessageBox.Show(
                this,
                $"A recovery snapshot from {latest.TimestampUtc.ToLocalTime():g} is available.\n\n" +
                $"{latest.OriginalPath ?? "Untitled document"}\n\nOpen it as an unsaved document?",
                "Recover unsaved XUI",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (recover == MessageBoxResult.Yes)
            {
                await OpenRecoveryAsync(latest).ConfigureAwait(true);
            }
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (!await ConfirmDiscardAsync().ConfigureAwait(true))
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = "Open Dying Light XUI",
            Filter = "Dying Light XUI (*.xui)|*.xui|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenDocumentAsync(dialog.FileName).ConfigureAwait(true);
        }
    }

    private async void OpenStock_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (!await ConfirmDiscardAsync().ConfigureAwait(true))
        {
            return;
        }

        if (!await EnsureInstallIndexAsync(showErrors: true).ConfigureAwait(true) ||
            _installIndex is null)
        {
            return;
        }

        StockXuiBrowserWindow browser = new(_installIndex)
        {
            Owner = this,
        };
        if (browser.ShowDialog() == true &&
            browser.SelectedEntry is XuiAssetEntry entry)
        {
            await OpenAssetDocumentAsync(entry).ConfigureAwait(true);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs eventArgs)
    {
        await SaveDocumentAsync(forceSaveAs: false).ConfigureAwait(true);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs eventArgs)
    {
        await SaveDocumentAsync(forceSaveAs: true).ConfigureAwait(true);
    }

    private async void AssetRoots_Click(object sender, RoutedEventArgs eventArgs)
    {
        string? priorInstall = _settings.DyingLightInstallPath;
        string priorLocale = _settings.Locale;
        AssetRootsWindow dialog = new(_settings)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await EditorSettingsStore.SaveAsync(_settings).ConfigureAwait(true);
        if (!string.Equals(
                priorInstall,
                _settings.DyingLightInstallPath,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                priorLocale,
                _settings.Locale,
                StringComparison.OrdinalIgnoreCase))
        {
            _installIndex = null;
        }

        await EnsureInstallIndexAsync(showErrors: false).ConfigureAwait(true);
        if (_document is not null)
        {
            await RebuildAssetResolverAsync().ConfigureAwait(true);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs eventArgs) => Close();

    private void Undo_Click(object sender, RoutedEventArgs eventArgs)
    {
        _document?.Undo();
    }

    private void Redo_Click(object sender, RoutedEventArgs eventArgs)
    {
        _document?.Redo();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_document is null)
        {
            return;
        }

        List<XuiSyntaxNode> nodes = SelectedNodes()
            .Where(node => node != _document.Root)
            .OrderByDescending(static node => node.Start)
            .ToList();
        ExecuteBatch(() =>
        {
            foreach (XuiSyntaxNode original in nodes)
            {
                XuiSyntaxNode? current = FindNodeAtStart(original.Start);
                if (current is not null)
                {
                    _document.Execute(
                        XuiCommandFactory.DuplicateElement(_document, current));
                }
            }
        });
    }

    private void AddChild_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            SelectedNodes() is not [XuiSyntaxNode parent])
        {
            StatusText.Text =
                "Select exactly one hierarchy element to add a child.";
            return;
        }

        if (IsLocked(parent.Key))
        {
            StatusText.Text =
                $"{DisplayNode(parent)} is locked in the editor.";
            return;
        }

        AddXuiElementWindow dialog = new(
            DisplayNode(parent),
            RenderedOrAuthoredSize(parent),
            SuggestedUniqueId)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true ||
            dialog.Request is not XuiElementCreationRequest request)
        {
            return;
        }

        try
        {
            InsertVisualChild(parent, request);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            InvalidOperationException or
            ArgumentException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Could not add XUI child",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Add child failed safely";
        }
    }

    private void InsertVisualChild(
        XuiSyntaxNode parent,
        XuiElementCreationRequest request)
    {
        if (_document is null)
        {
            throw new InvalidOperationException(
                "No XUI document is open.");
        }

        string parentKey = parent.Key;
        string raw = XuiElementFactory.CreateXml(
            request,
            _document.Format.NewLine);
        string createdId = CreatedElementId(raw);
        _document.Execute(XuiCommandFactory.InsertVisualChildXml(
            _document,
            parent,
            raw,
            $"Add {createdId}"));

        XuiSyntaxNode? created = _document.Root
            .DescendantsAndSelf()
            .Where(static node =>
                node.Kind == XuiSyntaxKind.Element &&
                !XuiModelReader.IsStructural(node))
            .LastOrDefault(node =>
                string.Equals(
                    XuiModelReader.GetId(node, _document.Text),
                    createdId,
                    StringComparison.Ordinal));
        _expanded.Add(parentKey);
        if (created is not null)
        {
            _selectedKeys.Clear();
            _selectedKeys.Add(created.Key);
            EnsureSelectedAncestorsExpanded();
        }

        BuildHierarchy();
        SelectRowsFromKeys(scrollIntoView: true);
        UpdateSelectionSurfaces();
        StatusText.Text = _document.Source?.IsReadOnly == true
            ? $"Added {createdId} in memory. Use Save As to create a writable mod copy."
            : $"Added {createdId}";
    }

    private string CreatedElementId(string raw)
    {
        if (_document is null)
        {
            throw new InvalidOperationException(
                "No XUI document is open.");
        }

        XuiSyntaxTree fragment = new XuiSyntaxParser().Parse(
            raw.ReplaceLineEndings(_document.Format.NewLine),
            _document.Format);
        string? id = XuiModelReader.GetId(
            fragment.Root,
            fragment.Source);
        return string.IsNullOrWhiteSpace(id)
            ? throw new InvalidDataException(
                "A new visual element requires a Properties/Id value.")
            : id;
    }

    private string SuggestedUniqueId(XuiElementPreset preset)
    {
        string prefix = XuiElementFactory.SuggestedIdPrefix(preset);
        if (_document is null)
        {
            return prefix;
        }

        HashSet<string> ids = _document.Root
            .DescendantsAndSelf()
            .Select(node => XuiModelReader.GetId(node, _document.Text))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (!ids.Contains(prefix))
        {
            return prefix;
        }

        for (int suffix = 2; suffix < 100_000; suffix++)
        {
            string candidate = prefix +
                               suffix.ToString(
                                   CultureInfo.InvariantCulture);
            if (!ids.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not allocate a unique Id beginning with {prefix}.");
    }

    private XuiVector2 RenderedOrAuthoredSize(XuiSyntaxNode parent)
    {
        XuiRenderNode? rendered = Viewport.FrameForTesting?.Nodes
            .FirstOrDefault(node =>
                node.Key.Equals(
                    parent.Key,
                    StringComparison.Ordinal));
        if (rendered is not null &&
            rendered.Size.X > 0 &&
            rendered.Size.Y > 0)
        {
            return rendered.Size;
        }

        double width = 0;
        double height = 0;
        if (_document is not null)
        {
            XuiValueParser.TryNumber(
                XuiModelReader.GetPropertyValue(
                    parent,
                    _document.Text,
                    "Width"),
                out width);
            XuiValueParser.TryNumber(
                XuiModelReader.GetPropertyValue(
                    parent,
                    _document.Text,
                    "Height"),
                out height);
        }

        if (width <= 0 || height <= 0)
        {
            return IsCanvasRoot(parent)
                ? new XuiVector2(1280, 720)
                : new XuiVector2(
                    Math.Max(320, width),
                    Math.Max(180, height));
        }

        return new XuiVector2(width, height);
    }

    private void Delete_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_document is null)
        {
            return;
        }

        List<XuiSyntaxNode> nodes = SelectedNodes()
            .Where(node => node != _document.Root)
            .OrderByDescending(static node => node.Start)
            .ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        ExecuteBatch(() =>
        {
            foreach (XuiSyntaxNode original in nodes)
            {
                XuiSyntaxNode? current = FindNodeAtStart(original.Start);
                if (current is not null)
                {
                    _document.Execute(
                        XuiCommandFactory.RemoveElement(_document, current));
                }
            }
        });
        _selectedKeys.Clear();
        RefreshAll();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs eventArgs) =>
        MoveSelected(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs eventArgs) =>
        MoveSelected(1);

    private void Indent_Click(object sender, RoutedEventArgs eventArgs) =>
        IndentSelected();

    private void Outdent_Click(object sender, RoutedEventArgs eventArgs) =>
        OutdentSelected();

    private void Grid_Click(object sender, RoutedEventArgs eventArgs)
    {
        _settings.ShowGrid = GridMenuItem.IsChecked;
        ApplyViewportSettings();
    }

    private void SafeArea_Click(object sender, RoutedEventArgs eventArgs)
    {
        _settings.ShowSafeArea = SafeAreaMenuItem.IsChecked;
        ApplyViewportSettings();
    }

    private void Bounds_Click(object sender, RoutedEventArgs eventArgs)
    {
        _settings.ShowUnknownBounds = BoundsMenuItem.IsChecked;
        ApplyViewportSettings();
    }

    private void Snap_Click(object sender, RoutedEventArgs eventArgs)
    {
        _settings.SnapEnabled = SnapMenuItem.IsChecked;
        ToolbarSnap.IsChecked = _settings.SnapEnabled;
        ApplyViewportSettings();
    }

    private void ToolbarSnap_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (SnapMenuItem is null)
        {
            return;
        }

        _settings.SnapEnabled = ToolbarSnap.IsChecked == true;
        SnapMenuItem.IsChecked = _settings.SnapEnabled;
        ApplyViewportSettings();
    }

    private void PreviewScenario_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (PreviewScenarioCombo.SelectedItem is not XuiPreviewScenario scenario)
        {
            return;
        }

        _settings.PreviewScenarioId = scenario.Id;
        PreviewScenarioCombo.ToolTip = scenario.Description;
        RefreshEvaluation();
    }

    private void ForceShowSelected_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null)
        {
            return;
        }

        foreach (XuiSyntaxNode node in SelectedNodes())
        {
            XuiSyntaxNode? current = node;
            while (current is not null)
            {
                if (!XuiModelReader.IsStructural(current))
                {
                    _forceShownKeys.Add(current.Key);
                }

                current = current.Parent;
            }
        }

        RefreshEvaluation();
    }

    private void ClearForceShown_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _forceShownKeys.Clear();
        RefreshEvaluation();
    }

    private void RestoreComposedPose_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        StopPlayback();
        if (_timelineWorkspace?.RestoreActiveComposedTick() != true)
        {
            return;
        }

        RefreshEvaluation();
        UpdateTimelineData();
        RefreshNamedFrameEditor();
    }

    private void LoadReferenceImage_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Load HUD or menu reference image",
            Filter =
                "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                Viewport.LoadReferenceImage(dialog.FileName);
                StatusText.Text =
                    $"Reference: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception exception) when (
                exception is IOException or
                NotSupportedException)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Could not load reference image",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void ClearReferenceImage_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        Viewport.ClearReferenceImage();
        StatusText.Text = "Reference image cleared";
    }

    private void ReferenceOpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        _settings.ReferenceOverlayOpacity = eventArgs.NewValue;
        if (Viewport is not null)
        {
            Viewport.ReferenceImageOpacity = eventArgs.NewValue;
        }
    }

    private void Fit_Click(object sender, RoutedEventArgs eventArgs) => Viewport.Fit();

    private void ActualPixels_Click(object sender, RoutedEventArgs eventArgs) =>
        Viewport.ActualPixels();

    private void ZoomIn_Click(object sender, RoutedEventArgs eventArgs) =>
        Viewport.ZoomBy(1.2);

    private void ZoomOut_Click(object sender, RoutedEventArgs eventArgs) =>
        Viewport.ZoomBy(1 / 1.2);

    private void PlayPause_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_timelineWorkspace?.ActiveScope is not XuiTimelineScope scope ||
            scope.MaximumTick <= 0)
        {
            return;
        }

        _isPlaying = !_isPlaying;
        PlayPauseButton.Content = _isPlaying ? "Pause" : "Play";
        if (_isPlaying)
        {
            _playbackRemainder = 0;
            _playbackClock.Restart();
            _playbackTimer.Start();
        }
        else
        {
            _playbackTimer.Stop();
            _playbackClock.Stop();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs eventArgs)
    {
        StopPlayback();
        SetCurrentTick(0);
    }

    private void PreviousTick_Click(object sender, RoutedEventArgs eventArgs)
    {
        StopPlayback();
        SetCurrentTick(Math.Max(0, CurrentTimelineTick - 1));
    }

    private void NextTick_Click(object sender, RoutedEventArgs eventArgs)
    {
        StopPlayback();
        SetCurrentTick(Math.Min(
            _timelineWorkspace?.ActiveScope?.MaximumTick ?? int.MaxValue,
            CurrentTimelineTick + 1));
    }

    private void AddKeyFrame_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_document is null || _timelineSet is null)
        {
            return;
        }

        XuiTimeline? timeline = TimelineForKeyEditing();
        if (timeline is null)
        {
            StatusText.Text = "Select an animated element or an existing keyframe first.";
            return;
        }

        string newline = _document.Format.NewLine;
        int currentTick = CurrentTimelineTick;
        List<string> lines =
        [
            "<KeyFrame>",
            $"<Time>{currentTick.ToString(CultureInfo.InvariantCulture)}</Time>",
            "<Interpolation>0</Interpolation>",
        ];
        foreach (XuiTrack track in timeline.Tracks)
        {
            XuiAnimatedValue? sampled = TimelineEvaluator.Sample(
                track,
                currentTick);
            lines.Add($"<Prop>{sampled?.ToXuiString() ?? DefaultTimelineValue(track.Property)}</Prop>");
        }

        lines.Add("</KeyFrame>");
        _document.Execute(XuiCommandFactory.InsertChildXml(
            _document,
            timeline.Syntax,
            string.Join(newline, lines),
            $"Add keyframe at {currentTick}"));
    }

    private void DeleteKeyFrame_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            TimelineEditor.SelectedKeyFrame is not XuiKeyFrame selected)
        {
            return;
        }

        XuiSyntaxNode? node = _document.SyntaxTree.FindByKey(selected.Syntax.Key);
        if (node is not null)
        {
            _document.Execute(XuiCommandFactory.RemoveElement(_document, node));
        }
    }

    private void CopyKeyFrame_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            TimelineEditor.SelectedKeyFrame is not XuiKeyFrame selected)
        {
            return;
        }

        XuiSyntaxNode? node = _document.SyntaxTree.FindByKey(selected.Syntax.Key);
        if (node is null)
        {
            return;
        }

        _copiedKeyFrameXml = _document.Text.Substring(
            node.Start,
            node.End - node.Start);
        StatusText.Text = $"Copied keyframe at tick {selected.Tick}.";
    }

    private void PasteKeyFrame_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            string.IsNullOrWhiteSpace(_copiedKeyFrameXml) ||
            TimelineForKeyEditing() is not XuiTimeline timeline)
        {
            return;
        }

        int currentTick = CurrentTimelineTick;
        string raw = ReplaceKeyFrameTime(_copiedKeyFrameXml, currentTick);
        _document.Execute(XuiCommandFactory.InsertChildXml(
            _document,
            timeline.Syntax,
            raw,
            $"Paste keyframe at {currentTick}"));
    }

    private void HierarchyExpand_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: HierarchyRow row })
        {
            return;
        }

        row.IsExpanded = !row.IsExpanded;
        if (!_filterActive)
        {
            if (row.IsExpanded)
            {
                _expanded.Add(row.NodeKey);
            }
            else
            {
                _expanded.Remove(row.NodeKey);
            }
        }

        BuildHierarchy();
    }

    private void CollapseHierarchy_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null)
        {
            return;
        }

        _expanded.Clear();
        _expanded.Add(_document.Root.Key);
        BuildHierarchy();
        SelectRowsFromKeys();
    }

    private void RevealHierarchySelection_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (EnsureSelectedAncestorsExpanded())
        {
            BuildHierarchy();
        }

        SelectRowsFromKeys(scrollIntoView: true);
        HierarchyList.Focus();
    }

    private void HierarchyList_KeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (HierarchyList.SelectedItem is not HierarchyRow row)
        {
            return;
        }

        if (eventArgs.Key == Key.Right && row.HasChildren)
        {
            _expanded.Add(row.NodeKey);
        }
        else if (eventArgs.Key == Key.Left)
        {
            if (_expanded.Remove(row.NodeKey))
            {
                // The selected row stays selected after the branch collapses.
            }
            else if (_hierarchyIndex?.Find(row.NodeKey)?.ParentKey is
                     string parentKey)
            {
                _selectedKeys.Clear();
                _selectedKeys.Add(parentKey);
                EnsureSelectedAncestorsExpanded();
            }
        }
        else
        {
            return;
        }

        BuildHierarchy();
        SelectRowsFromKeys();
        UpdateSelectionSurfaces();
        eventArgs.Handled = true;
    }

    private void AllTracksToggle_Changed(
        object sender,
        RoutedEventArgs eventArgs) =>
        UpdateTimelineData();

    private void HierarchyVisibility_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not CheckBox { Tag: HierarchyRow row })
        {
            return;
        }

        _hiddenKeysBeforeIsolation = null;
        if (row.IsEditorVisible)
        {
            _hiddenKeys.Remove(row.NodeKey);
        }
        else
        {
            _hiddenKeys.Add(row.NodeKey);
        }

        _hierarchyIndex?.UpdateEditorStates(_hiddenKeys, _lockedKeys);
        Viewport.SetHiddenKeys(EditorHiddenKeys());
        Viewport.SetLockedKeys(EditorLockedKeys());
    }

    private void HierarchyContextMenu_Opened(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            if (Equals(item.Tag, "RestoreIsolation"))
            {
                item.IsEnabled = _hiddenKeysBeforeIsolation is not null;
            }
        }
    }

    private void HierarchyIsolate_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is MenuItem { DataContext: HierarchyRow row })
        {
            IsolateHierarchy(row.NodeKey);
        }
    }

    private void HierarchyRestoreIsolation_Click(
        object sender,
        RoutedEventArgs eventArgs) =>
        RestoreHierarchyIsolation();

    private void HierarchyShowAll_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _hiddenKeysBeforeIsolation = null;
        _hiddenKeys.Clear();
        ApplyHierarchyVisibility();
    }

    private void IsolateHierarchy(string nodeKey)
    {
        if (_hierarchyIndex is null ||
            _hierarchyIndex.Find(nodeKey) is null)
        {
            return;
        }

        _hiddenKeysBeforeIsolation =
            new HashSet<string>(_hiddenKeys, StringComparer.Ordinal);
        _hiddenKeys.Clear();
        _hiddenKeys.UnionWith(
            _hierarchyIndex.HiddenBranchRootsExcept(nodeKey));
        _selectedKeys.Clear();
        _selectedKeys.Add(nodeKey);
        SelectRowsFromKeys(scrollIntoView: false);
        UpdateSelectionSurfaces();
        ApplyHierarchyVisibility();
    }

    private void RestoreHierarchyIsolation()
    {
        if (_hiddenKeysBeforeIsolation is null)
        {
            return;
        }

        _hiddenKeys.Clear();
        _hiddenKeys.UnionWith(_hiddenKeysBeforeIsolation);
        _hiddenKeysBeforeIsolation = null;
        ApplyHierarchyVisibility();
    }

    private void ApplyHierarchyVisibility()
    {
        _hierarchyIndex?.UpdateEditorStates(_hiddenKeys, _lockedKeys);
        Viewport.SetLockedKeys(EditorLockedKeys());
        Viewport.SetHiddenKeys(EditorHiddenKeys());
    }

    private void HierarchyLock_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not CheckBox { Tag: HierarchyRow row })
        {
            return;
        }

        if (row.IsLocked)
        {
            _lockedKeys.Add(row.NodeKey);
        }
        else
        {
            _lockedKeys.Remove(row.NodeKey);
        }

        _hierarchyIndex?.UpdateEditorStates(_hiddenKeys, _lockedKeys);
        Viewport.SetLockedKeys(EditorLockedKeys());
    }

    private void HierarchySearch_TextChanged(
        object sender,
        TextChangedEventArgs eventArgs)
    {
        _hierarchySearchTimer.Stop();
        _hierarchySearchTimer.Start();
    }

    private void ApplyHierarchyFilter()
    {
        string filter = HierarchySearch.Text.Trim();
        if (filter.Length > 0 && !_filterActive)
        {
            _filterActive = true;
            _expansionBeforeFilter = new HashSet<string>(
                _expanded,
                StringComparer.Ordinal);
        }
        else if (filter.Length == 0 && _filterActive)
        {
            _filterActive = false;
            _expanded.Clear();
            if (_expansionBeforeFilter is not null)
            {
                _expanded.UnionWith(_expansionBeforeFilter);
            }

            _expansionBeforeFilter = null;
        }

        BuildHierarchy();
    }

    private void HierarchyList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_syncingSelection)
        {
            return;
        }

        _selectedKeys.Clear();
        foreach (HierarchyRow row in HierarchyList.SelectedItems.Cast<HierarchyRow>())
        {
            _selectedKeys.Add(row.NodeKey);
        }

        UpdateSelectionSurfaces();
    }

    private void Viewport_SelectionRequested(
        object? sender,
        XuiSelectionRequestedEventArgs eventArgs)
    {
        if (eventArgs.NodeKey is null)
        {
            if (!eventArgs.Additive && !eventArgs.Toggle)
            {
                _selectedKeys.Clear();
            }
        }
        else if (eventArgs.Toggle)
        {
            if (!_selectedKeys.Remove(eventArgs.NodeKey))
            {
                _selectedKeys.Add(eventArgs.NodeKey);
            }
        }
        else if (eventArgs.Additive)
        {
            _selectedKeys.Add(eventArgs.NodeKey);
        }
        else
        {
            _selectedKeys.Clear();
            _selectedKeys.Add(eventArgs.NodeKey);
        }

        if (EnsureSelectedAncestorsExpanded())
        {
            BuildHierarchy();
        }

        SelectRowsFromKeys(scrollIntoView: true);
        UpdateSelectionSurfaces();
    }

    private void Viewport_TransformCommitted(
        object? sender,
        XuiTransformCommittedEventArgs eventArgs)
    {
        if (_document is null ||
            IsLocked(eventArgs.NodeKey))
        {
            return;
        }

        XuiSyntaxNode? eventNode =
            _document.SyntaxTree.FindByKey(eventArgs.NodeKey);
        if (eventNode is null || IsCanvasRoot(eventNode))
        {
            StatusText.Text = eventNode is null
                ? "Transform target no longer exists"
                : "The XUI canvas cannot be transformed from the preview";
            return;
        }

        if (eventArgs.Kind == XuiTransformKind.Move)
        {
            IReadOnlyList<string> targetKeys =
                _selectedKeys.Contains(eventArgs.NodeKey)
                    ? SelectedTransformRootKeys()
                    : [eventArgs.NodeKey];
            ExecuteBatch(() =>
            {
                PositionMovePlan[] plans = targetKeys
                    .Where(key => !IsLocked(key))
                    .Select(key =>
                    {
                        XuiSyntaxNode node =
                            _document.SyntaxTree.FindByKey(key) ??
                            throw new InvalidOperationException(
                                "A moved element no longer exists.");
                        if (IsCanvasRoot(node))
                        {
                            throw new InvalidOperationException(
                                "The XUI canvas cannot be moved.");
                        }

                        return PreparePositionMove(
                            node,
                            eventArgs.PositionDeltas.GetValueOrDefault(
                                key,
                                eventArgs.PositionDelta));
                    })
                    .ToArray();
                foreach (PositionMovePlan plan in plans)
                {
                    ApplyPositionMove(plan);
                }
            }, "Move selection");
            return;
        }

        XuiSyntaxNode? selectedNode =
            _document.SyntaxTree.FindByKey(eventArgs.NodeKey);
        if (selectedNode is null ||
            IsCanvasRoot(selectedNode))
        {
            return;
        }

        if (eventArgs.Kind == XuiTransformKind.Resize)
        {
            string selectedKey = selectedNode.Key;
            ExecuteBatch(() =>
            {
                ApplyPositionDelta(
                    selectedNode,
                    eventArgs.PositionDelta);
                XuiSyntaxNode? current =
                    _document.SyntaxTree.FindByKey(selectedKey);
                if (current is null)
                {
                    return;
                }

                if (Math.Abs(eventArgs.SizeDelta.X) > 0.0001)
                {
                    SetNodeProperty(
                        current,
                        "Width",
                        (eventArgs.OriginalSize.X + eventArgs.SizeDelta.X)
                        .ToString("0.000000", CultureInfo.InvariantCulture));
                    current = _document.SyntaxTree.FindByKey(selectedKey);
                    if (current is null)
                    {
                        return;
                    }
                }

                if (Math.Abs(eventArgs.SizeDelta.Y) > 0.0001)
                {
                    SetNodeProperty(
                        current,
                        "Height",
                        (eventArgs.OriginalSize.Y + eventArgs.SizeDelta.Y)
                        .ToString("0.000000", CultureInfo.InvariantCulture));
                }
            }, "Resize element");
            return;
        }

        if (eventArgs.Kind == XuiTransformKind.Rotate)
        {
            IReadOnlyList<string> targetKeys =
                _selectedKeys.Contains(eventArgs.NodeKey)
                    ? SelectedTransformRootKeys()
                    : [eventArgs.NodeKey];
            ExecuteBatch(() =>
            {
                foreach (string key in targetKeys)
                {
                    if (IsLocked(key))
                    {
                        continue;
                    }

                    XuiSyntaxNode? node =
                        _document.SyntaxTree.FindByKey(key);
                    if (node is not null &&
                        !IsCanvasRoot(node))
                    {
                        ApplyRotationDelta(
                            node,
                            eventArgs.RotationDelta);
                    }
                }
            }, "Rotate selection");
        }
    }

    private string[] SelectedTransformRootKeys()
    {
        if (_document is null)
        {
            return [];
        }

        return _selectedKeys
            .Where(key =>
            {
                XuiSyntaxNode? node =
                    _document.SyntaxTree.FindByKey(key);
                if (node is null ||
                    IsCanvasRoot(node) ||
                    IsLocked(key))
                {
                    return false;
                }

                XuiSyntaxNode? parent = node.Parent;
                while (parent is not null)
                {
                    if (_selectedKeys.Contains(parent.Key) &&
                        !IsCanvasRoot(parent))
                    {
                        return false;
                    }

                    parent = parent.Parent;
                }

                return true;
            })
            .ToArray();
    }

    private PositionMovePlan PreparePositionMove(
        XuiSyntaxNode node,
        XuiVector2 delta)
    {
        if (_document is null)
        {
            throw new InvalidOperationException(
                "No XUI document is open.");
        }

        XuiPropertyEntry? positionProperty = XuiModelReader.GetProperty(
            node,
            _document.Text,
            "Position");
        XuiVector3 position = default;
        if (positionProperty is not null &&
            !TryPosition(
                positionProperty.Value,
                out position,
                out _))
        {
            throw new InvalidOperationException(
                $"{DisplayNode(node)} has an invalid authored Position value.");
        }

        string authoredValue = string.Create(
            CultureInfo.InvariantCulture,
            $"{position.X + delta.X:0.000000},{position.Y + delta.Y:0.000000},{position.Z:0.000000}");
        List<PositionKeyMove> keyMoves = [];
        if (Math.Abs(delta.X) > 0.0001 ||
            Math.Abs(delta.Y) > 0.0001)
        {
            EnsureCompiledLayout();
            string? targetId =
                XuiModelReader.GetId(node, _document.Text);
            if (_layoutSession is not null &&
                !string.IsNullOrWhiteSpace(targetId))
            {
                string? recursionBarrier =
                    TimelineRecursionBarrierFor(node);
                (
                    XuiTimelineScope Scope,
                    XuiTimeline Timeline,
                    XuiTrack Track)[] positionTracks =
                    _layoutSession.TimelineScopes.Scopes
                        .Where(scope =>
                            KeyIsAncestorOrSelf(
                                scope.ScopeKey,
                                node.Key) &&
                            (recursionBarrier is null ||
                             KeyIsAncestorOrSelf(
                                 recursionBarrier,
                                 scope.ScopeKey)))
                        .SelectMany(scope =>
                            scope.Timelines
                                .Where(timeline =>
                                    timeline.TargetId.Equals(
                                        targetId,
                                        StringComparison.Ordinal))
                                .Select(timeline => (scope, timeline)))
                        .SelectMany(timeline =>
                            timeline.timeline.Tracks
                                .Where(static track =>
                                    track.Property ==
                                    XuiTimelineProperty.Position)
                                .Select(track => (
                                    timeline.scope,
                                    timeline.timeline,
                                    track)))
                        .ToArray();
                HashSet<string> plannedProps =
                    new(StringComparer.Ordinal);
                foreach ((
                             XuiTimelineScope scope,
                             XuiTimeline timeline,
                             XuiTrack track) in positionTracks)
                {
                    foreach (XuiSyntaxNode keyFrame in
                             timeline.Syntax.Elements("KeyFrame"))
                    {
                        XuiSyntaxNode[] propNodes =
                            keyFrame.Elements("Prop").ToArray();
                        if (track.SourcePropertyIndex < 0 ||
                            track.SourcePropertyIndex >= propNodes.Length)
                        {
                            throw new InvalidOperationException(
                                $"{DisplayNode(node)} has a Position key " +
                                $"with a missing Prop value in {scope.DisplayName}.");
                        }

                        XuiSyntaxNode prop =
                            propNodes[track.SourcePropertyIndex];
                        if (!plannedProps.Add(prop.Key))
                        {
                            continue;
                        }

                        string raw = prop.GetDecodedValue(_document.Text);
                        if (!TryPosition(
                                raw,
                                out XuiVector3 keyPosition,
                                out bool vector2))
                        {
                            throw new InvalidOperationException(
                                $"{DisplayNode(node)} has an invalid " +
                                $"Position key value '{raw}' in " +
                                $"{scope.DisplayName}.");
                        }

                        string value = vector2
                            ? string.Create(
                                CultureInfo.InvariantCulture,
                                $"{keyPosition.X + delta.X:0.000000},{keyPosition.Y + delta.Y:0.000000}")
                            : string.Create(
                                CultureInfo.InvariantCulture,
                                $"{keyPosition.X + delta.X:0.000000},{keyPosition.Y + delta.Y:0.000000},{keyPosition.Z:0.000000}");
                        keyMoves.Add(new PositionKeyMove(prop.Key, value));
                    }
                }
            }
        }

        return new PositionMovePlan(
            node.Key,
            authoredValue,
            keyMoves);
    }

    private string? TimelineRecursionBarrierFor(XuiSyntaxNode node)
    {
        if (_document is null)
        {
            return null;
        }

        XuiSyntaxNode? current = node.Parent;
        while (current is not null)
        {
            string? raw = XuiModelReader.GetPropertyValue(
                current,
                _document.Text,
                "DisableTimelineRecursion");
            if (XuiValueParser.TryBoolean(raw, out bool disabled) &&
                disabled)
            {
                return current.Key;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool KeyIsAncestorOrSelf(
        string ancestorKey,
        string nodeKey) =>
        string.Equals(
            ancestorKey,
            nodeKey,
            StringComparison.Ordinal) ||
        (nodeKey.StartsWith(
             ancestorKey,
             StringComparison.Ordinal) &&
         nodeKey.Length > ancestorKey.Length &&
         nodeKey[ancestorKey.Length] == '/');

    private void ApplyPositionMove(PositionMovePlan plan)
    {
        if (_document is null)
        {
            return;
        }

        XuiSyntaxNode node =
            _document.SyntaxTree.FindByKey(plan.NodeKey) ??
            throw new InvalidOperationException(
                "A moved element no longer exists.");
        SetNodeProperty(node, "Position", plan.AuthoredValue);
        foreach (PositionKeyMove keyMove in plan.KeyMoves)
        {
            XuiSyntaxNode prop =
                _document.SyntaxTree.FindByKey(keyMove.PropKey) ??
                throw new InvalidOperationException(
                    "A Position key changed while the move was being applied.");
            _document.Execute(XuiCommandFactory.SetElementValue(
                _document,
                prop,
                keyMove.Value));
        }
    }

    private void HierarchyAddChild_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not MenuItem { DataContext: HierarchyRow row })
        {
            return;
        }

        _selectedKeys.Clear();
        _selectedKeys.Add(row.NodeKey);
        SelectRowsFromKeys(scrollIntoView: false);
        UpdateSelectionSurfaces();
        AddChild_Click(sender, eventArgs);
    }

    private static bool TryPosition(
        string raw,
        out XuiVector3 position,
        out bool vector2)
    {
        if (XuiValueParser.TryVector3(raw, out position))
        {
            vector2 = false;
            return true;
        }

        if (XuiValueParser.TryVector2(raw, out XuiVector2 value))
        {
            position = new XuiVector3(value.X, value.Y, 0);
            vector2 = true;
            return true;
        }

        position = default;
        vector2 = false;
        return false;
    }

    private string DisplayNode(XuiSyntaxNode node) =>
        _document is null
            ? node.Name
            : XuiModelReader.GetId(node, _document.Text) is string id &&
              id.Length > 0
                ? id
                : node.Name;

    private void ApplyPositionDelta(
        XuiSyntaxNode node,
        XuiVector2 delta)
    {
        if (_document is null ||
            (Math.Abs(delta.X) <= 0.0001 &&
             Math.Abs(delta.Y) <= 0.0001))
        {
            return;
        }

        XuiPropertyEntry? positionProperty = XuiModelReader.GetProperty(
            node,
            _document.Text,
            "Position");
        XuiVector3 position = default;
        if (positionProperty is not null &&
            !XuiValueParser.TryVector3(positionProperty.Value, out position))
        {
            if (!XuiValueParser.TryVector2(
                    positionProperty.Value,
                    out XuiVector2 position2))
            {
                return;
            }

            position = new XuiVector3(position2.X, position2.Y, 0);
        }

        SetNodeProperty(
            node,
            "Position",
            FormattableString.Invariant(
                $"{position.X + delta.X:0.000000},{position.Y + delta.Y:0.000000},{position.Z:0.000000}"));
    }

    private void ApplyRotationDelta(
        XuiSyntaxNode node,
        double deltaDegrees)
    {
        if (_document is null || Math.Abs(deltaDegrees) <= 0.0001)
        {
            return;
        }

        string raw = XuiModelReader.GetPropertyValue(
            node,
            _document.Text,
            "Rotation") ?? string.Empty;
        string value;
        double degrees = 0;
        if (raw.Length == 0 ||
            XuiValueParser.TryNumber(raw, out degrees))
        {
            value = (degrees + deltaDegrees).ToString(
                "0.000000",
                CultureInfo.InvariantCulture);
        }
        else if (XuiValueParser.TryVector3(raw, out XuiVector3 vector))
        {
            value = FormattableString.Invariant(
                $"{vector.X:0.000000},{vector.Y:0.000000},{vector.Z + deltaDegrees:0.000000}");
        }
        else if (XuiValueParser.TryQuaternion(raw, out XuiQuaternion quaternion))
        {
            System.Numerics.Quaternion current = new(
                (float)quaternion.X,
                (float)quaternion.Y,
                (float)quaternion.Z,
                (float)quaternion.W);
            System.Numerics.Quaternion delta =
                System.Numerics.Quaternion.CreateFromAxisAngle(
                    System.Numerics.Vector3.UnitZ,
                    (float)(deltaDegrees * Math.PI / 180));
            System.Numerics.Quaternion rotated =
                System.Numerics.Quaternion.Normalize(
                    System.Numerics.Quaternion.Concatenate(
                        current,
                        delta));
            value = FormattableString.Invariant(
                $"{rotated.X:0.000000},{rotated.Y:0.000000},{rotated.Z:0.000000},{rotated.W:0.000000}");
        }
        else
        {
            StatusText.Text =
                "Rotation uses an unsupported authored value and was not changed.";
            return;
        }

        SetNodeProperty(node, "Rotation", value);
    }

    private void SetNodeProperty(
        XuiSyntaxNode node,
        string name,
        string value)
    {
        if (_document is null)
        {
            return;
        }

        XuiPropertyEntry? property = XuiModelReader.GetProperty(
            node,
            _document.Text,
            name);
        IXuiCommand command = property is null
            ? XuiCommandFactory.AddProperty(
                _document,
                node,
                name,
                value)
            : XuiCommandFactory.SetElementValue(
                _document,
                property.Element,
                value);
        _document.Execute(command);
    }

    private void InspectorValue_LostFocus(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is TextBox { Tag: InspectorPropertyRow row })
        {
            CommitInspectorValue(row);
        }
    }

    private void InspectorValue_KeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter &&
            sender is TextBox { Tag: InspectorPropertyRow row } textBox)
        {
            CommitInspectorValue(row);
            Keyboard.ClearFocus();
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape &&
                 sender is TextBox text)
        {
            BuildInspector();
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
    }

    private void InspectorChoice_DropDownClosed(object sender, EventArgs eventArgs)
    {
        if (sender is ComboBox { Tag: InspectorPropertyRow row })
        {
            CommitInspectorValue(row);
        }
    }

    private void InspectorChoice_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs)
    {
        if (sender is ComboBox { Tag: InspectorPropertyRow row })
        {
            CommitInspectorValue(row);
        }
    }

    private void InspectorChoice_KeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter &&
            sender is ComboBox { Tag: InspectorPropertyRow row } comboBox)
        {
            CommitInspectorValue(row);
            Keyboard.ClearFocus();
            comboBox.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.Next));
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            BuildInspector();
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
    }

    private void InspectorBoolean_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is CheckBox
            {
                Tag: InspectorPropertyRow row,
                IsChecked: bool value,
            })
        {
            row.BooleanValue = value;
            CommitInspectorValue(row);
        }
    }

    private void RawXmlExpander_Expanded(
        object sender,
        RoutedEventArgs eventArgs) =>
        RefreshRawXmlEditor();

    private void LoadLargeRawXml_Click(
        object sender,
        RoutedEventArgs eventArgs) =>
        RefreshRawXmlEditor(allowLarge: true);

    private void ResetRawXml_Click(object sender, RoutedEventArgs eventArgs) =>
        RefreshRawXmlEditor(
            allowLarge:
                _document is not null &&
                _rawXmlLoadedRevision == _document.Revision);

    private void ApplyRawXml_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            SelectedNodes() is not [XuiSyntaxNode selected] ||
            _rawXmlLoadedNodeKey != selected.Key ||
            _rawXmlLoadedRevision != _document.Revision)
        {
            RawXmlErrorText.Text =
                "Load the XML for the current selection before applying it.";
            return;
        }

        XuiSyntaxNode? current =
            _document.SyntaxTree.FindByKey(selected.Key);
        if (current is null)
        {
            RefreshRawXmlEditor();
            return;
        }

        try
        {
            IXuiCommand command = XuiCommandFactory.ReplaceElementXml(
                _document,
                current,
                RawXmlTextBox.Text);
            _document.Execute(command);
            RawXmlErrorText.Text = string.Empty;
            StatusText.Text = $"Replaced raw XML for {current.Name}";
        }
        catch (Exception exception) when (
            exception is XuiParseException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException)
        {
            RawXmlErrorText.Text = exception.Message;
            StatusText.Text = "Raw XML was rejected; the document was not changed.";
        }
    }

    private void TickSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (!_updatingTick)
        {
            StopPlayback();
            SetCurrentTick((int)Math.Round(eventArgs.NewValue));
        }
    }

    private void TimelineEditor_TickChanged(
        object? sender,
        TimelineTickChangedEventArgs eventArgs)
    {
        StopPlayback();
        SetCurrentTick(eventArgs.Tick);
    }

    private void TimelineEditor_SelectedKeyFrameChanged(
        object? sender,
        EventArgs eventArgs)
    {
        if (_document is not null &&
            TimelineEditor.SelectedTimeline is XuiTimeline timeline &&
            _timelineWorkspace is not null)
        {
            bool scopeChanged = _timelineWorkspace.ResolveSelection(
                [],
                _document.Text,
                timeline.ScopeKey);
            if (scopeChanged)
            {
                StopPlayback();
                RefreshNamedFrameEditor();
                UpdateTimelinePositionChrome();
            }
        }

        RefreshKeyFrameEditor();
    }

    private void KeyFrameValue_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs) =>
        CommitKeyFrameValue();

    private void KeyFrameValue_KeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            CommitKeyFrameValue();
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            RefreshKeyFrameEditor();
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
    }

    private void KeyInterpolation_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_updatingTimelineEditors ||
            _document is null ||
            TimelineEditor.SelectedKeyFrame is not XuiKeyFrame selected ||
            KeyInterpolationComboBox.SelectedItem is not ComboBoxItem
            {
                Tag: string interpolation,
            })
        {
            return;
        }

        SetTimelineChildValue(
            selected.Syntax.Key,
            "Interpolation",
            interpolation,
            removeWhenEmpty: false);
    }

    private void KeyFrameEase_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs) =>
        CommitKeyFrameEase();

    private void KeyFrameEase_KeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            CommitKeyFrameEase();
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            RefreshKeyFrameEditor();
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
    }

    private void NamedFrame_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_updatingTimelineEditors)
        {
            return;
        }

        _selectedNamedFrameKey =
            (NamedFrameComboBox.SelectedItem as XuiNamedFrame)?.Syntax.Key;
        PopulateNamedFrameFields(
            NamedFrameComboBox.SelectedItem as XuiNamedFrame);
    }

    private void AddNamedFrame_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            _timelineWorkspace?.ActiveScope is not XuiTimelineScope activeScope)
        {
            StatusText.Text =
                "Select nodes from one timeline scope before adding a named frame.";
            return;
        }

        XuiSyntaxNode scope = _document.SyntaxTree.FindByKey(
                                  activeScope.ScopeKey) ??
                              activeScope.Owner;
        HashSet<string> names = activeScope.NamedFrames
            .Select(static frame => frame.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int currentTick = CurrentTimelineTick;
        string name = $"Frame_{currentTick}";
        for (int suffix = 2; names.Contains(name); suffix++)
        {
            name = $"Frame_{currentTick}_{suffix}";
        }

        string frameXml = CreateNamedFrameXml(
            name,
            currentTick,
            string.Empty,
            string.Empty,
            _document.Format.NewLine);
        XuiSyntaxNode? timelines = scope.FirstElement("Timelines");
        if (timelines is null)
        {
            string raw = string.Join(
                _document.Format.NewLine,
                "<Timelines>",
                "<NamedFrames>",
                frameXml,
                "</NamedFrames>",
                "</Timelines>");
            _document.Execute(XuiCommandFactory.InsertChildXml(
                _document,
                scope,
                raw,
                $"Add named frame {name}"));
        }
        else
        {
            XuiSyntaxNode? namedFrames = timelines.FirstElement("NamedFrames");
            if (namedFrames is null)
            {
                string raw = string.Join(
                    _document.Format.NewLine,
                    "<NamedFrames>",
                    frameXml,
                    "</NamedFrames>");
                _document.Execute(XuiCommandFactory.InsertChildXml(
                    _document,
                    timelines,
                    raw,
                    $"Add named frame {name}"));
            }
            else
            {
                _document.Execute(XuiCommandFactory.InsertChildXml(
                    _document,
                    namedFrames,
                    frameXml,
                    $"Add named frame {name}"));
            }
        }

        _selectedNamedFrameKey = _timelineSet?.NamedFrames
            .LastOrDefault(frame =>
                frame.ScopeKey == scope.Key &&
                string.Equals(frame.Name, name, StringComparison.Ordinal))
            ?.Syntax.Key;
        RefreshNamedFrameEditor();
    }

    private void GoToNamedFrame_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (NamedFrameComboBox.SelectedItem is not XuiNamedFrame frame)
        {
            return;
        }

        StopPlayback();
        SetCurrentTick(frame.Tick);
    }

    private void ApplyNamedFrame_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            string.IsNullOrEmpty(_selectedNamedFrameKey))
        {
            return;
        }

        string name = NamedFrameNameTextBox.Text.Trim();
        if (name.Length == 0 ||
            !XuiValueParser.TryInteger(
                NamedFrameTickTextBox.Text,
                out int tick) ||
            tick < 0)
        {
            StatusText.Text =
                "A named frame needs a name and a non-negative integer tick.";
            return;
        }

        string command = NamedFrameCommandComboBox.Text.Trim();
        string target = NamedFrameTargetTextBox.Text.Trim();
        string key = _selectedNamedFrameKey;
        ExecuteBatch(() =>
        {
            SetTimelineChildValue(key, "Name", name, removeWhenEmpty: false);
            SetTimelineChildValue(
                key,
                "Time",
                tick.ToString(CultureInfo.InvariantCulture),
                removeWhenEmpty: false);
            SetTimelineChildValue(key, "Command", command, removeWhenEmpty: true);
            SetTimelineChildValue(
                key,
                "CommandParams",
                target,
                removeWhenEmpty: true);
        });
        SetCurrentTick(tick);
    }

    private void DeleteNamedFrame_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            string.IsNullOrEmpty(_selectedNamedFrameKey))
        {
            return;
        }

        XuiSyntaxNode? frame = _document.SyntaxTree.FindByKey(
            _selectedNamedFrameKey);
        if (frame is null)
        {
            return;
        }

        _selectedNamedFrameKey = null;
        _document.Execute(XuiCommandFactory.RemoveElement(_document, frame));
    }

    private void TimelineEditor_KeyFrameMoveRequested(
        object? sender,
        TimelineKeyFrameMoveRequestedEventArgs eventArgs)
    {
        if (_document is null)
        {
            return;
        }

        XuiSyntaxNode? keyFrame = _document.SyntaxTree.FindByKey(
            eventArgs.KeyFrameNodeKey);
        XuiSyntaxNode? time = keyFrame?.FirstElement("Time");
        if (time is null)
        {
            return;
        }

        _document.Execute(XuiCommandFactory.SetElementValue(
            _document,
            time,
            eventArgs.NewTick.ToString(CultureInfo.InvariantCulture)));
        SetCurrentTick(eventArgs.NewTick);
    }

    private void DiagnosticsSearch_TextChanged(
        object sender,
        TextChangedEventArgs eventArgs) =>
        FilterDiagnostics();

    private void Window_KeyDown(object sender, KeyEventArgs eventArgs)
    {
        bool editingText = Keyboard.FocusedElement is TextBoxBase;
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control && eventArgs.Key == Key.F)
        {
            HierarchySearch.Focus();
            HierarchySearch.SelectAll();
        }
        else if (modifiers == ModifierKeys.Control && eventArgs.Key == Key.O)
        {
            Open_Click(this, new RoutedEventArgs());
        }
        else if (modifiers == ModifierKeys.Control && eventArgs.Key == Key.S)
        {
            Save_Click(this, new RoutedEventArgs());
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
                 eventArgs.Key == Key.S)
        {
            SaveAs_Click(this, new RoutedEventArgs());
        }
        else if (!editingText &&
                 modifiers == ModifierKeys.Control &&
                 eventArgs.Key == Key.Z)
        {
            Undo_Click(this, new RoutedEventArgs());
        }
        else if (!editingText &&
                 modifiers == ModifierKeys.Control &&
                 eventArgs.Key == Key.Y)
        {
            Redo_Click(this, new RoutedEventArgs());
        }
        else if (!editingText &&
                 modifiers == ModifierKeys.Control &&
                 eventArgs.Key == Key.D)
        {
            Duplicate_Click(this, new RoutedEventArgs());
        }
        else if (!editingText &&
                 modifiers == ModifierKeys.Control &&
                 eventArgs.Key == Key.Insert)
        {
            AddChild_Click(this, new RoutedEventArgs());
        }
        else if (!editingText && eventArgs.Key == Key.Delete)
        {
            Delete_Click(this, new RoutedEventArgs());
        }
        else if (!editingText &&
                 modifiers == ModifierKeys.Alt &&
                 eventArgs.Key == Key.Up)
        {
            MoveSelected(-1);
        }
        else if (!editingText &&
                 modifiers == ModifierKeys.Alt &&
                 eventArgs.Key == Key.Down)
        {
            MoveSelected(1);
        }
        else if (!editingText &&
                 modifiers == ModifierKeys.Alt &&
                 eventArgs.Key == Key.Right)
        {
            IndentSelected();
        }
        else if (!editingText &&
                 modifiers == ModifierKeys.Alt &&
                 eventArgs.Key == Key.Left)
        {
            OutdentSelected();
        }
        else if (!editingText && eventArgs.Key == Key.F)
        {
            Viewport.Fit();
        }
        else if (!editingText &&
                 eventArgs.Key is Key.D0 or Key.NumPad0)
        {
            Viewport.ActualPixels();
        }
        else if (!editingText && eventArgs.Key == Key.Space)
        {
            PlayPause_Click(this, new RoutedEventArgs());
        }
        else if (!editingText && eventArgs.Key == Key.OemComma)
        {
            PreviousTick_Click(this, new RoutedEventArgs());
        }
        else if (!editingText && eventArgs.Key == Key.OemPeriod)
        {
            NextTick_Click(this, new RoutedEventArgs());
        }
        else
        {
            return;
        }

        eventArgs.Handled = true;
    }

    private async void Window_Closing(object? sender, CancelEventArgs eventArgs)
    {
        if (!_allowClose && _document?.IsDirty == true)
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                "Save changes before closing?",
                "Unsaved XUI changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel)
            {
                eventArgs.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                eventArgs.Cancel = true;
                if (await SaveDocumentAsync(forceSaveAs: false).ConfigureAwait(true))
                {
                    _allowClose = true;
                    Close();
                }

                return;
            }
        }

        SaveWindowSettings();
        Dispose();
        await EditorSettingsStore.SaveAsync(_settings).ConfigureAwait(true);
    }

    private async Task OpenDocumentAsync(string path)
    {
        try
        {
            StatusText.Text = "Opening XUI…";
            XuiDocument document = await XuiDocument.OpenAsync(
                path,
                CreateDocumentOptions()).ConfigureAwait(true);
            AttachDocument(document);
            _recoverySuggestedPath = null;
            _activeRecovery = null;
            AddRecentFile(path);
            ConfigureWatcher(path);
            RefreshAll();
            await RebuildAssetResolverAsync().ConfigureAwait(true);
            StatusText.Text = "Ready";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            XuiParseException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Could not open XUI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Open failed";
        }
    }

    private async Task OpenAssetDocumentAsync(XuiAssetEntry entry)
    {
        try
        {
            StatusText.Text = $"Opening stock {entry.FileName}…";
            XuiDocument document = await XuiDocument.OpenAssetAsync(
                entry,
                CreateDocumentOptions()).ConfigureAwait(true);
            AttachDocument(document);
            _recoverySuggestedPath = null;
            _activeRecovery = null;
            _watcher?.Dispose();
            _watcher = null;
            RefreshAll();
            await RebuildAssetResolverAsync().ConfigureAwait(true);
            StatusText.Text =
                "Stock XUI opened read-only · use Save As to make a mod copy";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            XuiParseException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Could not open stock XUI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Stock open failed";
        }
    }

    private async Task OpenRecoveryAsync(RecoverySnapshot snapshot)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(
                snapshot.ContentPath).ConfigureAwait(true);
            XuiSyntaxTree tree = new XuiSyntaxParser().Parse(bytes);
            XuiDocument document = XuiDocument.FromText(
                tree.Source,
                CreateDocumentOptions(),
                tree.Format);
            AttachDocument(document);
            _recoverySuggestedPath = snapshot.OriginalPath;
            _activeRecovery = snapshot;
            RefreshAll();
            await RebuildAssetResolverAsync().ConfigureAwait(true);
            StatusText.Text = "Recovery opened as an unsaved document";
        }
        catch (Exception exception) when (
            exception is IOException or XuiParseException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Could not open recovery",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AttachDocument(XuiDocument document)
    {
        if (_document is not null)
        {
            _document.Changed -= Document_Changed;
            _document.History.HistoryChanged -= History_HistoryChanged;
        }

        _document = document;
        _document.Changed += Document_Changed;
        _document.History.HistoryChanged += History_HistoryChanged;
        _expanded.Clear();
        _expanded.Add(document.Root.Key);
        _selectedKeys.Clear();
        _hiddenKeys.Clear();
        _hiddenKeysBeforeIsolation = null;
        _forceShownKeys.Clear();
        _lockedKeys.Clear();
        _selectedNamedFrameKey = null;
        TimelineEditor.SelectKeyFrame(null);
        _layoutSession = null;
        _timelineWorkspace = null;
        _hierarchyIndex = null;
        _evaluationDiagnostics = [];
        _evaluationDiagnosticsInitialized = false;
        StopPlayback();
    }

    private async Task RebuildAssetResolverAsync()
    {
        if (_document is null)
        {
            return;
        }

        await EnsureInstallIndexAsync(showErrors: false).ConfigureAwait(true);
        List<XuiAssetRoot> roots = [];
        XuiDocumentAssetContext? documentContext =
            _document.Path is null
                ? null
                : XuiDocumentAssetContext.Discover(_document.Path);
        string? documentAssetRoot =
            documentContext?.Root.FullPath;
        AssetRootSetting? configuredDocumentRoot =
            documentAssetRoot is null
                ? null
                : _settings.AssetRoots
                    .Where(root =>
                        !string.IsNullOrWhiteSpace(root.Path) &&
                        PathIsInside(root.Path, documentAssetRoot))
                    .OrderByDescending(static root => root.Path.Length)
                    .FirstOrDefault();
        bool documentIsInsideInstall =
            _document.Path is not null &&
            !string.IsNullOrWhiteSpace(
                _settings.DyingLightInstallPath) &&
            PathIsInside(
                _settings.DyingLightInstallPath,
                _document.Path);
        if (documentContext is not null &&
            documentAssetRoot is not null &&
            Directory.Exists(documentAssetRoot) &&
            !documentIsInsideInstall)
        {
            roots.Add(configuredDocumentRoot is null
                ? documentContext.Root
                : new XuiAssetRoot(
                    documentAssetRoot,
                    configuredDocumentRoot.Kind,
                    configuredDocumentRoot.EffectiveIsReadOnly));
        }

        if (!string.IsNullOrWhiteSpace(_settings.WorkspaceRoot) &&
            Directory.Exists(_settings.WorkspaceRoot))
        {
            roots.Add(new XuiAssetRoot(
                _settings.WorkspaceRoot,
                XuiAssetRootKind.Workspace,
                false));
        }

        roots.AddRange(
            _settings.AssetRoots
                .Where(static root => !string.IsNullOrWhiteSpace(root.Path))
                .Select(static root => root.ToAssetRoot()));
        List<IXuiAssetSource> sources = _settings.AdditionalAssetSources
            .Where(static source =>
                !string.IsNullOrWhiteSpace(source.Path))
            .Select(static source =>
                (IXuiAssetSource)source.ToAssetSource())
            .ToList();
        if (_installIndex is not null)
        {
            sources.Add(_installIndex);
        }

        _assetResolver = new DyingLightAssetResolver(
            roots,
            fontMappings: _settings.FontMappings,
            sources: sources,
            locale: _settings.Locale,
            inputGlyphScheme: _settings.InputGlyphScheme);
        _textureDiagnostics.Clear();
        _layoutSession = null;
        Viewport.SetAssetResolver(null);
        AssetStatusText.Text = "Indexing external assets…";
        try
        {
            await _assetResolver.RebuildAsync().ConfigureAwait(true);
            Viewport.SetAssetResolver(_assetResolver);
            int diagnosticCount = _assetResolver.Diagnostics.Count;
            AssetStatusText.Text =
                $"{_assetResolver.Files.Count:N0} assets · " +
                $"{_assetResolver.Localization?.Entries.Count ?? 0:N0} strings · " +
                $"{diagnosticCount:N0} " +
                (diagnosticCount == 1 ? "diagnostic" : "diagnostics");
            RefreshEvaluation();
        }
        catch (OperationCanceledException)
        {
            AssetStatusText.Text = "Asset indexing cancelled";
        }
    }

    private async Task<bool> EnsureInstallIndexAsync(bool showErrors)
    {
        string? install = _settings.DyingLightInstallPath;
        if (string.IsNullOrWhiteSpace(install) ||
            !DyingLightInstallIndex.LooksLikeInstall(install))
        {
            _installIndex = null;
            AssetStatusText.Text = "Dying Light install not configured";
            if (showErrors)
            {
                MessageBox.Show(
                    this,
                    "Choose the Dying Light installation folder from File → Dying Light Data first.",
                    "Dying Light data required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
        }

        string fullPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(install));
        if (_installIndex is not null &&
            _installIndex.Profile.FullPath.Equals(
                fullPath,
                StringComparison.OrdinalIgnoreCase) &&
            _installIndex.Profile.NormalizedLocale.Equals(
                DyingLightInstallProfile.NormalizeLocale(_settings.Locale),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            AssetStatusText.Text = "Indexing Dying Light PAKs and RPACKs…";
            DyingLightInstallIndex index = new(
                new DyingLightInstallProfile(fullPath, _settings.Locale));
            await index.RebuildAsync().ConfigureAwait(true);
            _installIndex = index;
            AssetStatusText.Text =
                $"{index.StockXuiFiles.Count:N0} stock XUIs · " +
                $"{index.Entries.Count:N0} install assets · " +
                index.Profile.NormalizedLocale;
            return index.StockXuiFiles.Count > 0;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _installIndex = null;
            AssetStatusText.Text = "Dying Light indexing failed";
            if (showErrors)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Could not index Dying Light",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return false;
        }
    }

    private XuiDocumentOptions CreateDocumentOptions()
    {
        IEnumerable<string> configured = _settings.AssetRoots
            .Where(static root => root.EffectiveIsReadOnly)
            .Select(static root => root.Path);
        if (!string.IsNullOrWhiteSpace(_settings.DyingLightInstallPath))
        {
            configured = configured.Append(_settings.DyingLightInstallPath);
        }

        return new XuiDocumentOptions(
            configured
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .ToArray());
    }

    private void Document_Changed(object? sender, EventArgs eventArgs)
    {
        _recoveryTimer.Stop();
        if (_document?.IsDirty == true)
        {
            _recoveryTimer.Start();
        }

        if (_suppressRefresh)
        {
            _refreshPending = true;
            return;
        }

        RefreshAll();
    }

    private void History_HistoryChanged(object? sender, EventArgs eventArgs) =>
        UpdateChrome();

    private void RefreshAll()
    {
        if (_document is null)
        {
            return;
        }

        EnsureCompiledLayout();
        BuildHierarchy();
        SelectRowsFromKeys(scrollIntoView: false);
        SelectionSnapshot selection = CaptureSelection();
        BuildInspector(selection);
        ResolveTimelineScopeFromSelection(
            selection,
            preferSelectedKeyFrame: true);
        UpdateTimelineData(selection);
        RefreshNamedFrameEditor();
        RefreshEvaluation();
        UpdateChrome();
    }

    private void RefreshEvaluation()
    {
        if (_document is null)
        {
            Viewport.SetFrame(null);
            return;
        }

        try
        {
            EnsureCompiledLayout();
            _layoutEvaluationCount++;
            XuiRenderSample sample = _layoutSession!.SampleWithChanges(
                XuiViewport.Default,
                _timelineWorkspace?.EvaluationState ??
                XuiTimelineEvaluationState.Initial,
                BuildRenderContext());
            XuiRenderFrame frame = sample.Frame;
            Viewport.SetSample(sample);
            Viewport.SetSelectedKeys(_selectedKeys);
            Viewport.SetHiddenKeys(EditorHiddenKeys());
            Viewport.SetLockedKeys(EditorLockedKeys());
            RefreshPreviewState();
            UpdateTimelinePositionChrome();
            if (!_evaluationDiagnosticsInitialized ||
                !_evaluationDiagnostics.SequenceEqual(frame.Diagnostics))
            {
                _evaluationDiagnostics = frame.Diagnostics;
                _evaluationDiagnosticsInitialized = true;
                RefreshDiagnosticsOnly();
            }

            DocumentStatsText.Text =
                $"{frame.Nodes.Count:N0} nodes · {_timelineSet?.Timelines.Count ?? 0:N0} timelines";
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            SetDiagnostics(
            [
                new XuiDiagnostic(
                    "XUI-EVAL001",
                    XuiDiagnosticSeverity.Error,
                    exception.Message),
            ]);
            StatusText.Text = "Evaluation failed safely";
        }
    }

    private void EnsureCompiledLayout()
    {
        if (_document is null)
        {
            _layoutSession = null;
            _timelineSet = null;
            _timelineWorkspace = null;
            return;
        }

        if (_layoutSession?.IsCurrent(_document, _assetResolver) == true)
        {
            _timelineSet = _layoutSession.Timelines;
            _timelineWorkspace ??=
                new XuiTimelineWorkspace(_layoutSession.TimelineScopes);
            return;
        }

        _layoutSession = DyingLightLayoutSession.Compile(
            _document,
            _assetResolver);
        _timelineSet = _layoutSession.Timelines;
        if (_timelineWorkspace is null)
        {
            _timelineWorkspace =
                new XuiTimelineWorkspace(_layoutSession.TimelineScopes);
        }
        else
        {
            _timelineWorkspace.Rebind(_layoutSession.TimelineScopes);
        }
    }

    private void RefreshDiagnosticsOnly()
    {
        SetDiagnostics(
            _evaluationDiagnostics
                .Concat(_assetResolver?.Diagnostics ?? [])
                .Concat(_textureDiagnostics.Values.SelectMany(
                    static diagnostics => diagnostics))
                .ToArray());
    }

    private XuiRenderContext BuildRenderContext()
    {
        XuiPreviewScenario selected =
            PreviewScenarioCombo?.SelectedItem as XuiPreviewScenario ??
            XuiPreviewScenario.Empty;
        return new XuiRenderContext(
            selected,
            _forceShownKeys,
            ForceHiddenTargets: null,
            ResolveLocalization: true);
    }

    private void Viewport_TextureDiagnosticsAvailable(
        object? sender,
        XuiTextureDiagnosticsEventArgs eventArgs)
    {
        _textureDiagnostics[eventArgs.ImagePath] = eventArgs.Diagnostics;
        RefreshDiagnosticsOnly();
    }

    private void BuildHierarchy()
    {
        if (_document is null)
        {
            HierarchyRows.ReplaceAll([]);
            _visibleHierarchyRows.Clear();
            _lastHierarchyFilter = null;
            return;
        }

        bool rebuiltIndex = false;
        if (_hierarchyIndex?.IsCurrent(_document) != true)
        {
            _hierarchyIndex = HierarchyIndex.Build(_document);
            rebuiltIndex = true;
        }

        _hierarchyIndex.UpdateEditorStates(_hiddenKeys, _lockedKeys);
        string filter = HierarchySearch?.Text.Trim() ?? string.Empty;
        IReadOnlyList<HierarchyRow> rows = _hierarchyIndex.Flatten(
            filter,
            _expanded);
        if (rebuiltIndex ||
            !string.Equals(
                filter,
                _lastHierarchyFilter,
                StringComparison.Ordinal))
        {
            HierarchyRows.ReplaceAll(rows);
        }
        else
        {
            HierarchyRows.Synchronize(
                rows,
                ReferenceEqualityComparer.Instance);
        }

        _lastHierarchyFilter = filter;
        _visibleHierarchyRows.Clear();
        foreach (HierarchyRow row in rows)
        {
            _visibleHierarchyRows.Add(row.NodeKey, row);
        }

        HierarchyCountText.Text = $"{rows.Count:N0}";
    }

    private void BuildInspector(SelectionSnapshot? selection = null)
    {
        InspectorProperties.Clear();
        if (_document is null)
        {
            RawXmlExpander.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionSnapshot snapshot = selection ?? CaptureSelection();
        XuiSyntaxNode[] nodes = snapshot.Nodes;
        SelectionCountText.Text = nodes.Length switch
        {
            0 => string.Empty,
            1 => "1 selected",
            _ => $"{nodes.Length} selected",
        };
        InspectorHint.Visibility = nodes.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (nodes.Length == 0)
        {
            BreadcrumbText.Text = string.Empty;
            RawXmlExpander.Visibility = Visibility.Collapsed;
            return;
        }

        List<string> propertyNames = [];
        foreach (XuiSyntaxNode node in nodes)
        {
            foreach (XuiPropertyEntry property in XuiModelReader.GetProperties(
                         node,
                         _document.Text))
            {
                if (!propertyNames.Contains(property.Name, StringComparer.Ordinal))
                {
                    propertyNames.Add(property.Name);
                }
            }
        }

        if (nodes.All(IsIuiTextNode) &&
            !propertyNames.Contains(
                "ColorControlSequenceEnabled",
                StringComparer.Ordinal))
        {
            propertyNames.Add("ColorControlSequenceEnabled");
        }

        foreach (string name in propertyNames)
        {
            string?[] values = nodes
                .Select(node => XuiModelReader.GetPropertyValue(
                    node,
                    _document.Text,
                    name))
                .ToArray();
            bool defaultDisabledColorControl =
                name == "ColorControlSequenceEnabled" &&
                values.All(static value => value is null);
            bool mixed = !defaultDisabledColorControl &&
                         (values.Any(static value => value is null) ||
                          values.Distinct(StringComparer.Ordinal).Count() > 1);
            InspectorPropertyRow row = new(
                name,
                mixed
                    ? MixedValue
                    : defaultDisabledColorControl
                        ? "false"
                        : values[0] ?? string.Empty,
                PropertyCategory(name),
                mixed,
                !KnownProperties.Contains(name),
                InspectorChoices(name),
                isBooleanToggle:
                    name == "ColorControlSequenceEnabled");
            row.Error = mixed ? null : ValidateProperty(name, row.Value);
            InspectorProperties.Add(row);
        }

        BreadcrumbText.Text = snapshot.Breadcrumb;
        RefreshRawXmlEditor(nodes);
    }

    private void RefreshRawXmlEditor(
        XuiSyntaxNode[]? selectedNodes = null,
        bool allowLarge = false)
    {
        if (_document is null ||
            (selectedNodes ?? SelectedNodes()) is not [XuiSyntaxNode selected])
        {
            RawXmlExpander.Visibility = Visibility.Collapsed;
            ClearRawXmlEditor();
            return;
        }

        XuiSyntaxNode? current =
            _document.SyntaxTree.FindByKey(selected.Key);
        if (current is null)
        {
            RawXmlExpander.Visibility = Visibility.Collapsed;
            ClearRawXmlEditor();
            return;
        }

        RawXmlExpander.Visibility = Visibility.Visible;
        int length = current.End - current.Start;
        RawXmlStatusText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{length:N0} characters");
        if (!RawXmlExpander.IsExpanded)
        {
            ClearRawXmlEditor(keepStatus: true);
            return;
        }

        if (length > AutomaticRawXmlCharacterLimit && !allowLarge)
        {
            ClearRawXmlEditor(keepStatus: true);
            RawXmlStatusText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{length / (1024.0 * 1024.0):0.0} MiB of XML — load explicitly to edit");
            LoadLargeRawXmlButton.Visibility = Visibility.Visible;
            return;
        }

        RawXmlTextBox.Text = _document.Text.Substring(
            current.Start,
            length);
        RawXmlTextBox.IsEnabled = true;
        ResetRawXmlButton.IsEnabled = true;
        ApplyRawXmlButton.IsEnabled = true;
        LoadLargeRawXmlButton.Visibility = Visibility.Collapsed;
        _rawXmlLoadedNodeKey = current.Key;
        _rawXmlLoadedRevision = _document.Revision;
        RawXmlErrorText.Text = string.Empty;
    }

    private void ClearRawXmlEditor(bool keepStatus = false)
    {
        RawXmlTextBox.Text = string.Empty;
        RawXmlTextBox.IsEnabled = false;
        ResetRawXmlButton.IsEnabled = false;
        ApplyRawXmlButton.IsEnabled = false;
        LoadLargeRawXmlButton.Visibility = Visibility.Collapsed;
        RawXmlErrorText.Text = string.Empty;
        if (!keepStatus)
        {
            RawXmlStatusText.Text = string.Empty;
        }

        _rawXmlLoadedNodeKey = null;
        _rawXmlLoadedRevision = -1;
    }

    private void CommitInspectorValue(InspectorPropertyRow row)
    {
        if (_document is null ||
            (row.IsMixed && row.Value == MixedValue))
        {
            return;
        }

        string? error = ValidateProperty(row.Name, row.Value);
        row.Error = error;
        if (error is not null)
        {
            StatusText.Text = error;
            return;
        }

        IReadOnlyList<string> keys = _selectedKeys.ToArray();
        bool needsChange = keys.Any(key =>
        {
            XuiSyntaxNode? node = _document.SyntaxTree.FindByKey(key);
            return node is not null &&
                   !string.Equals(
                       XuiModelReader.GetPropertyValue(
                           node,
                           _document.Text,
                           row.Name),
                       row.Value,
                       StringComparison.Ordinal);
        });
        if (!needsChange)
        {
            return;
        }

        ExecuteBatch(() =>
        {
            foreach (string key in keys)
            {
                XuiSyntaxNode? node = _document.SyntaxTree.FindByKey(key);
                if (node is null)
                {
                    continue;
                }

                XuiPropertyEntry? property = XuiModelReader.GetProperty(
                    node,
                    _document.Text,
                    row.Name);
                IXuiCommand command = property is null
                    ? XuiCommandFactory.AddProperty(
                        _document,
                        node,
                        row.Name,
                        row.Value)
                    : XuiCommandFactory.SetElementValue(
                        _document,
                        property.Element,
                        row.Value);
                _document.Execute(command);
            }
        });
    }

    private void UpdateSelectionSurfaces()
    {
        StopPlayback();
        Viewport.SetSelectedKeys(_selectedKeys);
        SelectionSnapshot selection = CaptureSelection();
        BuildInspector(selection);
        ResolveTimelineScopeFromSelection(selection);
        RefreshPreviewState(selection);
        UpdateTimelineData(selection);
        RefreshNamedFrameEditor();
    }

    private void RefreshPreviewState(
        SelectionSnapshot? selection = null)
    {
        SelectionSnapshot snapshot = selection ?? CaptureSelection();
        AddChildButton.IsEnabled =
            _document is not null &&
            snapshot.Nodes.Length == 1;
        if (_document is null || snapshot.Nodes.Length != 1)
        {
            PreviewStatePanel.Visibility = Visibility.Collapsed;
            return;
        }

        XuiSyntaxNode node = snapshot.Nodes[0];
        PreviewStatePanel.Visibility = Visibility.Visible;
        HierarchyRow? row = _hierarchyIndex?.FindRow(node.Key);
        if (row?.VisibilityState is
            HierarchyVisibilityState.Hidden or
            HierarchyVisibilityState.HiddenByAncestor)
        {
            PreviewStateText.Text =
                $"{row.DisplayName} is {row.VisibilityToolTip.ToLowerInvariant()}. " +
                "This is an editor-only hierarchy override.";
            ForceShowInspectorButton.IsEnabled = false;
            RestoreComposedPoseButton.IsEnabled =
                _timelineWorkspace?.ActiveScope is not null &&
                _timelineWorkspace.ActiveTickIsComposed == false;
            return;
        }

        XuiRenderFrame? frame = Viewport.FrameForTesting;
        if (_layoutSession is null || frame is null)
        {
            PreviewStateText.Text =
                "Preview state is not available until the document is rendered.";
            ForceShowInspectorButton.IsEnabled = false;
            RestoreComposedPoseButton.IsEnabled = false;
            return;
        }

        XuiPreviewStateExplanation explanation =
            _layoutSession.ExplainPreviewState(
                node.Key,
                frame,
                _timelineWorkspace?.EvaluationState ??
                XuiTimelineEvaluationState.Initial,
                BuildRenderContext());
        PreviewStateText.Text = explanation.Summary;
        ForceShowInspectorButton.IsEnabled =
            !explanation.IsVisible &&
            explanation.Reason is not
                XuiPreviewStateReason.Clipped and not
                XuiPreviewStateReason.OutsideCanvas;
        RestoreComposedPoseButton.IsEnabled =
            _timelineWorkspace?.ActiveScope is not null &&
            _timelineWorkspace.ActiveTickIsComposed == false;
    }

    private bool ResolveTimelineScopeFromSelection(
        SelectionSnapshot? selection = null,
        bool preferSelectedKeyFrame = false)
    {
        if (_document is null || _timelineWorkspace is null)
        {
            return false;
        }

        return _timelineWorkspace.ResolveScopes(
            (selection ?? CaptureSelection()).Scopes,
            preferSelectedKeyFrame
                ? TimelineEditor.SelectedTimeline?.ScopeKey
                : null);
    }

    private void UpdateTimelineData(SelectionSnapshot? selection = null)
    {
        if (_document is null)
        {
            TimelineEditor.SetScopeData(
                null,
                activeScopeKey: null,
                [],
                tick: 0,
                showAllInScope: false,
                mixedScopes: false);
            RefreshKeyFrameEditor();
            UpdateTimelineScopeChrome();
            return;
        }

        IReadOnlyList<string> selectedIds =
            (selection ?? CaptureSelection()).Ids;
        TimelineEditor.SetScopeData(
            _timelineWorkspace?.ActiveScope,
            selectedIds,
            CurrentTimelineTick,
            AllTracksToggle.IsChecked == true,
            _timelineWorkspace?.HasMixedSelection == true);
        UpdateTimelineScopeChrome();
        RefreshKeyFrameEditor();
        UpdateTimelinePositionChrome();
    }

    private void UpdateTimelinePositionChrome()
    {
        int currentTick = CurrentTimelineTick;
        TimelineEditor.SetTick(currentTick);
        int maximum = Math.Max(
            1,
            _timelineWorkspace?.ActiveScope?.MaximumTick ?? 0);
        _updatingTick = true;
        TickSlider.Maximum = maximum;
        TickSlider.Value = Math.Clamp(currentTick, 0, maximum);
        _updatingTick = false;
        TickText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{currentTick} ticks  ·  {currentTick / 60.0:0.000}s");
        UpdateTimelineScopeChrome();
    }

    private void UpdateTimelineScopeChrome()
    {
        XuiTimelineScope? scope = _timelineWorkspace?.ActiveScope;
        bool enabled = scope is not null &&
                       _timelineWorkspace?.HasMixedSelection != true;
        TimelineTransportPanel.IsEnabled = enabled;
        TimelineEditPanel.IsEnabled = enabled;
        TimelineEditor.IsEnabled = enabled;
        TimelineScopeText.Text = _timelineWorkspace?.HasMixedSelection == true
            ? "Mixed timeline scopes"
            : scope is null
                ? "No timeline scope"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{scope.DisplayName} · {CurrentTimelineTick} / {scope.MaximumTick}{(_timelineWorkspace?.ActiveTickIsComposed == true ? " · composed" : string.Empty)}");
    }

    private void RefreshKeyFrameEditor()
    {
        _updatingTimelineEditors = true;
        try
        {
            XuiKeyFrame? selected = TimelineEditor.SelectedKeyFrame;
            XuiTrack? track = TimelineEditor.SelectedTrack;
            bool enabled = selected is not null && track is not null;
            KeyValueTextBox.IsEnabled = enabled;
            KeyInterpolationComboBox.IsEnabled = enabled;
            EaseInTextBox.IsEnabled = enabled;
            EaseOutTextBox.IsEnabled = enabled;
            EaseScaleTextBox.IsEnabled = enabled;
            KeyValueErrorText.Text = string.Empty;
            if (selected is null || track is null)
            {
                KeyPropertyText.Text = string.Empty;
                KeyValueTextBox.Text = string.Empty;
                KeyInterpolationComboBox.SelectedIndex = -1;
                EaseInTextBox.Text = string.Empty;
                EaseOutTextBox.Text = string.Empty;
                EaseScaleTextBox.Text = string.Empty;
                return;
            }

            KeyPropertyText.Text = track.Property.ToString();
            XuiSyntaxNode? current =
                _document?.SyntaxTree.FindByKey(selected.Syntax.Key);
            XuiSyntaxNode? prop = current?
                .Elements("Prop")
                .ElementAtOrDefault(track.SourcePropertyIndex);
            KeyValueTextBox.Text = prop?.GetDecodedValue(_document!.Text) ??
                                   selected.Values
                                       .ElementAtOrDefault(track.PropertyIndex)
                                       ?.ToXuiString() ??
                                   string.Empty;
            KeyInterpolationComboBox.SelectedIndex =
                selected.RawInterpolation == 0
                    ? 0
                    : selected.RawInterpolation == 2
                        ? 1
                        : -1;
            EaseInTextBox.Text = selected.EaseIn.ToString(
                "0.######",
                CultureInfo.InvariantCulture);
            EaseOutTextBox.Text = selected.EaseOut.ToString(
                "0.######",
                CultureInfo.InvariantCulture);
            EaseScaleTextBox.Text = selected.EaseScale.ToString(
                "0.######",
                CultureInfo.InvariantCulture);
        }
        finally
        {
            _updatingTimelineEditors = false;
        }
    }

    private void CommitKeyFrameValue()
    {
        if (_updatingTimelineEditors ||
            _document is null ||
            TimelineEditor.SelectedKeyFrame is not XuiKeyFrame selected ||
            TimelineEditor.SelectedTrack is not XuiTrack track)
        {
            return;
        }

        string value = KeyValueTextBox.Text;
        if (!XuiTimelineParser.TryParsePropertyValue(
                track.Property,
                value,
                out _))
        {
            string message =
                $"'{value}' is invalid for {track.Property}. The keyframe was not changed.";
            KeyValueErrorText.Text = message;
            StatusText.Text = message;
            return;
        }

        XuiSyntaxNode? current =
            _document.SyntaxTree.FindByKey(selected.Syntax.Key);
        XuiSyntaxNode? prop = current?
            .Elements("Prop")
            .ElementAtOrDefault(track.SourcePropertyIndex);
        if (prop is null)
        {
            KeyValueErrorText.Text =
                "This malformed keyframe has no corresponding Prop element.";
            StatusText.Text = KeyValueErrorText.Text;
            return;
        }

        if (string.Equals(
                prop.GetDecodedValue(_document.Text),
                value,
                StringComparison.Ordinal))
        {
            KeyValueErrorText.Text = string.Empty;
            return;
        }

        _document.Execute(XuiCommandFactory.SetElementValue(
            _document,
            prop,
            value));
    }

    private void CommitKeyFrameEase()
    {
        if (_updatingTimelineEditors ||
            _document is null ||
            TimelineEditor.SelectedKeyFrame is not XuiKeyFrame selected)
        {
            return;
        }

        if (!XuiValueParser.TryNumber(EaseInTextBox.Text, out _) ||
            !XuiValueParser.TryNumber(EaseOutTextBox.Text, out _) ||
            !XuiValueParser.TryNumber(EaseScaleTextBox.Text, out _))
        {
            StatusText.Text = "Ease In, Out, and Scale must be finite numbers.";
            RefreshKeyFrameEditor();
            return;
        }

        string key = selected.Syntax.Key;
        ExecuteBatch(() =>
        {
            SetTimelineChildValue(
                key,
                "EaseIn",
                EaseInTextBox.Text.Trim(),
                removeWhenEmpty: false);
            SetTimelineChildValue(
                key,
                "EaseOut",
                EaseOutTextBox.Text.Trim(),
                removeWhenEmpty: false);
            SetTimelineChildValue(
                key,
                "EaseScale",
                EaseScaleTextBox.Text.Trim(),
                removeWhenEmpty: false);
        });
    }

    private void RefreshNamedFrameEditor()
    {
        _updatingTimelineEditors = true;
        try
        {
            IReadOnlyList<XuiNamedFrame> frames =
                _timelineWorkspace?.ActiveScope?.NamedFrames ?? [];
            string? selectedKey = _selectedNamedFrameKey;
            NamedFrameComboBox.ItemsSource = frames;
            XuiNamedFrame? selected = frames.FirstOrDefault(frame =>
                frame.Syntax.Key == selectedKey);
            selected ??= frames.Count > 0 ? frames[0] : null;
            NamedFrameComboBox.SelectedItem = selected;
            _selectedNamedFrameKey = selected?.Syntax.Key;
            PopulateNamedFrameFields(selected);
        }
        finally
        {
            _updatingTimelineEditors = false;
        }
    }

    private void PopulateNamedFrameFields(XuiNamedFrame? frame)
    {
        bool enabled = frame is not null;
        NamedFrameNameTextBox.IsEnabled = enabled;
        NamedFrameTickTextBox.IsEnabled = enabled;
        NamedFrameCommandComboBox.IsEnabled = enabled;
        NamedFrameTargetTextBox.IsEnabled = enabled;
        NamedFrameNameTextBox.Text = frame?.Name ?? string.Empty;
        NamedFrameTickTextBox.Text = frame?.Tick.ToString(
            CultureInfo.InvariantCulture) ?? string.Empty;
        NamedFrameCommandComboBox.Text = frame?.Command ?? string.Empty;
        NamedFrameTargetTextBox.Text = frame?.CommandParameter ?? string.Empty;
    }

    private void SetTimelineChildValue(
        string parentKey,
        string name,
        string value,
        bool removeWhenEmpty)
    {
        if (_document is null)
        {
            return;
        }

        XuiSyntaxNode? parent = _document.SyntaxTree.FindByKey(parentKey);
        if (parent is null)
        {
            return;
        }

        XuiSyntaxNode? child = parent.FirstElement(name);
        if (removeWhenEmpty && value.Length == 0)
        {
            if (child is not null)
            {
                _document.Execute(XuiCommandFactory.RemoveElement(
                    _document,
                    child));
            }

            return;
        }

        if (child is not null &&
            string.Equals(
                child.GetDecodedValue(_document.Text),
                value,
                StringComparison.Ordinal))
        {
            return;
        }

        IXuiCommand command = child is null
            ? XuiCommandFactory.InsertChildXml(
                _document,
                parent,
                $"<{name}>{WebUtility.HtmlEncode(value)}</{name}>",
                $"Add {name}")
            : XuiCommandFactory.SetElementValue(_document, child, value);
        _document.Execute(command);
    }

    private static string CreateNamedFrameXml(
        string name,
        int tick,
        string command,
        string target,
        string newline)
    {
        List<string> lines =
        [
            "<NamedFrame>",
            $"<Name>{WebUtility.HtmlEncode(name)}</Name>",
            $"<Time>{tick.ToString(CultureInfo.InvariantCulture)}</Time>",
        ];
        if (command.Length > 0)
        {
            lines.Add($"<Command>{WebUtility.HtmlEncode(command)}</Command>");
        }

        if (target.Length > 0)
        {
            lines.Add(
                $"<CommandParams>{WebUtility.HtmlEncode(target)}</CommandParams>");
        }

        lines.Add("</NamedFrame>");
        return string.Join(newline, lines);
    }

    private void SelectRowsFromKeys(bool scrollIntoView = false)
    {
        _syncingSelection = true;
        try
        {
            HierarchyRow[] selectedRows =
                HierarchyList.SelectedItems.Cast<HierarchyRow>().ToArray();
            foreach (HierarchyRow row in selectedRows)
            {
                if (!_selectedKeys.Contains(row.NodeKey))
                {
                    HierarchyList.SelectedItems.Remove(row);
                }
            }

            foreach (string key in _selectedKeys)
            {
                if (_visibleHierarchyRows.TryGetValue(key, out HierarchyRow? row) &&
                    !HierarchyList.SelectedItems.Contains(row))
                {
                    HierarchyList.SelectedItems.Add(row);
                }
            }

            if (scrollIntoView && HierarchyList.SelectedItems.Count > 0)
            {
                HierarchyList.ScrollIntoView(HierarchyList.SelectedItems[0]);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private bool EnsureSelectedAncestorsExpanded()
    {
        if (_document is null || _hierarchyIndex is null)
        {
            return false;
        }

        bool changed = false;
        foreach (string key in _selectedKeys)
        {
            foreach (string ancestorKey in _hierarchyIndex.Ancestors(key))
            {
                changed |= _expanded.Add(ancestorKey);
            }
        }

        return changed;
    }

    private XuiSyntaxNode[] SelectedNodes()
    {
        if (_document is null)
        {
            return [];
        }

        return _selectedKeys
            .Select(_document.SyntaxTree.FindByKey)
            .Where(static node => node is not null)
            .Cast<XuiSyntaxNode>()
            .OrderBy(static node => node.Start)
            .ToArray();
    }

    private SelectionSnapshot CaptureSelection()
    {
        XuiSyntaxNode[] nodes = SelectedNodes();
        if (_document is null || nodes.Length == 0)
        {
            return SelectionSnapshot.Empty;
        }

        string[] ids = nodes
            .Select(node => XuiModelReader.GetId(node, _document.Text))
            .Where(static id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToArray();
        XuiTimelineScope[] scopes = _timelineWorkspace is null
            ? []
            : nodes
                .Select(node =>
                    _timelineWorkspace.Catalog.ResolveForNode(
                        node,
                        _document.Text))
                .Where(static scope => scope is not null)
                .Cast<XuiTimelineScope>()
                .DistinctBy(static scope => scope.ScopeKey)
                .ToArray();
        return new SelectionSnapshot(
            nodes,
            ids,
            scopes,
            BuildBreadcrumb(nodes[0]));
    }

    private void MoveSelected(int direction)
    {
        XuiSyntaxNode[] selected = SelectedNodes();
        if (_document is null || selected.Length == 0)
        {
            return;
        }

        XuiSyntaxNode node = selected[0];
        try
        {
            _document.Execute(XuiCommandFactory.MoveSibling(
                _document,
                node,
                direction));
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void IndentSelected()
    {
        if (_document is null || SelectedNodes() is not [XuiSyntaxNode node])
        {
            return;
        }

        XuiSyntaxNode? parent = node.Parent;
        if (parent is null)
        {
            return;
        }

        List<XuiSyntaxNode> siblings =
            XuiModelReader.VisualChildren(parent).ToList();
        int index = siblings.IndexOf(node);
        if (index <= 0)
        {
            StatusText.Text =
                "Indent needs a previous visual sibling to become the new parent.";
            return;
        }

        ReparentAndReselect(node, siblings[index - 1], int.MaxValue);
    }

    private void OutdentSelected()
    {
        if (_document is null || SelectedNodes() is not [XuiSyntaxNode node])
        {
            return;
        }

        XuiSyntaxNode? parent = node.Parent;
        XuiSyntaxNode? grandParent = parent?.Parent;
        if (parent is null ||
            grandParent is null ||
            parent == _document.Root ||
            grandParent.Kind == XuiSyntaxKind.Document)
        {
            StatusText.Text = "The selected element is already at the top level.";
            return;
        }

        List<XuiSyntaxNode> siblings =
            XuiModelReader.VisualChildren(grandParent).ToList();
        int parentIndex = siblings.IndexOf(parent);
        if (parentIndex < 0)
        {
            return;
        }

        ReparentAndReselect(node, grandParent, parentIndex + 1);
    }

    private void ReparentAndReselect(
        XuiSyntaxNode node,
        XuiSyntaxNode newParent,
        int childIndex)
    {
        if (_document is null)
        {
            return;
        }

        string? id = XuiModelReader.GetId(node, _document.Text);
        string elementName = node.Name;
        string? parentId = XuiModelReader.GetId(newParent, _document.Text);
        try
        {
            _document.Execute(XuiCommandFactory.ReparentElement(
                _document,
                node,
                newParent,
                childIndex));
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        XuiSyntaxNode? moved = XuiModelReader.VisualDescendants(_document.Root)
            .FirstOrDefault(candidate =>
                candidate.Name == elementName &&
                (id is null ||
                 XuiModelReader.GetId(candidate, _document.Text) == id) &&
                (parentId is null ||
                 XuiModelReader.GetId(candidate.Parent!, _document.Text) ==
                 parentId));
        moved ??= XuiModelReader.VisualDescendants(_document.Root)
            .FirstOrDefault(candidate =>
                candidate.Name == elementName &&
                id is not null &&
                XuiModelReader.GetId(candidate, _document.Text) == id);
        _selectedKeys.Clear();
        if (moved is not null)
        {
            _selectedKeys.Add(moved.Key);
            EnsureSelectedAncestorsExpanded();
        }

        BuildHierarchy();
        SelectRowsFromKeys();
        UpdateSelectionSurfaces();
    }

    private void ExecuteBatch(
        Action edits,
        string description = "Edit selection")
    {
        _suppressRefresh = true;
        _refreshPending = false;
        try
        {
            if (_document is null)
            {
                edits();
            }
            else
            {
                _document.ExecuteBatch(description, edits);
            }
        }
        catch (Exception exception) when (
            exception is XuiParseException or
            InvalidOperationException or
            ArgumentException)
        {
            _refreshPending = true;
            StatusText.Text =
                $"Edit rejected; the document was restored: {exception.Message}";
        }
        finally
        {
            _suppressRefresh = false;
            if (_refreshPending)
            {
                RefreshAll();
            }
        }
    }

    private XuiSyntaxNode? FindNodeAtStart(int start) =>
        _document?.SyntaxTree.FindByStart(start);

    private void SetCurrentTick(int tick)
    {
        if (_timelineWorkspace?.ActiveScope is null)
        {
            return;
        }

        _timelineWorkspace.SetActiveTick(tick);
        RefreshEvaluation();
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs eventArgs)
    {
        if (!_isPlaying ||
            _timelineSet is null ||
            _timelineWorkspace?.ActiveScope is not XuiTimelineScope scope)
        {
            return;
        }

        double speed = SelectedSpeed();
        double elapsed = _playbackClock.Elapsed.TotalSeconds;
        _playbackClock.Restart();
        _playbackRemainder += elapsed * TimelineEvaluator.TicksPerSecond * speed;
        int steps = Math.Min(240, (int)_playbackRemainder);
        _playbackRemainder -= steps;
        if (steps <= 0)
        {
            return;
        }

        int tick = CurrentTimelineTick;
        bool playing = true;
        for (int index = 0; index < steps && playing; index++)
        {
            TimelinePlaybackState state = TimelinePlayback.Advance(
                scope,
                tick,
                true,
                LoopCheckBox.IsChecked == true);
            tick = state.Tick;
            playing = state.IsPlaying;
            foreach (XuiDiagnostic diagnostic in state.Diagnostics)
            {
                FilteredDiagnostics.Add(diagnostic);
            }
        }

        SetCurrentTick(tick);
        if (!playing)
        {
            StopPlayback();
        }
    }

    private void StopPlayback()
    {
        _isPlaying = false;
        _playbackTimer.Stop();
        _playbackClock.Stop();
        PlayPauseButton.Content = "Play";
    }

    private double SelectedSpeed()
    {
        if (SpeedComboBox.SelectedItem is ComboBoxItem { Tag: string value } &&
            double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double speed))
        {
            return speed;
        }

        return 1;
    }

    private XuiTimeline? TimelineForKeyEditing()
    {
        if (_timelineSet is null ||
            _timelineWorkspace?.ActiveScope is not XuiTimelineScope scope)
        {
            return null;
        }

        XuiKeyFrame? selected = TimelineEditor.SelectedKeyFrame;
        if (selected is not null)
        {
            return scope.Timelines.FirstOrDefault(timeline =>
                timeline.Tracks.Any(track =>
                    track.KeyFrames.Any(frame =>
                        frame.Syntax.Key == selected.Syntax.Key)));
        }

        HashSet<string> selectedIds = SelectedNodes()
            .Select(node => XuiModelReader.GetId(node, _document!.Text))
            .Where(static id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        return scope.Timelines.FirstOrDefault(timeline =>
            selectedIds.Contains(timeline.TargetId));
    }

    private void SetDiagnostics(IReadOnlyList<XuiDiagnostic> diagnostics)
    {
        _allDiagnostics = diagnostics;
        FilterDiagnostics();
    }

    private IReadOnlyList<XuiDiagnostic> _allDiagnostics = [];

    private void FilterDiagnostics()
    {
        string filter = DiagnosticsSearch?.Text.Trim() ?? string.Empty;
        FilteredDiagnostics.ReplaceAll(
            _allDiagnostics.Where(diagnostic =>
                filter.Length == 0 ||
                diagnostic.Code.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                diagnostic.Message.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                diagnostic.NodeKey?.Contains(
                    filter,
                    StringComparison.OrdinalIgnoreCase) == true));
    }

    private async Task<bool> SaveDocumentAsync(bool forceSaveAs)
    {
        if (_document is null)
        {
            return true;
        }

        string? target = null;
        if (forceSaveAs || _document.Path is null)
        {
            SaveFileDialog dialog = new()
            {
                Title = "Save Dying Light XUI As",
                Filter = "Dying Light XUI (*.xui)|*.xui|All files (*.*)|*.*",
                AddExtension = true,
                DefaultExt = ".xui",
                FileName = Path.GetFileName(
                    _recoverySuggestedPath ??
                    _document.Path ??
                    _document.DisplayName),
                InitialDirectory = InitialSaveDirectory(),
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            target = dialog.FileName;
        }

        try
        {
            string? priorPath = _document.Path;
            _ignoreWatcherUntilUtc = DateTime.UtcNow.AddSeconds(1);
            XuiSaveResult result = await _document.SaveAsync(target).ConfigureAwait(true);
            RecoveryService.DeleteForPath(priorPath);
            if (_activeRecovery is not null)
            {
                RecoveryService.Delete(_activeRecovery);
                _activeRecovery = null;
            }

            _recoverySuggestedPath = null;
            AddRecentFile(result.Path);
            ConfigureWatcher(result.Path);
            StatusText.Text = result.Disposition == XuiSaveDisposition.Unchanged
                ? "No changes to save"
                : result.BackupPath is null
                    ? "Saved"
                    : $"Saved · backup: {Path.GetFileName(result.BackupPath)}";
            UpdateChrome();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            if (!forceSaveAs)
            {
                StatusText.Text = "Source root is read-only; choose a workspace copy.";
                return await SaveDocumentAsync(forceSaveAs: true).ConfigureAwait(true);
            }

            return false;
        }
        catch (Exception exception) when (
            exception is IOException or XuiParseException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Could not save XUI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_document?.IsDirty != true)
        {
            return true;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            "Save changes before opening another file?",
            "Unsaved XUI changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => await SaveDocumentAsync(false).ConfigureAwait(true),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private async void RecoveryTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _recoveryTimer.Stop();
        if (_document?.IsDirty != true)
        {
            return;
        }

        try
        {
            _activeRecovery = await RecoveryService.WriteAsync(
                _document).ConfigureAwait(true);
            StatusText.Text = "Recovery snapshot saved";
        }
        catch (IOException)
        {
            StatusText.Text = "Recovery snapshot could not be written";
        }
    }

    private void ConfigureWatcher(string path)
    {
        _watcher?.Dispose();
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            return;
        }

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter =
                NotifyFilters.LastWrite |
                NotifyFilters.Size |
                NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += ExternalFile_Changed;
        _watcher.Renamed += ExternalFile_Changed;
        _watcher.Deleted += ExternalFile_Changed;
    }

    private void ExternalFile_Changed(object sender, FileSystemEventArgs eventArgs)
    {
        if (DateTime.UtcNow <= _ignoreWatcherUntilUtc)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text =
                "Source changed externally · Save is blocked until reload or Save As";
        });
    }

    private void UpdateChrome()
    {
        string display = _document is null
            ? "Untitled"
            : _document.Path is null && _recoverySuggestedPath is not null
                ? $"Recovered · {Path.GetFileName(_recoverySuggestedPath)}"
                : _document.DisplayName;
        bool dirty = _document?.IsDirty == true;
        Title = $"{(dirty ? "● " : string.Empty)}{display} — Dying Light XUI Editor";
        DocumentPathText.Text = _document?.Path ??
                                _document?.Source?.Origin ??
                                _recoverySuggestedPath ??
                                string.Empty;
        DirtyText.Text = dirty
            ? "Modified"
            : _document?.Source?.IsReadOnly == true
                ? "Read-only stock"
                : "Saved";
        UndoMenuItem.IsEnabled = _document?.History.CanUndo == true;
        RedoMenuItem.IsEnabled = _document?.History.CanRedo == true;
        UndoMenuItem.Header = _document?.History.UndoDescription is string undo
            ? $"_Undo {undo}"
            : "_Undo";
        RedoMenuItem.Header = _document?.History.RedoDescription is string redo
            ? $"_Redo {redo}"
            : "_Redo";
    }

    private void ApplyViewportSettings()
    {
        Viewport.ShowGrid = _settings.ShowGrid;
        Viewport.ShowSafeArea = _settings.ShowSafeArea;
        Viewport.ShowUnknownBounds = _settings.ShowUnknownBounds;
        Viewport.SnapEnabled = _settings.SnapEnabled;
        Viewport.GridSize = Math.Max(1, _settings.GridSize);
        if (_document is null)
        {
            Viewport.SetFrame(null);
            return;
        }

        RefreshEvaluation();
    }

    private void AddRecentFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        _settings.RecentFiles.RemoveAll(existing =>
            string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, fullPath);
        if (_settings.RecentFiles.Count > 12)
        {
            _settings.RecentFiles.RemoveRange(
                12,
                _settings.RecentFiles.Count - 12);
        }

        RebuildRecentFilesMenu();
    }

    private void RebuildRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();
        foreach (string path in _settings.RecentFiles.Where(File.Exists))
        {
            MenuItem item = new()
            {
                Header = path,
                ToolTip = path,
            };
            item.Click += async (_, _) =>
            {
                if (await ConfirmDiscardAsync().ConfigureAwait(true))
                {
                    await OpenDocumentAsync(path).ConfigureAwait(true);
                }
            };
            RecentFilesMenu.Items.Add(item);
        }

        RecentFilesMenu.IsEnabled = RecentFilesMenu.Items.Count > 0;
    }

    private void SaveWindowSettings()
    {
        _settings.WindowWidth = ActualWidth;
        _settings.WindowHeight = ActualHeight;
        _settings.HierarchyWidth = HierarchyColumn.ActualWidth;
        _settings.InspectorWidth = InspectorColumn.ActualWidth;
        _settings.TimelineHeight = TimelineRow.ActualHeight;
    }

    private string InitialSaveDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_settings.WorkspaceRoot) &&
            Directory.Exists(_settings.WorkspaceRoot))
        {
            return _settings.WorkspaceRoot;
        }

        string? suggestedDirectory = Path.GetDirectoryName(_recoverySuggestedPath);
        if (suggestedDirectory is not null && Directory.Exists(suggestedDirectory))
        {
            return suggestedDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private bool IsLocked(string key)
    {
        HierarchyRow? row = _hierarchyIndex?.FindRow(key);
        if (row is not null)
        {
            return row.LockState != HierarchyLockState.Unlocked;
        }

        XuiSyntaxNode? node = _document?.SyntaxTree.FindByKey(key);
        while (node is not null)
        {
            if (_lockedKeys.Contains(node.Key))
            {
                return true;
            }

            node = node.Parent;
        }

        return false;
    }

    private static bool IsCanvasRoot(XuiSyntaxNode node) =>
        node.Parent is null ||
        node.Name.Equals(
            "XuiCanvas",
            StringComparison.OrdinalIgnoreCase);

    private static bool PathIsInside(string root, string candidate)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root));
        string fullCandidate = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(candidate));
        string relative = Path.GetRelativePath(fullRoot, fullCandidate);
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith(
                   ".." + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal);
    }

    internal static string FindDocumentAssetRoot(string documentDirectory)
    {
        return XuiDocumentAssetContext
            .Discover(documentDirectory)
            .Root
            .FullPath;
    }

    private IReadOnlySet<string> EditorHiddenKeys()
    {
        if (_hierarchyIndex is not null)
        {
            return _hierarchyIndex.EffectivelyHiddenKeys;
        }

        if (_document is null || _hiddenKeys.Count == 0)
        {
            return _hiddenKeys;
        }

        HashSet<string> effective = new(_hiddenKeys, StringComparer.Ordinal);
        foreach (string key in _hiddenKeys)
        {
            XuiSyntaxNode? node = _document.SyntaxTree.FindByKey(key);
            if (node is null)
            {
                continue;
            }

            effective.UnionWith(
                XuiModelReader.VisualDescendants(node)
                    .Select(static descendant => descendant.Key));
        }

        return effective;
    }

    private IReadOnlySet<string> EditorLockedKeys()
    {
        if (_hierarchyIndex is not null)
        {
            return _hierarchyIndex.EffectivelyLockedKeys;
        }

        if (_document is null || _lockedKeys.Count == 0)
        {
            return _lockedKeys;
        }

        HashSet<string> effective = new(_lockedKeys, StringComparer.Ordinal);
        foreach (string key in _lockedKeys)
        {
            XuiSyntaxNode? node = _document.SyntaxTree.FindByKey(key);
            if (node is null)
            {
                continue;
            }

            effective.UnionWith(
                XuiModelReader.VisualDescendants(node)
                    .Select(static descendant => descendant.Key));
        }

        return effective;
    }

    private string BuildBreadcrumb(XuiSyntaxNode node)
    {
        if (_document is null)
        {
            return string.Empty;
        }

        Stack<string> parts = new();
        XuiSyntaxNode? current = node;
        while (current is not null)
        {
            string id = XuiModelReader.GetId(current, _document.Text) ?? current.Name;
            parts.Push(id);
            current = current.Parent;
        }

        return string.Join("  ›  ", parts);
    }

    private static string PropertyCategory(string name)
    {
        if (name is "Id" or "ClassOverride" or "Visual")
        {
            return "Identity";
        }

        if (name is "Width" or "Height" or "Position" or "Anchor" or "Pivot" or
            "Scale" or "Rotation" || name.StartsWith("Keep", StringComparison.Ordinal) ||
            name.StartsWith("HoldAspect", StringComparison.Ordinal) ||
            name.Contains("Resolution", StringComparison.Ordinal) ||
            name.Contains("ParentSize", StringComparison.Ordinal))
        {
            return "Layout";
        }

        if (name is "Opacity" or "Show" or "Color" or "Material" or "UseMask" or
            "MaskSource" or "ClipChildren" or "ClipMaskChannel" or
            "ForceMaterials" or "ImageMaskMaterial" or "TextMaskMaterial" or
            "AARectangleMaskMaterial")
        {
            return "Appearance";
        }

        if (name.Contains("Text", StringComparison.Ordinal) ||
            name.Contains("Font", StringComparison.Ordinal) ||
            name.Contains("Image", StringComparison.Ordinal) ||
            name is "PointSize" or "Uppercase" or "SizeMode" or
            "MultiLine" or "VerticalAlignDown" or "Outline" or
            "OutlineSize" or "OutlineColor" or "Shadow" or
            "ShadowColor" or "DropShadowColor" or "ShadowOffset" or
            "Bold" or "Italic" or "Underline" or "Strike" or
            "SourceString" or "CharacterSpacingAdjust" or
            "LineSpacingAdjust" or "ColorControlSequenceEnabled")
        {
            return "Text / Image";
        }

        if (name.StartsWith("Nav", StringComparison.Ordinal))
        {
            return "Navigation";
        }

        if (name is "TextProgress" or "Const0" or "Const1" or
            "DisableTimelineRecursion")
        {
            return "Animation";
        }

        return "Raw / Unknown";
    }

    private bool IsIuiTextNode(XuiSyntaxNode node)
    {
        if (_document is null)
        {
            return false;
        }

        string classOverride =
            XuiModelReader.GetPropertyValue(
                node,
                _document.Text,
                "ClassOverride") ??
            string.Empty;
        string combined = node.Name + " " + classOverride;
        return combined.Contains(
                   "Text",
                   StringComparison.OrdinalIgnoreCase) ||
               combined.Contains(
                   "Html",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidateProperty(string name, string value)
    {
        if (BooleanProperties.Contains(name) &&
            !XuiValueParser.TryBoolean(value, out _))
        {
            return $"{name} must be true or false.";
        }

        if (name is "Outline" or "Shadow")
        {
            return XuiValueParser.TryBoolean(value, out _) ||
                   XuiValueParser.TryNumber(value, out _)
                ? null
                : $"{name} must be true, false, or a finite numeric strength.";
        }

        if (NumberProperties.Contains(name) &&
            !XuiValueParser.TryNumber(value, out _))
        {
            return $"{name} must be a finite number.";
        }

        if (name == "Anchor" &&
            (!XuiValueParser.TryInteger(value, out int anchor) ||
             anchor < 0 ||
             (anchor & ~0x7f) != 0))
        {
            return "Anchor must be a valid Dying Light bitmask (0–127).";
        }

        if (name is "Position" or "Pivot" or "Scale" &&
            !XuiValueParser.TryVector3(value, out _) &&
            !XuiValueParser.TryVector2(value, out _))
        {
            return $"{name} must contain two or three comma-separated numbers.";
        }

        if (name == "Rotation" &&
            !XuiValueParser.TryQuaternion(value, out _) &&
            !XuiValueParser.TryVector3(value, out _) &&
            !XuiValueParser.TryNumber(value, out _))
        {
            return "Rotation must be a quaternion, Euler vector, or numeric angle.";
        }

        if (ColorProperties.Contains(name) &&
            !XuiValueParser.TryColor(value, out _))
        {
            return $"{name} must be 0xAARRGGBB, #AARRGGBB, or #RRGGBB.";
        }

        if ((name is "ContentHorizontalAlign" or "DefaultHorizontalAlign") &&
            value.Trim().ToLowerInvariant() is not
                ("left" or "center" or "right" or "justify" or
                 "0" or "1" or "2" or "3"))
        {
            return $"{name} must be left, center, right, or justify.";
        }

        if ((name is "ContentVerticalAlign" or "DefaultVerticalAlign") &&
            value.Trim().ToLowerInvariant() is not
                ("top" or "middle" or "bottom" or "0" or "1" or "2"))
        {
            return $"{name} must be top, middle, or bottom.";
        }

        return null;
    }

    private static IReadOnlyList<string> InspectorChoices(string name)
    {
        if (BooleanProperties.Contains(name))
        {
            return ["true", "false"];
        }

        if (name is "ContentHorizontalAlign" or "DefaultHorizontalAlign")
        {
            return ["left", "center", "right", "justify"];
        }

        return name is "ContentVerticalAlign" or "DefaultVerticalAlign"
            ? ["top", "middle", "bottom"]
            : [];
    }

    private static string DefaultTimelineValue(XuiTimelineProperty property) =>
        property switch
        {
            XuiTimelineProperty.Show => "true",
            XuiTimelineProperty.Scale => "1.000000,1.000000,1.000000",
            XuiTimelineProperty.Position or
            XuiTimelineProperty.Pivot => "0.000000,0.000000,0.000000",
            XuiTimelineProperty.Rotation =>
                "0.000000,0.000000,0.000000,1.000000",
            XuiTimelineProperty.Color or
            XuiTimelineProperty.TextColor or
            XuiTimelineProperty.OutlineColor or
            XuiTimelineProperty.DefaultFontColor => "0xffffffff",
            XuiTimelineProperty.ImagePath or
            XuiTimelineProperty.Material => string.Empty,
            _ => "0.000000",
        };

    private static string ReplaceKeyFrameTime(string raw, int tick)
    {
        const string open = "<Time>";
        const string close = "</Time>";
        int start = raw.IndexOf(open, StringComparison.Ordinal);
        int end = start < 0
            ? -1
            : raw.IndexOf(close, start + open.Length, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            return raw;
        }

        int valueStart = start + open.Length;
        return string.Concat(
            raw.AsSpan(0, valueStart),
            tick.ToString(CultureInfo.InvariantCulture),
            raw.AsSpan(end));
    }

    private sealed record SelectionSnapshot(
        XuiSyntaxNode[] Nodes,
        string[] Ids,
        XuiTimelineScope[] Scopes,
        string Breadcrumb)
    {
        public static SelectionSnapshot Empty { get; } =
            new([], [], [], string.Empty);
    }

    private sealed record PositionMovePlan(
        string NodeKey,
        string AuthoredValue,
        IReadOnlyList<PositionKeyMove> KeyMoves);

    private sealed record PositionKeyMove(
        string PropKey,
        string Value);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
        _playbackTimer.Stop();
        _recoveryTimer.Stop();
        _hierarchySearchTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
