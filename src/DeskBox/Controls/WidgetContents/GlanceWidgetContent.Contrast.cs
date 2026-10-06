using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class GlanceWidgetContent
{
    private readonly GlanceTextSampleService _textSamples = new();
    private readonly DispatcherTimer _contrastTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _contrastAccessibility = new();
    private CancellationTokenSource? _contrastCts;
    private bool _contrastSubscribed;
    private bool _highContrastSubscribed;
    private readonly HashSet<TextBlock> _contrastTexts = [];
    private string? _lastContrastLogPath;
    private void StartTextContrast()
    {
        if (!_contrastSubscribed)
        {
            _contrastSubscribed = true;
            _contrastTimer.Tick += ContrastTick;
            try { _contrastAccessibility.HighContrastChanged += HighContrastChanged; _highContrastSubscribed = true; }
            catch (System.Runtime.InteropServices.COMException) { }
        }
        QueueTextContrast();
    }
    private void StopTextContrast()
    {
        _contrastTimer.Stop(); _contrastCts?.Cancel(); _contrastCts = null;
        if (!_contrastSubscribed) return;
        _contrastSubscribed = false;
        _contrastTimer.Tick -= ContrastTick;
        if (_highContrastSubscribed)
        {
            try { _contrastAccessibility.HighContrastChanged -= HighContrastChanged; }
            catch (System.Runtime.InteropServices.COMException) { }
            _highContrastSubscribed = false;
        }
        foreach (var text in _contrastTexts) text.SizeChanged -= ContrastTextSizeChanged;
        _contrastTexts.Clear();
    }
    private void ContrastTextSizeChanged(object sender, SizeChangedEventArgs args) => QueueTextContrast();
    private void HighContrastChanged(Windows.UI.ViewManagement.AccessibilitySettings sender, object args) => DispatcherQueue.TryEnqueue(QueueTextContrast);
    private void QueueTextContrast()
    {
        if (!_isLoaded) return;
        _contrastCts?.Cancel();
        _contrastTimer.Stop(); _contrastTimer.Start();
    }
    private async void ContrastTick(object? sender, object args)
    {
        _contrastTimer.Stop();
        var cancellation = new CancellationTokenSource(); _contrastCts = cancellation;
        try
        {
            RootGrid.UpdateLayout();
            var texts = PhotoTextBlocks(ImageForegroundThemeScope).ToArray();
            foreach (var text in texts) if (_contrastTexts.Add(text)) text.SizeChanged += ContrastTextSizeChanged;
            TextContrastScrims.Children.Clear();
            bool highContrast = _contrastAccessibility.HighContrast;
            var systemForeground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            foreach (var text in texts) text.Foreground = systemForeground;
            if (highContrast)
            {
                var systemColors = new Windows.UI.ViewManagement.UISettings();
                foreach (var text in texts)
                {
                    text.Foreground = new SolidColorBrush(systemColors.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground));
                    var bounds = text.TransformToVisual(RootGrid).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
                    var backing = new Border { Background = new SolidColorBrush(systemColors.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)),
                        Width = bounds.Width + 8, Height = bounds.Height + 4 };
                    Canvas.SetLeft(backing, bounds.X - 4); Canvas.SetTop(backing, bounds.Y - 2); TextContrastScrims.Children.Add(backing);
                }
                return;
            }
            if (!_viewModel.HasVisibleCurrentImage) return;
            string? path = _viewModel.CurrentImagePath;
            var sample = await _textSamples.ReadAsync(path, cancellation.Token);
            if (!_isLoaded || cancellation.IsCancellationRequested || path != _viewModel.CurrentImagePath) return;
            double width = RootGrid.ActualWidth, height = RootGrid.ActualHeight;
            if (width <= 0 || height <= 0) return;
            bool fit = _viewModel.ImageFit == DeskBox.Models.GlanceImageFitMode.Fit;
            double ax = _viewModel.ImageFocus == DeskBox.Models.GlanceImageFocus.Left ? 0 : _viewModel.ImageFocus == DeskBox.Models.GlanceImageFocus.Right ? 1 : .5;
            double ay = _viewModel.ImageFocus == DeskBox.Models.GlanceImageFocus.Top ? 0 : _viewModel.ImageFocus == DeskBox.Models.GlanceImageFocus.Bottom ? 1 : .5;
            var surface = RootGrid.ActualTheme == ElementTheme.Dark ? new GlanceTextContrastPolicy.Rgb(32, 32, 32) : new GlanceTextContrastPolicy.Rgb(243, 243, 243);
            foreach (var text in texts)
            {
                var bounds = text.TransformToVisual(RootGrid).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                var colors = new List<GlanceTextContrastPolicy.Rgb>();
                for (double y = bounds.Top; y <= bounds.Bottom; y += Math.Max(1, bounds.Height / 8))
                for (double x = bounds.Left; x <= bounds.Right; x += Math.Max(1, bounds.Width / 16))
                {
                    var point = GlanceTextContrastPolicy.ImagePoint(x, y, width, height, sample?.ImageWidth ?? width, sample?.ImageHeight ?? height, fit, ax, ay);
                    var color = surface;
                    if (sample is not null && point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1)
                    {
                        int offset = (Math.Min(sample.Height - 1, (int)(point.Y * sample.Height)) * sample.Width + Math.Min(sample.Width - 1, (int)(point.X * sample.Width))) * 4;
                        color = GlanceTextContrastPolicy.Blend(new(sample.Pixels[offset + 2], sample.Pixels[offset + 1], sample.Pixels[offset]), surface,
                            _viewModel.BackgroundImageOpacity * sample.Pixels[offset + 3] / 255d);
                    }
                    // Existing readability layer lies behind the text as well.
                    color = GlanceTextContrastPolicy.Blend(_viewModel.IsCalendarLayout ? surface : new(0, 0, 0), color, _viewModel.ReadabilityOpacity);
                    double gradient = _viewModel.IsCalendarLayout
                        ? .72 * (112 / 255d) * Math.Clamp(1 - y / height / .38, 0, 1)
                        : .34 * (120 / 255d) * Math.Clamp((y / height - .34) / .66, 0, 1);
                    color = GlanceTextContrastPolicy.Blend(new(0, 0, 0), color, gradient);
                    colors.Add(color);
                }
                var result = GlanceTextContrastPolicy.Resolve(colors);
                text.Foreground = new SolidColorBrush(Color.FromArgb(255, result.Foreground.R, result.Foreground.G, result.Foreground.B));
                if (sample is null || _viewModel.BackgroundImageOpacity < .999)
                {
                    // The native material behind a translucent photo is not represented by image pixels.
                    result = new(new(255, 255, 255), .6);
                    text.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                }
                if (result.BlackScrim <= 0) continue;
                var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)), Opacity = result.BlackScrim,
                    Width = bounds.Width + 8, Height = bounds.Height + 4, CornerRadius = new CornerRadius(4) };
                Canvas.SetLeft(scrim, bounds.X - 4); Canvas.SetTop(scrim, bounds.Y - 2); TextContrastScrims.Children.Add(scrim);
            }
            if (_lastContrastLogPath != path)
            {
                _lastContrastLogPath = path;
                App.Log($"[GlanceTextContrast] Applied sampled={sample is not null} textRegions={texts.Length} scrims={TextContrastScrims.Children.Count}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.LogVerbose($"[GlanceTextContrast] Update failed: {ex.GetType().Name}"); }
        finally { if (ReferenceEquals(_contrastCts, cancellation)) _contrastCts = null; cancellation.Dispose(); }
    }
    private IEnumerable<TextBlock> PhotoTextBlocks(DependencyObject root)
    {
        if (ReferenceEquals(root, CalendarGlassSurface) || root is FrameworkElement { Visibility: Visibility.Collapsed }) yield break;
        if (root is TextBlock text && text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path is "TimeText" or "DateText" or "CenteredDateText" or "WeekdayText") yield return text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in PhotoTextBlocks(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
