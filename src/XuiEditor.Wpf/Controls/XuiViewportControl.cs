using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        XuiVector2 originalSize)
    {
        NodeKey = nodeKey;
        Kind = kind;
        PositionDelta = positionDelta;
        SizeDelta = sizeDelta;
        RotationDelta = rotationDelta;
        OriginalSize = originalSize;
    }

    public string NodeKey { get; }

    public XuiTransformKind Kind { get; }

    public XuiVector2 PositionDelta { get; }

    public XuiVector2 SizeDelta { get; }

    public double RotationDelta { get; }

    public XuiVector2 OriginalSize { get; }
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
    private readonly DrawingVisual _content = new();
    private readonly DrawingVisual _overlay = new();
    private readonly Dictionary<string, LoadedTexture> _textureBitmaps =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _requestedTextures =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenKeys =
        new(StringComparer.Ordinal);
    private XuiRenderFrame? _frame;
    private IAssetResolver? _assetResolver;
    private double _zoom = 1;
    private Vector _pan;
    private bool _panning;
    private Point _pointerStart;
    private string? _dragNodeKey;
    private XuiRenderNode? _dragNode;
    private XuiTransformKind _dragKind;
    private ResizeHandle _dragHandle;
    private XuiVector2 _dragWorldDelta;
    private XuiVector2 _dragPositionDelta;
    private XuiVector2 _dragSizeDelta;
    private double _dragRotationDelta;
    private XuiRect? _dragPreviewBounds;

    public XuiViewportControl()
    {
        Focusable = true;
        ClipToBounds = true;
        _visuals = new VisualCollection(this)
        {
            _content,
            _overlay,
        };
        SizeChanged += (_, _) => Redraw();
    }

    public event EventHandler<XuiSelectionRequestedEventArgs>? SelectionRequested;

    public event EventHandler<XuiTransformCommittedEventArgs>? TransformCommitted;

    public event EventHandler<XuiTextureDiagnosticsEventArgs>?
        TextureDiagnosticsAvailable;

    public bool ShowGrid { get; set; } = true;

    public bool ShowSafeArea { get; set; } = true;

    public bool ShowUnknownBounds { get; set; } = true;

    public bool SnapEnabled { get; set; } = true;

    public double GridSize { get; set; } = 8;

    public double Zoom => _zoom;

    internal bool IsSelectedForTesting(string nodeKey) =>
        _selectedKeys.Contains(nodeKey);

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    public void SetAssetResolver(IAssetResolver? assetResolver)
    {
        _assetResolver = assetResolver;
        _textureBitmaps.Clear();
        _requestedTextures.Clear();
        Redraw();
    }

    public void SetFrame(XuiRenderFrame? frame)
    {
        _frame = frame;
        Redraw();
    }

    public void SetSelectedKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _selectedKeys.Clear();
        _selectedKeys.UnionWith(keys);
        RedrawOverlay();
    }

    public void SetHiddenKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _hiddenKeys.Clear();
        _hiddenKeys.UnionWith(keys);
        Redraw();
    }

    public void Fit()
    {
        _zoom = 1;
        _pan = default;
        Redraw();
    }

    public void ActualPixels()
    {
        XuiRenderFrame? frame = _frame;
        if (frame is null)
        {
            return;
        }

        double fitScale = CalculateFitScale(frame);
        _zoom = fitScale <= 0 ? 1 : 1 / fitScale;
        _pan = default;
        Redraw();
    }

    public void ZoomBy(double factor)
    {
        _zoom = Math.Clamp(_zoom * factor, 0.05, 32);
        Redraw();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _pointerStart = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Middle)
        {
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

        XuiRenderNode? hit = HitTest(logical);
        ModifierKeys modifiers = Keyboard.Modifiers;
        SelectionRequested?.Invoke(
            this,
            new XuiSelectionRequestedEventArgs(
                hit?.SelectionKey,
                modifiers.HasFlag(ModifierKeys.Shift),
                modifiers.HasFlag(ModifierKeys.Control)));
        if (hit is not null)
        {
            XuiRenderNode dragNode = _frame.Nodes.FirstOrDefault(node =>
                node.Key == hit.SelectionKey) ?? hit;
            BeginTransform(dragNode, XuiTransformKind.Move, ResizeHandle.None);
        }

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
            Redraw();
            e.Handled = true;
            return;
        }

        if (_dragNodeKey is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            if (_dragNodeKey is null)
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
            _dragNodeKey = null;
            _dragNode = null;
            _dragHandle = ResizeHandle.None;
            _dragWorldDelta = default;
            _dragPositionDelta = default;
            _dragSizeDelta = default;
            _dragRotationDelta = 0;
            _dragPreviewBounds = null;
            ReleaseMouseCapture();
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
                        originalSize));
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
        Point pointer = e.GetPosition(this);
        XuiVector2 before = ControlToLogical(pointer);
        _zoom = Math.Clamp(
            _zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12),
            0.05,
            32);
        Matrix camera = CreateCamera();
        Point after = camera.Transform(new Point(before.X, before.Y));
        _pan += pointer - after;
        Redraw();
        e.Handled = true;
    }

    private void Redraw()
    {
        DrawContent();
        RedrawOverlay();
    }

    private void DrawContent()
    {
        using DrawingContext drawing = _content.RenderOpen();
        drawing.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(18, 20, 23)),
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));
        XuiRenderFrame? frame = _frame;
        if (frame is null || ActualWidth <= RulerSize || ActualHeight <= RulerSize)
        {
            DrawEmptyState(drawing);
            return;
        }

        Matrix camera = CreateCamera();
        drawing.PushTransform(new MatrixTransform(camera));
        DrawCanvasBackground(drawing, frame);
        if (ShowGrid)
        {
            DrawGrid(drawing, frame);
        }

        foreach (XuiRenderNode node in frame.Nodes)
        {
            DrawNode(drawing, node);
        }

        drawing.Pop();
        DrawRulers(drawing, frame, camera);
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
        if (_hiddenKeys.Contains(node.SelectionKey) ||
            !node.IsShown ||
            node.Opacity <= 0 ||
            node.Size.X <= 0 ||
            node.Size.Y <= 0)
        {
            return;
        }

        int pushes = 0;
        if (node.ClipBounds is XuiRect clip)
        {
            drawing.PushClip(new RectangleGeometry(ToRect(clip)));
            pushes++;
        }

        drawing.PushOpacity(node.Opacity);
        pushes++;
        drawing.PushTransform(new MatrixTransform(ToMatrix(node.WorldTransform)));
        pushes++;
        Rect bounds = new(0, 0, node.Size.X, node.Size.Y);
        Brush colorBrush = ToBrush(node.Color);

        switch (node.Kind)
        {
            case XuiRenderKind.Image:
                if (node.ImagePath.Length > 0 &&
                    _textureBitmaps.TryGetValue(node.ImagePath, out LoadedTexture? texture))
                {
                    DrawTexture(drawing, texture, bounds);
                    if (node.Color != XuiColor.White)
                    {
                        drawing.DrawRectangle(
                            new SolidColorBrush(Color.FromArgb(
                                (byte)Math.Min((int)node.Color.A, 90),
                                node.Color.R,
                                node.Color.G,
                                node.Color.B)),
                            null,
                            bounds);
                    }
                }
                else
                {
                    RequestTexture(node.ImagePath);
                }

                break;

            case XuiRenderKind.Rectangle:
                if (node.ImagePath.Length == 0)
                {
                    drawing.DrawRectangle(colorBrush, null, bounds);
                }
                else if (_textureBitmaps.TryGetValue(
                             node.ImagePath,
                             out LoadedTexture? rectangleTexture))
                {
                    DrawTexture(drawing, rectangleTexture, bounds);
                    if (node.Color != XuiColor.White)
                    {
                        drawing.DrawRectangle(
                            new SolidColorBrush(Color.FromArgb(
                                (byte)Math.Min((int)node.Color.A, 90),
                                node.Color.R,
                                node.Color.G,
                                node.Color.B)),
                            null,
                            bounds);
                    }
                }
                else
                {
                    RequestTexture(node.ImagePath);
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

        while (pushes-- > 0)
        {
            drawing.Pop();
        }
    }

    private static void DrawTexture(
        DrawingContext drawing,
        LoadedTexture texture,
        Rect destination)
    {
        XuiTextureRegion definition = texture.Resolved.Definition;
        if (definition.Primitive == XuiTexturePrimitive.TileSet &&
            texture.TileParts.Count > 0)
        {
            DrawTileSet(drawing, texture, destination);
            return;
        }

        if (definition.Primitive != XuiTexturePrimitive.RectangleWithCorner ||
            definition.CornerSize.X <= 0 ||
            definition.CornerSize.Y <= 0)
        {
            drawing.DrawImage(texture.Bitmap, destination);
            return;
        }

        double sourceCornerX = Math.Min(
            definition.CornerSize.X,
            texture.Bitmap.PixelWidth * 0.5);
        double sourceCornerY = Math.Min(
            definition.CornerSize.Y,
            texture.Bitmap.PixelHeight * 0.5);
        double destinationCornerX = Math.Min(
            sourceCornerX,
            destination.Width * 0.5);
        double destinationCornerY = Math.Min(
            sourceCornerY,
            destination.Height * 0.5);
        double[] sourceX =
        [
            0,
            sourceCornerX,
            texture.Bitmap.PixelWidth - sourceCornerX,
            texture.Bitmap.PixelWidth,
        ];
        double[] sourceY =
        [
            0,
            sourceCornerY,
            texture.Bitmap.PixelHeight - sourceCornerY,
            texture.Bitmap.PixelHeight,
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

                ImageBrush brush = new(texture.Bitmap)
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

    private static void DrawTileSet(
        DrawingContext drawing,
        LoadedTexture texture,
        Rect destination)
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
            if (corner)
            {
                drawing.DrawImage(part.Bitmap, target);
                continue;
            }

            ImageBrush brush = new(part.Bitmap)
            {
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Stretch = Stretch.Fill,
                TileMode = TileMode.Tile,
                Viewbox = new Rect(
                    0,
                    0,
                    part.Bitmap.PixelWidth,
                    part.Bitmap.PixelHeight),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(
                    target.Left,
                    target.Top,
                    part.Bitmap.PixelWidth,
                    part.Bitmap.PixelHeight),
                ViewportUnits = BrushMappingMode.Absolute,
            };
            brush.Freeze();
            drawing.DrawRectangle(brush, null, target);
        }
    }

    private static double TileColumnWidth(LoadedTexture texture, int column) =>
        texture.TileParts.Values
            .Where(part => TileCell(part.Resolved.Role).Column == column)
            .Select(static part => (double)part.Bitmap.PixelWidth)
            .DefaultIfEmpty(0)
            .Max();

    private static double TileRowHeight(LoadedTexture texture, int row) =>
        texture.TileParts.Values
            .Where(part => TileCell(part.Resolved.Role).Row == row)
            .Select(static part => (double)part.Bitmap.PixelHeight)
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
        string content = node.Uppercase
            ? node.Text.ToUpper(CultureInfo.CurrentUICulture)
            : node.Text;
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
        drawing.DrawGeometry(ToBrush(node.Color), outlinePen, geometry);
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
        foreach (XuiRenderNode node in frame.Nodes.Where(node =>
                     _selectedKeys.Contains(node.Key) &&
                     !_hiddenKeys.Contains(node.SelectionKey)))
        {
            XuiRect world = node.WorldBounds;
            if (_dragNodeKey == node.Key)
            {
                if (_dragPreviewBounds is XuiRect preview)
                {
                    world = preview;
                }
                else if (_dragKind == XuiTransformKind.Move)
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

    private void RequestTexture(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) ||
            _assetResolver is null ||
            !_requestedTextures.Add(imagePath))
        {
            return;
        }

        _ = LoadTextureAsync(imagePath);
    }

    private async Task LoadTextureAsync(string imagePath)
    {
        try
        {
            ResolvedTexture? texture = await _assetResolver!
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
                _textureBitmaps[imagePath] = new LoadedTexture(
                    bitmap,
                    texture,
                    tileParts);
                TextureDiagnosticsAvailable?.Invoke(
                    this,
                    new XuiTextureDiagnosticsEventArgs(
                        imagePath,
                        texture.Diagnostics));
                Redraw();
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
    }

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

    private XuiRenderNode? HitTest(XuiVector2 logicalPoint) =>
        _frame?.Nodes
            .Where(node =>
                !_hiddenKeys.Contains(node.SelectionKey) &&
                node.IsShown &&
                node.Opacity > 0)
            .Reverse()
            .FirstOrDefault(node =>
                node.WorldBounds.Contains(logicalPoint) &&
                (node.ClipBounds is null ||
                 node.ClipBounds.Value.Contains(logicalPoint)));

    private void BeginTransform(
        XuiRenderNode node,
        XuiTransformKind kind,
        ResizeHandle handle)
    {
        _dragNodeKey = node.Key;
        _dragNode = node;
        _dragKind = kind;
        _dragHandle = handle;
        _dragWorldDelta = default;
        _dragPositionDelta = default;
        _dragSizeDelta = default;
        _dragRotationDelta = 0;
        _dragPreviewBounds = null;
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
                XuiVector2 center = new(
                    node.WorldBounds.X + (node.WorldBounds.Width * 0.5),
                    node.WorldBounds.Y + (node.WorldBounds.Height * 0.5));
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
                _dragPreviewBounds = node.WorldBounds;
                break;
        }
    }

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
                         !_hiddenKeys.Contains(node.SelectionKey))
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

    private static double SelectRulerStep(double scale)
    {
        double[] steps = [10, 20, 50, 100, 200, 500];
        return steps.First(step => step * scale >= 45);
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

    private sealed record LoadedTilePart(
        BitmapSource Bitmap,
        ResolvedTileTexturePart Resolved);
}
