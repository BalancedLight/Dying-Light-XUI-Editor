using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Layout;
using XuiEditor.Core.Values;
using Matrix3x2 = System.Numerics.Matrix3x2;

namespace XuiEditor.Wpf.Controls;

public sealed class XuiSelectionRequestedEventArgs : EventArgs
{
    public XuiSelectionRequestedEventArgs(
        string? nodeKey,
        bool additive,
        bool toggle)
    {
        NodeKey = nodeKey;
        Additive = additive;
        Toggle = toggle;
    }

    public string? NodeKey { get; }

    public bool Additive { get; }

    public bool Toggle { get; }
}

public sealed class XuiTransformCommittedEventArgs : EventArgs
{
    public XuiTransformCommittedEventArgs(
        string nodeKey,
        XuiTransformKind kind,
        XuiVector2 positionDelta,
        XuiVector2 sizeDelta,
        double rotationDelta,
        XuiVector2 originalSize,
        IReadOnlyDictionary<string, XuiVector2>? positionDeltas = null)
    {
        NodeKey = nodeKey;
        Kind = kind;
        PositionDelta = positionDelta;
        SizeDelta = sizeDelta;
        RotationDelta = rotationDelta;
        OriginalSize = originalSize;
        PositionDeltas = positionDeltas ??
            new Dictionary<string, XuiVector2>(StringComparer.Ordinal);
    }

    public string NodeKey { get; }

    public XuiTransformKind Kind { get; }

    public XuiVector2 PositionDelta { get; }

    public XuiVector2 SizeDelta { get; }

    public double RotationDelta { get; }

    public XuiVector2 OriginalSize { get; }

    public IReadOnlyDictionary<string, XuiVector2> PositionDeltas { get; }
}

public enum XuiTransformKind
{
    Move,
    Resize,
    Rotate,
}

public sealed class XuiTextureDiagnosticsEventArgs : EventArgs
{
    public XuiTextureDiagnosticsEventArgs(
        string imagePath,
        IReadOnlyList<XuiDiagnostic> diagnostics)
    {
        ImagePath = imagePath;
        Diagnostics = diagnostics;
    }

    public string ImagePath { get; }

    public IReadOnlyList<XuiDiagnostic> Diagnostics { get; }
}

public sealed class XuiViewportControl : FrameworkElement
{
    private const double RulerSize = 22;
    private const int MaximumConcurrentTextureLoads = 4;
    private const int SelectedTexturePriority = 0;
    private const int VisibleTexturePriority = 10;
    private const int MaximumSnapshotDimension = 16_384;
    private const long MaximumSnapshotPixels = 64_000_000;
    [Flags]
    private enum ResizeHandle
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        Rotate = 16,
        TopLeft = Left | Top,
        TopRight = Right | Top,
        BottomRight = Right | Bottom,
        BottomLeft = Left | Bottom,
    }

    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _background = new();
    private readonly ContainerVisual _cameraLayer = new();
    private readonly DrawingVisual _canvas = new();
    private readonly ContainerVisual _nodeLayer = new();
    private readonly DrawingVisual _rulers = new();
    private readonly DrawingVisual _overlay = new();
    private readonly Dictionary<string, NodeVisual> _nodeVisuals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _imageUsers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _fontUsers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, LoadedTexture> _textureBitmaps =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, BitmapSource> _tintedBitmaps =
        new(StringComparer.Ordinal);
    private readonly object _textureQueueGate = new();
    private readonly Dictionary<TextureLoadKey, int> _pendingTextureLoads = [];
    private readonly HashSet<TextureLoadKey> _activeTextureLoads = [];
    private int _activeTextureLoadCount;
    private readonly Dictionary<string, LoadedBitmapFont> _bitmapFonts =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _requestedBitmapFonts =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _deferredResourceRedrawKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _lockedKeys =
        new(StringComparer.Ordinal);
    private XuiRenderFrame? _frame;
    private IAssetResolver? _assetResolver;
    private long _assetResolverGeneration;
    private BitmapSource? _referenceImage;
    private double _referenceImageOpacity = 0.5;
    private double _zoom = 1;
    private Vector _pan;
    private bool _panning;
    private Point _pointerStart;
    private PointerGesture? _pendingPointerGesture;
    private string? _dragNodeKey;
    private XuiRenderNode? _dragNode;
    private XuiTransformKind _dragKind;
    private ResizeHandle _dragHandle;
    private XuiVector2 _dragWorldDelta;
    private XuiVector2 _dragPositionDelta;
    private XuiVector2 _dragSizeDelta;
    private double _dragRotationDelta;
    private XuiRect? _dragPreviewBounds;
    private readonly Dictionary<string, Matrix3x2> _previewLocalTransforms =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, XuiVector2> _dragPositionDeltas =
        new(StringComparer.Ordinal);
    private readonly BitmapCache _navigationBitmapCache = new()
    {
        EnableClearType = false,
        RenderAtScale = 1,
    };
    private readonly DispatcherTimer _navigationCacheTimer;
    private bool _showGrid = true;
    private bool _showSafeArea = true;
    private bool _showUnknownBounds = true;
    private bool _finishingTransform;
    private long _nodeContentRedrawCount;
    private long _nodePresentationUpdateCount;
    private long _cameraUpdateCount;

    public XuiViewportControl()
    {
        Focusable = true;
        ClipToBounds = true;
        _cameraLayer.Children.Add(_canvas);
        _cameraLayer.Children.Add(_nodeLayer);
        _navigationCacheTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(180),
            DispatcherPriority.Background,
            (_, _) => EndNavigationCache(),
            Dispatcher)
        {
            IsEnabled = false,
        };
        _visuals = new VisualCollection(this)
        {
            _background,
            _cameraLayer,
            _rulers,
            _overlay,
        };
        SizeChanged += (_, _) => ResizePresentation();
    }

    public event EventHandler<XuiSelectionRequestedEventArgs>? SelectionRequested;

    public event EventHandler<XuiTransformCommittedEventArgs>? TransformCommitted;

    public event EventHandler<XuiTextureDiagnosticsEventArgs>?
        TextureDiagnosticsAvailable;

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (_showGrid == value)
            {
                return;
            }

            _showGrid = value;
            DrawCanvasLayer();
        }
    }

    public bool ShowSafeArea
    {
        get => _showSafeArea;
        set
        {
            if (_showSafeArea == value)
            {
                return;
            }

            _showSafeArea = value;
            DrawCanvasLayer();
        }
    }

    public bool ShowUnknownBounds
    {
        get => _showUnknownBounds;
        set
        {
            if (_showUnknownBounds == value)
            {
                return;
            }

            _showUnknownBounds = value;
            RedrawAllNodeContent();
        }
    }

    public bool SnapEnabled { get; set; } = true;

    public double GridSize { get; set; } = 8;

    public double ReferenceImageOpacity
    {
        get => _referenceImageOpacity;
        set
        {
            _referenceImageOpacity = Math.Clamp(value, 0, 1);
            DrawCanvasLayer();
        }
    }

    public double Zoom => _zoom;

    public bool HasRenderedFrame => _frame is not null;

    internal bool IsSelectedForTesting(string nodeKey) =>
        _selectedKeys.Contains(nodeKey);

    internal int RetainedNodeVisualCountForTesting => _nodeVisuals.Count;

    internal XuiRenderFrame? FrameForTesting => _frame;

    internal long NodeContentRedrawCountForTesting =>
        _nodeContentRedrawCount;

    internal long NodePresentationUpdateCountForTesting =>
        _nodePresentationUpdateCount;

    internal long CameraUpdateCountForTesting => _cameraUpdateCount;

    internal bool NavigationCacheActiveForTesting =>
        ReferenceEquals(_nodeLayer.CacheMode, _navigationBitmapCache);

    internal bool TextureLoadedForTesting(string imagePath) =>
        _textureBitmaps.ContainsKey(imagePath);

    internal bool RetainedNodeHasImageDrawingForTesting(string nodeKey) =>
        _nodeVisuals.TryGetValue(nodeKey, out NodeVisual? visual) &&
        ContainsImageDrawing(visual.Content.Drawing);

    internal IReadOnlyList<XuiColor> RetainedNodeBrushColorsForTesting(
        string nodeKey)
    {
        if (!_nodeVisuals.TryGetValue(
                nodeKey,
                out NodeVisual? visual))
        {
            return [];
        }

        List<XuiColor> colors = [];
        CollectDrawingColors(visual.Content.Drawing, colors);
        return colors;
    }

    internal static IReadOnlyList<XuiColor> BitmapGlyphColorsForTesting(
        XuiRenderNode node)
    {
        XuiTextPresentation presentation =
            XuiTextColorRunFormatter.Prepare(
                node.Text,
                node.TextColorRuns,
                node.Uppercase,
                CultureInfo.CurrentUICulture);
        List<XuiColor> colors = [];
        int textIndex = 0;
        int runIndex = 0;
        foreach (Rune rune in presentation.Text.EnumerateRunes())
        {
            int runeStart = textIndex;
            textIndex += rune.Utf16SequenceLength;
            if (rune.Value is '\r' or '\n')
            {
                continue;
            }

            colors.Add(
                ColorForTextIndex(
                    presentation.ColorRuns,
                    runeStart,
                    ref runIndex) ??
                node.Color);
        }

        return colors;
    }

    internal static bool BitmapFontSupportsTextForTesting(
        XuiBitmapFontMetrics metrics,
        string text) =>
        BitmapFontSupportsText(metrics, text);

    internal bool RetainedContainerHasClipForTesting(string nodeKey) =>
        _nodeVisuals[nodeKey].Container.Clip is not null;

    internal Matrix3x2 RetainedLocalTransformForTesting(string nodeKey) =>
        _nodeVisuals[nodeKey].Container.Transform is MatrixTransform transform
            ? new Matrix3x2(
                (float)transform.Matrix.M11,
                (float)transform.Matrix.M12,
                (float)transform.Matrix.M21,
                (float)transform.Matrix.M22,
                (float)transform.Matrix.OffsetX,
                (float)transform.Matrix.OffsetY)
            : Matrix3x2.Identity;

    internal void PreviewTransformForTesting(
        string nodeKey,
        XuiTransformKind kind,
        XuiVector2 worldDelta,
        double rotationDelta = 0)
    {
        XuiRenderNode node = _frame?.Nodes.Single(candidate =>
            candidate.Key == nodeKey) ??
            throw new InvalidOperationException("The test node is not rendered.");
        _dragNodeKey = nodeKey;
        _dragNode = node;
        _dragKind = kind;
        _dragWorldDelta = worldDelta;
        _dragPositionDelta = WorldToParentDelta(node, worldDelta);
        _dragRotationDelta = rotationDelta;
        ApplyTransformPreview();
        RedrawOverlay();
    }

    internal void CancelTransformForTesting() =>
        CancelTransform(releaseCapture: false);

    internal string? HitSelectionKeyForTesting(
        XuiVector2 logicalPoint,
        bool selectedBodyFirst = false,
        bool cycle = false)
    {
        XuiRenderNode? selected = selectedBodyFirst
            ? HitTestSelectedBody(logicalPoint)
            : null;
        return selected?.SelectionKey ??
               HitTest(logicalPoint, cycle)?.SelectionKey;
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    public void SetAssetResolver(IAssetResolver? assetResolver)
    {
        _assetResolver = assetResolver;
        _assetResolverGeneration++;
        _textureBitmaps.Clear();
        _tintedBitmaps.Clear();
        lock (_textureQueueGate)
        {
            _pendingTextureLoads.Clear();
        }

        _bitmapFonts.Clear();
        _requestedBitmapFonts.Clear();
        RequestSelectedTextures();
        RedrawAllNodeContent();
    }

    public void LoadReferenceImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        _referenceImage = bitmap;
        DrawCanvasLayer();
    }

    public void ClearReferenceImage()
    {
        _referenceImage = null;
        DrawCanvasLayer();
    }

    public BitmapSource RenderTransparentSnapshot(double scale = 2)
    {
        XuiRenderFrame frame = _frame ??
            throw new InvalidOperationException(
                "Open and render an XUI document before exporting a snapshot.");
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale),
                "Snapshot scale must be a positive finite number.");
        }

        double scaledWidth = frame.DesignSize.X * scale;
        double scaledHeight = frame.DesignSize.Y * scale;
        if (!double.IsFinite(scaledWidth) ||
            !double.IsFinite(scaledHeight) ||
            scaledWidth <= 0 ||
            scaledHeight <= 0 ||
            scaledWidth > MaximumSnapshotDimension ||
            scaledHeight > MaximumSnapshotDimension ||
            scaledWidth * scaledHeight > MaximumSnapshotPixels)
        {
            throw new InvalidOperationException(
                "The authored canvas is too large to export safely at the requested scale.");
        }

        int pixelWidth = checked((int)Math.Ceiling(scaledWidth));
        int pixelHeight = checked((int)Math.Ceiling(scaledHeight));
        EndNavigationCache();
        bool restoreUnknownBounds = _showUnknownBounds;
        if (restoreUnknownBounds)
        {
            _showUnknownBounds = false;
            RedrawAllNodeContent();
        }

        try
        {
            Rect designBounds = new(
                0,
                0,
                frame.DesignSize.X,
                frame.DesignSize.Y);
            VisualBrush contentBrush = new(_nodeLayer)
            {
                Viewbox = designBounds,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = designBounds,
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
                TileMode = TileMode.None,
            };
            RenderOptions.SetBitmapScalingMode(
                contentBrush,
                BitmapScalingMode.HighQuality);
            DrawingVisual exportVisual = new();
            RenderOptions.SetBitmapScalingMode(
                exportVisual,
                BitmapScalingMode.HighQuality);
            TextOptions.SetTextFormattingMode(
                exportVisual,
                TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(
                exportVisual,
                TextRenderingMode.Grayscale);
            using (DrawingContext drawing = exportVisual.RenderOpen())
            {
                drawing.DrawRectangle(
                    contentBrush,
                    null,
                    designBounds);
            }

            RenderTargetBitmap bitmap = new(
                pixelWidth,
                pixelHeight,
                96 * scale,
                96 * scale,
                PixelFormats.Pbgra32);
            bitmap.Render(exportVisual);
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            if (restoreUnknownBounds)
            {
                _showUnknownBounds = true;
                RedrawAllNodeContent();
            }
        }
    }

    public void SetFrame(XuiRenderFrame? frame)
    {
        EndNavigationCache();
        XuiRenderFrame? previous = _frame;
        _frame = frame;
        SynchronizeNodeVisuals();
        if (previous is null ||
            frame is null ||
            previous.DesignSize != frame.DesignSize ||
            previous.Viewport != frame.Viewport)
        {
            ResizePresentation();
        }
        else
        {
            RedrawOverlay();
        }
    }

    public void SetSample(XuiRenderSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.FullEvaluationRequired ||
            sample.ChangedRenderNodeKeys.Count > 0)
        {
            EndNavigationCache();
        }

        XuiRenderFrame? previous = _frame;
        _frame = sample.Frame;
        if (sample.FullEvaluationRequired)
        {
            SynchronizeNodeVisuals();
        }
        else
        {
            SynchronizeChangedNodeVisuals(
                sample.ChangedRenderNodeKeys);
        }

        if (previous is null ||
            previous.DesignSize != sample.Frame.DesignSize ||
            previous.Viewport != sample.Frame.Viewport)
        {
            ResizePresentation();
        }
        else
        {
            RedrawOverlay();
        }
    }

    public void SetSelectedKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        HashSet<string> replacement = new(keys, StringComparer.Ordinal);
        if (_selectedKeys.SetEquals(replacement))
        {
            return;
        }

        _selectedKeys.Clear();
        _selectedKeys.UnionWith(replacement);
        RequestSelectedTextures();
        RedrawOverlay();
    }

    public void SetHiddenKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        HashSet<string> replacement = new(keys, StringComparer.Ordinal);
        if (_hiddenKeys.SetEquals(replacement))
        {
            return;
        }

        EndNavigationCache();
        _hiddenKeys.Clear();
        _hiddenKeys.UnionWith(replacement);
        UpdateNodeVisibility();
        RedrawOverlay();
    }

    public void SetLockedKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        HashSet<string> replacement = new(keys, StringComparer.Ordinal);
        if (_lockedKeys.SetEquals(replacement))
        {
            return;
        }

        _lockedKeys.Clear();
        _lockedKeys.UnionWith(replacement);
        if (_pendingPointerGesture is PointerGesture pending &&
            pending.DragCandidate is XuiRenderNode candidate &&
            IsLocked(candidate))
        {
            ClearPendingPointerGesture(releaseCapture: true);
        }

        RedrawOverlay();
    }

    public void Fit()
    {
        BeginNavigationCache();
        _zoom = 1;
        _pan = default;
        UpdateCamera();
        ScheduleNavigationCacheRelease();
    }

    public void ActualPixels()
    {
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            return;
        }

        BeginNavigationCache();
        double fitScale = CalculateFitScale(frame);
        _zoom = fitScale <= 0 ? 1 : 1 / fitScale;
        _pan = default;
        UpdateCamera();
        ScheduleNavigationCacheRelease();
    }

    public void ZoomBy(double factor)
    {
        BeginNavigationCache();
        _zoom = Math.Clamp(_zoom * factor, 0.05, 32);
        UpdateCamera();
        ScheduleNavigationCacheRelease();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _pointerStart = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Middle)
        {
            BeginNavigationCache();
            _panning = true;
            CaptureMouse();
            Cursor = Cursors.ScrollAll;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left || _frame is null)
        {
            return;
        }

        XuiVector2 logical = ControlToLogical(_pointerStart);
        TransformHandleHit? transformHit = HitTestTransformHandle(logical);
        if (transformHit is not null)
        {
            BeginTransform(
                transformHit.Node,
                transformHit.Handle == ResizeHandle.Rotate
                    ? XuiTransformKind.Rotate
                    : XuiTransformKind.Resize,
                transformHit.Handle);
            e.Handled = true;
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        bool cycle = modifiers.HasFlag(ModifierKeys.Alt);
        XuiRenderNode? ordinaryHit = HitTest(logical, cycle);
        XuiRenderNode? selectedHit =
            !cycle &&
            !modifiers.HasFlag(ModifierKeys.Shift) &&
            !modifiers.HasFlag(ModifierKeys.Control)
                ? HitTestSelectedBody(logical)
                : null;
        _pendingPointerGesture = new PointerGesture(
            selectedHit ?? ordinaryHit,
            ordinaryHit ?? selectedHit,
            modifiers);
        CaptureMouse();

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point current = e.GetPosition(this);
        if (_panning && e.MiddleButton == MouseButtonState.Pressed)
        {
            Vector delta = current - _pointerStart;
            _pan += delta;
            _pointerStart = current;
            UpdateCamera();
            e.Handled = true;
            return;
        }

        if (_pendingPointerGesture is PointerGesture pending &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            Vector controlDelta = current - _pointerStart;
            if (Math.Abs(controlDelta.X) >=
                    SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(controlDelta.Y) >=
                    SystemParameters.MinimumVerticalDragDistance)
            {
                bool selectionOnly =
                    pending.Modifiers.HasFlag(ModifierKeys.Shift) ||
                    pending.Modifiers.HasFlag(ModifierKeys.Control);
                XuiRenderNode? candidate = pending.DragCandidate;
                if (!selectionOnly &&
                    candidate is not null &&
                    CanTransform(candidate))
                {
                    if (!_selectedKeys.Contains(candidate.SelectionKey))
                    {
                        SelectionRequested?.Invoke(
                            this,
                            new XuiSelectionRequestedEventArgs(
                                candidate.SelectionKey,
                                additive: false,
                                toggle: false));
                    }

                    XuiRenderNode transformNode =
                        TransformOwner(candidate);
                    _pendingPointerGesture = null;
                    BeginTransform(
                        transformNode,
                        XuiTransformKind.Move,
                        ResizeHandle.None);
                }
            }

            if (_dragNodeKey is null)
            {
                e.Handled = true;
                return;
            }
        }

        if (_dragNodeKey is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            if (_dragNodeKey is null &&
                _pendingPointerGesture is null)
            {
                TransformHandleHit? hover = HitTestTransformHandle(
                    ControlToLogical(current));
                Cursor = CursorForHandle(hover?.Handle ?? ResizeHandle.None);
            }

            return;
        }

        XuiVector2 start = ControlToLogical(_pointerStart);
        XuiVector2 end = ControlToLogical(current);
        double x = end.X - start.X;
        double y = end.Y - start.Y;
        if (_dragKind != XuiTransformKind.Rotate &&
            SnapEnabled &&
            GridSize > 0)
        {
            x = Math.Round(x / GridSize) * GridSize;
            y = Math.Round(y / GridSize) * GridSize;
        }

        _dragWorldDelta = new XuiVector2(x, y);
        UpdateTransformPreview(end);
        ApplyTransformPreview();
        RedrawOverlay();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = false;
            Cursor = Cursors.Arrow;
            ReleaseMouseCapture();
            ScheduleNavigationCacheRelease();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left &&
            _pendingPointerGesture is PointerGesture pending)
        {
            _pendingPointerGesture = null;
            ReleasePointerCapture();
            SelectionRequested?.Invoke(
                this,
                new XuiSelectionRequestedEventArgs(
                    pending.ClickCandidate?.SelectionKey,
                    pending.Modifiers.HasFlag(ModifierKeys.Shift),
                    pending.Modifiers.HasFlag(ModifierKeys.Control)));
            Cursor = Cursors.Arrow;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left &&
            _dragNodeKey is not null)
        {
            string nodeKey = _dragNodeKey;
            XuiTransformKind kind = _dragKind;
            XuiVector2 originalSize = _dragNode?.Size ?? default;
            XuiVector2 positionDelta = _dragPositionDelta;
            XuiVector2 sizeDelta = _dragSizeDelta;
            double rotationDelta = _dragRotationDelta;
            Dictionary<string, XuiVector2> positionDeltas = new(
                _dragPositionDeltas,
                StringComparer.Ordinal);
            RestoreTransformPreview();
            _previewLocalTransforms.Clear();
            _dragPositionDeltas.Clear();
            _dragNodeKey = null;
            _dragNode = null;
            _dragHandle = ResizeHandle.None;
            _dragWorldDelta = default;
            _dragPositionDelta = default;
            _dragSizeDelta = default;
            _dragRotationDelta = 0;
            _dragPreviewBounds = null;
            _finishingTransform = true;
            ReleaseMouseCapture();
            _finishingTransform = false;
            Cursor = Cursors.Arrow;
            RedrawOverlay();
            bool changed = kind switch
            {
                XuiTransformKind.Move =>
                    HasDelta(positionDelta),
                XuiTransformKind.Resize =>
                    HasDelta(positionDelta) || HasDelta(sizeDelta),
                XuiTransformKind.Rotate =>
                    Math.Abs(rotationDelta) > 0.0001,
                _ => false,
            };
            if (changed)
            {
                TransformCommitted?.Invoke(
                    this,
                    new XuiTransformCommittedEventArgs(
                        nodeKey,
                        kind,
                        positionDelta,
                        sizeDelta,
                        rotationDelta,
                        originalSize,
                        positionDeltas));
            }

            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragNodeKey is null && !_panning)
        {
            Cursor = Cursors.Arrow;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        BeginNavigationCache();
        Point pointer = e.GetPosition(this);
        XuiVector2 before = ControlToLogical(pointer);
        _zoom = Math.Clamp(
            _zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12),
            0.05,
            32);
        Matrix camera = CreateCamera();
        Point after = camera.Transform(new Point(before.X, before.Y));
        _pan += pointer - after;
        UpdateCamera();
        ScheduleNavigationCacheRelease();
        e.Handled = true;
    }

    private void ResizePresentation()
    {
        DrawBackgroundLayer();
        DrawCanvasLayer();
        UpdateCamera();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            _pendingPointerGesture is not null)
        {
            ClearPendingPointerGesture(releaseCapture: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _dragNodeKey is not null)
        {
            CancelTransform();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_finishingTransform && _dragNodeKey is not null)
        {
            CancelTransform(releaseCapture: false);
        }

        _pendingPointerGesture = null;
        _panning = false;
        ScheduleNavigationCacheRelease();
    }

    private void DrawBackgroundLayer()
    {
        using DrawingContext drawing = _background.RenderOpen();
        drawing.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(18, 20, 23)),
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));
        XuiRenderFrame? frame = _frame;
        if (frame is null || ActualWidth <= RulerSize || ActualHeight <= RulerSize)
        {
            DrawEmptyState(drawing);
        }
    }

    private void DrawCanvasLayer()
    {
        using DrawingContext drawing = _canvas.RenderOpen();
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            return;
        }

        DrawCanvasBackground(drawing, frame);
        if (ShowGrid)
        {
            DrawGrid(drawing, frame);
        }
    }

    private void SynchronizeNodeVisuals()
    {
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            _nodeLayer.Children.Clear();
            _nodeVisuals.Clear();
            _imageUsers.Clear();
            _fontUsers.Clear();
            return;
        }

        bool topologyChanged =
            _nodeVisuals.Count != frame.Nodes.Count ||
            frame.Nodes.Any(node =>
                !_nodeVisuals.TryGetValue(
                    node.Key,
                    out NodeVisual? existing) ||
                !string.Equals(
                    existing.Node.ParentKey,
                    node.ParentKey,
                    StringComparison.Ordinal));
        if (topologyChanged)
        {
            RebuildNodeVisualTree(frame);
            return;
        }

        bool resourcesChanged = false;
        foreach (XuiRenderNode node in frame.Nodes)
        {
            NodeVisual visual = _nodeVisuals[node.Key];
            bool repaint = !PaintEquivalent(visual.Node, node);
            bool presentationChanged =
                !PresentationEquivalent(visual.Node, node);
            resourcesChanged |=
                !ResourceEquivalent(visual.Node, node);
            visual.Node = node;
            if (presentationChanged)
            {
                UpdateNodePresentation(visual);
            }

            if (repaint)
            {
                DrawNodeContent(visual);
            }
        }

        if (resourcesChanged)
        {
            RebuildResourceUsers(frame.Nodes);
        }
    }

    private void SynchronizeChangedNodeVisuals(
        IReadOnlyList<string> changedKeys)
    {
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            SynchronizeNodeVisuals();
            return;
        }

        bool resourcesChanged = false;
        foreach (string key in changedKeys)
        {
            if (!_nodeVisuals.TryGetValue(
                    key,
                    out NodeVisual? visual))
            {
                SynchronizeNodeVisuals();
                return;
            }

            int index = visual.Node.DeclarationOrder;
            if (index < 0 ||
                index >= frame.Nodes.Count ||
                !frame.Nodes[index].Key.Equals(
                    key,
                    StringComparison.Ordinal))
            {
                SynchronizeNodeVisuals();
                return;
            }

            XuiRenderNode node = frame.Nodes[index];
            bool repaint = !PaintEquivalent(visual.Node, node);
            bool presentationChanged =
                !PresentationEquivalent(visual.Node, node);
            resourcesChanged |=
                !ResourceEquivalent(visual.Node, node);
            visual.Node = node;
            if (presentationChanged)
            {
                UpdateNodePresentation(visual);
            }

            if (repaint)
            {
                DrawNodeContent(visual);
            }
        }

        if (resourcesChanged)
        {
            RebuildResourceUsers(frame.Nodes);
        }
    }

    private void RebuildNodeVisualTree(XuiRenderFrame frame)
    {
        _nodeLayer.Children.Clear();
        _nodeVisuals.Clear();
        foreach (XuiRenderNode node in frame.Nodes)
        {
            ContainerVisual container = new();
            DrawingVisual content = new();
            container.Children.Add(content);
            _nodeVisuals.Add(
                node.Key,
                new NodeVisual(container, content, node));
        }

        foreach (XuiRenderNode node in frame.Nodes)
        {
            NodeVisual visual = _nodeVisuals[node.Key];
            if (node.ParentKey is string parentKey &&
                _nodeVisuals.TryGetValue(
                    parentKey,
                    out NodeVisual? parent))
            {
                parent.Container.Children.Add(visual.Container);
            }
            else
            {
                _nodeLayer.Children.Add(visual.Container);
            }
        }

        RebuildResourceUsers(frame.Nodes);
        foreach (NodeVisual visual in _nodeVisuals.Values)
        {
            UpdateNodePresentation(visual);
            DrawNodeContent(visual);
        }
    }

    private void RebuildResourceUsers(IEnumerable<XuiRenderNode> nodes)
    {
        _imageUsers.Clear();
        _fontUsers.Clear();
        foreach (XuiRenderNode node in nodes)
        {
            if (node.PaintKind == XuiPaintKind.Texture &&
                node.ImagePath.Length > 0)
            {
                AddResourceUser(_imageUsers, node.ImagePath, node.Key);
            }

            if (node.Kind == XuiRenderKind.Text &&
                node.Font.Length > 0)
            {
                AddResourceUser(_fontUsers, node.Font, node.Key);
            }
        }
    }

    private static void AddResourceUser(
        Dictionary<string, HashSet<string>> users,
        string resource,
        string nodeKey)
    {
        if (!users.TryGetValue(resource, out HashSet<string>? keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            users.Add(resource, keys);
        }

        keys.Add(nodeKey);
    }

    private void UpdateNodePresentation(NodeVisual visual)
    {
        _nodePresentationUpdateCount++;
        XuiRenderNode node = visual.Node;
        bool wasVisible = visual.Content.Opacity > 0;
        visual.Container.Transform =
            new MatrixTransform(ToMatrix(node.LocalTransform));
        visual.Content.Opacity = EffectiveNodeOpacity(node);
        visual.Container.Clip = CreateLocalClip(node);
        if (visual.Content.Opacity <= 0)
        {
            return;
        }

        if (!wasVisible && !visual.HasContent)
        {
            DrawNodeContent(visual);
        }
        else if (node.PaintKind == XuiPaintKind.Texture)
        {
            RequestTexture(
                node.ImagePath,
                _selectedKeys.Contains(node.SelectionKey)
                    ? SelectedTexturePriority
                    : VisibleTexturePriority);
        }
    }

    private double EffectiveNodeOpacity(XuiRenderNode node) =>
        _hiddenKeys.Contains(node.SelectionKey) ||
        !node.IsShown ||
        node.Opacity <= 0
            ? 0
            : node.Opacity;

    private static StreamGeometry? CreateLocalClip(XuiRenderNode node)
    {
        if (node.ClipBounds is not XuiRect clip ||
            !Matrix3x2.Invert(
                node.WorldTransform,
                out Matrix3x2 inverse))
        {
            return null;
        }

        XuiVector2 topLeft = TransformPoint(clip.X, clip.Y, inverse);
        XuiVector2 topRight = TransformPoint(clip.Right, clip.Y, inverse);
        XuiVector2 bottomRight =
            TransformPoint(clip.Right, clip.Bottom, inverse);
        XuiVector2 bottomLeft =
            TransformPoint(clip.X, clip.Bottom, inverse);
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(
                new Point(topLeft.X, topLeft.Y),
                isFilled: true,
                isClosed: true);
            context.PolyLineTo(
            [
                new Point(topRight.X, topRight.Y),
                new Point(bottomRight.X, bottomRight.Y),
                new Point(bottomLeft.X, bottomLeft.Y),
            ],
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static bool PaintEquivalent(
        XuiRenderNode left,
        XuiRenderNode right) =>
        left.Kind == right.Kind &&
        left.PaintKind == right.PaintKind &&
        left.Size == right.Size &&
        left.Text == right.Text &&
        left.ImagePath == right.ImagePath &&
        left.MaterialProfile == right.MaterialProfile &&
        left.Font == right.Font &&
        left.Color == right.Color &&
        left.VisualResolved == right.VisualResolved &&
        left.PointSize.Equals(right.PointSize) &&
        left.Uppercase == right.Uppercase &&
        left.MultiLine == right.MultiLine &&
        left.Bold == right.Bold &&
        left.Italic == right.Italic &&
        left.Underline == right.Underline &&
        left.HorizontalTextAlignment == right.HorizontalTextAlignment &&
        left.VerticalTextAlignment == right.VerticalTextAlignment &&
        left.TextBorder == right.TextBorder &&
        left.CharacterSpacingAdjust.Equals(right.CharacterSpacingAdjust) &&
        left.ColorControlSequenceEnabled ==
        right.ColorControlSequenceEnabled &&
        left.TextColorRuns.SequenceEqual(right.TextColorRuns) &&
        left.Outline == right.Outline &&
        left.OutlineSize.Equals(right.OutlineSize) &&
        left.OutlineColor == right.OutlineColor &&
        left.Shadow == right.Shadow &&
        left.ShadowOffset.Equals(right.ShadowOffset) &&
        left.ShadowColor == right.ShadowColor;

    private static bool PresentationEquivalent(
        XuiRenderNode left,
        XuiRenderNode right) =>
        left.LocalTransform == right.LocalTransform &&
        left.IsShown == right.IsShown &&
        left.Opacity.Equals(right.Opacity) &&
        left.SelectionKey == right.SelectionKey &&
        left.ClipBounds == right.ClipBounds &&
        (left.ClipBounds is null ||
         left.WorldTransform == right.WorldTransform);

    private static bool ResourceEquivalent(
        XuiRenderNode left,
        XuiRenderNode right) =>
        left.PaintKind == right.PaintKind &&
        left.ImagePath == right.ImagePath &&
        left.Kind == right.Kind &&
        left.Font == right.Font;

    private void DrawNodeContent(NodeVisual visual)
    {
        _nodeContentRedrawCount++;
        using DrawingContext drawing = visual.Content.RenderOpen();
        bool visible = EffectiveNodeOpacity(visual.Node) > 0;
        if (visible)
        {
            DrawNode(drawing, visual.Node);
        }

        visual.HasContent = visible;
    }

    private void RedrawAllNodeContent()
    {
        foreach (NodeVisual visual in _nodeVisuals.Values)
        {
            DrawNodeContent(visual);
        }
    }

    private void RedrawResourceUsers(
        Dictionary<string, HashSet<string>> users,
        string resource)
    {
        if (!users.TryGetValue(resource, out HashSet<string>? keys))
        {
            return;
        }

        if (NavigationCacheActiveForTesting)
        {
            foreach (string key in keys)
            {
                if (_nodeVisuals.TryGetValue(key, out NodeVisual? visual) &&
                    EffectiveNodeOpacity(visual.Node) > 0)
                {
                    _deferredResourceRedrawKeys.Add(key);
                }
                else if (visual is not null)
                {
                    visual.HasContent = false;
                }
            }

            return;
        }

        foreach (string key in keys)
        {
            if (_nodeVisuals.TryGetValue(key, out NodeVisual? visual) &&
                EffectiveNodeOpacity(visual.Node) > 0)
            {
                DrawNodeContent(visual);
            }
        }
    }

    private void UpdateNodeVisibility()
    {
        foreach (NodeVisual visual in _nodeVisuals.Values)
        {
            bool wasVisible = visual.Content.Opacity > 0;
            visual.Content.Opacity = EffectiveNodeOpacity(visual.Node);
            if (!wasVisible &&
                visual.Content.Opacity > 0 &&
                !visual.HasContent)
            {
                DrawNodeContent(visual);
            }
            else if (visual.Content.Opacity > 0 &&
                visual.Node.PaintKind == XuiPaintKind.Texture)
            {
                RequestTexture(
                    visual.Node.ImagePath,
                    _selectedKeys.Contains(visual.Node.SelectionKey)
                        ? SelectedTexturePriority
                        : VisibleTexturePriority);
            }
        }
    }

    private void UpdateCamera()
    {
        _cameraUpdateCount++;
        Matrix camera = CreateCamera();
        _cameraLayer.Transform = new MatrixTransform(camera);
        DrawRulerLayer(camera);
        RedrawOverlay();
    }

    private void DrawRulerLayer(Matrix camera)
    {
        using DrawingContext drawing = _rulers.RenderOpen();
        if (_frame is XuiRenderFrame frame)
        {
            DrawRulers(drawing, frame, camera);
        }
    }

    private void DrawCanvasBackground(
        DrawingContext drawing,
        XuiRenderFrame frame)
    {
        Rect canvas = new(
            0,
            0,
            frame.DesignSize.X,
            frame.DesignSize.Y);
        drawing.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(25, 27, 30)),
            new Pen(new SolidColorBrush(Color.FromRgb(74, 78, 84)), 1),
            canvas);
        if (_referenceImage is not null &&
            _referenceImageOpacity > 0)
        {
            ImageBrush referenceBrush = new(_referenceImage)
            {
                Stretch = Stretch.Uniform,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };
            referenceBrush.Freeze();
            drawing.PushOpacity(_referenceImageOpacity);
            drawing.DrawRectangle(referenceBrush, null, canvas);
            drawing.Pop();
        }

        if (ShowSafeArea)
        {
            double insetX = frame.DesignSize.X * 0.05;
            double insetY = frame.DesignSize.Y * 0.05;
            Pen safePen = new(
                new SolidColorBrush(Color.FromArgb(160, 242, 140, 40)),
                1);
            safePen.DashStyle = DashStyles.Dash;
            drawing.DrawRectangle(
                null,
                safePen,
                new Rect(
                    insetX,
                    insetY,
                    frame.DesignSize.X - (insetX * 2),
                    frame.DesignSize.Y - (insetY * 2)));
        }
    }

    private void DrawGrid(
        DrawingContext drawing,
        XuiRenderFrame frame)
    {
        double spacing = GridSize > 0 ? GridSize : 8;
        int verticalCount = (int)Math.Ceiling(frame.DesignSize.X / spacing);
        int horizontalCount = (int)Math.Ceiling(frame.DesignSize.Y / spacing);
        if (verticalCount + horizontalCount > 2_000)
        {
            spacing *= Math.Ceiling((verticalCount + horizontalCount) / 2_000.0);
        }

        Pen minor = new(
            new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            0.5);
        for (double x = spacing; x < frame.DesignSize.X; x += spacing)
        {
            drawing.DrawLine(minor, new Point(x, 0), new Point(x, frame.DesignSize.Y));
        }

        for (double y = spacing; y < frame.DesignSize.Y; y += spacing)
        {
            drawing.DrawLine(minor, new Point(0, y), new Point(frame.DesignSize.X, y));
        }
    }

    private void DrawNode(DrawingContext drawing, XuiRenderNode node)
    {
        if (node.Size.X <= 0 ||
            node.Size.Y <= 0)
        {
            return;
        }

        Rect bounds = new(0, 0, node.Size.X, node.Size.Y);
        Brush colorBrush = ToBrush(node.Color);

        switch (node.Kind)
        {
            case XuiRenderKind.Image:
            case XuiRenderKind.Rectangle:
            case XuiRenderKind.Shape:
                if (node.PaintKind == XuiPaintKind.SolidColor)
                {
                    drawing.DrawRectangle(colorBrush, null, bounds);
                }
                else if (node.PaintKind == XuiPaintKind.Texture &&
                         _textureBitmaps.TryGetValue(
                             node.ImagePath,
                             out LoadedTexture? texture))
                {
                    DrawTexture(
                        drawing,
                        texture,
                        bounds,
                        node.Color);
                }
                else if (node.PaintKind == XuiPaintKind.Texture)
                {
                    if (EffectiveNodeOpacity(node) > 0)
                    {
                        RequestTexture(
                            node.ImagePath,
                            _selectedKeys.Contains(node.SelectionKey)
                                ? SelectedTexturePriority
                                : VisibleTexturePriority);
                    }
                }

                if (node.MaterialProfile.RequiresRuntimeData &&
                    node.MaterialProfile.SuppressSelfPaint &&
                    ShowUnknownBounds)
                {
                    Pen runtimeShapePen = new(
                        new SolidColorBrush(Color.FromArgb(135, 91, 177, 255)),
                        0.75);
                    runtimeShapePen.DashStyle = DashStyles.Dash;
                    drawing.DrawRectangle(null, runtimeShapePen, bounds);
                }

                break;

            case XuiRenderKind.Text:
                DrawText(drawing, node, bounds);
                break;

            case XuiRenderKind.Control:
            case XuiRenderKind.Presenter:
                if (!node.VisualResolved)
                {
                    drawing.DrawRectangle(
                        new SolidColorBrush(Color.FromArgb(18, 242, 140, 40)),
                        new Pen(
                            new SolidColorBrush(Color.FromArgb(75, 242, 140, 40)),
                            0.75),
                        bounds);
                }

                break;

            case XuiRenderKind.Unknown when ShowUnknownBounds:
                Pen unknownPen = new(
                    new SolidColorBrush(Color.FromArgb(110, 160, 170, 180)),
                    0.75);
                unknownPen.DashStyle = DashStyles.Dot;
                drawing.DrawRectangle(null, unknownPen, bounds);
                break;
        }
    }

    private void DrawTexture(
        DrawingContext drawing,
        LoadedTexture texture,
        Rect destination,
        XuiColor tint)
    {
        XuiTextureRegion definition = texture.Resolved.Definition;
        if (definition.Primitive == XuiTexturePrimitive.TileSet &&
            texture.TileParts.Count > 0)
        {
            DrawTileSet(drawing, texture, destination, tint);
            return;
        }

        BitmapSource bitmap = TintedBitmap(
            texture.Bitmap,
            texture.Resolved.BgraPixels,
            texture.Resolved.ContentHash,
            tint);
        if (definition.Primitive != XuiTexturePrimitive.RectangleWithCorner ||
            definition.CornerSize.X <= 0 ||
            definition.CornerSize.Y <= 0)
        {
            drawing.DrawImage(bitmap, destination);
            return;
        }

        double sourceCornerX = Math.Min(
            definition.CornerSize.X *
                texture.Resolved.DefinitionToPhysicalScale.X,
            bitmap.PixelWidth * 0.5);
        double sourceCornerY = Math.Min(
            definition.CornerSize.Y *
                texture.Resolved.DefinitionToPhysicalScale.Y,
            bitmap.PixelHeight * 0.5);
        double destinationCornerX = Math.Min(
            definition.CornerSize.X,
            destination.Width * 0.5);
        double destinationCornerY = Math.Min(
            definition.CornerSize.Y,
            destination.Height * 0.5);
        double[] sourceX =
        [
            0,
            sourceCornerX,
            bitmap.PixelWidth - sourceCornerX,
            bitmap.PixelWidth,
        ];
        double[] sourceY =
        [
            0,
            sourceCornerY,
            bitmap.PixelHeight - sourceCornerY,
            bitmap.PixelHeight,
        ];
        double[] destinationX =
        [
            destination.Left,
            destination.Left + destinationCornerX,
            destination.Right - destinationCornerX,
            destination.Right,
        ];
        double[] destinationY =
        [
            destination.Top,
            destination.Top + destinationCornerY,
            destination.Bottom - destinationCornerY,
            destination.Bottom,
        ];

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Rect source = new(
                    sourceX[x],
                    sourceY[y],
                    sourceX[x + 1] - sourceX[x],
                    sourceY[y + 1] - sourceY[y]);
                Rect target = new(
                    destinationX[x],
                    destinationY[y],
                    destinationX[x + 1] - destinationX[x],
                    destinationY[y + 1] - destinationY[y]);
                if (source.Width <= 0 ||
                    source.Height <= 0 ||
                    target.Width <= 0 ||
                    target.Height <= 0)
                {
                    continue;
                }

                ImageBrush brush = new(bitmap)
                {
                    Stretch = Stretch.Fill,
                    Viewbox = source,
                    ViewboxUnits = BrushMappingMode.Absolute,
                };
                brush.Freeze();
                drawing.DrawRectangle(brush, null, target);
            }
        }
    }

    private void DrawTileSet(
        DrawingContext drawing,
        LoadedTexture texture,
        Rect destination,
        XuiColor tint)
    {
        double left = TileColumnWidth(texture, 0);
        double right = TileColumnWidth(texture, 2);
        double top = TileRowHeight(texture, 0);
        double bottom = TileRowHeight(texture, 2);
        double horizontalScale = left + right > destination.Width
            ? destination.Width / Math.Max(1, left + right)
            : 1;
        double verticalScale = top + bottom > destination.Height
            ? destination.Height / Math.Max(1, top + bottom)
            : 1;
        left *= horizontalScale;
        right *= horizontalScale;
        top *= verticalScale;
        bottom *= verticalScale;
        double[] x =
        [
            destination.Left,
            destination.Left + left,
            destination.Right - right,
            destination.Right,
        ];
        double[] y =
        [
            destination.Top,
            destination.Top + top,
            destination.Bottom - bottom,
            destination.Bottom,
        ];

        foreach (LoadedTilePart part in texture.TileParts.Values
                     .OrderBy(static part => TileDrawOrder(part.Resolved.Role)))
        {
            (int column, int row) = TileCell(part.Resolved.Role);
            Rect target = new(
                x[column],
                y[row],
                Math.Max(0, x[column + 1] - x[column]),
                Math.Max(0, y[row + 1] - y[row]));
            if (target.Width <= 0 || target.Height <= 0)
            {
                continue;
            }

            bool corner = part.Resolved.Role is
                XuiTileRole.CornerTopLeft or
                XuiTileRole.CornerTopRight or
                XuiTileRole.CornerBottomLeft or
                XuiTileRole.CornerBottomRight;
            BitmapSource bitmap = TintedBitmap(
                part.Bitmap,
                part.Resolved.BgraPixels,
                part.Resolved.ContentHash,
                tint);
            if (corner)
            {
                drawing.DrawImage(bitmap, target);
                continue;
            }

            ImageBrush brush = new(bitmap)
            {
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Stretch = Stretch.Fill,
                TileMode = TileMode.Tile,
                Viewbox = new Rect(
                    0,
                    0,
                    bitmap.PixelWidth,
                    bitmap.PixelHeight),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(
                    target.Left,
                    target.Top,
                    part.Resolved.LogicalSize.X,
                    part.Resolved.LogicalSize.Y),
                ViewportUnits = BrushMappingMode.Absolute,
            };
            brush.Freeze();
            drawing.DrawRectangle(brush, null, target);
        }
    }

    private static double TileColumnWidth(LoadedTexture texture, int column) =>
        texture.TileParts.Values
            .Where(part => TileCell(part.Resolved.Role).Column == column)
            .Select(static part => part.Resolved.LogicalSize.X)
            .DefaultIfEmpty(0)
            .Max();

    private static double TileRowHeight(LoadedTexture texture, int row) =>
        texture.TileParts.Values
            .Where(part => TileCell(part.Resolved.Role).Row == row)
            .Select(static part => part.Resolved.LogicalSize.Y)
            .DefaultIfEmpty(0)
            .Max();

    private static (int Column, int Row) TileCell(XuiTileRole role) =>
        role switch
        {
            XuiTileRole.CornerTopLeft => (0, 0),
            XuiTileRole.Top => (1, 0),
            XuiTileRole.CornerTopRight => (2, 0),
            XuiTileRole.Left => (0, 1),
            XuiTileRole.Middle => (1, 1),
            XuiTileRole.Right => (2, 1),
            XuiTileRole.CornerBottomLeft => (0, 2),
            XuiTileRole.Bottom => (1, 2),
            XuiTileRole.CornerBottomRight => (2, 2),
            _ => (1, 1),
        };

    private static int TileDrawOrder(XuiTileRole role) =>
        role switch
        {
            XuiTileRole.Middle => 0,
            XuiTileRole.Top or
            XuiTileRole.Bottom or
            XuiTileRole.Left or
            XuiTileRole.Right => 1,
            _ => 2,
        };

    private void DrawText(
        DrawingContext drawing,
        XuiRenderNode node,
        Rect bounds)
    {
        if (string.IsNullOrEmpty(node.Text))
        {
            return;
        }

        XuiTextPresentation presentation =
            XuiTextColorRunFormatter.Prepare(
                node.Text,
                node.TextColorRuns,
                node.Uppercase,
                CultureInfo.CurrentUICulture);
        if (!string.IsNullOrWhiteSpace(node.Font))
        {
            if (_bitmapFonts.TryGetValue(
                    node.Font,
                    out LoadedBitmapFont? bitmapFont) &&
                BitmapFontSupportsText(
                    bitmapFont.Resolved.Metrics,
                    presentation.Text))
            {
                DrawBitmapText(
                    drawing,
                    node,
                    bounds,
                    bitmapFont,
                    presentation);
                return;
            }

            RequestBitmapFont(node.Font);
        }

        ResolvedFont? font = _assetResolver?.ResolveFont(
            node.Font,
            node.PointSize);
        FontFamily family = CreateFontFamily(font);
        Typeface typeface = new(
            family,
            node.Italic ? FontStyles.Italic : FontStyles.Normal,
            node.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        double size = Math.Clamp(
            node.PointSize > 0
                ? node.PointSize
                : font?.Size ?? Math.Min(20, bounds.Height * 0.75),
            1,
            256);
        string content = presentation.Text;
        Rect textBounds = new(
            bounds.Left + node.TextBorder.X,
            bounds.Top + node.TextBorder.Y,
            Math.Max(1, bounds.Width - (node.TextBorder.X * 2)),
            Math.Max(1, bounds.Height - (node.TextBorder.Y * 2)));
        FormattedText text = new(
            content,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            ToBrush(node.Color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = textBounds.Width,
            MaxTextHeight = textBounds.Height,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = node.HorizontalTextAlignment switch
            {
                XuiTextHorizontalAlignment.Center => TextAlignment.Center,
                XuiTextHorizontalAlignment.Right => TextAlignment.Right,
                XuiTextHorizontalAlignment.Justify => TextAlignment.Justify,
                _ => TextAlignment.Left,
            },
        };
        if (!node.MultiLine)
        {
            text.MaxLineCount = 1;
        }

        if (node.Underline)
        {
            text.SetTextDecorations(TextDecorations.Underline);
        }

        foreach (XuiTextColorRun run in presentation.ColorRuns)
        {
            int start = Math.Clamp(run.Start, 0, content.Length);
            int length = Math.Clamp(
                run.Length,
                0,
                content.Length - start);
            if (length > 0)
            {
                text.SetForegroundBrush(
                    ToBrush(run.Color),
                    start,
                    length);
            }
        }

        double renderedHeight = Math.Min(text.Height, textBounds.Height);
        double y = node.VerticalTextAlignment switch
        {
            XuiTextVerticalAlignment.Middle =>
                textBounds.Top + ((textBounds.Height - renderedHeight) * 0.5),
            XuiTextVerticalAlignment.Bottom =>
                textBounds.Bottom - renderedHeight,
            _ => textBounds.Top,
        };
        Point origin = new(textBounds.Left, y);
        if (!node.Outline && !node.Shadow)
        {
            drawing.DrawText(text, origin);
            return;
        }

        Geometry geometry = text.BuildGeometry(origin);
        if (node.Shadow)
        {
            drawing.PushTransform(new TranslateTransform(
                node.ShadowOffset,
                node.ShadowOffset));
            drawing.DrawGeometry(ToBrush(node.ShadowColor), null, geometry);
            drawing.Pop();
        }

        Pen? outlinePen = node.Outline
            ? new Pen(ToBrush(node.OutlineColor), node.OutlineSize)
            : null;
        outlinePen?.Freeze();
        if (outlinePen is not null)
        {
            drawing.DrawGeometry(null, outlinePen, geometry);
        }

        drawing.DrawText(text, origin);
    }

    private static bool BitmapFontSupportsText(
        XuiBitmapFontMetrics metrics,
        string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value is '\r' or '\n')
            {
                continue;
            }

            if (!metrics.Glyphs.ContainsKey(rune.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static void DrawBitmapText(
        DrawingContext drawing,
        XuiRenderNode node,
        Rect bounds,
        LoadedBitmapFont font,
        XuiTextPresentation presentation)
    {
        string content = presentation.Text;
        Rect textBounds = new(
            bounds.Left + node.TextBorder.X,
            bounds.Top + node.TextBorder.Y,
            Math.Max(1, bounds.Width - (node.TextBorder.X * 2)),
            Math.Max(1, bounds.Height - (node.TextBorder.Y * 2)));
        double requestedSize = node.PointSize > 0
            ? node.PointSize
            : font.Resolved.Size;
        double baseScale = Math.Clamp(
            requestedSize / Math.Max(1, font.Resolved.FontHeight),
            0.01,
            64);
        List<BitmapTextLine> lines = LayoutBitmapText(
            content,
            font.Resolved,
            baseScale,
            textBounds.Width,
            node.MultiLine,
            node.CharacterSpacingAdjust,
            presentation.ColorRuns);
        if (lines.Count == 0)
        {
            return;
        }

        double lineHeight = font.Resolved.FontHeight * baseScale;
        int visibleLineCount = Math.Max(
            1,
            Math.Min(
                lines.Count,
                (int)Math.Floor(textBounds.Height / Math.Max(1, lineHeight))));
        if (!node.MultiLine)
        {
            visibleLineCount = 1;
        }

        IReadOnlyList<BitmapTextLine> visibleLines =
            lines.Take(visibleLineCount).ToArray();
        double blockHeight = Math.Min(
            textBounds.Height,
            visibleLines.Count * lineHeight);
        double y = node.VerticalTextAlignment switch
        {
            XuiTextVerticalAlignment.Middle =>
                textBounds.Top + ((textBounds.Height - blockHeight) * 0.5),
            XuiTextVerticalAlignment.Bottom =>
                textBounds.Bottom - blockHeight,
            _ => textBounds.Top,
        };

        drawing.PushClip(new RectangleGeometry(textBounds));
        if (node.Shadow)
        {
            DrawBitmapTextPass(
                drawing,
                node,
                font,
                visibleLines,
                textBounds,
                y,
                lineHeight,
                node.ShadowOffset,
                node.ShadowOffset,
                node.ShadowColor);
        }

        if (node.Outline)
        {
            double radius = Math.Max(0.5, node.OutlineSize);
            foreach ((double x, double yOffset) in new[]
                     {
                         (-radius, -radius),
                         (0, -radius),
                         (radius, -radius),
                         (-radius, 0),
                         (radius, 0),
                         (-radius, radius),
                         (0, radius),
                         (radius, radius),
                     })
            {
                DrawBitmapTextPass(
                    drawing,
                    node,
                    font,
                    visibleLines,
                    textBounds,
                    y,
                    lineHeight,
                    x,
                    yOffset,
                    node.OutlineColor);
            }
        }

        DrawBitmapTextPass(
            drawing,
            node,
            font,
            visibleLines,
            textBounds,
            y,
            lineHeight,
            0,
            0,
            uniformColor: null);
        drawing.Pop();
    }

    private static List<BitmapTextLine> LayoutBitmapText(
        string content,
        ResolvedBitmapFont font,
        double baseScale,
        double maximumWidth,
        bool multiline,
        double characterSpacingAdjust,
        IReadOnlyList<XuiTextColorRun> colorRuns)
    {
        List<BitmapTextLine> lines = [];
        List<BitmapGlyphPlacement> placements = [];
        double width = 0;
        void CommitLine()
        {
            lines.Add(new BitmapTextLine(placements.ToArray(), width));
            placements = [];
            width = 0;
        }

        int textIndex = 0;
        int colorRunIndex = 0;
        foreach (Rune rune in content.EnumerateRunes())
        {
            int runeStart = textIndex;
            textIndex += rune.Utf16SequenceLength;
            if (rune.Value == '\r')
            {
                continue;
            }

            if (rune.Value == '\n')
            {
                if (!multiline)
                {
                    break;
                }

                CommitLine();
                continue;
            }

            XuiBitmapGlyph? glyph =
                font.Metrics.Glyphs.GetValueOrDefault(rune.Value) ??
                font.Metrics.Glyphs.GetValueOrDefault('?');
            if (glyph is null)
            {
                continue;
            }

            double glyphScale = baseScale *
                                (glyph.IsSpecial
                                    ? font.SpecialSignsScale
                                    : 1);
            double advance = Math.Max(
                0,
                (glyph.Advance +
                 font.CharacterSpacing +
                 characterSpacingAdjust) *
                glyphScale);
            if (multiline &&
                placements.Count > 0 &&
                width + advance > maximumWidth)
            {
                CommitLine();
            }

            XuiColor? glyphColor = ColorForTextIndex(
                colorRuns,
                runeStart,
                ref colorRunIndex);
            placements.Add(new BitmapGlyphPlacement(
                glyph,
                glyphScale,
                advance,
                glyphColor));
            width += advance;
        }

        if (placements.Count > 0 || lines.Count == 0)
        {
            CommitLine();
        }

        return lines;
    }

    private static XuiColor? ColorForTextIndex(
        IReadOnlyList<XuiTextColorRun> colorRuns,
        int textIndex,
        ref int colorRunIndex)
    {
        while (colorRunIndex < colorRuns.Count &&
               textIndex >= colorRuns[colorRunIndex].End)
        {
            colorRunIndex++;
        }

        return colorRunIndex < colorRuns.Count &&
               textIndex >= colorRuns[colorRunIndex].Start &&
               textIndex < colorRuns[colorRunIndex].End
            ? colorRuns[colorRunIndex].Color
            : null;
    }

    private static void DrawBitmapTextPass(
        DrawingContext drawing,
        XuiRenderNode node,
        LoadedBitmapFont font,
        IReadOnlyList<BitmapTextLine> lines,
        Rect textBounds,
        double top,
        double lineHeight,
        double offsetX,
        double offsetY,
        XuiColor? uniformColor)
    {
        Brush? uniformBrush = uniformColor is XuiColor passColor
            ? ToBrush(passColor)
            : null;
        Dictionary<XuiColor, Brush>? brushes =
            uniformBrush is null ? [] : null;
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            BitmapTextLine line = lines[lineIndex];
            double x = node.HorizontalTextAlignment switch
            {
                XuiTextHorizontalAlignment.Center =>
                    textBounds.Left + ((textBounds.Width - line.Width) * 0.5),
                XuiTextHorizontalAlignment.Right =>
                    textBounds.Right - line.Width,
                _ => textBounds.Left,
            };
            double y = top + (lineIndex * lineHeight);
            foreach (BitmapGlyphPlacement placement in line.Glyphs)
            {
                XuiColor color =
                    uniformColor ??
                    placement.Color ??
                    node.Color;
                Brush? colorBrush = uniformBrush;
                if (colorBrush is null &&
                    !brushes!.TryGetValue(color, out colorBrush))
                {
                    colorBrush = ToBrush(color);
                    brushes.Add(color, colorBrush);
                }

                XuiRect source = placement.Glyph.SourceRectangle;
                if (source.Width > 0 && source.Height > 0)
                {
                    Rect destination = new(
                        x + offsetX,
                        y + offsetY +
                        (placement.Glyph.VerticalOffset * placement.Scale),
                        source.Width * placement.Scale,
                        source.Height * placement.Scale);
                    ImageBrush mask = new(
                        placement.Glyph.IsSpecial
                            ? font.SpecialMaskBitmap
                            : font.RegularMaskBitmap)
                    {
                        Stretch = Stretch.Fill,
                        Viewbox = ToRect(source),
                        ViewboxUnits = BrushMappingMode.Absolute,
                    };
                    mask.Freeze();
                    drawing.PushOpacityMask(mask);
                    drawing.DrawRectangle(
                        colorBrush,
                        null,
                        destination);
                    drawing.Pop();
                }

                x += placement.Advance;
            }
        }
    }

    private static FontFamily CreateFontFamily(ResolvedFont? font)
    {
        if (font?.FontFile is string fontFile)
        {
            string? directory = Path.GetDirectoryName(fontFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Uri baseUri = new(
                    Path.EndsInDirectorySeparator(directory)
                        ? directory
                        : directory + Path.DirectorySeparatorChar,
                    UriKind.Absolute);
                return new FontFamily(baseUri, $"./#{font.Family}");
            }
        }

        return new FontFamily(
            string.IsNullOrWhiteSpace(font?.Family)
                ? "Segoe UI"
                : font.Family);
    }

    private void DrawRulers(
        DrawingContext drawing,
        XuiRenderFrame frame,
        Matrix camera)
    {
        Brush background = new SolidColorBrush(Color.FromRgb(31, 34, 38));
        drawing.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, RulerSize));
        drawing.DrawRectangle(background, null, new Rect(0, 0, RulerSize, ActualHeight));
        Pen tickPen = new(new SolidColorBrush(Color.FromRgb(120, 126, 134)), 1);
        double step = SelectRulerStep(camera.M11);
        for (double x = 0; x <= frame.DesignSize.X; x += step)
        {
            Point point = camera.Transform(new Point(x, 0));
            if (point.X < RulerSize || point.X > ActualWidth)
            {
                continue;
            }

            drawing.DrawLine(
                tickPen,
                new Point(point.X, RulerSize - 7),
                new Point(point.X, RulerSize));
            DrawRulerLabel(drawing, x, new Point(point.X + 2, 2));
        }

        for (double y = 0; y <= frame.DesignSize.Y; y += step)
        {
            Point point = camera.Transform(new Point(0, y));
            if (point.Y < RulerSize || point.Y > ActualHeight)
            {
                continue;
            }

            drawing.DrawLine(
                tickPen,
                new Point(RulerSize - 7, point.Y),
                new Point(RulerSize, point.Y));
        }
    }

    private void RedrawOverlay()
    {
        using DrawingContext drawing = _overlay.RenderOpen();
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            return;
        }

        Matrix camera = CreateCamera();
        drawing.PushTransform(new MatrixTransform(camera));
        Pen selectionPen = new(
            new SolidColorBrush(Color.FromRgb(242, 140, 40)),
            1.5 / Math.Max(camera.M11, 0.001));
        double handleSize = 7 / Math.Max(camera.M11, 0.001);
        foreach (string selectedKey in _selectedKeys)
        {
            if (!_nodeVisuals.TryGetValue(
                    selectedKey,
                    out NodeVisual? selectedVisual) ||
                _hiddenKeys.Contains(selectedVisual.Node.SelectionKey))
            {
                continue;
            }

            XuiRenderNode node = selectedVisual.Node;
            XuiRect world = PreviewBounds(node);
            if (_dragNodeKey == node.Key)
            {
                if (_dragPreviewBounds is XuiRect preview)
                {
                    world = preview;
                }
                else if (_dragKind == XuiTransformKind.Move &&
                         _previewLocalTransforms.Count == 0)
                {
                    world = world with
                    {
                        X = world.X + _dragWorldDelta.X,
                        Y = world.Y + _dragWorldDelta.Y,
                    };
                }
            }

            Rect rect = ToRect(world);
            drawing.DrawRectangle(null, selectionPen, rect);
            if (!CanTransform(node))
            {
                continue;
            }

            foreach ((ResizeHandle _, Point handle) in Handles(
                         rect,
                         20 / Math.Max(camera.M11, 0.001)))
            {
                drawing.DrawRectangle(
                    new SolidColorBrush(Color.FromRgb(242, 140, 40)),
                    new Pen(Brushes.Black, 0.5 / Math.Max(camera.M11, 0.001)),
                    new Rect(
                        handle.X - (handleSize * 0.5),
                        handle.Y - (handleSize * 0.5),
                        handleSize,
                        handleSize));
            }

            Point rotationHandle = RotationHandle(
                rect,
                20 / Math.Max(camera.M11, 0.001));
            drawing.DrawLine(
                selectionPen,
                new Point(rect.Left + (rect.Width * 0.5), rect.Top),
                rotationHandle);
            drawing.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(242, 140, 40)),
                new Pen(Brushes.Black, 0.5 / Math.Max(camera.M11, 0.001)),
                rotationHandle,
                handleSize * 0.55,
                handleSize * 0.55);

            if (_dragNodeKey == node.Key &&
                _dragKind == XuiTransformKind.Rotate)
            {
                DrawOverlayLabel(
                    drawing,
                    FormattableString.Invariant(
                        $"{_dragRotationDelta:+0.0;-0.0;0}°"),
                    new Point(rect.Right + (8 / Math.Max(camera.M11, 0.001)),
                        rect.Top),
                    camera.M11);
            }
        }

        drawing.Pop();
        DrawZoomLabel(drawing, camera.M11);
    }

    private XuiRect PreviewBounds(XuiRenderNode node)
    {
        if (_previewLocalTransforms.Count == 0 ||
            _frame is not XuiRenderFrame frame)
        {
            return node.WorldBounds;
        }

        Dictionary<string, XuiRenderNode> byKey = frame.Nodes.ToDictionary(
            static candidate => candidate.Key,
            StringComparer.Ordinal);
        Dictionary<string, Matrix3x2> worldCache =
            new(StringComparer.Ordinal);
        Matrix3x2 WorldFor(XuiRenderNode candidate)
        {
            if (worldCache.TryGetValue(
                    candidate.Key,
                    out Matrix3x2 cached))
            {
                return cached;
            }

            Matrix3x2 local =
                _previewLocalTransforms.GetValueOrDefault(
                    candidate.Key,
                    candidate.LocalTransform);
            Matrix3x2 world =
                candidate.ParentKey is string parentKey &&
                byKey.TryGetValue(parentKey, out XuiRenderNode? parent)
                    ? local * WorldFor(parent)
                    : local;
            worldCache[candidate.Key] = world;
            return world;
        }

        return TransformBounds(node.LocalBounds, WorldFor(node));
    }

    private void RequestSelectedTextures()
    {
        foreach (XuiRenderNode node in _nodeVisuals.Values
                     .Select(static visual => visual.Node)
                     .Where(node =>
                         _selectedKeys.Contains(node.SelectionKey) &&
                         node.PaintKind == XuiPaintKind.Texture &&
                         EffectiveNodeOpacity(node) > 0))
        {
            RequestTexture(node.ImagePath, SelectedTexturePriority);
        }
    }

    private void RequestTexture(
        string imagePath,
        int priority = VisibleTexturePriority)
    {
        IAssetResolver? resolver = _assetResolver;
        if (string.IsNullOrWhiteSpace(imagePath) ||
            resolver is null ||
            _textureBitmaps.ContainsKey(imagePath))
        {
            return;
        }

        TextureLoadKey key = new(_assetResolverGeneration, imagePath);
        lock (_textureQueueGate)
        {
            if (_activeTextureLoads.Contains(key))
            {
                return;
            }

            if (_pendingTextureLoads.TryGetValue(key, out int current))
            {
                _pendingTextureLoads[key] = Math.Min(current, priority);
            }
            else
            {
                _pendingTextureLoads.Add(key, priority);
            }
        }

        StartPendingTextureLoads();
    }

    private void RequestBitmapFont(string fontId)
    {
        IAssetResolver? resolver = _assetResolver;
        if (string.IsNullOrWhiteSpace(fontId) ||
            resolver is null ||
            !_requestedBitmapFonts.Add(fontId))
        {
            return;
        }

        _ = LoadBitmapFontAsync(
            fontId,
            resolver,
            resolver.Revision,
            _assetResolverGeneration);
    }

    private async Task LoadBitmapFontAsync(
        string fontId,
        IAssetResolver resolver,
        long resolverRevision,
        long resolverGeneration)
    {
        try
        {
            ResolvedBitmapFont? font = await resolver
                .ResolveBitmapFontAsync(fontId)
                .ConfigureAwait(false);
            if (font is null)
            {
                return;
            }

            BitmapSource regularMask = CreateFontMaskBitmap(
                font.AtlasWidth,
                font.AtlasHeight,
                font.AtlasBgraPixels,
                specialGlyphs: false);
            BitmapSource specialMask = CreateFontMaskBitmap(
                font.AtlasWidth,
                font.AtlasHeight,
                font.AtlasBgraPixels,
                specialGlyphs: true);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentResolver(
                        resolver,
                        resolverRevision,
                        resolverGeneration))
                {
                    return;
                }

                _bitmapFonts[fontId] = new LoadedBitmapFont(
                    regularMask,
                    specialMask,
                    font);
                RedrawResourceUsers(_fontUsers, fontId);
            });
        }
        catch (IOException)
        {
            // The resolver keeps missing/corrupt font details in diagnostics.
        }
        catch (InvalidDataException)
        {
            // Invalid font data falls back to the configured system font.
        }
    }

    private async Task LoadTextureAsync(
        TextureLoadKey loadKey,
        string imagePath,
        IAssetResolver resolver,
        long resolverRevision,
        long resolverGeneration)
    {
        try
        {
            ResolvedTexture? texture = await resolver
                .ResolveTextureAsync(imagePath)
                .ConfigureAwait(false);
            if (texture is null)
            {
                return;
            }

            BitmapSource bitmap = CreateBitmap(
                texture.Width,
                texture.Height,
                texture.BgraPixels);
            IReadOnlyDictionary<XuiTileRole, LoadedTilePart> tileParts =
                texture.TileParts.ToDictionary(
                    static part => part.Role,
                    static part => new LoadedTilePart(
                        CreateBitmap(
                            part.Width,
                            part.Height,
                            part.BgraPixels),
                        part));
            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentResolver(
                        resolver,
                        resolverRevision,
                        resolverGeneration))
                {
                    return;
                }

                _textureBitmaps[imagePath] = new LoadedTexture(
                    bitmap,
                    texture,
                    tileParts);
                TextureDiagnosticsAvailable?.Invoke(
                    this,
                    new XuiTextureDiagnosticsEventArgs(
                        imagePath,
                        texture.Diagnostics));
                RedrawResourceUsers(_imageUsers, imagePath);
            });
        }
        catch (IOException)
        {
            // The resource resolver reports missing/corrupt assets as diagnostics.
        }
        catch (InvalidDataException)
        {
            // A malformed DDS remains a placeholder and never breaks the editor.
        }
        finally
        {
            CompleteTextureLoad(loadKey);
        }
    }

    private void StartPendingTextureLoads()
    {
        List<(TextureLoadKey Key, IAssetResolver Resolver, long Revision)> start =
            [];
        lock (_textureQueueGate)
        {
            while (_activeTextureLoadCount <
                   MaximumConcurrentTextureLoads &&
                   _pendingTextureLoads.Count > 0)
            {
                KeyValuePair<TextureLoadKey, int> next =
                    _pendingTextureLoads.MinBy(static pair => pair.Value);
                _pendingTextureLoads.Remove(next.Key);
                if (next.Key.Generation != _assetResolverGeneration ||
                    _assetResolver is null)
                {
                    continue;
                }

                _activeTextureLoads.Add(next.Key);
                _activeTextureLoadCount++;
                start.Add((
                    next.Key,
                    _assetResolver,
                    _assetResolver.Revision));
            }
        }

        foreach ((TextureLoadKey key, IAssetResolver resolver, long revision)
                 in start)
        {
            _ = LoadTextureAsync(
                key,
                key.ImagePath,
                resolver,
                revision,
                key.Generation);
        }
    }

    private void CompleteTextureLoad(TextureLoadKey key)
    {
        lock (_textureQueueGate)
        {
            if (_activeTextureLoads.Remove(key))
            {
                _activeTextureLoadCount--;
            }
        }

        StartPendingTextureLoads();
    }

    private void BeginNavigationCache()
    {
        _navigationCacheTimer.Stop();
        _nodeLayer.CacheMode = _navigationBitmapCache;
    }

    private void ScheduleNavigationCacheRelease()
    {
        _navigationCacheTimer.Stop();
        _navigationCacheTimer.Start();
    }

    private void EndNavigationCache()
    {
        _navigationCacheTimer.Stop();
        if (ReferenceEquals(_nodeLayer.CacheMode, _navigationBitmapCache))
        {
            _nodeLayer.CacheMode = null;
        }

        if (_deferredResourceRedrawKeys.Count == 0)
        {
            return;
        }

        string[] keys = _deferredResourceRedrawKeys.ToArray();
        _deferredResourceRedrawKeys.Clear();
        foreach (string key in keys)
        {
            if (_nodeVisuals.TryGetValue(key, out NodeVisual? visual) &&
                EffectiveNodeOpacity(visual.Node) > 0)
            {
                DrawNodeContent(visual);
            }
            else if (visual is not null)
            {
                visual.HasContent = false;
            }
        }
    }

    private static bool ContainsImageDrawing(Drawing? drawing)
    {
        if (drawing is ImageDrawing)
        {
            return true;
        }

        return drawing is DrawingGroup group &&
               group.Children.Any(ContainsImageDrawing);
    }

    private bool IsCurrentResolver(
        IAssetResolver resolver,
        long resolverRevision,
        long resolverGeneration) =>
        ReferenceEquals(_assetResolver, resolver) &&
        _assetResolverGeneration == resolverGeneration &&
        resolver.Revision == resolverRevision;

    private static BitmapSource CreateBitmap(
        int width,
        int height,
        byte[] pixels)
    {
        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateFontMaskBitmap(
        int width,
        int height,
        byte[] pixels,
        bool specialGlyphs)
    {
        bool variableAlpha = false;
        for (int offset = 3; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != byte.MaxValue)
            {
                variableAlpha = true;
                break;
            }
        }

        byte[] mask = GC.AllocateUninitializedArray<byte>(pixels.Length);
        for (int offset = 0; offset <= pixels.Length - 4; offset += 4)
        {
            byte coverage = SelectFontMaskCoverage(
                pixels[offset],
                pixels[offset + 1],
                pixels[offset + 2],
                pixels[offset + 3],
                variableAlpha,
                specialGlyphs);
            mask[offset] = byte.MaxValue;
            mask[offset + 1] = byte.MaxValue;
            mask[offset + 2] = byte.MaxValue;
            mask[offset + 3] = coverage;
        }

        return CreateBitmap(width, height, mask);
    }

    internal static byte SelectFontMaskCoverage(
        byte blue,
        byte green,
        byte red,
        byte alpha,
        bool variableAlpha,
        bool specialGlyph)
    {
        // Chrome Engine bitmap fonts use the alpha channel for ordinary
        // characters, but their private-use input glyphs are authored in
        // RGB. In particular, the alpha plane of PC_ENTER/PC_ESC is only a
        // solid rounded keycap; the arrow and lettering exist in RGB.
        if (specialGlyph || !variableAlpha)
        {
            return Math.Max(blue, Math.Max(green, red));
        }

        return alpha;
    }

    private BitmapSource TintedBitmap(
        BitmapSource original,
        byte[] pixels,
        string contentHash,
        XuiColor tint)
    {
        if (tint == XuiColor.White)
        {
            return original;
        }

        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"{contentHash}:{tint.A:X2}{tint.R:X2}{tint.G:X2}{tint.B:X2}");
        if (_tintedBitmaps.TryGetValue(key, out BitmapSource? cached))
        {
            return cached;
        }

        byte[] modulated = GC.AllocateUninitializedArray<byte>(pixels.Length);
        for (int offset = 0; offset <= pixels.Length - 4; offset += 4)
        {
            modulated[offset] = Multiply(pixels[offset], tint.B);
            modulated[offset + 1] = Multiply(pixels[offset + 1], tint.G);
            modulated[offset + 2] = Multiply(pixels[offset + 2], tint.R);
            modulated[offset + 3] = Multiply(pixels[offset + 3], tint.A);
        }

        BitmapSource result = CreateBitmap(
            original.PixelWidth,
            original.PixelHeight,
            modulated);
        _tintedBitmaps[key] = result;
        return result;
    }

    private static byte Multiply(byte left, byte right) =>
        (byte)((left * right + 127) / 255);

    private Matrix CreateCamera()
    {
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            return Matrix.Identity;
        }

        double scale = CalculateFitScale(frame) * _zoom;
        double contentWidth = frame.DesignSize.X * scale;
        double contentHeight = frame.DesignSize.Y * scale;
        double x = RulerSize +
                   ((Math.Max(0, ActualWidth - RulerSize) - contentWidth) * 0.5) +
                   _pan.X;
        double y = RulerSize +
                   ((Math.Max(0, ActualHeight - RulerSize) - contentHeight) * 0.5) +
                   _pan.Y;
        return new Matrix(scale, 0, 0, scale, x, y);
    }

    private double CalculateFitScale(XuiRenderFrame frame)
    {
        double width = Math.Max(1, ActualWidth - RulerSize - 36);
        double height = Math.Max(1, ActualHeight - RulerSize - 36);
        return Math.Max(
            0.0001,
            Math.Min(
                width / frame.DesignSize.X,
                height / frame.DesignSize.Y));
    }

    private XuiVector2 ControlToLogical(Point point)
    {
        Matrix camera = CreateCamera();
        if (!camera.HasInverse)
        {
            return default;
        }

        camera.Invert();
        Point logical = camera.Transform(point);
        return new XuiVector2(logical.X, logical.Y);
    }

    private XuiRenderNode? HitTest(
        XuiVector2 logicalPoint,
        bool cycle = false)
    {
        XuiRenderNode[] candidates = HitTestCandidates(logicalPoint)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        if (!cycle)
        {
            return candidates[0];
        }

        int selectedIndex = Array.FindIndex(
            candidates,
            candidate =>
                _selectedKeys.Contains(candidate.SelectionKey));
        return selectedIndex < 0
            ? candidates[0]
            : candidates[(selectedIndex + 1) % candidates.Length];
    }

    private IEnumerable<XuiRenderNode> HitTestCandidates(
        XuiVector2 logicalPoint)
    {
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            yield break;
        }

        HashSet<string> returnedOwners = new(StringComparer.Ordinal);
        foreach (XuiRenderNode node in frame.Nodes
                     .Where(node =>
                         !IsCanvasRoot(node) &&
                         !_hiddenKeys.Contains(node.SelectionKey) &&
                         node.IsShown &&
                         node.Opacity > 0)
                     .Reverse())
        {
            if (HitTestNode(node, logicalPoint) &&
                returnedOwners.Add(node.SelectionKey))
            {
                yield return node;
            }
        }
    }

    private XuiRenderNode? HitTestSelectedBody(
        XuiVector2 logicalPoint)
    {
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            return null;
        }

        return frame.Nodes
            .Where(node =>
                !IsCanvasRoot(node) &&
                _selectedKeys.Contains(node.SelectionKey) &&
                !_hiddenKeys.Contains(node.SelectionKey))
            .Reverse()
            .FirstOrDefault(node =>
                HitTestNode(node, logicalPoint));
    }

    private static bool HitTestNode(
        XuiRenderNode node,
        XuiVector2 logicalPoint)
    {
        if (node.ClipBounds is XuiRect clip &&
            !clip.Contains(logicalPoint))
        {
            return false;
        }

        if (!Matrix3x2.Invert(
                node.WorldTransform,
                out Matrix3x2 inverse))
        {
            return node.WorldBounds.Contains(logicalPoint);
        }

        XuiVector2 local = TransformPoint(
            logicalPoint.X,
            logicalPoint.Y,
            inverse);
        return node.LocalBounds.Contains(local);
    }

    private XuiRenderNode TransformOwner(XuiRenderNode candidate) =>
        _frame?.Nodes.FirstOrDefault(node =>
            node.Key.Equals(
                candidate.SelectionKey,
                StringComparison.Ordinal)) ?? candidate;

    private bool CanTransform(XuiRenderNode node) =>
        !IsCanvasRoot(node) &&
        !IsLocked(node);

    private bool IsLocked(XuiRenderNode node) =>
        _lockedKeys.Contains(node.SelectionKey) ||
        _lockedKeys.Contains(node.Key);

    private static bool IsCanvasRoot(XuiRenderNode node) =>
        node.ParentKey is null ||
        node.ElementName.Equals(
            "XuiCanvas",
            StringComparison.OrdinalIgnoreCase);

    private void BeginTransform(
        XuiRenderNode node,
        XuiTransformKind kind,
        ResizeHandle handle)
    {
        if (!CanTransform(node))
        {
            return;
        }

        _dragNodeKey = node.Key;
        _dragNode = node;
        _dragKind = kind;
        _dragHandle = handle;
        _dragWorldDelta = default;
        _dragPositionDelta = default;
        _dragSizeDelta = default;
        _dragRotationDelta = 0;
        _dragPreviewBounds = null;
        _previewLocalTransforms.Clear();
        _dragPositionDeltas.Clear();
        CaptureMouse();
        Cursor = kind == XuiTransformKind.Move
            ? Cursors.SizeAll
            : CursorForHandle(handle);
    }

    private void UpdateTransformPreview(XuiVector2 pointer)
    {
        XuiRenderNode? node = _dragNode;
        if (node is null)
        {
            return;
        }

        switch (_dragKind)
        {
            case XuiTransformKind.Move:
                _dragPositionDelta = WorldToParentDelta(
                    node,
                    _dragWorldDelta);
                _dragSizeDelta = default;
                _dragPreviewBounds = null;
                break;

            case XuiTransformKind.Resize:
                UpdateResizePreview(node);
                break;

            case XuiTransformKind.Rotate:
                XuiVector2 center = TransformPoint(
                    node.Pivot.X,
                    node.Pivot.Y,
                    node.WorldTransform);
                XuiVector2 start = ControlToLogical(_pointerStart);
                double startAngle = Math.Atan2(
                    start.Y - center.Y,
                    start.X - center.X);
                double endAngle = Math.Atan2(
                    pointer.Y - center.Y,
                    pointer.X - center.X);
                double degrees = (endAngle - startAngle) * 180 / Math.PI;
                if (SnapEnabled)
                {
                    degrees = Math.Round(degrees / 15) * 15;
                }

                _dragRotationDelta = NormalizeDegrees(degrees);
                _dragPositionDelta = default;
                _dragSizeDelta = default;
                _dragPreviewBounds = null;
                break;
        }
    }

    private void ApplyTransformPreview()
    {
        XuiRenderFrame? frame = _frame;
        XuiRenderNode? primary = _dragNode;
        if (frame is null || primary is null)
        {
            return;
        }

        RestoreTransformPreview();
        _previewLocalTransforms.Clear();
        _dragPositionDeltas.Clear();
        if (_dragKind == XuiTransformKind.Move)
        {
            foreach (XuiRenderNode node in SelectedTransformRoots(primary))
            {
                XuiVector2 parentDelta = WorldToParentDelta(
                    node,
                    _dragWorldDelta);
                Matrix3x2 preview = node.LocalTransform;
                preview.M31 += (float)parentDelta.X;
                preview.M32 += (float)parentDelta.Y;
                _dragPositionDeltas[node.Key] = parentDelta;
                SetPreviewLocalTransform(node, preview);
            }
        }
        else if (_dragKind == XuiTransformKind.Rotate)
        {
            foreach (XuiRenderNode node in SelectedTransformRoots(primary))
            {
                Matrix3x2 preview = CreateLocalTransform(
                    node.Position,
                    node.Pivot,
                    node.Scale,
                    node.RotationDegrees + _dragRotationDelta);
                SetPreviewLocalTransform(node, preview);
            }
        }
    }

    private IEnumerable<XuiRenderNode> SelectedTransformRoots(
        XuiRenderNode primary)
    {
        XuiRenderFrame? frame = _frame;
        if (frame is null || !_selectedKeys.Contains(primary.Key))
        {
            yield return primary;
            yield break;
        }

        Dictionary<string, XuiRenderNode> byKey = frame.Nodes.ToDictionary(
            static node => node.Key,
            StringComparer.Ordinal);
        foreach (XuiRenderNode candidate in frame.Nodes.Where(node =>
                     _selectedKeys.Contains(node.Key) &&
                     CanTransform(node)))
        {
            string? parentKey = candidate.ParentKey;
            bool selectedAncestor = false;
            while (parentKey is not null &&
                   byKey.TryGetValue(parentKey, out XuiRenderNode? parent))
            {
                if (_selectedKeys.Contains(parent.Key) &&
                    CanTransform(parent))
                {
                    selectedAncestor = true;
                    break;
                }

                parentKey = parent.ParentKey;
            }

            if (!selectedAncestor)
            {
                yield return candidate;
            }
        }
    }

    private void SetPreviewLocalTransform(
        XuiRenderNode node,
        Matrix3x2 transform)
    {
        _previewLocalTransforms[node.Key] = transform;
        if (_nodeVisuals.TryGetValue(node.Key, out NodeVisual? visual))
        {
            visual.Container.Transform =
                new MatrixTransform(ToMatrix(transform));
        }
    }

    private void RestoreTransformPreview()
    {
        foreach (string key in _previewLocalTransforms.Keys)
        {
            if (_nodeVisuals.TryGetValue(key, out NodeVisual? visual))
            {
                visual.Container.Transform =
                    new MatrixTransform(ToMatrix(visual.Node.LocalTransform));
            }
        }
    }

    private void CancelTransform(bool releaseCapture = true)
    {
        RestoreTransformPreview();
        _previewLocalTransforms.Clear();
        _dragPositionDeltas.Clear();
        _dragNodeKey = null;
        _dragNode = null;
        _dragHandle = ResizeHandle.None;
        _dragWorldDelta = default;
        _dragPositionDelta = default;
        _dragSizeDelta = default;
        _dragRotationDelta = 0;
        _dragPreviewBounds = null;
        if (releaseCapture && IsMouseCaptured)
        {
            _finishingTransform = true;
            ReleaseMouseCapture();
            _finishingTransform = false;
        }

        Cursor = Cursors.Arrow;
        RedrawOverlay();
    }

    private void ClearPendingPointerGesture(bool releaseCapture)
    {
        _pendingPointerGesture = null;
        if (releaseCapture)
        {
            ReleasePointerCapture();
        }

        Cursor = Cursors.Arrow;
    }

    private void ReleasePointerCapture()
    {
        if (!IsMouseCaptured)
        {
            return;
        }

        _finishingTransform = true;
        ReleaseMouseCapture();
        _finishingTransform = false;
    }

    private static Matrix3x2 CreateLocalTransform(
        XuiVector3 position,
        XuiVector3 pivot,
        XuiVector3 scale,
        double rotationDegrees) =>
        Matrix3x2.CreateTranslation(
            (float)-pivot.X,
            (float)-pivot.Y) *
        Matrix3x2.CreateScale(
            (float)scale.X,
            (float)scale.Y) *
        Matrix3x2.CreateRotation(
            (float)(rotationDegrees * Math.PI / 180)) *
        Matrix3x2.CreateTranslation(
            (float)(position.X + pivot.X),
            (float)(position.Y + pivot.Y));

    private void UpdateResizePreview(XuiRenderNode node)
    {
        if (!Matrix3x2.Invert(
                node.WorldTransform,
                out Matrix3x2 inverseWorld))
        {
            return;
        }

        XuiVector2 startWorld = ControlToLogical(_pointerStart);
        XuiVector2 endWorld = new(
            startWorld.X + _dragWorldDelta.X,
            startWorld.Y + _dragWorldDelta.Y);
        System.Numerics.Vector2 startLocal =
            System.Numerics.Vector2.Transform(
                new System.Numerics.Vector2(
                    (float)startWorld.X,
                    (float)startWorld.Y),
                inverseWorld);
        System.Numerics.Vector2 endLocal =
            System.Numerics.Vector2.Transform(
                new System.Numerics.Vector2(
                    (float)endWorld.X,
                    (float)endWorld.Y),
                inverseWorld);
        double deltaX = endLocal.X - startLocal.X;
        double deltaY = endLocal.Y - startLocal.Y;
        double left = 0;
        double top = 0;
        double right = node.Size.X;
        double bottom = node.Size.Y;

        if (_dragHandle.HasFlag(ResizeHandle.Left))
        {
            left = Math.Min(node.Size.X - 1, deltaX);
        }

        if (_dragHandle.HasFlag(ResizeHandle.Right))
        {
            right = Math.Max(1, node.Size.X + deltaX);
        }

        if (_dragHandle.HasFlag(ResizeHandle.Top))
        {
            top = Math.Min(node.Size.Y - 1, deltaY);
        }

        if (_dragHandle.HasFlag(ResizeHandle.Bottom))
        {
            bottom = Math.Max(1, node.Size.Y + deltaY);
        }

        double newWidth = Math.Max(1, right - left);
        double newHeight = Math.Max(1, bottom - top);
        _dragSizeDelta = new XuiVector2(
            newWidth - node.Size.X,
            newHeight - node.Size.Y);
        System.Numerics.Vector2 parentShift =
            System.Numerics.Vector2.TransformNormal(
                new System.Numerics.Vector2((float)left, (float)top),
                node.LocalTransform);
        _dragPositionDelta = new XuiVector2(
            parentShift.X,
            parentShift.Y);
        _dragPreviewBounds = TransformBounds(
            new XuiRect(left, top, newWidth, newHeight),
            node.WorldTransform);
    }

    private XuiVector2 WorldToParentDelta(
        XuiRenderNode node,
        XuiVector2 worldDelta)
    {
        XuiRenderNode? parent = _frame?.Nodes.FirstOrDefault(candidate =>
            candidate.Key == node.ParentKey);
        if (parent is null ||
            !Matrix3x2.Invert(
                parent.WorldTransform,
                out Matrix3x2 inverseParent))
        {
            return worldDelta;
        }

        System.Numerics.Vector2 result =
            System.Numerics.Vector2.TransformNormal(
                new System.Numerics.Vector2(
                    (float)worldDelta.X,
                    (float)worldDelta.Y),
                inverseParent);
        return new XuiVector2(result.X, result.Y);
    }

    private TransformHandleHit? HitTestTransformHandle(XuiVector2 point)
    {
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            return null;
        }

        double scale = Math.Max(CreateCamera().M11, 0.001);
        double radius = 7 / scale;
        double rotationOffset = 20 / scale;
        foreach (XuiRenderNode node in frame.Nodes
                     .Where(node =>
                         _selectedKeys.Contains(node.Key) &&
                         !_hiddenKeys.Contains(node.SelectionKey) &&
                         CanTransform(node))
                     .Reverse())
        {
            Rect bounds = ToRect(node.WorldBounds);
            Point rotation = RotationHandle(bounds, rotationOffset);
            if (Near(point, rotation, radius))
            {
                return new TransformHandleHit(node, ResizeHandle.Rotate);
            }

            foreach ((ResizeHandle handle, Point location) in
                     Handles(bounds, rotationOffset))
            {
                if (Near(point, location, radius))
                {
                    return new TransformHandleHit(node, handle);
                }
            }
        }

        return null;
    }

    private static bool Near(
        XuiVector2 point,
        Point target,
        double radius) =>
        Math.Abs(point.X - target.X) <= radius &&
        Math.Abs(point.Y - target.Y) <= radius;

    private static Cursor CursorForHandle(ResizeHandle handle) =>
        handle switch
        {
            ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
            ResizeHandle.Top or ResizeHandle.Bottom => Cursors.SizeNS,
            ResizeHandle.TopLeft or
                ResizeHandle.BottomRight => Cursors.SizeNWSE,
            ResizeHandle.TopRight or
                ResizeHandle.BottomLeft => Cursors.SizeNESW,
            ResizeHandle.Rotate => Cursors.Cross,
            _ => Cursors.Arrow,
        };

    private static bool HasDelta(XuiVector2 delta) =>
        Math.Abs(delta.X) > 0.0001 || Math.Abs(delta.Y) > 0.0001;

    private static double NormalizeDegrees(double degrees)
    {
        double normalized = degrees % 360;
        if (normalized > 180)
        {
            normalized -= 360;
        }
        else if (normalized < -180)
        {
            normalized += 360;
        }

        return normalized;
    }

    private static XuiRect TransformBounds(
        XuiRect bounds,
        Matrix3x2 transform)
    {
        Span<XuiVector2> points =
        [
            TransformPoint(bounds.X, bounds.Y, transform),
            TransformPoint(bounds.Right, bounds.Y, transform),
            TransformPoint(bounds.Right, bounds.Bottom, transform),
            TransformPoint(bounds.X, bounds.Bottom, transform),
        ];
        return XuiRect.FromPoints(points);
    }

    private static XuiVector2 TransformPoint(
        double x,
        double y,
        Matrix3x2 transform)
    {
        System.Numerics.Vector2 point =
            System.Numerics.Vector2.Transform(
                new System.Numerics.Vector2((float)x, (float)y),
                transform);
        return new XuiVector2(point.X, point.Y);
    }

    private static Matrix ToMatrix(Matrix3x2 matrix) =>
        new(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.M31,
            matrix.M32);

    private static Rect ToRect(XuiRect rectangle) =>
        new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    private static SolidColorBrush ToBrush(XuiColor color)
    {
        SolidColorBrush brush = new(
            Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static void CollectDrawingColors(
        Drawing? drawing,
        List<XuiColor> colors)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                foreach (Drawing child in group.Children)
                {
                    CollectDrawingColors(child, colors);
                }

                break;
            case GlyphRunDrawing glyph:
                AddBrushColor(glyph.ForegroundBrush, colors);
                break;
            case GeometryDrawing geometry:
                AddBrushColor(geometry.Brush, colors);
                AddBrushColor(geometry.Pen?.Brush, colors);
                break;
        }
    }

    private static void AddBrushColor(
        Brush? brush,
        List<XuiColor> colors)
    {
        if (brush is not SolidColorBrush solid)
        {
            return;
        }

        colors.Add(new XuiColor(
            solid.Color.A,
            solid.Color.R,
            solid.Color.G,
            solid.Color.B));
    }

    private static IEnumerable<(ResizeHandle Handle, Point Point)> Handles(
        Rect rectangle,
        double rotationOffset)
    {
        _ = rotationOffset;
        yield return (ResizeHandle.TopLeft, rectangle.TopLeft);
        yield return (
            ResizeHandle.Top,
            new Point(rectangle.Left + (rectangle.Width * 0.5), rectangle.Top));
        yield return (ResizeHandle.TopRight, rectangle.TopRight);
        yield return (
            ResizeHandle.Right,
            new Point(rectangle.Right, rectangle.Top + (rectangle.Height * 0.5)));
        yield return (
            ResizeHandle.BottomRight,
            rectangle.BottomRight);
        yield return (
            ResizeHandle.Bottom,
            new Point(rectangle.Left + (rectangle.Width * 0.5), rectangle.Bottom));
        yield return (
            ResizeHandle.BottomLeft,
            rectangle.BottomLeft);
        yield return (
            ResizeHandle.Left,
            new Point(rectangle.Left, rectangle.Top + (rectangle.Height * 0.5)));
    }

    private static Point RotationHandle(Rect rectangle, double offset) =>
        new(rectangle.Left + (rectangle.Width * 0.5), rectangle.Top - offset);

    private sealed record TransformHandleHit(
        XuiRenderNode Node,
        ResizeHandle Handle);

    private sealed class NodeVisual
    {
        public NodeVisual(
            ContainerVisual container,
            DrawingVisual content,
            XuiRenderNode node)
        {
            Container = container;
            Content = content;
            Node = node;
        }

        public ContainerVisual Container { get; }

        public DrawingVisual Content { get; }

        public XuiRenderNode Node { get; set; }

        public bool HasContent { get; set; }
    }

    private void DrawOverlayLabel(
        DrawingContext drawing,
        string value,
        Point origin,
        double scale)
    {
        FormattedText text = new(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11 / Math.Max(scale, 0.001),
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(text, origin);
    }

    internal static double SelectRulerStep(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            return 500;
        }

        double required = Math.Max(10, 45 / scale);
        if (!double.IsFinite(required))
        {
            return double.MaxValue;
        }

        double magnitude = Math.Pow(
            10,
            Math.Floor(Math.Log10(required)));
        double normalized = required / magnitude;
        double multiplier = normalized <= 1
            ? 1
            : normalized <= 2
                ? 2
                : normalized <= 5
                    ? 5
                    : 10;
        double step = magnitude * multiplier;
        return double.IsFinite(step) && step > 0
            ? step
            : double.MaxValue;
    }

    private void DrawRulerLabel(
        DrawingContext drawing,
        double value,
        Point origin)
    {
        FormattedText text = new(
            value.ToString("0", CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            9,
            new SolidColorBrush(Color.FromRgb(154, 161, 170)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(text, origin);
    }

    private void DrawZoomLabel(DrawingContext drawing, double scale)
    {
        string label = string.Create(
            CultureInfo.InvariantCulture,
            $"{scale * 100:0}%");
        FormattedText text = new(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            new SolidColorBrush(Color.FromRgb(154, 161, 170)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        Rect background = new(
            ActualWidth - text.Width - 18,
            ActualHeight - text.Height - 10,
            text.Width + 12,
            text.Height + 6);
        drawing.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(210, 31, 34, 38)),
            null,
            background,
            3,
            3);
        drawing.DrawText(
            text,
            new Point(background.X + 6, background.Y + 3));
    }

    private void DrawEmptyState(DrawingContext drawing)
    {
        FormattedText text = new(
            "Open a Dying Light .xui file to begin",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            18,
            new SolidColorBrush(Color.FromRgb(154, 161, 170)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(
            text,
            new Point(
                Math.Max(20, (ActualWidth - text.Width) * 0.5),
                Math.Max(20, (ActualHeight - text.Height) * 0.5)));
    }

    private sealed record LoadedTexture(
        BitmapSource Bitmap,
        ResolvedTexture Resolved,
        IReadOnlyDictionary<XuiTileRole, LoadedTilePart> TileParts);

    private readonly record struct TextureLoadKey(
        long Generation,
        string ImagePath);

    private sealed record LoadedTilePart(
        BitmapSource Bitmap,
        ResolvedTileTexturePart Resolved);

    private sealed record LoadedBitmapFont(
        BitmapSource RegularMaskBitmap,
        BitmapSource SpecialMaskBitmap,
        ResolvedBitmapFont Resolved);

    private sealed record BitmapGlyphPlacement(
        XuiBitmapGlyph Glyph,
        double Scale,
        double Advance,
        XuiColor? Color);

    private sealed record BitmapTextLine(
        IReadOnlyList<BitmapGlyphPlacement> Glyphs,
        double Width);

    private sealed record PointerGesture(
        XuiRenderNode? DragCandidate,
        XuiRenderNode? ClickCandidate,
        ModifierKeys Modifiers);
}
