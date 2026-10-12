using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using SystemColors = System.Windows.SystemColors;

namespace ExtractUtil.App.Controls;

/// <summary>A small activity cue, never a completion estimate. Only active, visible rows animate.</summary>
public sealed class TaskActivityIndicator : FrameworkElement
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(TaskActivityIndicator), new PropertyMetadata(false, ActivityChanged));
    public static readonly DependencyProperty IsAnimationEnabledProperty = DependencyProperty.Register(
        nameof(IsAnimationEnabled), typeof(bool), typeof(TaskActivityIndicator), new PropertyMetadata(true, ActivityChanged));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(TaskActivityIndicator),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Geometry Arc = CreateArc();
    private readonly DispatcherTimer _timer;
    private readonly RotateTransform _rotation = new(0, 7, 7);
    private Window? _window;

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public bool IsAnimationEnabled { get => (bool)GetValue(IsAnimationEnabledProperty); set => SetValue(IsAnimationEnabledProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public bool IsAnimating => _timer.IsEnabled;
    public long RenderCount { get; private set; }

    public TaskActivityIndicator()
    {
        Width = Height = 14;
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(1000d / 24) };
        _timer.Tick += (_, _) =>
        {
            _rotation.Angle = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency / 1.4 * 360 % 360;
            InvalidateVisual();
        };
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window is not null) _window.StateChanged += OnWindowStateChanged;
            SystemParameters.StaticPropertyChanged += OnSystemChanged;
            UpdateActivity();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
            _window = null;
            SystemParameters.StaticPropertyChanged -= OnSystemChanged;
        };
        IsVisibleChanged += (_, _) => UpdateActivity();
    }

    private static Geometry CreateArc()
    {
        var geometry = Geometry.Parse("M7,1 A6,6 0 1 1 1,7");
        geometry.Freeze();
        return geometry;
    }

    private static void ActivityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((TaskActivityIndicator)sender).UpdateActivity();
    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateActivity();
    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))) return;
        if (Dispatcher.CheckAccess()) UpdateActivity();
        else Dispatcher.BeginInvoke(new Action(UpdateActivity));
    }

    private void UpdateActivity()
    {
        if (IsActive && IsAnimationEnabled && IsLoaded && IsVisible && _window?.WindowState != WindowState.Minimized &&
            SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            _timer.Start();
        else
        {
            _timer.Stop();
            _rotation.Angle = 0;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        RenderCount++;
        var pen = new Pen(SystemParameters.HighContrast ? SystemColors.HighlightBrush : Stroke, 1.4)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        context.PushTransform(_rotation);
        context.DrawGeometry(null, pen, Arc);
        context.Pop();
    }
}
