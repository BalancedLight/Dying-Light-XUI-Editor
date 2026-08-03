using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Editing;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Navigation;
using XuiEditor.Core.Schema;
using XuiEditor.Core.Values;
using XuiEditor.Wpf.Controls;
using XuiEditor.Wpf.Models;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class MainWindow : Window, IDisposable
{
    private static string MixedValue =>
        UiLocalization.Text("Ui.Common.Mixed");
    private const int AutomaticRawXmlCharacterLimit = 256 * 1024;
    private const double SnapshotExportScale = 2;
    private const string HierarchyDragDataFormat =
        "XuiEditor.Wpf.HierarchyNodeKey";
    private static readonly string[] NavigationPropertyNames =
    [
        "NavLeft",
        "NavRight",
        "NavUp",
        "NavDown",
        "NavTabForward",
        "NavTabBackward",
    ];
    private static readonly XuiClassCatalog ClassCatalog =
        XuiClassCatalog.Default;
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
    private DyingLightXuiAssetCatalog? _assetCatalog;
    private DyingLightLayoutSession? _layoutSession;
    private HierarchyIndex? _hierarchyIndex;
    private string? _lastHierarchyFilter;
    private XuiTimelineSet? _timelineSet;
    private XuiTimelineWorkspace? _timelineWorkspace;
    private bool _syncingSelection;
    private bool _updatingTick;
    private bool _updatingTimelineEditors;
    private bool _updatingSemanticEditors;
    private bool _suppressRefresh;
    private bool _refreshPending;
    private bool _filterActive;
    private HashSet<string>? _expansionBeforeFilter;
    private bool _isPlaying;
    private double _playbackRemainder;
    private long _layoutEvaluationCount;
    private string? _copiedKeyFrameXml;
    private XuiInspectorPropertyClipboard? _propertyClipboard;
    private string? _selectedNamedFrameKey;
    private string? _rawXmlLoadedNodeKey;
    private long _rawXmlLoadedRevision = -1;
    private XuiPreviewScenario _previewScenario = XuiPreviewScenario.Empty;
    private bool _allowClose;
    private string? _recoverySuggestedPath;
    private RecoverySnapshot? _activeRecovery;
    private XuiReferenceTransactionResult? _lastReferenceTransaction;
    private int _viewportLoadingDepth;
    private bool _disposed;
    private Point _assetDragStart;
    private Point _hierarchyDragStart;
    private string? _hierarchyDragSourceKey;
    private string? _hierarchyDropTargetKey;
    private HierarchyDropPlacement _hierarchyDropPlacement;
    private XuiMessageDescriptor? _statusDescriptor;
    private string? _lastLocalizedStatusText;
    private XuiMessageDescriptor? _assetStatusDescriptor;
    private string? _lastLocalizedAssetStatusText;

    private int CurrentTimelineTick =>
        _timelineWorkspace?.ActiveTick ?? 0;

    public MainWindow()
    {
        _settings =
            (Application.Current as App)?.Settings ??
            EditorSettingsStore.Load();
        UiLocalization.EnsureApplied(_settings.UiLanguage);
        HierarchyRows = [];
        InspectorProperties = [];
        FilteredDiagnostics = [];
        AssetRows = [];
        InitializeComponent();
        Language = UiLocalization.XmlLanguage;
        UiLocalization.LanguageChanged += UiLocalization_LanguageChanged;
        BuildInterfaceLanguageMenu();
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);
        CenterOnPrimaryWorkArea();
        DataContext = this;
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

    private void BuildInterfaceLanguageMenu()
    {
        InterfaceLanguageMenuItem.Items.Clear();
        MenuItem automatic = new()
        {
            Header = UiLocalization.Text("Ui.Settings.Language.Automatic"),
            Tag = UiLocalization.AutomaticLanguage,
            IsCheckable = true,
            IsChecked = _settings.UiLanguage.Equals(
                UiLocalization.AutomaticLanguage,
                StringComparison.OrdinalIgnoreCase),
        };
        automatic.Click += InterfaceLanguage_Click;
        InterfaceLanguageMenuItem.Items.Add(automatic);
        InterfaceLanguageMenuItem.Items.Add(new Separator());
        foreach (UiLanguageDefinition language in UiLocalization.Languages)
        {
            MenuItem item = new()
            {
                Header = language.NativeName,
                Tag = language.Code,
                IsCheckable = true,
                IsChecked = _settings.UiLanguage.Equals(
                    language.Code,
                    StringComparison.OrdinalIgnoreCase),
            };
            item.Click += InterfaceLanguage_Click;
            InterfaceLanguageMenuItem.Items.Add(item);
        }
    }

    private async void InterfaceLanguage_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not MenuItem { Tag: string code })
        {
            return;
        }

        _settings.UiLanguage = UiLocalization.NormalizeSelection(code);
        UiLocalization.Apply(_settings.UiLanguage);
        await EditorSettingsStore.SaveAsync(_settings).ConfigureAwait(true);
    }

    private void UiLocalization_LanguageChanged(
        object? sender,
        EventArgs eventArgs)
    {
        Language = UiLocalization.XmlLanguage;
        BuildInterfaceLanguageMenu();
        foreach (HierarchyRow row in HierarchyRows)
        {
            row.RefreshLocalization();
        }

        SelectionSnapshot selection = CaptureSelection();
        BuildInspector(selection);
        RefreshPreviewState(selection);
        UpdateTimelineData(selection);
        RefreshNamedFrameEditor();
        UpdatePropertyTransferChrome();
        FilterDiagnostics();
        DiagnosticsGrid.Items.Refresh();
        RefreshLocalizedStatus();
        RefreshLocalizedAssetStatus();
        UpdateChrome();
        Viewport.InvalidateVisual();
    }

    public BatchObservableCollection<HierarchyRow> HierarchyRows { get; }

    public BatchObservableCollection<InspectorPropertyRow> InspectorProperties { get; }

    public BatchObservableCollection<XuiDiagnostic> FilteredDiagnostics { get; }

    public BatchObservableCollection<XuiCatalogAsset> AssetRows { get; }

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
        TimelineActionsPanel.IsEnabled &&
        TimelineEditPanel.IsEnabled &&
        TimelineEditor.IsEnabled &&
        TickSlider.IsEnabled &&
        AnimationMenuItem.IsEnabled;

    internal bool IncludeDescendantsEnabledForTesting =>
        IncludeDescendantsToggle.IsEnabled;

    internal bool AnimationCreationEnabledForTesting =>
        AddAnimationButton.IsEnabled && AnimationMenuItem.IsEnabled;

    internal bool TrackCreationEnabledForTesting =>
        AddTrackButton.IsEnabled;

    internal long LayoutEvaluationCountForTesting =>
        _layoutEvaluationCount;

    internal bool RawXmlMaterializedForTesting =>
        _rawXmlLoadedNodeKey is not null;

    internal string RawXmlStatusForTesting => RawXmlStatusText.Text;

    internal string PreviewStateForTesting => PreviewStateText.Text;

    internal string StatusForTesting => StatusText.Text;

    internal string AssetStatusForTesting => AssetStatusText.Text;

    internal string UndoHeaderForTesting =>
        UndoMenuItem.Header?.ToString() ?? string.Empty;

    internal string RedoHeaderForTesting =>
        RedoMenuItem.Header?.ToString() ?? string.Empty;

    internal bool ViewportLoadingOverlayVisibleForTesting =>
        ViewportLoadingOverlay.Visibility == Visibility.Visible;

    internal bool PreviewStateIsInAnimationTabForTesting =>
        HasLogicalAncestor(PreviewStatePanel, AnimationTab);

    internal bool PreviewStateIsSeparatedFromTransportForTesting =>
        Grid.GetRow(PreviewStatePanel) >
        Grid.GetRow(TimelineTransportPanel) &&
        TimelineTransportPanel.VerticalAlignment ==
        VerticalAlignment.Top;

    internal bool HierarchyHeaderButtonsSeparatedForTesting =>
        CollapseHierarchyButton.Margin.Right >= 4;

    internal HierarchyRow? HierarchyRowForTesting(string nodeKey) =>
        _hierarchyIndex?.FindRow(nodeKey);

    private static bool HasLogicalAncestor(
        DependencyObject descendant,
        DependencyObject ancestor)
    {
        DependencyObject? current = descendant;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    internal XuiRenderContext PreviewRenderContextForTesting =>
        BuildRenderContext();

    internal void SetPreviewScenarioForTesting(string scenarioId)
    {
        _previewScenario =
            XuiPreviewScenarioCatalog.Defaults.Single(scenario =>
                scenario.Id.Equals(scenarioId, StringComparison.Ordinal));
        RefreshEvaluation();
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

    internal Task<bool> SaveDocumentForTesting() =>
        SaveDocumentAsync(forceSaveAs: false);

    internal int CopiedInspectorPropertyCountForTesting =>
        _propertyClipboard?.Properties.Count ?? 0;

    internal void CopyInspectorPropertiesForTesting(
        IEnumerable<string> propertyNames)
    {
        if (!TryGetSinglePropertySource(
                out _,
                out string sourceDisplayName,
                out string sourceClassName,
                out IReadOnlyList<XuiCatalogPropertySelection> properties))
        {
            throw new InvalidOperationException(
                "A single source element must be selected.");
        }

        HashSet<string> names = propertyNames.ToHashSet(
            StringComparer.Ordinal);
        SetPropertyClipboard(
            sourceDisplayName,
            sourceClassName,
            properties
                .Where(property =>
                    names.Contains(property.Definition.Name) &&
                    XuiPropertyTransfer.CanCopy(
                        property.Definition.Name))
                .Select(ToCopiedProperty)
                .ToArray());
    }

    internal XuiInspectorPropertyPasteResult
        PasteInspectorPropertiesForTesting() =>
        PasteInspectorProperties();

    internal BitmapSource ExportTransparentPngForTesting(
        string path,
        double scale = SnapshotExportScale) =>
        SaveTransparentPng(path, scale);

    internal void SetAssetResolverForTesting(
        DyingLightAssetResolver resolver)
    {
        _assetResolver = resolver ??
                         throw new ArgumentNullException(nameof(resolver));
        _assetCatalog = new DyingLightXuiAssetCatalog(resolver);
        RefreshAssetRows();
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

    internal bool ReparentHierarchyForTesting(
        string sourceKey,
        string targetKey) =>
        MoveHierarchy(
            sourceKey,
            new HierarchyDropIntent(
                targetKey,
                HierarchyDropPlacement.Inside));

    internal bool ReorderHierarchyForTesting(
        string sourceKey,
        string targetKey,
        bool after) =>
        MoveHierarchy(
            sourceKey,
            new HierarchyDropIntent(
                targetKey,
                after
                    ? HierarchyDropPlacement.After
                    : HierarchyDropPlacement.Before));

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

    internal void SetInspectorValueForTesting(
        string propertyName,
        string value)
    {
        InspectorPropertyRow row =
            InspectorProperties.Single(property =>
                property.Name.Equals(
                    propertyName,
                    StringComparison.Ordinal));
        row.Value = value;
        CommitInspectorValue(row);
    }

    internal void SetSemanticTextFlagForTesting(
        XuiKnownTextStyle style,
        bool enabled)
    {
        string propertyName = style switch
        {
            XuiKnownTextStyle.Bold => "Bold",
            XuiKnownTextStyle.Italic => "Italic",
            XuiKnownTextStyle.Underline => "Underline",
            _ => throw new ArgumentOutOfRangeException(
                nameof(style),
                style,
                "Only semantic text-format flags are supported."),
        };
        ApplyTextStyleFlag(propertyName, style, enabled);
    }

    internal void ResetInspectorPropertyForTesting(string propertyName)
    {
        if (_document is null)
        {
            return;
        }

        string[] keys = _selectedKeys.ToArray();
        ExecuteBatch(
            () =>
            {
                foreach (string key in keys)
                {
                    XuiSyntaxNode? node =
                        _document.SyntaxTree.FindByKey(key);
                    XuiPropertyEntry? property = node is null
                        ? null
                        : XuiModelReader.GetProperty(
                            node,
                            _document.Text,
                            propertyName);
                    if (property is not null)
                    {
                        _document.Execute(
                            XuiCommandFactory.RemoveElement(
                                _document,
                                property.Element));
                    }
                }
            },
            $"Reset {propertyName}",
            new XuiMessageDescriptor(
                "Ui.Command.Reset",
                "Reset {0}",
                propertyName));
    }

    internal void SetIncludeDescendantsForTesting(bool enabled)
    {
        IncludeDescendantsToggle.IsChecked = enabled;
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

    internal void AlignSelectionForTesting(XuiElementAlignment alignment) =>
        AlignSelection(alignment);

    internal void CommitPivotForTesting(
        string nodeKey,
        XuiVector3 pivot,
        bool preserve) =>
        CommitPivot(
            nodeKey,
            pivot,
            "Test pivot edit",
            preserve,
            new XuiMessageDescriptor(
                "Ui.Command.EditPivot",
                "Edit pivot"));

    internal void CommitNavigationForTesting(
        string sourceNodeKey,
        string propertyName,
        string? targetNodeKey) =>
        Viewport_NavigationEditRequested(
            this,
            new XuiNavigationEditRequestedEventArgs(
                sourceNodeKey,
                propertyName,
                targetNodeKey));

    internal void SetAdvancedInspectorForTesting(bool advanced)
    {
        _settings.ShowAdvancedInspector = advanced;
        AdvancedInspectorToggle.IsChecked = advanced;
        BuildInspector();
    }

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

    internal void AddParentForTesting(
        string elementKey,
        XuiElementCreationRequest request)
    {
        if (_document?.SyntaxTree.FindByKey(elementKey) is
            not XuiSyntaxNode element)
        {
            throw new InvalidOperationException(
                "The requested element does not exist.");
        }

        InsertVisualParent(element, request);
    }

    internal void AddPropertyForTesting(
        string elementKey,
        string name,
        string value)
    {
        if (_document?.SyntaxTree.FindByKey(elementKey) is
            not XuiSyntaxNode element)
        {
            throw new InvalidOperationException(
                "The requested element does not exist.");
        }

        InsertProperty(element, name, value);
    }

    internal void CreateAnimationForTesting(
        string presetId,
        string ownerKey,
        IReadOnlyList<string> targetKeys,
        int startTick = 0,
        string prefix = "",
        bool markersOnly = false)
    {
        if (_document is null)
        {
            throw new InvalidOperationException("No test document is attached.");
        }

        ExecuteAnimationPlan(XuiAnimationAuthoringService.Plan(
            _document,
            new XuiAnimationAuthoringRequest(
                ownerKey,
                targetKeys,
                XuiAnimationPresets.Find(presetId),
                startTick,
                prefix,
                markersOnly)));
    }

    internal void AddTimelineTrackForTesting(
        string propertyName,
        string? value = null) =>
        AddTimelineTrack(propertyName, value, showDialog: false);

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
        ParentMaskMenuItem.IsChecked = _settings.ShowParentMask;
        GrayOutsideGroupMenuItem.IsChecked =
            _settings.GrayOutsideSelectedGroup;
        DesignTimeMenuItem.IsChecked = _settings.ShowDesignTimeElements;
        NavigationMenuItem.IsChecked =
            _settings.ShowNavigationConnections;
        NavigationAllMenuItem.IsChecked =
            _settings.ShowAllNavigationConnections;
        ForceShowGroupMenuItem.IsChecked =
            _settings.ForceShowCurrentGroup;
        ToolbarSnap.IsChecked = _settings.SnapEnabled;
        AdvancedInspectorToggle.IsChecked =
            _settings.ShowAdvancedInspector;
        PreservePivotPositionCheckBox.IsChecked =
            _settings.PreservePivotVisualPosition;
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
                UiLocalization.Format(
                    "Ui.Main.RecoveryPrompt",
                    latest.TimestampUtc.ToLocalTime(),
                    latest.OriginalPath ??
                    UiLocalization.Text("Ui.Main.UntitledDocument")),
                UiLocalization.Text("Ui.Main.RecoveryTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (recover == MessageBoxResult.Yes)
            {
                await OpenRecoveryAsync(latest).ConfigureAwait(true);
            }
        }
    }

    private void CenterOnPrimaryWorkArea()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private async void Open_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (!await ConfirmDiscardAsync().ConfigureAwait(true))
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = UiLocalization.Text("Ui.Main.Open.Title"),
            Filter = UiLocalization.Text("Ui.Main.Filter.XuiAll"),
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
            SetStatus("Ui.Main.Status.SelectOneAddChild");
            return;
        }

        if (IsLocked(parent.Key))
        {
            SetStatus(
                "Ui.Main.Status.ElementLocked",
                DisplayNode(parent));
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
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.AddChild"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetStatus("Ui.Main.Status.AddChildFailed");
        }
    }

    private void AddParent_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            SelectedNodes() is not [XuiSyntaxNode element])
        {
            SetStatus("Ui.Main.Status.SelectOneAddParent");
            return;
        }

        if (IsCanvasRoot(element))
        {
            SetStatus("Ui.Main.Status.CanvasCannotWrap");
            return;
        }

        if (IsLocked(element.Key))
        {
            SetStatus(
                "Ui.Main.Status.ElementLocked",
                DisplayNode(element));
            return;
        }

        XuiSyntaxNode? currentParent = element.Parent;
        XuiVector2 parentSize =
            currentParent is { Kind: XuiSyntaxKind.Element }
                ? RenderedOrAuthoredSize(currentParent)
                : new XuiVector2(
                    XuiViewport.Default.Width,
                    XuiViewport.Default.Height);
        AddXuiElementWindow dialog = new(
            DisplayNode(element),
            parentSize,
            SuggestedUniqueId,
            [
                XuiElementPreset.Group,
                XuiElementPreset.CustomXml,
            ],
            identityPlacement: true,
            windowTitle: UiLocalization.Text("Ui.Main.AddParent.Title"),
            actionLabel: UiLocalization.Text("Ui.Main.AddParent.Action"),
            instruction:
                UiLocalization.Format(
                    "Ui.Main.AddParent.Instruction",
                    DisplayNode(element)))
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
            InsertVisualParent(element, request);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            InvalidOperationException or
            ArgumentException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.AddParent"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetStatus("Ui.Main.Status.AddParentFailed");
        }
    }

    private void AddProperty_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            SelectedNodes() is not [XuiSyntaxNode element])
        {
            SetStatus("Ui.Main.Status.SelectOneAddProperty");
            return;
        }

        if (IsLocked(element.Key))
        {
            SetStatus(
                "Ui.Main.Status.ElementLocked",
                DisplayNode(element));
            return;
        }

        if (element.FirstElement("Properties") is null)
        {
            SetStatus(
                "Ui.Main.Status.NoPropertiesBlock",
                DisplayNode(element));
            return;
        }

        XuiResolvedClassDefinition resolvedClass =
            ClassCatalog.ResolveClass(element, _document.Text);
        string[] authoredNames = XuiModelReader
            .GetProperties(element, _document.Text)
            .Select(static property => property.Name)
            .ToArray();
        AddXuiPropertyWindow dialog = new(
            DisplayNode(element),
            resolvedClass.Properties,
            authoredNames)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            InsertProperty(
                element,
                dialog.PropertyName,
                dialog.PropertyValue);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.AddProperty"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetStatus("Ui.Main.Status.AddPropertyFailed");
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
        SetStatus(
            _document.Source?.IsReadOnly == true
                ? "Ui.Main.Status.AddedReadOnly"
                : "Ui.Main.Status.Added",
            createdId);
    }

    private void InsertVisualParent(
        XuiSyntaxNode element,
        XuiElementCreationRequest request)
    {
        if (_document is null)
        {
            throw new InvalidOperationException(
                "No XUI document is open.");
        }

        string raw = XuiElementFactory.CreateXml(
            request,
            _document.Format.NewLine);
        string createdId = CreatedElementId(raw);
        _document.Execute(XuiCommandFactory.WrapWithVisualParentXml(
            _document,
            element,
            raw,
            $"Add parent {createdId}"));

        XuiSyntaxNode? created = _document.Root
            .DescendantsAndSelf()
            .Where(static node =>
                node.Kind == XuiSyntaxKind.Element &&
                !XuiModelReader.IsStructural(node))
            .SingleOrDefault(node =>
                string.Equals(
                    XuiModelReader.GetId(node, _document.Text),
                    createdId,
                    StringComparison.Ordinal));
        if (created is not null)
        {
            _expanded.Add(created.Key);
            _selectedKeys.Clear();
            _selectedKeys.Add(created.Key);
            EnsureSelectedAncestorsExpanded();
        }

        BuildHierarchy();
        SelectRowsFromKeys(scrollIntoView: true);
        UpdateSelectionSurfaces();
        SetStatus(
            _document.Source?.IsReadOnly == true
                ? "Ui.Main.Status.AddedParentReadOnly"
                : "Ui.Main.Status.AddedParent",
            createdId);
    }

    private void InsertProperty(
        XuiSyntaxNode element,
        string name,
        string value)
    {
        if (_document is null)
        {
            throw new InvalidOperationException(
                "No XUI document is open.");
        }

        if (XuiModelReader.GetProperty(
                element,
                _document.Text,
                name) is not null)
        {
            throw new InvalidOperationException(
                UiLocalization.Format(
                    "Ui.Main.Error.PropertyAlreadyExists",
                    DisplayNode(element),
                    name));
        }

        _document.Execute(XuiCommandFactory.AddProperty(
            _document,
            element,
            name,
            value));
        SetStatus(
            "Ui.Main.Status.AddedProperty",
            name,
            DisplayNode(element));
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

    private void GridSettings_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        GridSettingsWindow dialog = new(_settings)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            ApplyViewportSettings();
        }
    }

    private void ParentMask_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _settings.ShowParentMask = ParentMaskMenuItem.IsChecked;
        ApplyViewportSettings();
    }

    private void GrayOutsideGroup_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _settings.GrayOutsideSelectedGroup =
            GrayOutsideGroupMenuItem.IsChecked;
        ApplyViewportSettings();
    }

    private void DesignTime_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _settings.ShowDesignTimeElements = DesignTimeMenuItem.IsChecked;
        ApplyViewportSettings();
    }

    private void Navigation_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _settings.ShowNavigationConnections =
            NavigationMenuItem.IsChecked;
        if (!_settings.ShowNavigationConnections)
        {
            _settings.ShowAllNavigationConnections = false;
            NavigationAllMenuItem.IsChecked = false;
        }

        ApplyViewportSettings();
        UpdateNavigationConnections();
    }

    private void NavigationAll_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _settings.ShowAllNavigationConnections =
            NavigationAllMenuItem.IsChecked;
        if (_settings.ShowAllNavigationConnections)
        {
            _settings.ShowNavigationConnections = true;
            NavigationMenuItem.IsChecked = true;
        }

        ApplyViewportSettings();
        UpdateNavigationConnections();
    }

    private void SelectParentGroup_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            SelectedNodes() is not [XuiSyntaxNode selected] ||
            FindVisualParent(selected) is not XuiSyntaxNode parent)
        {
            SetStatus("Ui.Main.Status.NoParentGroup");
            return;
        }

        _selectedKeys.Clear();
        _selectedKeys.Add(parent.Key);
        EnsureSelectedAncestorsExpanded();
        BuildHierarchy();
        SelectRowsFromKeys(scrollIntoView: true);
        UpdateSelectionSurfaces();
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

    private void ForceShowCurrentGroup_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _settings.ForceShowCurrentGroup =
            ForceShowGroupMenuItem.IsChecked;
        RebuildCurrentGroupForceShow();
        RefreshEvaluation();
    }

    private void ForceShowAll_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null)
        {
            return;
        }

        _settings.ForceShowCurrentGroup = false;
        ForceShowGroupMenuItem.IsChecked = false;
        _forceShownKeys.Clear();
        _forceShownKeys.Add(_document.Root.Key);
        _forceShownKeys.UnionWith(
            XuiModelReader.VisualDescendants(_document.Root)
                .Select(static node => node.Key));
        RefreshEvaluation();
    }

    private void ClearForceShown_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _forceShownKeys.Clear();
        _settings.ForceShowCurrentGroup = false;
        ForceShowGroupMenuItem.IsChecked = false;
        RefreshEvaluation();
    }

    private void RebuildCurrentGroupForceShow()
    {
        _forceShownKeys.Clear();
        if (!_settings.ForceShowCurrentGroup ||
            _document is null ||
            SelectedNodes() is not [XuiSyntaxNode selected])
        {
            return;
        }

        XuiSyntaxNode group =
            XuiModelReader.VisualChildren(selected).Any()
                ? selected
                : FindVisualParent(selected) ?? selected;
        XuiSyntaxNode? ancestor = group;
        while (ancestor is not null)
        {
            if (!XuiModelReader.IsStructural(ancestor))
            {
                _forceShownKeys.Add(ancestor.Key);
            }

            ancestor = ancestor.Parent;
        }

        _forceShownKeys.UnionWith(
            XuiModelReader.VisualDescendants(group)
                .Select(static node => node.Key));
    }

    private static XuiSyntaxNode? FindVisualParent(XuiSyntaxNode node)
    {
        XuiSyntaxNode? parent = node.Parent;
        while (parent is not null &&
               XuiModelReader.IsStructural(parent))
        {
            parent = parent.Parent;
        }

        return parent;
    }

    private void RestoreComposedPose_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!TimelineEditor.HasVisibleTracks)
        {
            return;
        }

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
            Title = UiLocalization.Text("Ui.Main.Reference.LoadTitle"),
            Filter = UiLocalization.Text("Ui.Main.Filter.ImagesAll"),
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                Viewport.LoadReferenceImage(dialog.FileName);
                SetStatus(
                    "Ui.Main.Status.Reference",
                    Path.GetFileName(dialog.FileName));
            }
            catch (Exception exception) when (
                exception is IOException or
                NotSupportedException)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Format(
                        "Ui.Common.ErrorDetails",
                        exception.Message),
                    UiLocalization.Text(
                        "Ui.Main.Error.LoadReference"),
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
        SetStatus("Ui.Main.Status.ReferenceCleared");
    }

    private void ExportPng_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            !Viewport.HasRenderedFrame)
        {
            SetStatus("Ui.Main.Status.ExportNeedsDocument");
            return;
        }

        string stem = Path.GetFileNameWithoutExtension(
            _document.DisplayName);
        SaveFileDialog dialog = new()
        {
            Title = UiLocalization.Text("Ui.Main.Export.Title"),
            Filter = UiLocalization.Text("Ui.Main.Filter.Png"),
            AddExtension = true,
            DefaultExt = ".png",
            FileName = $"{stem}-preview.png",
            InitialDirectory = InitialSaveDirectory(),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SetStatus("Ui.Main.Status.RenderingPng");
            BitmapSource bitmap = SaveTransparentPng(
                dialog.FileName,
                SnapshotExportScale);
            SetStatus(
                "Ui.Main.Status.ExportedPng",
                bitmap.PixelWidth,
                bitmap.PixelHeight);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException or
            OutOfMemoryException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.ExportPng"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetStatus("Ui.Main.Status.ExportPngFailed");
        }
    }

    private BitmapSource SaveTransparentPng(
        string path,
        double scale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        BitmapSource bitmap =
            Viewport.RenderTransparentSnapshot(scale);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException(
                "The PNG destination has no parent directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                encoder.Save(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                fullPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return bitmap;
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
        if (!TimelineEditor.HasVisibleTracks ||
            _timelineWorkspace?.ActiveScope is not XuiTimelineScope scope ||
            scope.MaximumTick <= 0)
        {
            return;
        }

        _isPlaying = !_isPlaying;
        PlayPauseButton.Content = _isPlaying
            ? UiLocalization.Text("Ui.Main.Transport.Pause")
            : UiLocalization.Text("Ui.Main.Transport.Play");
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

    private void AddAnimation_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_document is null)
        {
            return;
        }

        SelectionSnapshot selection = CaptureSelection();
        if (selection.Nodes.Length == 0)
        {
            SetStatus("Ui.Animation.Status.SelectTarget");
            return;
        }

        List<XuiAnimationScopeOption> scopes =
            AnimationScopeOptions(selection);
        if (scopes.Count == 0)
        {
            SetStatus("Ui.Animation.Status.MixedScopes");
            return;
        }

        string[] targetKeys = selection.Nodes
            .Select(static node => node.Key)
            .ToArray();
        CreateXuiAnimationWindow dialog = new(
            scopes,
            ClassCatalog.TimelinePropertyNames,
            dialogSelection => XuiAnimationAuthoringService.Plan(
                _document,
                BuildAuthoringRequest(dialogSelection, targetKeys),
                _timelineSet)
                .ConflictReport)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true ||
            dialog.Selection is not XuiAnimationDialogSelection chosen)
        {
            return;
        }

        XuiAnimationAuthoringResult plan =
            XuiAnimationAuthoringService.Plan(
                _document,
                BuildAuthoringRequest(chosen, targetKeys),
                _timelineSet);
        ExecuteAnimationPlan(plan);
    }

    private void AddTrack_Click(object sender, RoutedEventArgs eventArgs)
    {
        AddTimelineTrack(initialProperty: null);
    }

    private void InspectorAnimationDiamond_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: InspectorPropertyRow row } ||
            !row.IsAnimatable)
        {
            return;
        }

        CommitInspectorValue(row);
        AddTimelineTrack(row.Name, row.Value, showDialog: false);
    }

    private void AddTimelineTrack(
        string? initialProperty,
        string? initialValue = null,
        bool showDialog = true)
    {
        if (_document is null)
        {
            return;
        }

        SelectionSnapshot selection = CaptureSelection();
        if (selection.Nodes.Length != 1)
        {
            SetStatus("Ui.Animation.Status.OneTargetForTrack");
            return;
        }

        XuiSyntaxNode target = selection.Nodes[0];
        XuiTimelineScope? activeScope = _timelineWorkspace?.ActiveScope;
        string ownerKey = activeScope?.ScopeKey ?? target.Key;
        string propertyName = initialProperty ?? string.Empty;
        string propertyValue = initialValue ?? string.Empty;
        if (showDialog)
        {
            AddXuiTimelineTrackWindow dialog = new(
                ClassCatalog.TimelinePropertyNames,
                property => EffectiveTimelinePropertyValue(
                    target,
                    property),
                initialProperty)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            propertyName = dialog.PropertyName;
            propertyValue = dialog.PropertyValue;
        }

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return;
        }

        if (string.IsNullOrEmpty(propertyValue))
        {
            propertyValue = EffectiveTimelinePropertyValue(target, propertyName);
        }

        XuiAnimationAuthoringResult plan =
            XuiAnimationAuthoringService.PlanTrackKey(
                _document,
                ownerKey,
                target.Key,
                propertyName,
                propertyValue,
                CurrentTimelineTick,
                _timelineSet);
        ExecuteAnimationPlan(plan);
    }

    private List<XuiAnimationScopeOption> AnimationScopeOptions(
        SelectionSnapshot selection)
    {
        if (_document is null || selection.Nodes.Length == 0)
        {
            return [];
        }

        if (selection.Nodes.Length > 1)
        {
            XuiTimelineScope[] scopes = selection.Scopes
                .DistinctBy(static scope => scope.ScopeKey)
                .ToArray();
            return scopes.Length == 1
                ? [new XuiAnimationScopeOption(
                    scopes[0].ScopeKey,
                    scopes[0].DisplayName,
                    _timelineWorkspace?.TickFor(scopes[0].ScopeKey) ?? 0,
                    IsLocal: false)]
                : [];
        }

        XuiSyntaxNode target = selection.Nodes[0];
        string displayName = XuiModelReader.GetId(target, _document.Text) ??
                             target.Name;
        List<XuiAnimationScopeOption> options =
        [
            new(
                target.Key,
                UiLocalization.Format(
                    "Ui.Animation.Scope.Local",
                    displayName),
                0,
                IsLocal: true),
        ];
        if (_layoutSession is not null)
        {
            options.AddRange(_layoutSession.TimelineScopes.Scopes
                .Where(scope =>
                    KeyIsAncestorOrSelf(scope.ScopeKey, target.Key) &&
                    !scope.ScopeKey.Equals(target.Key, StringComparison.Ordinal))
                .OrderByDescending(static scope => scope.ScopeKey.Length)
                .Select(scope => new XuiAnimationScopeOption(
                    scope.ScopeKey,
                    UiLocalization.Format(
                        "Ui.Animation.Scope.Existing",
                        scope.DisplayName),
                    _timelineWorkspace?.TickFor(scope.ScopeKey) ?? 0,
                    IsLocal: false)));
        }

        return options;
    }

    private static XuiAnimationAuthoringRequest BuildAuthoringRequest(
        XuiAnimationDialogSelection selection,
        IReadOnlyList<string> targetKeys) =>
        new(
            selection.Scope.OwnerKey,
            targetKeys,
            selection.Preset,
            selection.StartTick,
            selection.Prefix,
            selection.MarkersOnly,
            selection.PropertyName,
            selection.StartValue,
            selection.EndValue,
            selection.Duration);

    private void ExecuteAnimationPlan(
        XuiAnimationAuthoringResult plan)
    {
        XuiAnimationConflict? error = plan.ConflictReport.Conflicts
            .FirstOrDefault(static conflict =>
                conflict.Severity == XuiAnimationConflictSeverity.Error);
        if (error is not null)
        {
            _statusDescriptor = null;
            _lastLocalizedStatusText = null;
            StatusText.Text = error.ResourceKey is null
                ? error.Message
                : UiLocalization.Format(
                    error.ResourceKey,
                    error.Arguments?.ToArray() ?? []);
            return;
        }

        if (_document is null || plan.Command is null)
        {
            SetStatus("Ui.Animation.Status.NoChanges");
            return;
        }

        if (!ExecuteBatch(
                () => _document.Execute(plan.Command),
                plan.Command.Description,
                animationMetadataOnly: true))
        {
            return;
        }
        if (_timelineWorkspace?.Catalog.Find(plan.OwnerKey) is
            XuiTimelineScope createdScope)
        {
            _timelineWorkspace.ResolveScopes(
                [createdScope],
                createdScope.ScopeKey);
            _timelineWorkspace.SetActiveTick(plan.FirstTick);
            UpdateTimelineData();
            if (plan.GeneratedTracks.Count > 0)
            {
                (string targetId, string propertyName) =
                    plan.GeneratedTracks[0];
                XuiKeyFrame? firstKey = createdScope.Timelines
                    .Where(timeline => timeline.TargetId.Equals(
                        targetId,
                        StringComparison.Ordinal))
                    .SelectMany(static timeline => timeline.Tracks)
                    .Where(track => track.PropertyName.Equals(
                        propertyName,
                        StringComparison.Ordinal))
                    .SelectMany(static track => track.KeyFrames)
                    .OrderBy(static key => key.Tick)
                    .FirstOrDefault();
                TimelineEditor.SelectKeyFrame(firstKey?.Syntax.Key);
                RefreshKeyFrameEditor();
            }
            RefreshInspectorAnimationIndicators();
            RefreshEvaluation();
        }

        SetStatus("Ui.Animation.Status.Created");
    }

    private string EffectiveTimelinePropertyValue(
        XuiSyntaxNode target,
        string propertyName)
    {
        if (_document is not null &&
            XuiModelReader.GetId(target, _document.Text) is string targetId &&
            _timelineWorkspace?.ActiveScope is XuiTimelineScope scope)
        {
            XuiTrack? track = scope.Timelines
                .Where(timeline => timeline.TargetId.Equals(
                    targetId,
                    StringComparison.Ordinal))
                .SelectMany(static timeline => timeline.Tracks)
                .FirstOrDefault(candidate => candidate.PropertyName.Equals(
                    propertyName,
                    StringComparison.Ordinal));
            if (track is not null &&
                TimelineEvaluator.Sample(track, CurrentTimelineTick) is
                    XuiAnimatedValue sampled)
            {
                return sampled.ToXuiString();
            }
        }

        return _document is null
            ? "0"
            : XuiModelReader.GetPropertyValue(
                  target,
                  _document.Text,
                  propertyName) ??
              ClassCatalog.FindProperty(propertyName)?.DefaultValue ??
              propertyName switch
              {
                  "Show" => "true",
                  "Opacity" => "1",
                  "Scale" => "1,1,1",
                  "Color" or "TextColor" or "OutlineColor" or
                      "DefaultFontColor" => "0xffffffff",
                  _ => "0",
              };
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
            SetStatus("Ui.Main.Status.SelectAnimatedElement");
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
            lines.Add(
                $"<Prop>{sampled?.ToXuiString() ?? DefaultTimelineValue(track)}</Prop>");
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
        SetStatus(
            "Ui.Main.Status.CopiedKeyframe",
            selected.Tick);
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

    private void IncludeDescendantsToggle_Changed(
        object sender,
        RoutedEventArgs eventArgs)
    {
        StopPlayback();
        UpdateTimelineData();
    }

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

    private void HierarchyList_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        _hierarchyDragStart = eventArgs.GetPosition(HierarchyList);
        _hierarchyDragSourceKey =
            HierarchyRowFromEventSource(eventArgs.OriginalSource)?.NodeKey;
    }

    private void HierarchyList_MouseMove(
        object sender,
        MouseEventArgs eventArgs)
    {
        if (eventArgs.LeftButton != MouseButtonState.Pressed ||
            _hierarchyDragSourceKey is null)
        {
            return;
        }

        Vector delta =
            eventArgs.GetPosition(HierarchyList) - _hierarchyDragStart;
        if (Math.Abs(delta.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        string sourceKey = _hierarchyDragSourceKey;
        _hierarchyDragSourceKey = null;
        DataObject data = new(HierarchyDragDataFormat, sourceKey);
        DragDrop.DoDragDrop(HierarchyList, data, DragDropEffects.Move);
        ClearHierarchyDropTarget();
    }

    private void HierarchyList_DragOver(
        object sender,
        DragEventArgs eventArgs)
    {
        string? sourceKey =
            eventArgs.Data.GetData(HierarchyDragDataFormat) as string;
        HierarchyDropIntent? intent = HierarchyDropIntentFor(eventArgs);
        bool canMove = sourceKey is not null &&
                       intent is HierarchyDropIntent dropIntent &&
                       TryGetHierarchyMove(
                           sourceKey,
                           dropIntent,
                           out _,
                           out _,
                           out _);
        SetHierarchyDropTarget(canMove ? intent : null);
        eventArgs.Effects = canMove
            ? DragDropEffects.Move
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private void HierarchyList_DragLeave(
        object sender,
        DragEventArgs eventArgs)
    {
        if (!HierarchyList.IsMouseOver)
        {
            ClearHierarchyDropTarget();
        }
    }

    private void HierarchyList_Drop(
        object sender,
        DragEventArgs eventArgs)
    {
        string? sourceKey =
            eventArgs.Data.GetData(HierarchyDragDataFormat) as string;
        HierarchyDropIntent? intent = HierarchyDropIntentFor(eventArgs);
        ClearHierarchyDropTarget();
        if (sourceKey is not null &&
            intent is HierarchyDropIntent dropIntent &&
            MoveHierarchy(sourceKey, dropIntent))
        {
            eventArgs.Effects = DragDropEffects.Move;
        }
        else
        {
            eventArgs.Effects = DragDropEffects.None;
        }

        eventArgs.Handled = true;
    }

    private HierarchyRow? HierarchyRowFromEventSource(object? source)
    {
        if (source is not DependencyObject dependencyObject)
        {
            return null;
        }

        return ItemsControl.ContainerFromElement(
                   HierarchyList,
                   dependencyObject) is ListBoxItem item
            ? item.DataContext as HierarchyRow
            : null;
    }

    private HierarchyDropIntent? HierarchyDropIntentFor(
        DragEventArgs eventArgs)
    {
        if (_document is null)
        {
            return null;
        }

        if (eventArgs.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(
                HierarchyList,
                source) is not ListBoxItem item ||
            item.DataContext is not HierarchyRow row)
        {
            return new HierarchyDropIntent(
                _document.Root.Key,
                HierarchyDropPlacement.Inside);
        }

        double height = item.ActualHeight > 0 ? item.ActualHeight : 24;
        double y = eventArgs.GetPosition(item).Y;
        HierarchyDropPlacement placement = y < height * 0.25
            ? HierarchyDropPlacement.Before
            : y > height * 0.75
                ? HierarchyDropPlacement.After
                : HierarchyDropPlacement.Inside;
        return new HierarchyDropIntent(row.NodeKey, placement);
    }

    private bool TryGetHierarchyMove(
        string sourceKey,
        HierarchyDropIntent intent,
        [NotNullWhen(true)] out XuiSyntaxNode? source,
        [NotNullWhen(true)] out XuiSyntaxNode? destinationParent,
        out int childIndex)
    {
        source = null;
        destinationParent = null;
        childIndex = int.MaxValue;
        if (_document is null)
        {
            return false;
        }

        source = _document.SyntaxTree.FindByKey(sourceKey);
        XuiSyntaxNode? target =
            _document.SyntaxTree.FindByKey(intent.TargetKey);
        if (source is null ||
            target is null ||
            source == _document.Root ||
            source == target ||
            IsLocked(source.Key))
        {
            return false;
        }

        if (intent.Placement == HierarchyDropPlacement.Inside)
        {
            if (IsLocked(target.Key) ||
                target.IsSelfClosing ||
                target.EndTagStart < 0 ||
                source.DescendantsAndSelf().Contains(target) ||
                (source.Parent == target &&
                 XuiModelReader.VisualChildren(target).LastOrDefault() ==
                 source))
            {
                return false;
            }

            destinationParent = target;
            return true;
        }

        if (intent.Placement is not (
                HierarchyDropPlacement.Before or
                HierarchyDropPlacement.After) ||
            source.Parent is null ||
            target.Parent != source.Parent ||
            IsLocked(target.Key) ||
            IsLocked(source.Parent.Key))
        {
            return false;
        }

        destinationParent = source.Parent;
        List<XuiSyntaxNode> original =
            XuiModelReader.VisualChildren(destinationParent).ToList();
        List<XuiSyntaxNode> reordered = original.ToList();
        reordered.Remove(source);
        int targetIndex = reordered.IndexOf(target);
        if (targetIndex < 0)
        {
            return false;
        }

        childIndex = targetIndex +
                     (intent.Placement == HierarchyDropPlacement.After
                         ? 1
                         : 0);
        reordered.Insert(childIndex, source);
        return !original.SequenceEqual(reordered);
    }

    private bool MoveHierarchy(
        string sourceKey,
        HierarchyDropIntent intent)
    {
        if (!TryGetHierarchyMove(
                sourceKey,
                intent,
                out XuiSyntaxNode? source,
                out XuiSyntaxNode? destinationParent,
                out int childIndex))
        {
            return false;
        }

        ReparentAndReselect(source, destinationParent, childIndex);
        return true;
    }

    private void SetHierarchyDropTarget(HierarchyDropIntent? intent)
    {
        string? nodeKey = intent?.TargetKey;
        HierarchyDropPlacement placement =
            intent?.Placement ?? HierarchyDropPlacement.None;
        if (string.Equals(
                _hierarchyDropTargetKey,
                nodeKey,
                StringComparison.Ordinal) &&
            _hierarchyDropPlacement == placement)
        {
            return;
        }

        if (_hierarchyDropTargetKey is string previousKey &&
            _hierarchyIndex?.FindRow(previousKey) is HierarchyRow previous)
        {
            previous.DropPlacement = HierarchyDropPlacement.None;
        }

        _hierarchyDropTargetKey = nodeKey;
        _hierarchyDropPlacement = placement;
        if (nodeKey is not null &&
            _hierarchyIndex?.FindRow(nodeKey) is HierarchyRow current)
        {
            current.DropPlacement = placement;
        }
    }

    private void ClearHierarchyDropTarget() =>
        SetHierarchyDropTarget(null);

    private void ViewportContextMenu_Opened(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is ContextMenu menu &&
            menu.Items.OfType<MenuItem>().FirstOrDefault() is MenuItem alignmentMenu)
        {
            alignmentMenu.IsEnabled = CanAlignSelection();
        }
    }

    private void UpdateNavigationConnections()
    {
        if (_document is null ||
            !_settings.ShowNavigationConnections)
        {
            Viewport.SetNavigationConnections([]);
            return;
        }

        XuiNavigationPathResolver resolver = new(
            _document.Root,
            _document.Text);
        IEnumerable<XuiSyntaxNode> allNodes =
            XuiModelReader.VisualDescendants(_document.Root)
                .Prepend(_document.Root);
        IEnumerable<XuiSyntaxNode> sources =
            _settings.ShowAllNavigationConnections
                ? allNodes.Where(node =>
                    NavigationPropertyNames.Any(name =>
                        !string.IsNullOrWhiteSpace(
                            XuiModelReader.GetPropertyValue(
                                node,
                                _document.Text,
                                name))) ||
                    _selectedKeys.Contains(node.Key))
                : SelectedNodes();

        List<XuiNavigationConnection> connections = [];
        foreach (XuiSyntaxNode source in sources
                     .DistinctBy(static node => node.Key, StringComparer.Ordinal))
        {
            foreach (string propertyName in NavigationPropertyNames)
            {
                string authoredPath =
                    XuiModelReader.GetPropertyValue(
                        source,
                        _document.Text,
                        propertyName)?.Trim() ??
                    string.Empty;
                XuiNavigationResolution resolution =
                    resolver.Resolve(source, authoredPath);
                connections.Add(new XuiNavigationConnection(
                    source.Key,
                    propertyName,
                    authoredPath,
                    resolution.Target?.Key,
                    resolution.Status,
                    resolution.Message));
            }
        }

        Viewport.SetNavigationConnections(connections);
    }

    private void Viewport_NavigationEditRequested(
        object? sender,
        XuiNavigationEditRequestedEventArgs eventArgs)
    {
        if (_document?.SyntaxTree.FindByKey(
                eventArgs.SourceNodeKey) is not XuiSyntaxNode source ||
            IsLocked(source.Key))
        {
            SetStatus("Ui.Main.Status.NavigationSourceUnavailable");
            return;
        }

        if (eventArgs.TargetNodeKey is null)
        {
            XuiPropertyEntry? existing = XuiModelReader.GetProperty(
                source,
                _document.Text,
                eventArgs.PropertyName);
            if (existing is null)
            {
                SetStatus(
                    "Ui.Main.Status.NavigationAlreadyClear",
                    eventArgs.PropertyName);
                return;
            }

            _document.Execute(XuiCommandFactory.RemoveElement(
                _document,
                existing.Element));
            SetStatus(
                "Ui.Main.Status.NavigationCleared",
                eventArgs.PropertyName);
            return;
        }

        XuiSyntaxNode? target =
            _document.SyntaxTree.FindByKey(eventArgs.TargetNodeKey);
        if (target is null)
        {
            SetStatus("Ui.Main.Status.NavigationTargetMissing");
            return;
        }

        XuiNavigationPathResolver resolver = new(
            _document.Root,
            _document.Text);
        if (!resolver.TryCreateStablePath(
                source,
                target,
                out string path,
                out string? error))
        {
            SetStatus(
                "Ui.Main.Status.NavigationAmbiguousDetails",
                error ?? UiLocalization.Text(
                    "Ui.Main.Status.NavigationAmbiguous"));
            return;
        }

        SetNodeProperty(source, eventArgs.PropertyName, path);
        SetStatus(
            "Ui.Main.Status.NavigationTargetSet",
            eventArgs.PropertyName,
            path);
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
            SetStatus(
                eventNode is null
                    ? "Ui.Main.Status.TransformTargetMissing"
                    : "Ui.Main.Status.CanvasCannotTransform");
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
            },
            "Move selection",
            new XuiMessageDescriptor(
                "Ui.Command.MoveSelection",
                "Move selection"));
            return;
        }

        XuiSyntaxNode? selectedNode =
            _document.SyntaxTree.FindByKey(eventArgs.NodeKey);
        if (selectedNode is null ||
            IsCanvasRoot(selectedNode))
        {
            return;
        }

        if (eventArgs.Kind == XuiTransformKind.Pivot)
        {
            CommitPivot(
                selectedNode.Key,
                eventArgs.NewPivot,
                "Move pivot",
                eventArgs.PreservePivotVisualPosition,
                new XuiMessageDescriptor(
                    "Ui.Command.EditPivot",
                    "Edit pivot"));
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
            },
            "Resize element",
            new XuiMessageDescriptor(
                "Ui.Command.ResizeElement",
                "Resize element"));
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
            },
            "Rotate selection",
            new XuiMessageDescriptor(
                "Ui.Command.RotateSelection",
                "Rotate selection"));
        }
    }

    private void AlignSelection_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { Tag: string tag } ||
            !Enum.TryParse<XuiElementAlignment>(
                tag,
                ignoreCase: true,
                out XuiElementAlignment alignment))
        {
            return;
        }

        AlignSelection(alignment);
    }

    private void AlignSelection(XuiElementAlignment alignment)
    {
        if (_document is null)
        {
            return;
        }

        if (Viewport.FrameForTesting is null)
        {
            RefreshEvaluation();
        }

        XuiRenderFrame? frame = Viewport.FrameForTesting;
        string[] targetKeys = SelectedTransformRootKeys();
        if (frame is null || targetKeys.Length == 0)
        {
            return;
        }

        Dictionary<string, XuiRenderNode> renderedNodes = frame.Nodes
            .GroupBy(static node => node.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last(),
                StringComparer.Ordinal);
        ExecuteBatch(
            () =>
            {
                List<PositionMovePlan> plans = [];
                foreach (string key in targetKeys)
                {
                    XuiSyntaxNode? node =
                        _document.SyntaxTree.FindByKey(key);
                    if (node is null ||
                        !renderedNodes.TryGetValue(key, out XuiRenderNode? rendered) ||
                        rendered.ParentKey is not string parentKey ||
                        !renderedNodes.TryGetValue(
                            parentKey,
                            out XuiRenderNode? parent) ||
                        !XuiElementAlignmentCalculator.TryGetPositionDelta(
                            alignment,
                            parent.Size,
                            rendered.Position,
                            rendered.Size,
                            out XuiVector2 delta) ||
                        (Math.Abs(delta.X) <= 0.0001 &&
                         Math.Abs(delta.Y) <= 0.0001))
                    {
                        continue;
                    }

                    plans.Add(PreparePositionMove(node, delta));
                }

                foreach (PositionMovePlan plan in plans)
                {
                    ApplyPositionMove(plan);
                }
            },
            "Align selection",
            new XuiMessageDescriptor(
                "Ui.Command.AlignSelection",
                "Align selection"));
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
                UiLocalization.Format(
                    "Ui.Main.Error.InvalidAuthoredPosition",
                    DisplayNode(node)));
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
                                    track.KnownProperty ==
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
                                UiLocalization.Format(
                                    "Ui.Main.Error.MissingPositionProp",
                                    DisplayNode(node),
                                    scope.DisplayName));
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
                                UiLocalization.Format(
                                    "Ui.Main.Error.InvalidPositionKey",
                                    DisplayNode(node),
                                    raw,
                                    scope.DisplayName));
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
            SetStatus("Ui.Main.Status.UnsupportedRotation");
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

    private void InspectorList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs) =>
        UpdatePropertyTransferChrome();

    private void InspectorCopySingleProperty_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button
            {
                Tag: InspectorPropertyRow row,
            })
        {
            return;
        }

        if (row.IsMixed || row.Value == MixedValue)
        {
            SetStatus("Ui.Main.Status.MixedCannotCopy");
            return;
        }

        if (!XuiPropertyTransfer.CanCopy(row.Name))
        {
            SetStatus(
                "Ui.Main.Status.ProtectedCannotCopy",
                row.Name);
            return;
        }

        if (row.HasError)
        {
            SetStatus(
                "Ui.Main.Status.FixInvalidBeforeCopy",
                row.Name);
            return;
        }

        SelectionSnapshot selection = CaptureSelection();
        string? sourceId = selection.Nodes.Length == 1
            ? XuiModelReader.GetId(
                selection.Nodes[0],
                _document!.Text)
            : null;
        string sourceDisplayName = selection.Nodes.Length == 1
            ? !string.IsNullOrWhiteSpace(sourceId)
                ? sourceId
                : selection.Nodes[0].Name
            : UiLocalization.Format(
                "Ui.Main.Selection.SelectedElements",
                selection.Nodes.Length);
        string sourceClassName = selection.Nodes.Length == 1
            ? ClassCatalog.ResolveClass(
                selection.Nodes[0],
                _document!.Text).Class.Name
            : UiLocalization.Text("Ui.Main.Selection.Common");
        SetPropertyClipboard(
            sourceDisplayName,
            sourceClassName,
            [
                new XuiCopiedInspectorProperty(
                    row.Name,
                    row.Value,
                    row.Category,
                    row.Definition?.Type ??
                    XuiPropertyType.Textual,
                    row.IsAuthored),
            ]);
    }

    private void CopyInspectorProperties_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!TryGetSinglePropertySource(
                out _,
                out string sourceDisplayName,
                out string sourceClassName,
                out IReadOnlyList<XuiCatalogPropertySelection> properties))
        {
            SetStatus("Ui.Main.Status.SelectSourceToCopy");
            return;
        }

        SetPropertyClipboard(
            sourceDisplayName,
            sourceClassName,
            properties
                .Where(property =>
                    property.IsAuthored &&
                    XuiPropertyTransfer.CanCopy(
                        property.Definition.Name))
                .Select(ToCopiedProperty)
                .ToArray());
    }

    private void AdvancedCopyInspectorProperties_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!TryGetSinglePropertySource(
                out _,
                out string sourceDisplayName,
                out string sourceClassName,
                out IReadOnlyList<XuiCatalogPropertySelection> properties))
        {
            SetStatus("Ui.Main.Status.SelectSourceAdvancedCopy");
            return;
        }

        CopyXuiPropertiesWindow dialog = new(
            sourceDisplayName,
            sourceClassName,
            properties)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            SetPropertyClipboard(
                sourceDisplayName,
                sourceClassName,
                dialog.SelectedProperties);
        }
    }

    private void PasteInspectorProperties_Click(
        object sender,
        RoutedEventArgs eventArgs) =>
        PasteInspectorProperties();

    private bool TryGetSinglePropertySource(
        [NotNullWhen(true)] out XuiSyntaxNode? source,
        out string sourceDisplayName,
        out string sourceClassName,
        out IReadOnlyList<XuiCatalogPropertySelection> properties)
    {
        source = null;
        sourceDisplayName = string.Empty;
        sourceClassName = string.Empty;
        properties = [];
        if (_document is null ||
            SelectedNodes() is not [XuiSyntaxNode selected])
        {
            return false;
        }

        source = selected;
        string? id = XuiModelReader.GetId(source, _document.Text);
        sourceDisplayName = string.IsNullOrWhiteSpace(id)
            ? source.Name
            : id;
        sourceClassName = ClassCatalog.ResolveClass(
            source,
            _document.Text).Class.Name;
        properties = ClassCatalog.SelectProperties(
            [source],
            _document.Text,
            includeAdvanced: true);
        return true;
    }

    private void SetPropertyClipboard(
        string sourceDisplayName,
        string sourceClassName,
        IReadOnlyList<XuiCopiedInspectorProperty> properties)
    {
        XuiCopiedInspectorProperty[] copyable = properties
            .Where(property =>
                XuiPropertyTransfer.CanCopy(property.Name))
            .GroupBy(
                static property => property.Name,
                StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(
                static property => property.Category,
                StringComparer.Ordinal)
            .ThenBy(
                static property => property.Name,
                StringComparer.Ordinal)
            .ToArray();
        if (copyable.Length == 0)
        {
            _propertyClipboard = null;
            SetStatus("Ui.Main.Status.NoCopyableProperties");
            UpdatePropertyTransferChrome();
            return;
        }

        _propertyClipboard = new XuiInspectorPropertyClipboard(
            sourceDisplayName,
            sourceClassName,
            copyable);
        SetStatus(
            copyable.Length == 1
                ? "Ui.Main.Status.CopiedOneProperty"
                : "Ui.Main.Status.CopiedProperties",
            copyable.Length == 1
                ? copyable[0].Name
                : copyable.Length,
            sourceDisplayName);
        UpdatePropertyTransferChrome();
    }

    private XuiInspectorPropertyPasteResult PasteInspectorProperties()
    {
        if (_document is null ||
            _propertyClipboard is not
                XuiInspectorPropertyClipboard clipboard)
        {
            SetStatus("Ui.Main.Status.CopyBeforePaste");
            return new XuiInspectorPropertyPasteResult(0, 0, 0, 0);
        }

        XuiSyntaxNode[] destinations = SelectedNodes();
        if (destinations.Length == 0)
        {
            SetStatus("Ui.Main.Status.SelectPasteDestination");
            return new XuiInspectorPropertyPasteResult(0, 0, 0, 0);
        }

        List<PropertyPasteAssignment> assignments = [];
        int incompatible = 0;
        int unchanged = 0;
        foreach (XuiSyntaxNode destination in destinations)
        {
            if (IsLocked(destination.Key))
            {
                incompatible += clipboard.Properties.Count;
                continue;
            }

            foreach (XuiCopiedInspectorProperty property in
                     clipboard.Properties)
            {
                if (!XuiPropertyTransfer.IsApplicable(
                        ClassCatalog,
                        destination,
                        _document.Text,
                        property.Name) ||
                    ValidateProperty(
                        property.Name,
                        property.Value) is not null)
                {
                    incompatible++;
                    continue;
                }

                string? currentValue =
                    XuiModelReader.GetPropertyValue(
                        destination,
                        _document.Text,
                        property.Name);
                if (string.Equals(
                        currentValue,
                        property.Value,
                        StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }

                assignments.Add(new PropertyPasteAssignment(
                    destination.Key,
                    property.Name,
                    property.Value));
            }
        }

        if (assignments.Count > 0)
        {
            string description = clipboard.Properties.Count == 1
                ? $"Paste {clipboard.Properties[0].Name}"
                : $"Paste {clipboard.Properties.Count:N0} inspector properties";
            XuiMessageDescriptor descriptionDescriptor =
                clipboard.Properties.Count == 1
                    ? new XuiMessageDescriptor(
                        "Ui.Command.PasteProperty",
                        "Paste {0}",
                        clipboard.Properties[0].Name)
                    : new XuiMessageDescriptor(
                        "Ui.Command.PasteInspectorProperties",
                        "Paste {0:N0} inspector properties",
                        clipboard.Properties.Count);
            bool pasted = ExecuteBatch(
                () =>
                {
                    foreach (PropertyPasteAssignment assignment in
                             assignments)
                    {
                        XuiSyntaxNode? current =
                            _document.SyntaxTree.FindByKey(
                                assignment.NodeKey);
                        if (current is not null)
                        {
                            SetNodeProperty(
                                current,
                                assignment.PropertyName,
                                assignment.Value);
                        }
                    }
                },
                description,
                descriptionDescriptor);
            if (!pasted)
            {
                return new XuiInspectorPropertyPasteResult(
                    destinations.Length,
                    0,
                    incompatible,
                    unchanged);
            }
        }

        XuiInspectorPropertyPasteResult result = new(
            destinations.Length,
            assignments.Count,
            incompatible,
            unchanged);
        if (assignments.Count == 0)
        {
            SetStatus(
                incompatible > 0
                    ? "Ui.Main.Status.NothingPasted"
                    : "Ui.Main.Status.NothingPastedMatched",
                incompatible,
                unchanged);
        }
        else
        {
            SetStatus(
                "Ui.Main.Status.PastedProperties",
                assignments.Count,
                destinations.Length,
                incompatible,
                unchanged);
        }
        return result;
    }

    private void UpdatePropertyTransferChrome()
    {
        bool singleSource = _document is not null &&
                            _selectedKeys.Count == 1;
        bool hasDestination = _document is not null &&
                              _selectedKeys.Count > 0;
        bool hasClipboard =
            _propertyClipboard?.Properties.Count > 0;
        CopyInspectorPropertiesButton.IsEnabled = singleSource;
        AdvancedCopyInspectorPropertiesButton.IsEnabled = singleSource;
        PasteInspectorPropertiesButton.IsEnabled =
            hasDestination && hasClipboard;
        CopyInspectorPropertiesMenuItem.IsEnabled = singleSource;
        AdvancedCopyInspectorPropertiesMenuItem.IsEnabled = singleSource;
        PasteInspectorPropertiesMenuItem.IsEnabled =
            hasDestination && hasClipboard;
        PropertyClipboardText.Text = _propertyClipboard is null
            ? UiLocalization.Text("Ui.Main.Clipboard.Empty")
            : UiLocalization.Format(
                "Ui.Main.Clipboard.Summary",
                _propertyClipboard.Properties.Count,
                _propertyClipboard.SourceDisplayName);
        PropertyClipboardText.ToolTip = _propertyClipboard is null
            ? UiLocalization.Text("Ui.Main.Clipboard.Help")
            : BuildPropertyClipboardToolTip(_propertyClipboard);
    }

    private static string BuildPropertyClipboardToolTip(
        XuiInspectorPropertyClipboard clipboard)
    {
        const int visiblePropertyLimit = 12;
        IEnumerable<string> lines = clipboard.Properties
            .Take(visiblePropertyLimit)
            .Select(property =>
                $"{property.Name} = {property.Value}");
        if (clipboard.Properties.Count > visiblePropertyLimit)
        {
            lines = lines.Append(
                UiLocalization.Format(
                    "Ui.Main.Clipboard.More",
                    clipboard.Properties.Count - visiblePropertyLimit));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static XuiCopiedInspectorProperty ToCopiedProperty(
        XuiCatalogPropertySelection property) =>
        new(
            property.Definition.Name,
            property.EffectiveValue,
            property.Definition.Category,
            property.Definition.Type,
            property.IsAuthored);

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

    private void AdvancedInspector_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _settings.ShowAdvancedInspector =
            AdvancedInspectorToggle.IsChecked == true;
        BuildInspector();
    }

    private void InspectorReset_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            sender is not Button
            {
                Tag: InspectorPropertyRow
                {
                    CanReset: true,
                } row,
            })
        {
            return;
        }

        string[] keys = _selectedKeys.ToArray();
        ExecuteBatch(
            () =>
            {
                foreach (string key in keys)
                {
                    XuiSyntaxNode? node =
                        _document.SyntaxTree.FindByKey(key);
                    XuiPropertyEntry? property = node is null
                        ? null
                        : XuiModelReader.GetProperty(
                            node,
                            _document.Text,
                            row.Name);
                    if (property is not null)
                    {
                        _document.Execute(
                            XuiCommandFactory.RemoveElement(
                                _document,
                                property.Element));
                    }
                }
            },
            $"Reset {row.Name}",
            new XuiMessageDescriptor(
                "Ui.Command.Reset",
                "Reset {0}",
                row.Name));
    }

    private void TextStyleFlag_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_updatingSemanticEditors ||
            sender is not CheckBox { IsChecked: bool enabled } checkBox)
        {
            return;
        }

        (string Name, XuiKnownTextStyle Flag) property = checkBox.Name switch
        {
            nameof(TextBoldCheckBox) =>
                ("Bold", XuiKnownTextStyle.Bold),
            nameof(TextItalicCheckBox) =>
                ("Italic", XuiKnownTextStyle.Italic),
            nameof(TextUnderlineCheckBox) =>
                ("Underline", XuiKnownTextStyle.Underline),
            _ => default,
        };
        if (property.Name is null)
        {
            return;
        }

        ApplyTextStyleFlag(property.Name, property.Flag, enabled);
    }

    private void TextHorizontal_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_updatingSemanticEditors ||
            TextHorizontalComboBox.SelectedItem is not ComboBoxItem
            {
                Tag: string tag,
            } ||
            !Enum.TryParse(
                tag,
                ignoreCase: false,
                out XuiTextHorizontalStyle alignment))
        {
            return;
        }

        ApplyHorizontalTextAlignment(alignment);
    }

    private void TextVertical_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_updatingSemanticEditors ||
            TextVerticalComboBox.SelectedItem is not ComboBoxItem
            {
                Tag: string alignment,
            })
        {
            return;
        }

        ApplyVerticalTextAlignment(alignment);
    }

    private void PivotValue_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs)
    {
        if (!_updatingSemanticEditors)
        {
            CommitPivotEditorValues();
        }
    }

    private void PivotValue_KeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            CommitPivotEditorValues();
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            RefreshSemanticEditors(SelectedNodes());
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
    }

    private void PivotPreset_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_document is null ||
            sender is not Button { Tag: string tag } ||
            !Enum.TryParse(
                tag,
                ignoreCase: false,
                out XuiPivotPreset preset) ||
            SelectedNodes() is not [XuiSyntaxNode node])
        {
            return;
        }

        XuiVector3 current = ReadVector3(node, "Pivot", default);
        XuiVector2 size = ReadElementSize(node);
        CommitPivot(
            node.Key,
            XuiPivotEditing.ApplyPreset(
                preset,
                size,
                current.Z),
            $"Set pivot to {tag}",
            descriptionDescriptor: new XuiMessageDescriptor(
                "Ui.Command.SetPivotPreset",
                "Set pivot preset"));
    }

    private void PreservePivotPosition_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_updatingSemanticEditors)
        {
            return;
        }

        _settings.PreservePivotVisualPosition =
            PreservePivotPositionCheckBox.IsChecked == true;
        Viewport.PreservePivotVisualPosition =
            _settings.PreservePivotVisualPosition;
        RefreshSemanticEditors(SelectedNodes());
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
            RawXmlErrorText.Text = UiLocalization.Text(
                "Ui.Main.RawXml.LoadBeforeApply");
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
            SetStatus(
                "Ui.Main.Status.ReplacedRawXml",
                current.Name);
        }
        catch (Exception exception) when (
            exception is XuiParseException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException)
        {
            RawXmlErrorText.Text = UiLocalization.Format(
                "Ui.Common.ErrorDetails",
                exception.Message);
            SetStatus("Ui.Main.Status.RawXmlRejected");
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
            SetStatus("Ui.Main.Status.SelectTimelineScope");
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
            SetStatus("Ui.Main.Status.NamedFrameInvalid");
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

    private void AssetSearch_TextChanged(
        object sender,
        TextChangedEventArgs eventArgs) =>
        RefreshAssetRows();

    private void AssetKind_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs) =>
        RefreshAssetRows();

    private void RefreshAssetRows()
    {
        if (_assetCatalog is null ||
            AssetSearchTextBox is null ||
            AssetKindComboBox is null)
        {
            AssetRows.ReplaceAll([]);
            UpdateAssetEmptyState();
            return;
        }

        string query = AssetSearchTextBox.Text.Trim();
        string kind = (AssetKindComboBox.SelectedItem as ComboBoxItem)?
            .Tag?.ToString() ?? "All";
        AssetRows.ReplaceAll(_assetCatalog.Assets.Where(asset =>
            (kind == "All" ||
             asset.Kind.ToString().Equals(
                 kind,
                 StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 ||
             asset.Name.Contains(
                 query,
                 StringComparison.OrdinalIgnoreCase) ||
             asset.LogicalPath.Contains(
                 query,
                 StringComparison.OrdinalIgnoreCase) ||
             asset.SourceDisplayPath.Contains(
                 query,
                 StringComparison.OrdinalIgnoreCase))));
        UpdateAssetEmptyState();
    }

    private void UpdateAssetEmptyState()
    {
        if (AssetEmptyStateText is not null)
        {
            AssetEmptyStateText.Visibility = AssetRows.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void AssetList_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs) =>
        _assetDragStart = eventArgs.GetPosition(AssetList);

    private void AssetList_MouseMove(
        object sender,
        MouseEventArgs eventArgs)
    {
        if (eventArgs.LeftButton != MouseButtonState.Pressed ||
            AssetList.SelectedItem is not XuiCatalogAsset asset)
        {
            return;
        }

        Vector delta = eventArgs.GetPosition(AssetList) - _assetDragStart;
        if (Math.Abs(delta.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DataObject data = new(typeof(XuiCatalogAsset), asset);
        DragDrop.DoDragDrop(
            AssetList,
            data,
            DragDropEffects.Copy);
        Viewport.ClearAssetDragPreview();
    }

    private async void AssetList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs eventArgs) =>
        await OpenSelectedAssetAsync().ConfigureAwait(true);

    private void Viewport_AssetDragOver(
        object sender,
        DragEventArgs eventArgs)
    {
        if (eventArgs.Data.GetData(typeof(XuiCatalogAsset)) is not
                XuiCatalogAsset asset ||
            asset.Kind == XuiCatalogAssetKind.Screen)
        {
            eventArgs.Effects = DragDropEffects.None;
            Viewport.ClearAssetDragPreview();
            eventArgs.Handled = true;
            return;
        }

        XuiVector2 logical = Viewport.LogicalPointFromControl(
            eventArgs.GetPosition(Viewport));
        XuiVector2 size = asset.LogicalSize ??
            new XuiVector2(160, 32);
        Viewport.SetAssetDragPreview(asset.Name, logical, size);
        eventArgs.Effects = DragDropEffects.Copy;
        eventArgs.Handled = true;
    }

    private void Viewport_AssetDragLeave(
        object sender,
        DragEventArgs eventArgs) =>
        Viewport.ClearAssetDragPreview();

    private void Viewport_AssetDrop(
        object sender,
        DragEventArgs eventArgs)
    {
        Viewport.ClearAssetDragPreview();
        if (_document is null ||
            eventArgs.Data.GetData(typeof(XuiCatalogAsset)) is not
                XuiCatalogAsset asset)
        {
            return;
        }

        XuiVector2 logical = Viewport.LogicalPointFromControl(
            eventArgs.GetPosition(Viewport));
        string? hitKey = Viewport.HitNodeKey(logical);
        XuiSyntaxNode? hit = hitKey is null
            ? null
            : _document.SyntaxTree.FindByKey(hitKey);
        bool autoSize =
            AutoSizeAssetDropCheckBox.IsChecked == true ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (asset.Kind == XuiCatalogAssetKind.Texture)
        {
            XuiRenderNode? rendered = Viewport.FrameForTesting?.Nodes
                .LastOrDefault(node =>
                    node.SelectionKey.Equals(
                        hitKey,
                        StringComparison.Ordinal) &&
                    !node.IsVisualTemplatePart);
            if (hit is not null &&
                rendered?.Kind == XuiRenderKind.Image &&
                !IsLocked(hit.Key))
            {
                ExecuteBatch(
                    () =>
                    {
                        SetNodeProperty(hit, "ImagePath", asset.Name);
                        if (autoSize &&
                            asset.LogicalSize is XuiVector2 textureSize &&
                            _document.SyntaxTree.FindByKey(hit.Key) is
                                XuiSyntaxNode current)
                        {
                            SetNodeProperty(
                                current,
                                "Width",
                                textureSize.X.ToString(
                                    "0.000000",
                                    CultureInfo.InvariantCulture));
                            XuiSyntaxNode? resized =
                                _document.SyntaxTree.FindByKey(hit.Key);
                            if (resized is not null)
                            {
                                SetNodeProperty(
                                    resized,
                                    "Height",
                                    textureSize.Y.ToString(
                                        "0.000000",
                                        CultureInfo.InvariantCulture));
                            }
                        }
                    },
                    "Set image texture",
                    new XuiMessageDescriptor(
                        "Ui.Command.SetImageTexture",
                        "Set image texture"));
                return;
            }

            XuiSyntaxNode parent =
                hit is not null &&
                (rendered?.Kind is
                    XuiRenderKind.Group or
                    XuiRenderKind.Scene ||
                 XuiModelReader.VisualChildren(hit).Any())
                    ? hit
                    : hit is null
                        ? _document.Root
                        : FindVisualParent(hit) ?? _document.Root;
            if (IsLocked(parent.Key))
            {
            SetStatus("Ui.Main.Status.TextureTargetLocked");
                return;
            }

            XuiVector2 local = Viewport.WorldPointToNodeLocal(
                parent.Key,
                logical);
            XuiVector2 textureSize =
                asset.LogicalSize ?? new XuiVector2(128, 128);
            InsertVisualChild(
                parent,
                new XuiElementCreationRequest
                {
                    Preset = XuiElementPreset.Image,
                    Id = SuggestedUniqueId(XuiElementPreset.Image),
                    Width = textureSize.X,
                    Height = textureSize.Y,
                    Position = new XuiVector3(local.X, local.Y, 0),
                    ImagePath = asset.Name,
                    Color = "0xffffffff",
                });
            return;
        }

        if (hit is null || IsCanvasRoot(hit) || IsLocked(hit.Key))
        {
            SetStatus("Ui.Main.Status.DropOnEditable");
            return;
        }

        if (asset.Kind == XuiCatalogAssetKind.Visual)
        {
            SetNodeProperty(hit, "Visual", asset.Name);
        }
        else if (asset.Kind == XuiCatalogAssetKind.Font)
        {
            SetNodeProperty(hit, "Font", asset.Name);
        }
    }

    private async void AssetOpen_Click(
        object sender,
        RoutedEventArgs eventArgs) =>
        await OpenSelectedAssetAsync().ConfigureAwait(true);

    private async Task OpenSelectedAssetAsync()
    {
        if (AssetList.SelectedItem is not XuiCatalogAsset asset ||
            asset.Kind is not (
                XuiCatalogAssetKind.Screen or
                XuiCatalogAssetKind.Visual) ||
            asset.SourceFile is null ||
            !await ConfirmDiscardAsync().ConfigureAwait(true))
        {
            return;
        }

        XuiResolvedFile source = asset.SourceFile;
        if (!source.IsVirtual &&
            !asset.IsReadOnly &&
            File.Exists(source.Path))
        {
            await OpenDocumentAsync(source.Path).ConfigureAwait(true);
            SelectOpenedVisual(asset);
            return;
        }

        using DelegateDisposable loading = BeginViewportLoading();
        try
        {
            byte[] bytes = await source.ReadAllBytesAsync()
                .ConfigureAwait(true);
            XuiDocument document = XuiDocument.FromBytes(
                bytes,
                new XuiDocumentSource(
                    Path.GetFileName(source.RelativePath),
                    source.DisplayPath,
                    source.RelativePath,
                    IsReadOnly: true),
                CreateDocumentOptions());
            AttachDocument(document);
            RefreshAll();
            await RebuildAssetResolverAsync().ConfigureAwait(true);
            SelectOpenedVisual(asset);
            SetStatus("Ui.Main.Status.ReadOnlyAssetOpened");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            XuiParseException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.OpenAsset"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SelectOpenedVisual(XuiCatalogAsset asset)
    {
        if (asset.Kind != XuiCatalogAssetKind.Visual ||
            _document is null ||
            asset.SourceFile is null)
        {
            return;
        }

        bool sameSource =
            string.Equals(
                _document.Path,
                asset.SourceFile.Path,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                _document.Source?.Origin,
                asset.SourceFile.DisplayPath,
                StringComparison.OrdinalIgnoreCase);
        if (!sameSource)
        {
            return;
        }

        XuiSyntaxNode[] matches = _document.Root
            .DescendantsAndSelf()
            .Where(node =>
                node.Name == "XuiVisual" &&
                string.Equals(
                    XuiModelReader.GetId(node, _document.Text),
                    asset.Name,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 1)
        {
            SelectNodeKeysForTesting([matches[0].Key]);
        }
    }

    private async void AssetCopy_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (AssetList.SelectedItem is not XuiCatalogAsset asset ||
            _assetCatalog is null ||
            !TryGetWorkspaceRoot(out string workspace))
        {
            return;
        }

        try
        {
            string destination =
                await _assetCatalog.CopyToWorkspaceAsync(
                    asset,
                    workspace).ConfigureAwait(true);
            SetStatus(
                "Ui.Main.Status.CopiedTo",
                destination);
            await RebuildAssetResolverAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.CopyWorkspace"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AssetNewScreen_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!TryGetWorkspaceRoot(out string workspace))
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = UiLocalization.Text(
                "Ui.Main.Workspace.CreateScreen"),
            InitialDirectory = workspace,
            Filter = UiLocalization.Text("Ui.Main.Filter.Xui"),
            DefaultExt = ".xui",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = "NewScreen.xui",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (!PathIsInside(workspace, dialog.FileName))
            {
                throw new InvalidOperationException(
                    UiLocalization.Text(
                        "Ui.Main.Error.ScreenOutsideWorkspace"));
            }

            XuiWorkspaceResourceService service = new(workspace);
            string path = await service.CreateScreenAsync(
                Path.GetRelativePath(workspace, dialog.FileName))
                .ConfigureAwait(true);
            await OpenDocumentAsync(path).ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.CreateScreen"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AssetNewVisual_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!TryGetWorkspaceRoot(out string workspace))
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = UiLocalization.Text(
                "Ui.Main.Workspace.CreateVisual"),
            InitialDirectory = workspace,
            Filter = UiLocalization.Text("Ui.Main.Filter.Xui"),
            DefaultExt = ".xui",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = "NewVisual.xui",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (!PathIsInside(workspace, dialog.FileName))
            {
                throw new InvalidOperationException(
                    UiLocalization.Text(
                        "Ui.Main.Error.VisualOutsideWorkspace"));
            }

            string visualId =
                Path.GetFileNameWithoutExtension(dialog.FileName);
            XuiWorkspaceResourceService service = new(workspace);
            string path = await service.CreateVisualAsync(
                Path.GetRelativePath(workspace, dialog.FileName),
                visualId).ConfigureAwait(true);
            await OpenDocumentAsync(path).ConfigureAwait(true);
            if (_document is not null)
            {
                XuiSyntaxNode? visual = _document.Root
                    .DescendantsAndSelf()
                    .SingleOrDefault(node =>
                        node.Name == "XuiVisual" &&
                        XuiModelReader.GetId(
                            node,
                            _document.Text) == visualId);
                if (visual is not null)
                {
                    SelectNodeKeysForTesting([visual.Key]);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.CreateVisual"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AssetRename_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!TryGetEditableWorkspaceAsset(
                out XuiCatalogAsset? asset,
                out XuiWorkspaceResourceService? service))
        {
            return;
        }

        if (asset.Kind == XuiCatalogAssetKind.Visual)
        {
            if (!EnsureWorkspaceTransactionReady())
            {
                return;
            }

            ReferenceReplacementWindow visualDialog = new(
                service,
                asset.Name,
                asset.SourceFile!.Path)
            {
                Owner = this,
            };
            if (visualDialog.ShowDialog() == true &&
                visualDialog.Result is
                    XuiReferenceTransactionResult result)
            {
                SetStatus(
                    "Ui.Main.Status.RenamedVisual",
                    Math.Max(0, result.ChangedReferences - 1),
                    result.BackupDirectory);
                _lastReferenceTransaction = result;
                UndoReferenceTransactionButton.IsEnabled = true;
                await RefreshAfterWorkspaceTransactionAsync(result)
                    .ConfigureAwait(true);
            }

            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = UiLocalization.Text(
                "Ui.Main.Workspace.RenameXui"),
            InitialDirectory =
                Path.GetDirectoryName(asset.SourceFile!.Path),
            Filter = UiLocalization.Text("Ui.Main.Filter.Xui"),
            DefaultExt = ".xui",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = Path.GetFileName(asset.SourceFile.Path),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            string destination = service.RenameLooseXui(
                asset.SourceFile.Path,
                Path.GetRelativePath(
                    service.WorkspaceRoot,
                    dialog.FileName));
            SetStatus(
                "Ui.Main.Status.RenamedTo",
                destination);
            await RebuildAssetResolverAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.RenameXui"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AssetDelete_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!TryGetEditableWorkspaceAsset(
                out XuiCatalogAsset? asset,
                out XuiWorkspaceResourceService? service))
        {
            return;
        }

        if (_document?.Path is string current &&
            current.Equals(
                asset.SourceFile!.Path,
                StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Ui.Main.Status.CloseBeforeDelete");
            return;
        }

        if (MessageBox.Show(
                this,
                asset.Kind == XuiCatalogAssetKind.Visual
                    ? UiLocalization.Format(
                        "Ui.Main.Delete.VisualPrompt",
                        asset.Name)
                    : UiLocalization.Format(
                        "Ui.Main.Delete.XuiPrompt",
                        asset.Name),
                asset.Kind == XuiCatalogAssetKind.Visual
                    ? UiLocalization.Text(
                        "Ui.Main.Delete.VisualTitle")
                    : UiLocalization.Text(
                        "Ui.Main.Delete.XuiTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (asset.Kind == XuiCatalogAssetKind.Visual)
            {
                XuiVisualDeleteResult result =
                    await service.DeleteLooseVisualAsync(
                        asset.SourceFile!.Path,
                        asset.Name).ConfigureAwait(true);
                SetStatus(
                    "Ui.Main.Status.DeletedVisual",
                    result.BackupFile);
            }
            else
            {
                string trash =
                    service.DeleteLooseXui(asset.SourceFile!.Path);
                SetStatus(
                    "Ui.Main.Status.MovedToTrash",
                    trash);
            }

            await RebuildAssetResolverAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.DeleteXui"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AssetReferences_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (AssetList.SelectedItem is not XuiCatalogAsset asset ||
            !TryGetWorkspaceRoot(out string workspace) ||
            !EnsureWorkspaceTransactionReady())
        {
            return;
        }

        ReferenceReplacementWindow dialog = new(
            new XuiWorkspaceResourceService(workspace),
            asset.Name)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true &&
            dialog.Result is XuiReferenceTransactionResult result)
        {
            _lastReferenceTransaction = result;
            UndoReferenceTransactionButton.IsEnabled = true;
            SetStatus(
                "Ui.Main.Status.ReboundReferences",
                result.ChangedReferences,
                result.ChangedFiles,
                result.BackupDirectory);
            await RefreshAfterWorkspaceTransactionAsync(result)
                .ConfigureAwait(true);
        }
    }

    private async void AssetUndoReferenceTransaction_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_lastReferenceTransaction is not
                XuiReferenceTransactionResult transaction ||
            !TryGetWorkspaceRoot(out string workspace) ||
            !EnsureWorkspaceTransactionReady())
        {
            return;
        }

        try
        {
            XuiWorkspaceResourceService service = new(workspace);
            int restored = await service.UndoReplacementAsync(
                transaction).ConfigureAwait(true);
            _lastReferenceTransaction = null;
            UndoReferenceTransactionButton.IsEnabled = false;
            SetStatus(
                "Ui.Main.Status.UndidRebind",
                restored);
            await RefreshAfterWorkspaceTransactionAsync(transaction)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.UndoRebind"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool EnsureWorkspaceTransactionReady()
    {
        if (_document?.IsDirty != true)
        {
            return true;
        }

        MessageBox.Show(
            this,
            UiLocalization.Text(
                "Ui.Main.Workspace.SaveBeforeTransaction"),
            UiLocalization.Text("Ui.Main.Workspace.UnsavedTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private async Task RefreshAfterWorkspaceTransactionAsync(
        XuiReferenceTransactionResult transaction)
    {
        string? currentPath = _document?.Path;
        if (currentPath is not null &&
            transaction.CommittedFiles.Any(snapshot =>
                snapshot.FilePath.Equals(
                    currentPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            await OpenDocumentAsync(currentPath).ConfigureAwait(true);
            return;
        }

        await RebuildAssetResolverAsync().ConfigureAwait(true);
    }

    private bool TryGetWorkspaceRoot(out string workspace)
    {
        workspace = _settings.WorkspaceRoot?.Trim() ?? string.Empty;
        if (workspace.Length > 0)
        {
            workspace = Path.GetFullPath(workspace);
            Directory.CreateDirectory(workspace);
            return true;
        }

        MessageBox.Show(
            this,
            UiLocalization.Text("Ui.Main.Workspace.RequiredPrompt"),
            UiLocalization.Text("Ui.Main.Workspace.RequiredTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private bool TryGetEditableWorkspaceAsset(
        [NotNullWhen(true)] out XuiCatalogAsset? asset,
        [NotNullWhen(true)] out XuiWorkspaceResourceService? service)
    {
        asset = AssetList.SelectedItem as XuiCatalogAsset;
        service = null;
        if (asset is null ||
            asset.Kind is not (
                XuiCatalogAssetKind.Screen or
                XuiCatalogAssetKind.Visual) ||
            asset.SourceFile is null ||
            asset.SourceFile.IsVirtual ||
            asset.IsReadOnly ||
            !TryGetWorkspaceRoot(out string workspace) ||
            !PathIsInside(workspace, asset.SourceFile.Path))
        {
            SetStatus("Ui.Main.Status.AssetNotEditable");
            return false;
        }

        service = new XuiWorkspaceResourceService(workspace);
        return true;
    }

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
                 eventArgs.Key == Key.C)
        {
            CopyInspectorProperties_Click(this, new RoutedEventArgs());
        }
        else if (!editingText &&
                 modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
                 eventArgs.Key == Key.C)
        {
            AdvancedCopyInspectorProperties_Click(
                this,
                new RoutedEventArgs());
        }
        else if (!editingText &&
                 modifiers == ModifierKeys.Control &&
                 eventArgs.Key == Key.V)
        {
            PasteInspectorProperties_Click(this, new RoutedEventArgs());
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
                UiLocalization.Text("Ui.Main.Unsaved.ClosePrompt"),
                UiLocalization.Text("Ui.Main.Unsaved.Title"),
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
        using DelegateDisposable loading = BeginViewportLoading();
        try
        {
            SetStatus("Ui.Main.Status.OpeningXui");
            XuiDocument document = await XuiDocument.OpenAsync(
                path,
                CreateDocumentOptions()).ConfigureAwait(true);
            AttachDocument(document);
            _recoverySuggestedPath = null;
            _activeRecovery = null;
            AddRecentFile(path);
            RefreshAll();
            await RebuildAssetResolverAsync().ConfigureAwait(true);
            SetStatus("Ui.Main.Status.Ready");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            XuiParseException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.OpenXui"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetStatus("Ui.Main.Status.OpenFailed");
        }
    }

    private async Task OpenAssetDocumentAsync(XuiAssetEntry entry)
    {
        using DelegateDisposable loading = BeginViewportLoading();
        try
        {
            SetStatus(
                "Ui.Main.Status.OpeningStock",
                entry.FileName);
            XuiDocument document = await XuiDocument.OpenAssetAsync(
                entry,
                CreateDocumentOptions()).ConfigureAwait(true);
            AttachDocument(document);
            _recoverySuggestedPath = null;
            _activeRecovery = null;
            RefreshAll();
            await RebuildAssetResolverAsync().ConfigureAwait(true);
            SetStatus("Ui.Main.Status.StockOpened");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            XuiParseException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.OpenStock"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetStatus("Ui.Main.Status.StockOpenFailed");
        }
    }

    private async Task OpenRecoveryAsync(RecoverySnapshot snapshot)
    {
        using DelegateDisposable loading = BeginViewportLoading();
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
            SetStatus("Ui.Main.Status.RecoveryOpened");
        }
        catch (Exception exception) when (
            exception is IOException or XuiParseException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.OpenRecovery"),
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

        using DelegateDisposable loading = BeginViewportLoading();
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
        _assetCatalog = null;
        AssetRows.ReplaceAll([]);
        UpdateAssetEmptyState();
        _textureDiagnostics.Clear();
        _layoutSession = null;
        Viewport.SetAssetResolver(null);
        SetAssetStatus("Ui.Main.Asset.IndexingExternal");
        try
        {
            await _assetResolver.RebuildAsync().ConfigureAwait(true);
            _assetCatalog = new DyingLightXuiAssetCatalog(_assetResolver);
            RefreshAssetRows();
            Viewport.SetAssetResolver(_assetResolver);
            int diagnosticCount = _assetResolver.Diagnostics.Count;
            SetAssetStatus(
                "Ui.Main.Asset.Summary",
                _assetResolver.Files.Count,
                _assetResolver.Localization?.Entries.Count ?? 0,
                DyingLightInstallProfile.NormalizeLocale(
                    _settings.Locale),
                diagnosticCount);
            RefreshEvaluation();
        }
        catch (OperationCanceledException)
        {
            SetAssetStatus("Ui.Main.Asset.IndexingCancelled");
        }
    }

    private async Task<bool> EnsureInstallIndexAsync(bool showErrors)
    {
        string? install = _settings.DyingLightInstallPath;
        if (string.IsNullOrWhiteSpace(install) ||
            !DyingLightInstallIndex.LooksLikeInstall(install))
        {
            _installIndex = null;
            SetAssetStatus("Ui.Main.Asset.InstallNotConfigured");
            if (showErrors)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text(
                        "Ui.Main.Asset.ConfigureInstall"),
                    UiLocalization.Text(
                        "Ui.Main.Asset.DataRequiredTitle"),
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

        using DelegateDisposable loading = BeginViewportLoading();
        try
        {
            SetAssetStatus("Ui.Main.Asset.IndexingGame");
            DyingLightInstallIndex index = new(
                new DyingLightInstallProfile(fullPath, _settings.Locale));
            await index.RebuildAsync().ConfigureAwait(true);
            _installIndex = index;
            SetAssetStatus(
                "Ui.Main.Asset.InstallSummary",
                index.StockXuiFiles.Count,
                index.Entries.Count,
                index.Profile.NormalizedLocale);
            return index.StockXuiFiles.Count > 0;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _installIndex = null;
            SetAssetStatus("Ui.Main.Asset.IndexingFailed");
            if (showErrors)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Format(
                        "Ui.Common.ErrorDetails",
                        exception.Message),
                    UiLocalization.Text("Ui.Main.Error.IndexGame"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return false;
        }
    }

    private DelegateDisposable BeginViewportLoading()
    {
        _viewportLoadingDepth++;
        ViewportLoadingOverlay.Visibility = Visibility.Visible;
        return new DelegateDisposable(EndViewportLoading);
    }

    private void EndViewportLoading()
    {
        _viewportLoadingDepth = Math.Max(0, _viewportLoadingDepth - 1);
        if (_viewportLoadingDepth == 0)
        {
            ViewportLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    internal IDisposable BeginViewportLoadingForTesting() =>
        BeginViewportLoading();

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() =>
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    private XuiDocumentOptions CreateDocumentOptions()
        => CreateDocumentOptions(_settings);

    internal static XuiDocumentOptions CreateDocumentOptions(
        EditorSettings settings)
    {
        IEnumerable<string> protectedRoots = settings.AssetRoots
            .Where(static root => root.EffectiveIsReadOnly)
            .Select(static root => root.Path);
        IEnumerable<string> writableRoots = settings.AssetRoots
            .Where(static root => !root.EffectiveIsReadOnly)
            .Select(static root => root.Path);
        if (!string.IsNullOrWhiteSpace(settings.WorkspaceRoot))
        {
            writableRoots = writableRoots.Append(settings.WorkspaceRoot);
        }

        if (!string.IsNullOrWhiteSpace(settings.DyingLightInstallPath))
        {
            protectedRoots = protectedRoots.Append(
                settings.DyingLightInstallPath);
            writableRoots = writableRoots.Append(Path.Combine(
                settings.DyingLightInstallPath,
                "DevTools",
                "workshop"));
        }

        return new XuiDocumentOptions(
            protectedRoots
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .ToArray(),
            writableRoots
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .ToArray());
    }

    private void Document_Changed(object? sender, EventArgs eventArgs)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!_disposed && !Dispatcher.HasShutdownStarted)
            {
                _ = Dispatcher.InvokeAsync(
                    () => Document_Changed(sender, eventArgs),
                    DispatcherPriority.DataBind);
            }

            return;
        }

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

    private void History_HistoryChanged(object? sender, EventArgs eventArgs)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!_disposed && !Dispatcher.HasShutdownStarted)
            {
                _ = Dispatcher.InvokeAsync(
                    UpdateChrome,
                    DispatcherPriority.DataBind);
            }

            return;
        }

        UpdateChrome();
    }

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
        ResolveTimelineScopeFromSelection(
            selection,
            preferSelectedKeyFrame: true);
        BuildInspector(selection);
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
            UpdateNavigationConnections();
            RefreshPreviewState();
            UpdateTimelinePositionChrome();
            if (!_evaluationDiagnosticsInitialized ||
                !_evaluationDiagnostics.SequenceEqual(frame.Diagnostics))
            {
                _evaluationDiagnostics = frame.Diagnostics;
                _evaluationDiagnosticsInitialized = true;
                RefreshDiagnosticsOnly();
            }

            DocumentStatsText.Text = UiLocalization.Format(
                "Ui.Main.DocumentStats",
                frame.Nodes.Count,
                _timelineSet?.Timelines.Count ?? 0);
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
            SetStatus("Ui.Main.Status.EvaluationFailed");
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
        return new XuiRenderContext(
            _previewScenario,
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

        bool priorSelectionSync = _syncingSelection;
        _syncingSelection = true;
        try
        {
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

            HierarchyCountText.Text = UiLocalization.Format(
                "Ui.Main.Hierarchy.Count",
                rows.Count);
        }
        finally
        {
            _syncingSelection = priorSelectionSync;
        }
    }

    private void BuildInspector(SelectionSnapshot? selection = null)
    {
        InspectorProperties.Clear();
        if (_document is null)
        {
            RawXmlExpander.Visibility = Visibility.Collapsed;
            RefreshSemanticEditors([]);
            return;
        }

        SelectionSnapshot snapshot = selection ?? CaptureSelection();
        XuiSyntaxNode[] nodes = snapshot.Nodes;
        SelectionCountText.Text = nodes.Length switch
        {
            0 => string.Empty,
            1 => UiLocalization.Text("Ui.Main.Selection.One"),
            _ => UiLocalization.Format(
                "Ui.Main.Selection.Many",
                nodes.Length),
        };
        InspectorHint.Visibility = nodes.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (nodes.Length == 0)
        {
            BreadcrumbText.Text = string.Empty;
            RawXmlExpander.Visibility = Visibility.Collapsed;
            RefreshSemanticEditors(nodes);
            return;
        }

        IReadOnlyList<XuiCatalogPropertySelection> selections =
            ClassCatalog.SelectProperties(
                nodes,
                _document.Text,
                _settings.ShowAdvancedInspector);
        foreach (XuiCatalogPropertySelection propertySelection in selections)
        {
            string name = propertySelection.Definition.Name;
            string?[] values = nodes
                .Select(node => XuiModelReader.GetPropertyValue(
                    node,
                    _document.Text,
                    name))
                .ToArray();
            bool anyAuthored = values.Any(static value => value is not null);
            bool mixed = anyAuthored &&
                         (values.Any(static value => value is null) ||
                          values.Distinct(StringComparer.Ordinal).Count() > 1);
            bool hasAnimationTrack = false;
            bool hasAnimationKey = false;
            if (nodes.Length == 1 &&
                propertySelection.Definition.IsAnimatable &&
                _timelineWorkspace?.ActiveScope is XuiTimelineScope activeScope &&
                snapshot.Ids.FirstOrDefault() is string targetId)
            {
                XuiTrack[] animationTracks = activeScope.Timelines
                    .Where(timeline => timeline.TargetId.Equals(
                        targetId,
                        StringComparison.Ordinal))
                    .SelectMany(static timeline => timeline.Tracks)
                    .Where(track => track.PropertyName.Equals(
                        name,
                        StringComparison.Ordinal))
                    .ToArray();
                hasAnimationTrack = animationTracks.Length > 0;
                hasAnimationKey = animationTracks.Any(track =>
                    track.KeyFrames.Any(frame =>
                        frame.Tick == CurrentTimelineTick));
            }
            InspectorPropertyRow row = new(
                name,
                mixed
                    ? MixedValue
                    : values[0] ??
                      propertySelection.Definition.DefaultValue,
                propertySelection.Definition.Category,
                mixed,
                ClassCatalog.FindProperty(name) is null,
                propertySelection.Definition.Choices,
                isBooleanToggle:
                    propertySelection.Definition.Type ==
                    XuiPropertyType.Boolean,
                isAuthored: anyAuthored,
                propertySelection.Definition,
                hasAnimationTrack,
                hasAnimationKey);
            row.Error = mixed ? null : ValidateProperty(name, row.Value);
            InspectorProperties.Add(row);
        }

        BreadcrumbText.Text = snapshot.Breadcrumb;
        RefreshSemanticEditors(nodes);
        RefreshRawXmlEditor(nodes);
    }

    private void RefreshSemanticEditors(XuiSyntaxNode[] nodes)
    {
        _updatingSemanticEditors = true;
        try
        {
            bool textSelection =
                nodes.Length > 0 &&
                nodes.All(IsIuiTextNode);
            TextStyleExpander.Visibility = textSelection
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (textSelection)
            {
                SetTriState(
                    TextBoldCheckBox,
                    nodes.Select(node => EffectiveTextFlag(
                        node,
                        "Bold",
                        XuiKnownTextStyle.Bold)));
                SetTriState(
                    TextItalicCheckBox,
                    nodes.Select(node => EffectiveTextFlag(
                        node,
                        "Italic",
                        XuiKnownTextStyle.Italic)));
                SetTriState(
                    TextUnderlineCheckBox,
                    nodes.Select(node => EffectiveTextFlag(
                        node,
                        "Underline",
                        XuiKnownTextStyle.Underline)));
                SelectComboTag(
                    TextHorizontalComboBox,
                    CommonValue(nodes.Select(EffectiveHorizontalAlignment)));
                SelectComboTag(
                    TextVerticalComboBox,
                    CommonValue(nodes.Select(EffectiveVerticalAlignment)));
                int[] styles = nodes
                    .Select(ReadTextStyle)
                    .Distinct()
                    .ToArray();
                TextStyleRawText.Text = styles.Length == 1
                    ? UiLocalization.Format(
                        "Ui.Main.TextStyle.Raw",
                        styles[0],
                        XuiTextStyleCodec.ToHexString(styles[0]),
                        XuiTextStyleCodec.ToHexString(
                            XuiTextStyleCodec.Decode(
                                styles[0]).UnmappedBits))
                    : UiLocalization.Text(
                        "Ui.Main.TextStyle.Mixed");
            }

            bool pivotSelection = nodes.Length == 1;
            PivotExpander.Visibility = pivotSelection
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (!pivotSelection)
            {
                return;
            }

            XuiSyntaxNode node = nodes[0];
            XuiVector3 pivot = ReadVector3(node, "Pivot", default);
            PivotXTextBox.Text = pivot.X.ToString(
                "0.######",
                CultureInfo.InvariantCulture);
            PivotYTextBox.Text = pivot.Y.ToString(
                "0.######",
                CultureInfo.InvariantCulture);
            PivotZTextBox.Text = pivot.Z.ToString(
                "0.######",
                CultureInfo.InvariantCulture);
            bool canPreserve = CanPreservePivot(node, out string reason);
            PreservePivotPositionCheckBox.IsEnabled = canPreserve;
            PreservePivotPositionCheckBox.IsChecked =
                _settings.PreservePivotVisualPosition;
            PivotStatusText.Text = canPreserve
                ? _settings.PreservePivotVisualPosition
                    ? UiLocalization.Text(
                        "Ui.Main.Pivot.PreserveMode")
                    : UiLocalization.Text(
                        "Ui.Main.Pivot.RawMode")
                : reason;
            Viewport.PreservePivotVisualPosition =
                _settings.PreservePivotVisualPosition && canPreserve;
        }
        finally
        {
            _updatingSemanticEditors = false;
        }
    }

    private bool EffectiveTextFlag(
        XuiSyntaxNode node,
        string propertyName,
        XuiKnownTextStyle style)
    {
        if (_document is not null &&
            XuiModelReader.GetPropertyValue(
                node,
                _document.Text,
                propertyName) is string authored &&
            XuiValueParser.TryBoolean(authored, out bool value))
        {
            return value;
        }

        return XuiTextStyleCodec.Decode(ReadTextStyle(node)).Has(style);
    }

    private string EffectiveHorizontalAlignment(XuiSyntaxNode node)
    {
        string? explicitValue = FirstAuthoredValue(
            node,
            "HorizontalAlign",
            "ContentHorizontalAlign",
            "DefaultHorizontalAlign");
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue.Trim().ToLowerInvariant() switch
            {
                "left" or "0" => "Left",
                "center" or "1" => "Center",
                "right" or "2" => "Right",
                _ => "Unspecified",
            };
        }

        return XuiTextStyleCodec
            .Decode(ReadTextStyle(node))
            .HorizontalAlignment
            .ToString();
    }

    private string EffectiveVerticalAlignment(XuiSyntaxNode node)
    {
        string? explicitValue = FirstAuthoredValue(
            node,
            "VerticalAlign",
            "ContentVerticalAlign",
            "DefaultVerticalAlign");
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue.Trim().ToLowerInvariant() switch
            {
                "middle" or "1" => "Middle",
                "bottom" or "2" => "Bottom",
                _ => "Top",
            };
        }

        if (_document is not null &&
            XuiModelReader.GetPropertyValue(
                node,
                _document.Text,
                "VerticalAlignDown") is string bottom &&
            XuiValueParser.TryBoolean(bottom, out bool isBottom) &&
            isBottom)
        {
            return "Bottom";
        }

        return XuiTextStyleCodec.Decode(ReadTextStyle(node)).VerticalMiddle
            ? "Middle"
            : "Top";
    }

    private string? FirstAuthoredValue(
        XuiSyntaxNode node,
        params string[] propertyNames)
    {
        if (_document is null)
        {
            return null;
        }

        foreach (string propertyName in propertyNames)
        {
            string? value = XuiModelReader.GetPropertyValue(
                node,
                _document.Text,
                propertyName);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private int ReadTextStyle(XuiSyntaxNode node)
    {
        string? raw = _document is null
            ? null
            : XuiModelReader.GetPropertyValue(
                node,
                _document.Text,
                "TextStyle");
        return XuiTextStyleCodec.TryParse(
            raw ?? "0",
            out XuiDecodedTextStyle style)
            ? style.RawValue
            : 0;
    }

    private static void SetTriState(
        CheckBox checkBox,
        IEnumerable<bool> values)
    {
        bool[] distinct = values.Distinct().ToArray();
        checkBox.IsThreeState = distinct.Length > 1;
        checkBox.IsChecked = distinct.Length == 1
            ? distinct[0]
            : null;
    }

    private static string? CommonValue(IEnumerable<string> values)
    {
        string[] distinct = values
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return distinct.Length == 1
            ? distinct[0]
            : null;
    }

    private static void SelectComboTag(
        ComboBox comboBox,
        string? tag)
    {
        comboBox.SelectedItem = tag is null
            ? null
            : comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Tag as string,
                        tag,
                        StringComparison.Ordinal));
    }

    private void ApplyTextStyleFlag(
        string propertyName,
        XuiKnownTextStyle style,
        bool enabled)
    {
        if (_document is null)
        {
            return;
        }

        string[] keys = SelectedNodes()
            .Where(IsIuiTextNode)
            .Select(static node => node.Key)
            .ToArray();
        ExecuteBatch(
            () =>
            {
                foreach (string key in keys)
                {
                    XuiSyntaxNode? node =
                        _document.SyntaxTree.FindByKey(key);
                    if (node is null)
                    {
                        continue;
                    }

                    if (XuiModelReader.GetProperty(
                            node,
                            _document.Text,
                            propertyName) is not null)
                    {
                        SetNodeProperty(
                            node,
                            propertyName,
                            enabled ? "true" : "false");
                        continue;
                    }

                    SetLegacyTextStyle(
                        key,
                        raw => XuiTextStyleCodec.SetFlag(
                            raw,
                            style,
                            enabled));
                }
            },
            $"Set text {propertyName}",
            new XuiMessageDescriptor(
                "Ui.Command.SetTextProperty",
                "Set text {0}",
                propertyName));
    }

    private void ApplyHorizontalTextAlignment(
        XuiTextHorizontalStyle alignment)
    {
        if (_document is null)
        {
            return;
        }

        string[] keys = SelectedNodes()
            .Where(IsIuiTextNode)
            .Select(static node => node.Key)
            .ToArray();
        ExecuteBatch(
            () =>
            {
                foreach (string key in keys)
                {
                    XuiSyntaxNode? node =
                        _document.SyntaxTree.FindByKey(key);
                    if (node is null)
                    {
                        continue;
                    }

                    string? authoredName = FirstAuthoredPropertyName(
                        node,
                        "HorizontalAlign",
                        "ContentHorizontalAlign",
                        "DefaultHorizontalAlign");
                    if (authoredName is not null)
                    {
                        SetNodeProperty(
                            node,
                            authoredName,
                            alignment.ToString().ToLowerInvariant());
                    }
                    else
                    {
                        SetLegacyTextStyle(
                            key,
                            raw => XuiTextStyleCodec.SetHorizontalAlignment(
                                raw,
                                alignment));
                    }
                }
            },
            "Set horizontal text alignment",
            new XuiMessageDescriptor(
                "Ui.Command.SetHorizontalTextAlignment",
                "Set horizontal text alignment"));
    }

    private void ApplyVerticalTextAlignment(string alignment)
    {
        if (_document is null)
        {
            return;
        }

        string normalized = alignment.ToLowerInvariant();
        string[] keys = SelectedNodes()
            .Where(IsIuiTextNode)
            .Select(static node => node.Key)
            .ToArray();
        ExecuteBatch(
            () =>
            {
                foreach (string key in keys)
                {
                    XuiSyntaxNode? node =
                        _document.SyntaxTree.FindByKey(key);
                    if (node is null)
                    {
                        continue;
                    }

                    string? authoredName = FirstAuthoredPropertyName(
                        node,
                        "VerticalAlign",
                        "ContentVerticalAlign",
                        "DefaultVerticalAlign");
                    if (authoredName is not null)
                    {
                        SetNodeProperty(node, authoredName, normalized);
                        continue;
                    }

                    SetLegacyTextStyle(
                        key,
                        raw => XuiTextStyleCodec.SetVerticalMiddle(
                            raw,
                            normalized == "middle"));
                    node = _document.SyntaxTree.FindByKey(key);
                    if (node is null)
                    {
                        continue;
                    }

                    bool isBottom = normalized == "bottom";
                    if (isBottom ||
                        XuiModelReader.GetProperty(
                            node,
                            _document.Text,
                            "VerticalAlignDown") is not null)
                    {
                        SetNodeProperty(
                            node,
                            "VerticalAlignDown",
                            isBottom ? "true" : "false");
                    }
                }
            },
            "Set vertical text alignment",
            new XuiMessageDescriptor(
                "Ui.Command.SetVerticalTextAlignment",
                "Set vertical text alignment"));
    }

    private string? FirstAuthoredPropertyName(
        XuiSyntaxNode node,
        params string[] propertyNames)
    {
        if (_document is null)
        {
            return null;
        }

        return propertyNames.FirstOrDefault(name =>
            XuiModelReader.GetProperty(
                node,
                _document.Text,
                name) is not null);
    }

    private void SetLegacyTextStyle(
        string nodeKey,
        Func<int, int> update)
    {
        if (_document?.SyntaxTree.FindByKey(nodeKey) is not
            XuiSyntaxNode node)
        {
            return;
        }

        string raw = XuiModelReader.GetPropertyValue(
            node,
            _document.Text,
            "TextStyle") ?? "0";
        if (!XuiTextStyleCodec.TryParse(
                raw,
                out XuiDecodedTextStyle current))
        {
            throw new InvalidOperationException(
                UiLocalization.Format(
                    "Ui.Main.Error.InvalidTextStyleSemantic",
                    raw));
        }

        int updated = update(current.RawValue);
        if (updated == current.RawValue &&
            XuiModelReader.GetProperty(
                node,
                _document.Text,
                "TextStyle") is null)
        {
            return;
        }

        SetNodeProperty(
            node,
            "TextStyle",
            XuiTextStyleCodec.ToDecimalString(updated));
    }

    private void CommitPivotEditorValues()
    {
        if (_document is null ||
            SelectedNodes() is not [XuiSyntaxNode node] ||
            !XuiValueParser.TryNumber(
                PivotXTextBox.Text,
                out double x) ||
            !XuiValueParser.TryNumber(
                PivotYTextBox.Text,
                out double y) ||
            !XuiValueParser.TryNumber(
                PivotZTextBox.Text,
                out double z))
        {
            PivotStatusText.Text = UiLocalization.Text(
                "Ui.Main.Pivot.RequiresThreeNumbers");
            return;
        }

        CommitPivot(
            node.Key,
            new XuiVector3(x, y, z),
            "Edit pivot",
            descriptionDescriptor: new XuiMessageDescriptor(
                "Ui.Command.EditPivot",
                "Edit pivot"));
    }

    private void CommitPivot(
        string nodeKey,
        XuiVector3 newPivot,
        string description,
        bool? preserveOverride = null,
        XuiMessageDescriptor? descriptionDescriptor = null)
    {
        if (_document?.SyntaxTree.FindByKey(nodeKey) is not
            XuiSyntaxNode node)
        {
            return;
        }

        XuiVector3 oldPivot = ReadVector3(node, "Pivot", default);
        XuiVector3 delta = newPivot - oldPivot;
        if (Math.Abs(delta.X) <= 0.000001 &&
            Math.Abs(delta.Y) <= 0.000001 &&
            Math.Abs(delta.Z) <= 0.000001)
        {
            return;
        }

        string? targetId = XuiModelReader.GetId(node, _document.Text);
        bool preserve =
            (preserveOverride ??
             _settings.PreservePivotVisualPosition) &&
            CanPreservePivot(node, out _);
        XuiVector3 oldPosition = ReadVector3(
            node,
            "Position",
            default);
        XuiVector3 newPosition = oldPosition;
        XuiVector3 scale = ReadVector3(
            node,
            "Scale",
            new XuiVector3(1, 1, 1));
        double rotation = ReadRotationDegrees(node);
        if (preserve)
        {
            newPosition = XuiPivotEditing.CompensatePosition(
                oldPosition,
                oldPivot,
                newPivot,
                scale,
                rotation);
        }

        List<TimelineVectorEdit> timelineEdits = [];
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            timelineEdits.AddRange(PlanTimelineVectorEdits(
                node,
                targetId,
                XuiTimelineProperty.Pivot,
                value => value + delta));
            if (preserve)
            {
                timelineEdits.AddRange(PlanTimelineVectorEdits(
                    node,
                    targetId,
                    XuiTimelineProperty.Position,
                    value => XuiPivotEditing.CompensatePosition(
                        value,
                        oldPivot,
                        newPivot,
                        scale,
                        rotation)));
            }
        }

        ExecuteBatch(
            () =>
            {
                XuiSyntaxNode? current =
                    _document.SyntaxTree.FindByKey(nodeKey);
                if (current is null)
                {
                    return;
                }

                SetNodeProperty(
                    current,
                    "Pivot",
                    FormatVector3(newPivot));
                if (preserve)
                {
                    current = _document.SyntaxTree.FindByKey(nodeKey);
                    if (current is not null)
                    {
                        SetNodeProperty(
                            current,
                            "Position",
                            FormatVector3(newPosition));
                    }
                }

                foreach (TimelineVectorEdit edit in timelineEdits)
                {
                    XuiSyntaxNode? propertyNode =
                        _document.SyntaxTree.FindByKey(
                            edit.PropertyNodeKey);
                    if (propertyNode is not null)
                    {
                        _document.Execute(
                            XuiCommandFactory.SetElementValue(
                                _document,
                                propertyNode,
                                edit.Value));
                    }
                }
            },
            description,
            descriptionDescriptor);
        SetStatus(
            preserve
                ? "Ui.Main.Status.PivotPreserved"
                : "Ui.Main.Status.PivotRaw");
    }

    private List<TimelineVectorEdit> PlanTimelineVectorEdits(
        XuiSyntaxNode node,
        string targetId,
        XuiTimelineProperty property,
        Func<XuiVector3, XuiVector3> transform)
    {
        EnsureCompiledLayout();
        if (_layoutSession is null)
        {
            return [];
        }

        string? recursionBarrier = TimelineRecursionBarrierFor(node);
        List<TimelineVectorEdit> edits = [];
        foreach (XuiTrack track in _layoutSession.TimelineScopes.Scopes
                     .Where(scope =>
                         KeyIsAncestorOrSelf(
                             scope.ScopeKey,
                             node.Key) &&
                         (recursionBarrier is null ||
                          KeyIsAncestorOrSelf(
                              recursionBarrier,
                              scope.ScopeKey)))
                     .SelectMany(static scope => scope.Timelines)
                     .Where(timeline => timeline.TargetId.Equals(
                         targetId,
                         StringComparison.Ordinal))
                     .SelectMany(static timeline => timeline.Tracks)
                     .Where(track => track.KnownProperty == property))
        {
            foreach (XuiSyntaxNode keyFrame in track.KeyFrames
                         .Select(static frame => frame.Syntax))
            {
                XuiSyntaxNode? propertyNode = keyFrame
                    .Elements("Prop")
                    .ElementAtOrDefault(track.SourcePropertyIndex);
                if (propertyNode is null)
                {
                    continue;
                }

                string raw = propertyNode.GetDecodedValue(_document!.Text);
                XuiVector3 value;
                bool authoredVector3 =
                    XuiValueParser.TryVector3(raw, out value);
                if (!authoredVector3 &&
                    XuiValueParser.TryVector2(
                        raw,
                        out XuiVector2 vector2))
                {
                    value = new XuiVector3(vector2.X, vector2.Y, 0);
                }
                else if (!authoredVector3)
                {
                    throw new InvalidOperationException(
                        UiLocalization.Format(
                            "Ui.Main.Error.InvalidVectorKey",
                            property,
                            raw));
                }

                XuiVector3 updated = transform(value);
                edits.Add(new TimelineVectorEdit(
                    propertyNode.Key,
                    authoredVector3
                        ? FormatVector3(updated)
                        : FormatVector2(new XuiVector2(
                            updated.X,
                            updated.Y))));
            }
        }

        return edits;
    }

    private bool CanPreservePivot(
        XuiSyntaxNode node,
        out string reason)
    {
        string? targetId = _document is null
            ? null
            : XuiModelReader.GetId(node, _document.Text);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            reason =
                UiLocalization.Text(
                    "Ui.Main.Pivot.RequiresId");
            return false;
        }

        if (_timelineSet is not null &&
            !XuiPivotEditing.CanPreserveVisualPosition(
                _timelineSet.Timelines,
                targetId))
        {
            reason =
                UiLocalization.Text(
                    "Ui.Main.Pivot.AnimatedUnavailable");
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private XuiVector2 ReadElementSize(XuiSyntaxNode node)
    {
        XuiRenderNode? rendered = Viewport.FrameForTesting?
            .Nodes
            .FirstOrDefault(candidate =>
                candidate.SelectionKey.Equals(
                    node.Key,
                    StringComparison.Ordinal) &&
                !candidate.IsVisualTemplatePart);
        if (rendered is not null)
        {
            return rendered.AuthoredSize;
        }

        double width = ReadNumber(node, "Width", 40);
        double height = ReadNumber(node, "Height", 20);
        return new XuiVector2(width, height);
    }

    private double ReadNumber(
        XuiSyntaxNode node,
        string name,
        double fallback)
    {
        string? raw = _document is null
            ? null
            : XuiModelReader.GetPropertyValue(
                node,
                _document.Text,
                name);
        return XuiValueParser.TryNumber(raw ?? string.Empty, out double value)
            ? value
            : fallback;
    }

    private XuiVector3 ReadVector3(
        XuiSyntaxNode node,
        string name,
        XuiVector3 fallback)
    {
        string? raw = _document is null
            ? null
            : XuiModelReader.GetPropertyValue(
                node,
                _document.Text,
                name);
        if (XuiValueParser.TryVector3(raw ?? string.Empty, out XuiVector3 value))
        {
            return value;
        }

        return XuiValueParser.TryVector2(
            raw ?? string.Empty,
            out XuiVector2 vector2)
            ? new XuiVector3(vector2.X, vector2.Y, fallback.Z)
            : fallback;
    }

    private double ReadRotationDegrees(XuiSyntaxNode node)
    {
        string? raw = _document is null
            ? null
            : XuiModelReader.GetPropertyValue(
                node,
                _document.Text,
                "Rotation");
        if (XuiValueParser.TryNumber(raw ?? string.Empty, out double number))
        {
            return number;
        }

        if (XuiValueParser.TryVector3(
                raw ?? string.Empty,
                out XuiVector3 vector))
        {
            return vector.Z;
        }

        return XuiValueParser.TryQuaternion(
            raw ?? string.Empty,
            out XuiQuaternion quaternion)
            ? quaternion.ZRotationDegrees
            : 0;
    }

    private static string FormatVector2(XuiVector2 value) =>
        FormattableString.Invariant(
            $"{value.X:0.######},{value.Y:0.######}");

    private static string FormatVector3(XuiVector3 value) =>
        FormattableString.Invariant(
            $"{value.X:0.######},{value.Y:0.######},{value.Z:0.######}");

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
        RawXmlStatusText.Text = UiLocalization.Format(
            "Ui.Main.RawXml.Characters",
            length);
        if (!RawXmlExpander.IsExpanded)
        {
            ClearRawXmlEditor(keepStatus: true);
            return;
        }

        if (length > AutomaticRawXmlCharacterLimit && !allowLarge)
        {
            ClearRawXmlEditor(keepStatus: true);
            RawXmlStatusText.Text = UiLocalization.Format(
                "Ui.Main.RawXml.Large",
                length / (1024.0 * 1024.0));
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
            _statusDescriptor = null;
            _lastLocalizedStatusText = null;
            StatusText.Text = error;
            return;
        }

        string committedValue = row.Name == "TextStyle" &&
                                XuiTextStyleCodec.TryParse(
                                    row.Value,
                                    out XuiDecodedTextStyle textStyle)
            ? XuiTextStyleCodec.ToDecimalString(textStyle.RawValue)
            : row.Value;
        row.Value = committedValue;
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
                       committedValue,
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
                        committedValue)
                    : XuiCommandFactory.SetElementValue(
                        _document,
                        property.Element,
                        committedValue);
                _document.Execute(command);
            }
        });
    }

    private void UpdateSelectionSurfaces()
    {
        StopPlayback();
        if (_settings.ForceShowCurrentGroup)
        {
            RebuildCurrentGroupForceShow();
            RefreshEvaluation();
        }

        Viewport.SetSelectedKeys(_selectedKeys);
        UpdateAlignmentChrome();
        UpdateNavigationConnections();
        SelectionSnapshot selection = CaptureSelection();
        ResolveTimelineScopeFromSelection(selection);
        BuildInspector(selection);
        UpdatePropertyTransferChrome();
        RefreshPreviewState(selection);
        UpdateTimelineData(selection);
        RefreshNamedFrameEditor();
    }

    private void RefreshPreviewState(
        SelectionSnapshot? selection = null)
    {
        SelectionSnapshot snapshot = selection ?? CaptureSelection();
        XuiSyntaxNode? selectedNode =
            snapshot.Nodes.Length == 1
                ? snapshot.Nodes[0]
                : null;
        bool singleEditable =
            _document is not null &&
            selectedNode is not null &&
            !IsLocked(selectedNode.Key);
        AddChildButton.IsEnabled = singleEditable;
        AddParentButton.IsEnabled =
            singleEditable &&
            !IsCanvasRoot(selectedNode!);
        AddPropertyButton.IsEnabled =
            singleEditable &&
            selectedNode!.FirstElement("Properties") is not null;
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
            PreviewStateText.Text = UiLocalization.Format(
                "Ui.Main.Preview.HiddenOverride",
                row.DisplayName,
                row.VisibilityToolTip);
            ForceShowInspectorButton.IsEnabled = false;
            RestoreComposedPoseButton.IsEnabled =
                _timelineWorkspace?.ActiveScope is not null &&
                _timelineWorkspace.ActiveTickIsComposed == false;
            return;
        }

        XuiRenderFrame? frame = Viewport.FrameForTesting;
        if (_layoutSession is null || frame is null)
        {
            PreviewStateText.Text = UiLocalization.Text(
                "Ui.Main.Preview.NotRendered");
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
        PreviewStateText.Text = LocalizePreviewExplanation(
            explanation);
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
                mixedScopes: false);
            RefreshKeyFrameEditor();
            UpdateTimelineScopeChrome();
            return;
        }

        SelectionSnapshot snapshot = selection ?? CaptureSelection();
        TimelineEditor.SetScopeData(
            _timelineWorkspace?.ActiveScope,
            TimelineTargetIds(snapshot),
            CurrentTimelineTick,
            _timelineWorkspace?.HasMixedSelection == true);
        UpdateTimelineScopeChrome();
        RefreshKeyFrameEditor();
        UpdateTimelinePositionChrome();
    }

    private string[] TimelineTargetIds(
        SelectionSnapshot selection)
    {
        if (_document is null ||
            _timelineWorkspace?.ActiveScope is not XuiTimelineScope activeScope)
        {
            return [];
        }

        IEnumerable<XuiSyntaxNode> candidates =
            IncludeDescendantsToggle.IsChecked == true
                ? selection.Nodes.SelectMany(node =>
                    new[] { node }.Concat(
                        XuiModelReader.VisualDescendants(node)))
                : selection.Nodes;
        return candidates
            .Where(node =>
                _timelineWorkspace.Catalog.ResolveForNode(
                    node,
                    _document.Text) is XuiTimelineScope nodeScope &&
                nodeScope.ScopeKey.Equals(
                    activeScope.ScopeKey,
                    StringComparison.Ordinal))
            .Select(node => XuiModelReader.GetId(node, _document.Text))
            .Where(static id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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
        TickText.Text = UiLocalization.Format(
            "Ui.Main.Timeline.Position",
            currentTick,
            currentTick / 60.0);
        UpdateTimelineScopeChrome();
    }

    private void UpdateTimelineScopeChrome()
    {
        XuiTimelineScope? scope = _timelineWorkspace?.ActiveScope;
        bool hasScope = scope is not null &&
                        _timelineWorkspace?.HasMixedSelection != true;
        bool hasVisibleTracks =
            hasScope && TimelineEditor.HasVisibleTracks;
        TimelineTransportPanel.IsEnabled = _document is not null;
        AddAnimationButton.IsEnabled =
            _document is not null && _selectedKeys.Count > 0;
        AddTrackButton.IsEnabled =
            _document is not null && _selectedKeys.Count == 1;
        IncludeDescendantsToggle.IsEnabled =
            hasScope && _selectedKeys.Count > 0;
        TimelineActionsPanel.IsEnabled = hasVisibleTracks;
        TimelineEditPanel.IsEnabled = hasVisibleTracks;
        TimelineEditor.IsEnabled = hasVisibleTracks;
        TickSlider.IsEnabled = hasVisibleTracks;
        AnimationMenuItem.IsEnabled = _document is not null;
        RestoreComposedPoseButton.IsEnabled =
            hasVisibleTracks &&
            _timelineWorkspace?.ActiveTickIsComposed == false;
        TimelineScopeText.Text = _timelineWorkspace?.HasMixedSelection == true
            ? UiLocalization.Text("Ui.Main.Timeline.MixedScopes")
            : scope is null
                ? UiLocalization.Text("Ui.Main.Timeline.NoScope")
                : UiLocalization.Format(
                    _timelineWorkspace?.ActiveTickIsComposed == true
                        ? "Ui.Main.Timeline.ScopeComposed"
                        : "Ui.Main.Timeline.Scope",
                    scope.DisplayName,
                    CurrentTimelineTick,
                    scope.MaximumTick);
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

            KeyPropertyText.Text = track.PropertyName;
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
                track.PropertyName,
                track.KnownProperty,
                value,
                out _))
        {
            string message =
                UiLocalization.Format(
                    "Ui.Main.Keyframe.InvalidValue",
                    value,
                    track.PropertyName);
            KeyValueErrorText.Text = message;
            SetStatus(
                "Ui.Main.Keyframe.InvalidValue",
                value,
                track.PropertyName);
            return;
        }

        XuiSyntaxNode? current =
            _document.SyntaxTree.FindByKey(selected.Syntax.Key);
        XuiSyntaxNode? prop = current?
            .Elements("Prop")
            .ElementAtOrDefault(track.SourcePropertyIndex);
        if (prop is null)
        {
            KeyValueErrorText.Text = UiLocalization.Text(
                "Ui.Main.Keyframe.MissingProp");
            SetStatus("Ui.Main.Keyframe.MissingProp");
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
            SetStatus("Ui.Main.Status.InvalidEase");
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
            _statusDescriptor = null;
            _lastLocalizedStatusText = null;
            StatusText.Text = UiLocalization.Format(
                "Ui.Common.ErrorDetails",
                exception.Message);
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
            SetStatus("Ui.Main.Status.IndentNeedsSibling");
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
            SetStatus("Ui.Main.Status.AlreadyTopLevel");
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
            _statusDescriptor = null;
            _lastLocalizedStatusText = null;
            StatusText.Text = UiLocalization.Format(
                "Ui.Common.ErrorDetails",
                exception.Message);
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

    private bool ExecuteBatch(
        Action edits,
        string description = "Edit selection",
        XuiMessageDescriptor? descriptionDescriptor = null,
        bool animationMetadataOnly = false)
    {
        bool succeeded = true;
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
                descriptionDescriptor ??= new XuiMessageDescriptor(
                    "Ui.Command.EditSelection",
                    "Edit selection");
                _document.ExecuteBatch(
                    description,
                    edits,
                    descriptionDescriptor);
            }
        }
        catch (Exception exception) when (
            exception is XuiParseException or
            InvalidOperationException or
            ArgumentException)
        {
            _refreshPending = true;
            succeeded = false;
            SetStatus(
                "Ui.Main.Status.EditRejected",
                exception.Message);
        }
        finally
        {
            _suppressRefresh = false;
            if (_refreshPending)
            {
                if (!animationMetadataOnly ||
                    !TryRefreshAnimationMetadataOnly())
                {
                    RefreshAll();
                }
            }
        }

        return succeeded;
    }

    private bool TryRefreshAnimationMetadataOnly()
    {
        if (_document is null ||
            _layoutSession is null ||
            !_layoutSession.TryRebindAnimationMetadata(
                _document,
                _assetResolver))
        {
            return false;
        }

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

        return true;
    }

    private XuiSyntaxNode? FindNodeAtStart(int start) =>
        _document?.SyntaxTree.FindByStart(start);

    private void SetCurrentTick(int tick)
    {
        if (!TimelineEditor.HasVisibleTracks ||
            _timelineWorkspace?.ActiveScope is null)
        {
            return;
        }

        _timelineWorkspace.SetActiveTick(tick);
        RefreshInspectorAnimationIndicators();
        RefreshEvaluation();
    }

    private void RefreshInspectorAnimationIndicators()
    {
        if (_document is null ||
            _selectedKeys.Count != 1 ||
            _timelineWorkspace?.ActiveScope is not XuiTimelineScope scope ||
            _document.SyntaxTree.FindByKey(_selectedKeys.First()) is not
                XuiSyntaxNode node ||
            XuiModelReader.GetId(node, _document.Text) is not string targetId)
        {
            foreach (InspectorPropertyRow row in InspectorProperties)
            {
                row.UpdateAnimationState(false, false);
            }
            return;
        }

        Dictionary<string, XuiTrack[]> tracks = scope.Timelines
            .Where(timeline => timeline.TargetId.Equals(
                targetId,
                StringComparison.Ordinal))
            .SelectMany(static timeline => timeline.Tracks)
            .GroupBy(static track => track.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                StringComparer.Ordinal);
        int tick = CurrentTimelineTick;
        foreach (InspectorPropertyRow row in InspectorProperties)
        {
            XuiTrack[] matching = tracks.GetValueOrDefault(row.Name) ?? [];
            row.UpdateAnimationState(
                matching.Length > 0,
                matching.Any(track => track.KeyFrames.Any(frame =>
                    frame.Tick == tick)));
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs eventArgs)
    {
        if (!_isPlaying ||
            !TimelineEditor.HasVisibleTracks ||
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
        PlayPauseButton.Content =
            UiLocalization.Text("Ui.Main.Transport.Play");
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
                Title = UiLocalization.Text("Ui.Main.SaveAs.Title"),
                Filter = UiLocalization.Text("Ui.Main.Filter.XuiAll"),
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
            XuiSaveResult result = await _document.SaveAsync(target).ConfigureAwait(true);
            RecoveryService.DeleteForPath(priorPath);
            if (_activeRecovery is not null)
            {
                RecoveryService.Delete(_activeRecovery);
                _activeRecovery = null;
            }

            _recoverySuggestedPath = null;
            AddRecentFile(result.Path);
            SetStatus(
                result.Disposition == XuiSaveDisposition.Unchanged
                    ? "Ui.Main.Status.NoChanges"
                    : result.BackupPath is null
                        ? "Ui.Main.Status.Saved"
                        : "Ui.Main.Status.SavedBackup",
                result.BackupPath is null
                    ? string.Empty
                    : Path.GetFileName(result.BackupPath));
            UpdateChrome();
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            if (!forceSaveAs)
            {
                SetStatus("Ui.Main.Status.SourceReadOnly");
                return await SaveDocumentAsync(forceSaveAs: true).ConfigureAwait(true);
            }

            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.SaveXui"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or
            XuiParseException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.Common.ErrorDetails",
                    exception.Message),
                UiLocalization.Text("Ui.Main.Error.SaveXui"),
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
            UiLocalization.Text("Ui.Main.Unsaved.OpenPrompt"),
            UiLocalization.Text("Ui.Main.Unsaved.Title"),
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
            SetStatus("Ui.Main.Status.RecoverySaved");
        }
        catch (IOException)
        {
            SetStatus("Ui.Main.Status.RecoveryFailed");
        }
    }

    private void SetStatus(string key, params object?[] arguments)
    {
        _statusDescriptor = new XuiMessageDescriptor(
            key,
            UiLocalization.EnglishText(key),
            arguments);
        RefreshLocalizedStatus(force: true);
    }

    private void RefreshLocalizedStatus(bool force = false)
    {
        if (_statusDescriptor is null ||
            (!force &&
             _lastLocalizedStatusText is not null &&
             !string.Equals(
                 StatusText.Text,
                 _lastLocalizedStatusText,
                 StringComparison.Ordinal)))
        {
            return;
        }

        _lastLocalizedStatusText = UiLocalization.Message(
            _statusDescriptor,
            _statusDescriptor.EnglishFallback);
        StatusText.Text = _lastLocalizedStatusText;
    }

    private void SetAssetStatus(string key, params object?[] arguments)
    {
        _assetStatusDescriptor = new XuiMessageDescriptor(
            key,
            UiLocalization.EnglishText(key),
            arguments);
        RefreshLocalizedAssetStatus(force: true);
    }

    private void RefreshLocalizedAssetStatus(bool force = false)
    {
        if (_assetStatusDescriptor is null ||
            (!force &&
             _lastLocalizedAssetStatusText is not null &&
             !string.Equals(
                 AssetStatusText.Text,
                 _lastLocalizedAssetStatusText,
                 StringComparison.Ordinal)))
        {
            return;
        }

        _lastLocalizedAssetStatusText = UiLocalization.Message(
            _assetStatusDescriptor,
            _assetStatusDescriptor.EnglishFallback);
        AssetStatusText.Text = _lastLocalizedAssetStatusText;
    }

    private static string LocalizePreviewExplanation(
        XuiPreviewStateExplanation explanation)
    {
        if (UiLocalization.EffectiveLanguage == "En")
        {
            return explanation.Summary;
        }

        return explanation.Reason == XuiPreviewStateReason.AnimatedHidden &&
               explanation.ScopeTick is int tick
            ? UiLocalization.Format(
                "Ui.Main.Preview.Reason.AnimatedHiddenAtTick",
                tick)
            : UiLocalization.Text(
                $"Ui.Main.Preview.Reason.{explanation.Reason}");
    }

    private void UpdateChrome()
    {
        string display = _document is null
            ? UiLocalization.Text("Ui.Main.Untitled")
            : _document.Path is null && _recoverySuggestedPath is not null
                ? UiLocalization.Format(
                    "Ui.Main.Recovered",
                    Path.GetFileName(_recoverySuggestedPath))
                : _document.DisplayName;
        bool dirty = _document?.IsDirty == true;
        Title = UiLocalization.Format(
            "Ui.Main.WindowTitle",
            dirty ? "● " : string.Empty,
            display);
        DocumentPathText.Text = _document?.Path ??
                                _document?.Source?.Origin ??
                                _recoverySuggestedPath ??
                                string.Empty;
        DirtyText.Text = dirty
            ? UiLocalization.Text("Ui.Main.Document.Modified")
            : _document?.Source?.IsReadOnly == true
                ? UiLocalization.Text("Ui.Main.Document.ReadOnly")
                : UiLocalization.Text("Ui.Main.Document.Saved");
        UndoMenuItem.IsEnabled = _document?.History.CanUndo == true;
        RedoMenuItem.IsEnabled = _document?.History.CanRedo == true;
        bool canExport = _document is not null &&
                         Viewport.HasRenderedFrame;
        ExportPngButton.IsEnabled = canExport;
        ExportPngMenuItem.IsEnabled = canExport;
        UpdateAlignmentChrome();
        UpdatePropertyTransferChrome();
        UndoMenuItem.Header = _document?.History.UndoDescription is string undo
            ? UiLocalization.Format(
                "Ui.Command.Undo",
                UiLocalization.Message(
                    _document.History.UndoDescriptionDescriptor,
                    undo))
            : UiLocalization.Text("Ui.Xaml.MainWindow.012");
        RedoMenuItem.Header = _document?.History.RedoDescription is string redo
            ? UiLocalization.Format(
                "Ui.Command.Redo",
                UiLocalization.Message(
                    _document.History.RedoDescriptionDescriptor,
                    redo))
            : UiLocalization.Text("Ui.Xaml.MainWindow.013");
    }

    private void UpdateAlignmentChrome()
    {
        bool canAlign = CanAlignSelection();
        AlignLeftButton.IsEnabled = canAlign;
        AlignCenterButton.IsEnabled = canAlign;
        AlignRightButton.IsEnabled = canAlign;
        AlignTopButton.IsEnabled = canAlign;
        AlignBottomButton.IsEnabled = canAlign;
    }

    private bool CanAlignSelection() =>
        _document is not null &&
        SelectedTransformRootKeys().Length > 0;

    private void ApplyViewportSettings()
    {
        Viewport.ShowGrid = _settings.ShowGrid;
        Viewport.ShowSafeArea = _settings.ShowSafeArea;
        Viewport.ShowUnknownBounds = _settings.ShowUnknownBounds;
        Viewport.SnapEnabled = _settings.SnapEnabled;
        Viewport.GridSize = Math.Max(1, _settings.GridSize);
        Viewport.MajorGridSize = _settings.MajorGridSize;
        Viewport.CoarseGridSize = _settings.CoarseGridSize;
        Viewport.SnapGridSize = _settings.SnapGridTier switch
        {
            XuiGridTier.Major => _settings.MajorGridSize,
            XuiGridTier.Coarse => _settings.CoarseGridSize,
            _ => _settings.GridSize,
        };
        Viewport.MinorGridColor = ParseGuideColor(
            _settings.MinorGridColor,
            Color.FromArgb(32, 54, 59, 64));
        Viewport.MajorGridColor = ParseGuideColor(
            _settings.MajorGridColor,
            Color.FromArgb(64, 83, 90, 98));
        Viewport.CoarseGridColor = ParseGuideColor(
            _settings.CoarseGridColor,
            Color.FromArgb(96, 112, 120, 130));
        Viewport.PreservePivotVisualPosition =
            _settings.PreservePivotVisualPosition;
        Viewport.ShowParentMask = _settings.ShowParentMask;
        Viewport.GrayOutsideSelectedGroup =
            _settings.GrayOutsideSelectedGroup;
        Viewport.ShowDesignTimeElements =
            _settings.ShowDesignTimeElements;
        Viewport.ShowNavigationConnections =
            _settings.ShowNavigationConnections;
        Viewport.RefreshEditorGuides();
        if (_document is null)
        {
            Viewport.SetFrame(null);
            return;
        }

        RefreshEvaluation();
    }

    private static Color ParseGuideColor(
        string? value,
        Color fallback)
    {
        try
        {
            return ColorConverter.ConvertFromString(value) is Color color
                ? color
                : fallback;
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
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
        if (name is "Outline" or "Shadow")
        {
            return XuiValueParser.TryBoolean(value, out _) ||
                   XuiValueParser.TryNumber(value, out _)
                ? null
                : UiLocalization.Format(
                    "Ui.Validation.BooleanOrStrength",
                    name);
        }

        if (name == "Anchor" &&
            (!XuiValueParser.TryInteger(value, out int anchor) ||
             anchor < 0 ||
             (anchor & ~0x7f) != 0))
        {
            return UiLocalization.Text(
                "Ui.Validation.AnchorBitmask");
        }

        if (name == "Rotation" &&
            !XuiValueParser.TryQuaternion(value, out _) &&
            !XuiValueParser.TryVector3(value, out _) &&
            !XuiValueParser.TryNumber(value, out _))
        {
            return UiLocalization.Text(
                "Ui.Validation.Rotation");
        }

        if (name == "TextStyle" &&
            !XuiTextStyleCodec.TryParse(value, out _))
        {
            return UiLocalization.Text(
                "Ui.Validation.TextStyle");
        }

        if ((name is "HorizontalAlign" or
             "ContentHorizontalAlign" or
             "DefaultHorizontalAlign") &&
            value.Trim().ToLowerInvariant() is not
                ("left" or "center" or "right" or "justify" or
                 "0" or "1" or "2" or "3"))
        {
            return UiLocalization.Format(
                "Ui.Validation.HorizontalAlign",
                name);
        }

        if ((name is "VerticalAlign" or
             "ContentVerticalAlign" or
             "DefaultVerticalAlign") &&
            value.Trim().ToLowerInvariant() is not
                ("top" or "middle" or "bottom" or "0" or "1" or "2"))
        {
            return UiLocalization.Format(
                "Ui.Validation.VerticalAlign",
                name);
        }

        XuiPropertyDefinition? definition = ClassCatalog.FindProperty(name);
        if (definition is null || name == "TextStyle")
        {
            return null;
        }

        bool valid = definition.Type switch
        {
            XuiPropertyType.Boolean =>
                XuiValueParser.TryBoolean(value, out _),
            XuiPropertyType.WholeNumber =>
                XuiValueParser.TryInteger(value, out _),
            XuiPropertyType.Number =>
                XuiValueParser.TryNumber(value, out _),
            XuiPropertyType.Vector2 =>
                XuiValueParser.TryVector2(value, out _),
            XuiPropertyType.Vector3 =>
                XuiValueParser.TryVector3(value, out _) ||
                XuiValueParser.TryVector2(value, out _),
            XuiPropertyType.Vector4 =>
                XuiValueParser.TryVector4(value, out _),
            XuiPropertyType.Quaternion =>
                XuiValueParser.TryQuaternion(value, out _) ||
                XuiValueParser.TryVector3(value, out _) ||
                XuiValueParser.TryVector2(value, out _) ||
                XuiValueParser.TryNumber(value, out _),
            XuiPropertyType.Color =>
                XuiValueParser.TryColor(value, out _),
            _ => true,
        };
        return valid
            ? null
            : UiLocalization.Format(
                "Ui.Validation.PropertyType",
                name,
                UiLocalization.PropertyType(definition.Type));
    }

    private static string DefaultTimelineValue(XuiTrack track) =>
        track.KnownProperty switch
        {
            XuiTimelineProperty.Show => "true",
            XuiTimelineProperty.Play => "false",
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
            XuiTimelineProperty.Material or
            XuiTimelineProperty.Text => string.Empty,
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

    private readonly record struct HierarchyDropIntent(
        string TargetKey,
        HierarchyDropPlacement Placement);

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

    private sealed record TimelineVectorEdit(
        string PropertyNodeKey,
        string Value);

    private sealed record PropertyPasteAssignment(
        string NodeKey,
        string PropertyName,
        string Value);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UiLocalization.LanguageChanged -= UiLocalization_LanguageChanged;
        _playbackTimer.Stop();
        _recoveryTimer.Stop();
        _hierarchySearchTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
