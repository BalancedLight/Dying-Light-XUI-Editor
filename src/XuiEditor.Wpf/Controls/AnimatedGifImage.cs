using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using System.Windows.Threading;

namespace XuiEditor.Wpf.Controls;

public sealed class AnimatedGifImage : Image
{
    public static readonly DependencyProperty SourceUriProperty =
        DependencyProperty.Register(
            nameof(SourceUri),
            typeof(Uri),
            typeof(AnimatedGifImage),
            new PropertyMetadata(null, SourceUri_Changed));

    private readonly DispatcherTimer _frameTimer;
    private AnimationFrame[] _frames = [];
    private int _frameIndex;

    public AnimatedGifImage()
    {
        _frameTimer = new DispatcherTimer(
            DispatcherPriority.Render,
            Dispatcher)
        {
            IsEnabled = false,
        };
        _frameTimer.Tick += FrameTimer_Tick;
        Loaded += AnimatedGifImage_Loaded;
        Unloaded += AnimatedGifImage_Unloaded;
        IsVisibleChanged += AnimatedGifImage_IsVisibleChanged;
    }

    public Uri? SourceUri
    {
        get => (Uri?)GetValue(SourceUriProperty);
        set => SetValue(SourceUriProperty, value);
    }

    private static void SourceUri_Changed(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        AnimatedGifImage image = (AnimatedGifImage)dependencyObject;
        image._frameTimer.Stop();
        image._frames = [];
        image._frameIndex = 0;
        image.Source = null;
        image.UpdatePlayback();
    }

    private void AnimatedGifImage_Loaded(
        object sender,
        RoutedEventArgs eventArgs) =>
        UpdatePlayback();

    private void AnimatedGifImage_Unloaded(
        object sender,
        RoutedEventArgs eventArgs) =>
        _frameTimer.Stop();

    private void AnimatedGifImage_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs eventArgs) =>
        UpdatePlayback();

    private void LoadFrames()
    {
        _frameTimer.Stop();
        _frames = [];
        _frameIndex = 0;
        Source = null;

        Uri? uri = SourceUri;
        if (uri is null)
        {
            return;
        }

        StreamResourceInfo? resource = Application.GetResourceStream(uri);
        if (resource is null)
        {
            return;
        }

        using Stream stream = resource.Stream;
        GifBitmapDecoder decoder = new(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        _frames = decoder.Frames
            .Select(frame => new AnimationFrame(
                frame,
                ReadFrameDelay(frame.Metadata as BitmapMetadata)))
            .ToArray();

        if (_frames.Length > 0)
        {
            ShowFrame(0);
        }
    }

    private void UpdatePlayback()
    {
        if (IsLoaded && IsVisible && _frames.Length == 0)
        {
            LoadFrames();
        }

        if (!IsLoaded || !IsVisible || _frames.Length < 2)
        {
            _frameTimer.Stop();
            return;
        }

        _frameTimer.Interval = _frames[_frameIndex].Delay;
        _frameTimer.Start();
    }

    private void FrameTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _frameIndex = (_frameIndex + 1) % _frames.Length;
        ShowFrame(_frameIndex);
    }

    private void ShowFrame(int frameIndex)
    {
        AnimationFrame frame = _frames[frameIndex];
        Source = frame.Bitmap;
        _frameTimer.Interval = frame.Delay;
    }

    private static TimeSpan ReadFrameDelay(BitmapMetadata? metadata)
    {
        const int DefaultDelayMilliseconds = 100;
        const int MinimumDelayMilliseconds = 20;

        object? value = null;
        try
        {
            value = metadata?.GetQuery("/grctlext/Delay");
        }
        catch (ArgumentException)
        {
            // A valid GIF frame can omit the graphics-control extension.
        }

        int centiseconds;
        try
        {
            centiseconds = value is null
                ? DefaultDelayMilliseconds / 10
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            centiseconds = DefaultDelayMilliseconds / 10;
        }
        catch (InvalidCastException)
        {
            centiseconds = DefaultDelayMilliseconds / 10;
        }
        catch (OverflowException)
        {
            centiseconds = DefaultDelayMilliseconds / 10;
        }

        return TimeSpan.FromMilliseconds(Math.Max(
            MinimumDelayMilliseconds,
            centiseconds * 10));
    }

    private sealed record AnimationFrame(
        BitmapSource Bitmap,
        TimeSpan Delay);
}
