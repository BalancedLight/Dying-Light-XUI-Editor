using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using XuiEditor.Core.Animation;

namespace XuiEditor.Wpf.Controls;

public sealed class TimelineTickChangedEventArgs : EventArgs
{
    public TimelineTickChangedEventArgs(int tick) => Tick = tick;

    public int Tick { get; }
}

public sealed class TimelineKeyFrameMoveRequestedEventArgs : EventArgs
{
    public TimelineKeyFrameMoveRequestedEventArgs(
        string keyFrameNodeKey,
        int oldTick,
        int newTick)
    {
        KeyFrameNodeKey = keyFrameNodeKey;
        OldTick = oldTick;
        NewTick = newTick;
    }

    public string KeyFrameNodeKey { get; }

    public int OldTick { get; }

    public int NewTick { get; }
}

public sealed class TimelineEditorControl : FrameworkElement
{
    private const double HeaderHeight = 32;
    private const double LabelWidth = 210;
    private const double RowHeight = 26;
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _drawing = new();
    private readonly List<TrackItem> _tracks = [];
    private XuiTimelineSet? _timelineSet;
    private int _tick;
    private double _pixelsPerTick = 3;
    private double _horizontalTick;
    private double _verticalOffset;
    private TrackKey? _selectedKey;
    private TrackKey? _dragKey;
    private int _dragTick;

    public TimelineEditorControl()
    {
        Focusable = true;
        ClipToBounds = true;
        _visuals = new VisualCollection(this)
        {
            _drawing,
        };
        SizeChanged += (_, _) => Redraw();
    }

    public event EventHandler<TimelineTickChangedEventArgs>? TickChanged;

    public event EventHandler<TimelineKeyFrameMoveRequestedEventArgs>?
        KeyFrameMoveRequested;

    public event EventHandler? SelectedKeyFrameChanged;

    public XuiKeyFrame? SelectedKeyFrame => _selectedKey?.Frame;

    public XuiTrack? SelectedTrack => _selectedKey?.Track;

    public XuiTimeline? SelectedTimeline => _selectedKey?.Timeline;

    internal int VisibleTrackCountForTesting => _tracks.Count;

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    public void SetData(
        XuiTimelineSet? timelineSet,
        IEnumerable<string> selectedIds,
        int tick)
    {
        ArgumentNullException.ThrowIfNull(selectedIds);
        string? selectedSyntaxKey = _selectedKey?.Frame.Syntax.Key;
        _timelineSet = timelineSet;
        _tick = Math.Max(0, tick);
        HashSet<string> targets = new(selectedIds, StringComparer.Ordinal);
        _tracks.Clear();
        if (timelineSet is not null)
        {
            IEnumerable<XuiTimeline> timelines = targets.Count == 0
                ? timelineSet.Timelines
                : timelineSet.Timelines.Where(timeline =>
                    targets.Contains(timeline.TargetId));
            foreach (XuiTimeline timeline in timelines)
            {
                foreach (XuiTrack track in timeline.Tracks)
                {
                    _tracks.Add(new TrackItem(timeline, track));
                }
            }
        }

        _selectedKey = selectedSyntaxKey is null
            ? null
            : FindKeys().FirstOrDefault(candidate =>
                candidate.Frame.Syntax.Key == selectedSyntaxKey);
        _verticalOffset = Math.Clamp(
            _verticalOffset,
            0,
            Math.Max(0, (_tracks.Count * RowHeight) - ActualHeight + HeaderHeight));
        Redraw();
    }

    public void SetTick(int tick)
    {
        _tick = Math.Max(0, tick);
        EnsureTickVisible(_tick);
        Redraw();
    }

    public void SelectKeyFrame(string? key)
    {
        _selectedKey = string.IsNullOrEmpty(key)
            ? null
            : FindKeys().FirstOrDefault(candidate =>
                candidate.Frame.Syntax.Key == key);
        SelectedKeyFrameChanged?.Invoke(this, EventArgs.Empty);
        Redraw();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Point point = e.GetPosition(this);
        TrackKey? key = HitTestKey(point);
        if (key is not null)
        {
            bool changed = _selectedKey?.Frame.Syntax.Key != key.Frame.Syntax.Key ||
                           _selectedKey.Track != key.Track;
            _selectedKey = key;
            _dragKey = key;
            _dragTick = key.Frame.Tick;
            CaptureMouse();
            if (changed)
            {
                SelectedKeyFrameChanged?.Invoke(this, EventArgs.Empty);
            }

            Redraw();
            e.Handled = true;
            return;
        }

        if (point.X >= LabelWidth)
        {
            if (_selectedKey is not null)
            {
                _selectedKey = null;
                SelectedKeyFrameChanged?.Invoke(this, EventArgs.Empty);
            }

            int tick = PointToTick(point.X);
            _tick = tick;
            TickChanged?.Invoke(this, new TimelineTickChangedEventArgs(tick));
            Redraw();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragKey is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        _dragTick = PointToTick(e.GetPosition(this).X);
        Redraw();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton != MouseButton.Left || _dragKey is null)
        {
            return;
        }

        TrackKey key = _dragKey;
        int newTick = _dragTick;
        _dragKey = null;
        ReleaseMouseCapture();
        if (newTick != key.Frame.Tick)
        {
            KeyFrameMoveRequested?.Invoke(
                this,
                new TimelineKeyFrameMoveRequestedEventArgs(
                    key.Frame.Syntax.Key,
                    key.Frame.Tick,
                    newTick));
        }

        Redraw();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Point point = e.GetPosition(this);
            int anchorTick = PointToTick(point.X);
            _pixelsPerTick = Math.Clamp(
                _pixelsPerTick * (e.Delta > 0 ? 1.2 : 1 / 1.2),
                0.25,
                24);
            _horizontalTick = Math.Max(
                0,
                anchorTick - ((point.X - LabelWidth) / _pixelsPerTick));
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _horizontalTick = Math.Max(
                0,
                _horizontalTick - (e.Delta / _pixelsPerTick));
        }
        else
        {
            _verticalOffset = Math.Clamp(
                _verticalOffset - (e.Delta * 0.25),
                0,
                Math.Max(
                    0,
                    (_tracks.Count * RowHeight) -
                    Math.Max(0, ActualHeight - HeaderHeight)));
        }

        Redraw();
        e.Handled = true;
    }

    private void Redraw()
    {
        using DrawingContext drawing = _drawing.RenderOpen();
        drawing.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(24, 27, 30)),
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));
        DrawNamedFrameRanges(drawing);
        DrawHeader(drawing);
        if (_timelineSet is null)
        {
            DrawEmpty(drawing, "No timeline data");
            return;
        }

        if (_tracks.Count == 0)
        {
            DrawEmpty(drawing, "The current selection has no animated tracks");
        }

        DrawRows(drawing);
        DrawPlayhead(drawing);
    }

    private void DrawHeader(DrawingContext drawing)
    {
        drawing.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(37, 41, 46)),
            new Pen(new SolidColorBrush(Color.FromRgb(52, 58, 64)), 1),
            new Rect(0, 0, ActualWidth, HeaderHeight));
        DrawText(
            drawing,
            "Target / property",
            new Point(10, 8),
            11,
            Color.FromRgb(200, 204, 210));
        drawing.PushClip(new RectangleGeometry(
            new Rect(LabelWidth, 0, Math.Max(0, ActualWidth - LabelWidth), HeaderHeight)));
        double major = SelectMajorTick();
        int first = (int)(Math.Floor(_horizontalTick / major) * major);
        int maximum = PointToTick(ActualWidth) + (int)major;
        for (double tick = first; tick <= maximum; tick += major)
        {
            double x = TickToPoint((int)tick);
            drawing.DrawLine(
                new Pen(new SolidColorBrush(Color.FromRgb(116, 122, 130)), 1),
                new Point(x, HeaderHeight - 8),
                new Point(x, HeaderHeight));
            DrawText(
                drawing,
                tick.ToString("0", CultureInfo.InvariantCulture),
                new Point(x + 3, 5),
                10,
                Color.FromRgb(154, 161, 170));
        }

        if (_timelineSet is not null)
        {
            foreach (XuiNamedFrame frame in _timelineSet.NamedFrames)
            {
                double x = TickToPoint(frame.Tick);
                if (x < LabelWidth || x > ActualWidth)
                {
                    continue;
                }

                drawing.DrawLine(
                    new Pen(new SolidColorBrush(Color.FromArgb(170, 90, 170, 235)), 1),
                    new Point(x, 0),
                    new Point(x, HeaderHeight));
                DrawText(
                    drawing,
                    frame.Name,
                    new Point(x + 3, 17),
                    9,
                    Color.FromRgb(164, 199, 238));
            }
        }

        drawing.Pop();
    }

    private void DrawNamedFrameRanges(DrawingContext drawing)
    {
        if (_timelineSet is null ||
            _timelineSet.NamedFrames.Count == 0 ||
            ActualHeight <= HeaderHeight)
        {
            return;
        }

        XuiNamedFrame[] frames = _timelineSet.NamedFrames
            .OrderBy(static frame => frame.Tick)
            .ThenBy(static frame => frame.Name, StringComparer.Ordinal)
            .ToArray();
        drawing.PushClip(new RectangleGeometry(new Rect(
            LabelWidth,
            HeaderHeight,
            Math.Max(0, ActualWidth - LabelWidth),
            Math.Max(0, ActualHeight - HeaderHeight))));
        for (int index = 0; index < frames.Length; index++)
        {
            int startTick = frames[index].Tick;
            int endTick = index + 1 < frames.Length
                ? frames[index + 1].Tick
                : Math.Max(
                    startTick + 1,
                    _timelineSet.MaximumTick + 1);
            if (endTick <= startTick)
            {
                continue;
            }

            double left = Math.Max(LabelWidth, TickToPoint(startTick));
            double right = Math.Min(ActualWidth, TickToPoint(endTick));
            if (right <= left)
            {
                continue;
            }

            Color color = index % 2 == 0
                ? Color.FromArgb(16, 77, 134, 189)
                : Color.FromArgb(10, 122, 84, 150);
            drawing.DrawRectangle(
                new SolidColorBrush(color),
                null,
                new Rect(
                    left,
                    HeaderHeight,
                    right - left,
                    ActualHeight - HeaderHeight));
        }

        drawing.Pop();
    }

    private void DrawRows(DrawingContext drawing)
    {
        drawing.PushClip(new RectangleGeometry(
            new Rect(0, HeaderHeight, ActualWidth, Math.Max(0, ActualHeight - HeaderHeight))));
        for (int index = 0; index < _tracks.Count; index++)
        {
            double y = HeaderHeight + (index * RowHeight) - _verticalOffset;
            if (y + RowHeight < HeaderHeight || y > ActualHeight)
            {
                continue;
            }

            TrackItem item = _tracks[index];
            Brush rowBrush = new SolidColorBrush(
                index % 2 == 0
                    ? Color.FromRgb(29, 32, 36)
                    : Color.FromRgb(26, 29, 33));
            drawing.DrawRectangle(
                rowBrush,
                null,
                new Rect(0, y, ActualWidth, RowHeight));
            drawing.DrawLine(
                new Pen(new SolidColorBrush(Color.FromRgb(48, 53, 59)), 1),
                new Point(0, y + RowHeight),
                new Point(ActualWidth, y + RowHeight));
            DrawText(
                drawing,
                $"{item.Timeline.TargetId}  ·  {item.Track.Property}",
                new Point(10, y + 5),
                11,
                Color.FromRgb(220, 223, 227));

            foreach (XuiKeyFrame frame in item.Track.KeyFrames)
            {
                int drawnTick = _dragKey?.Frame == frame ? _dragTick : frame.Tick;
                double x = TickToPoint(drawnTick);
                if (x < LabelWidth - 8 || x > ActualWidth + 8)
                {
                    continue;
                }

                bool selected = _selectedKey?.Frame.Syntax.Key == frame.Syntax.Key &&
                                _selectedKey.Track == item.Track;
                DrawKey(
                    drawing,
                    new Point(x, y + (RowHeight * 0.5)),
                    selected);
            }
        }

        drawing.Pop();
    }

    private void DrawPlayhead(DrawingContext drawing)
    {
        double x = TickToPoint(_tick);
        if (x < LabelWidth || x > ActualWidth)
        {
            return;
        }

        Pen playhead = new(
            new SolidColorBrush(Color.FromRgb(242, 140, 40)),
            1.5);
        drawing.DrawLine(
            playhead,
            new Point(x, 0),
            new Point(x, ActualHeight));
        StreamGeometry triangle = new();
        using (StreamGeometryContext geometry = triangle.Open())
        {
            geometry.BeginFigure(new Point(x - 5, 0), true, true);
            geometry.LineTo(new Point(x + 5, 0), true, false);
            geometry.LineTo(new Point(x, 8), true, false);
        }

        triangle.Freeze();
        drawing.DrawGeometry(
            new SolidColorBrush(Color.FromRgb(242, 140, 40)),
            null,
            triangle);
    }

    private void DrawEmpty(DrawingContext drawing, string message)
    {
        DrawText(
            drawing,
            message,
            new Point(18, HeaderHeight + 18),
            12,
            Color.FromRgb(154, 161, 170));
    }

    private static void DrawKey(
        DrawingContext drawing,
        Point center,
        bool selected)
    {
        double radius = selected ? 6 : 5;
        StreamGeometry diamond = new();
        using (StreamGeometryContext geometry = diamond.Open())
        {
            geometry.BeginFigure(
                new Point(center.X, center.Y - radius),
                true,
                true);
            geometry.LineTo(
                new Point(center.X + radius, center.Y),
                true,
                false);
            geometry.LineTo(
                new Point(center.X, center.Y + radius),
                true,
                false);
            geometry.LineTo(
                new Point(center.X - radius, center.Y),
                true,
                false);
        }

        diamond.Freeze();
        drawing.DrawGeometry(
            new SolidColorBrush(
                selected
                    ? Color.FromRgb(242, 140, 40)
                    : Color.FromRgb(198, 203, 210)),
            new Pen(
                new SolidColorBrush(Color.FromRgb(20, 22, 24)),
                1),
            diamond);
    }

    private TrackKey? HitTestKey(Point point)
    {
        if (point.Y < HeaderHeight || point.X < LabelWidth)
        {
            return null;
        }

        int row = (int)Math.Floor(
            (point.Y - HeaderHeight + _verticalOffset) / RowHeight);
        if (row < 0 || row >= _tracks.Count)
        {
            return null;
        }

        TrackItem item = _tracks[row];
        XuiKeyFrame? closest = item.Track.KeyFrames
            .OrderBy(frame => Math.Abs(TickToPoint(frame.Tick) - point.X))
            .FirstOrDefault();
        if (closest is null ||
            Math.Abs(TickToPoint(closest.Tick) - point.X) > 9)
        {
            return null;
        }

        return new TrackKey(item.Timeline, item.Track, closest);
    }

    private IEnumerable<TrackKey> FindKeys() =>
        _tracks.SelectMany(item =>
            item.Track.KeyFrames.Select(frame =>
                new TrackKey(item.Timeline, item.Track, frame)));

    private double TickToPoint(int tick) =>
        LabelWidth + ((tick - _horizontalTick) * _pixelsPerTick);

    private int PointToTick(double x) =>
        Math.Max(
            0,
            (int)Math.Round(
                _horizontalTick +
                ((Math.Max(LabelWidth, x) - LabelWidth) / _pixelsPerTick)));

    private double SelectMajorTick()
    {
        double[] steps = [1, 2, 5, 10, 20, 30, 60, 120, 300, 600];
        return steps.First(step => step * _pixelsPerTick >= 55);
    }

    private void EnsureTickVisible(int tick)
    {
        int left = (int)_horizontalTick;
        int right = PointToTick(ActualWidth);
        if (tick < left)
        {
            _horizontalTick = tick;
        }
        else if (tick > right)
        {
            _horizontalTick = Math.Max(
                0,
                tick - ((ActualWidth - LabelWidth) / _pixelsPerTick) + 8);
        }
    }

    private void DrawText(
        DrawingContext drawing,
        string value,
        Point origin,
        double size,
        Color color)
    {
        FormattedText text = new(
            value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, LabelWidth - origin.X - 6),
            Trimming = TextTrimming.CharacterEllipsis,
        };
        drawing.DrawText(text, origin);
    }

    private sealed record TrackItem(
        XuiTimeline Timeline,
        XuiTrack Track);

    private sealed record TrackKey(
        XuiTimeline Timeline,
        XuiTrack Track,
        XuiKeyFrame Frame);
}
