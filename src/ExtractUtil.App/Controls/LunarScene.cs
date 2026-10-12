using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ExtractUtil.App.Themes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using SystemColors = System.Windows.SystemColors;
using ToolTip = System.Windows.Controls.ToolTip;

namespace ExtractUtil.App.Controls;

/// <summary>
/// Painted landscape with a moon whose illuminated fraction follows real work progress.
/// The clean sky and original painted lunar texture are separate resources: no halo,
/// transparent dark hemisphere, cloud overlay, or perpetual decorative animation.
/// </summary>
public sealed class LunarScene : FrameworkElement
{
    private const double IndeterminatePhase = .16;
    private static readonly Color NightColor = Color.FromRgb(11, 25, 30);
    private static readonly double[] Linear = Enumerable.Range(0, 256).Select(ToLinear).ToArray();
    private static readonly byte[] Gamma = Enumerable.Range(0, 8193).Select(i => ToGamma(i / 8192.0)).ToArray();

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(LunarScene),
        new FrameworkPropertyMetadata(100d, ProgressChanged, CoerceProgress));
    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(LunarScene),
        new FrameworkPropertyMetadata(false, ProgressChanged));
    public static readonly DependencyProperty IsAnimationEnabledProperty = DependencyProperty.Register(
        nameof(IsAnimationEnabled), typeof(bool), typeof(LunarScene),
        new FrameworkPropertyMetadata(true, MotionChanged));
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText), typeof(string), typeof(LunarScene),
        new FrameworkPropertyMetadata(string.Empty, InformationChanged));
    public static readonly DependencyProperty DetailsTextProperty = DependencyProperty.Register(
        nameof(DetailsText), typeof(string), typeof(LunarScene),
        new FrameworkPropertyMetadata(string.Empty, InformationChanged));

    private readonly DispatcherTimer _timer;
    private readonly ToolTip _tooltip;
    private Window? _window;
    private Artwork? _artwork;
    private string? _artworkThemeId;
    private double _phase = 1, _from = 1, _target = 1, _lastRasterPhase = double.NaN;
    private long _transitionStarted;
    private double _transitionMilliseconds = 650;
    private bool _layoutDirty = true;
    private Rect _artBounds, _moonBounds, _bitmapBounds;
    private Sample[] _samples = [];
    private byte[] _output = [];
    private WriteableBitmap? _moonBitmap;
    private LinearGradientBrush? _leftShade, _bottomShade;

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool IsIndeterminate { get => (bool)GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }
    public bool IsAnimationEnabled { get => (bool)GetValue(IsAnimationEnabledProperty); set => SetValue(IsAnimationEnabledProperty, value); }
    public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public string DetailsText { get => (string)GetValue(DetailsTextProperty); set => SetValue(DetailsTextProperty, value); }

    // Read-only diagnostics are also useful to verify that inactive skins do no animation work.
    public double RenderedProgress => _phase * 100;
    public bool IsAnimating => _timer.IsEnabled;
    public long RenderCount { get; private set; }
    public Rect MoonBounds => _moonBounds;
    public string? ArtworkThemeId => _artworkThemeId;
    private bool CanAnimate => IsAnimationEnabled && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    private bool CanDisplay => IsLoaded && IsVisible && _window?.WindowState != WindowState.Minimized;
    private double TargetPhase => IsIndeterminate ? IndeterminatePhase : Progress / 100;

    public LunarScene()
    {
        ClipToBounds = true;
        Focusable = true;
        // Focus opens the same information as hover, without changing the artwork.
        FocusVisualStyle = null;
        _tooltip = new ToolTip { PlacementTarget = this, Placement = PlacementMode.Bottom, MaxWidth = 360 };
        ToolTip = _tooltip;
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 350);
        System.Windows.Controls.ToolTipService.SetShowDuration(this, 30000);
        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
        _timer.Tick += Advance;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => SynchronizeVisibility();
        UpdateInformation();
    }

    private static object CoerceProgress(DependencyObject d, object value) =>
        value is double number && double.IsFinite(number) ? Math.Clamp(number, 0, 100) : 0d;

    private static void ProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var scene = (LunarScene)d;
        scene.UpdateInformation();
        scene.MoveToTarget();
    }

    private static void InformationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LunarScene)d).UpdateInformation();

    private static void MotionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var scene = (LunarScene)d;
        if (!scene.CanAnimate) scene.SnapToTarget();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;
        ThemeManager.Current.PropertyChanged += OnThemeChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        SnapToTarget();
        ReloadArtwork();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _tooltip.IsOpen = false;
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        ThemeManager.Current.PropertyChanged -= OnThemeChanged;
        _window = null;
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnSystemParameterChanged(sender, e))); return; }
            SnapToTarget();
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => SynchronizeVisibility();

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ThemeManager.SelectedTheme)) return;
        Dispatcher.BeginInvoke(new Action(ReloadArtwork), DispatcherPriority.Loaded);
    }

    private void ReloadArtwork()
    {
        var themeId = ThemeManager.Current.SelectedTheme.Id;
        if (_artwork is not null && _artworkThemeId == themeId) return;
        _artwork = LoadArtwork(themeId);
        _artworkThemeId = themeId;
        _layoutDirty = true;
        _lastRasterPhase = double.NaN;
        InvalidateVisual();
    }

    private void SynchronizeVisibility()
    {
        // Resume at actual state, never replay a backlog after minimization or a skin change.
        SnapToTarget();
        if (!CanDisplay) _tooltip.IsOpen = false;
    }

    private void MoveToTarget()
    {
        // Unknown progress must not animate a fabricated completion estimate.
        if (!CanDisplay || !CanAnimate || IsIndeterminate) { SnapToTarget(); return; }
        if (_timer.IsEnabled) AdvancePhase();
        _target = TargetPhase;
        _from = _phase;
        if (Math.Abs(_target - _from) < .00001) { SnapToTarget(); return; }
        // A skipped progress report must not squeeze half a lunar cycle into a tiny
        // final step. Only the painting catches up; task state/tooltip stay current.
        var angularDistance = Math.Abs(PhaseAngle(_target) - PhaseAngle(_from));
        _transitionMilliseconds = 450 + angularDistance / Math.PI * 1250;
        _transitionStarted = Stopwatch.GetTimestamp();
        _timer.Start();
    }

    private void SnapToTarget()
    {
        _timer.Stop();
        _phase = _from = _target = TargetPhase;
        // A one-shot invalidation also supports an unshown offscreen preview. WPF does
        // not paint a hidden element, and the timer remains stopped in that state.
        InvalidateVisual();
    }

    private void Advance(object? sender, EventArgs e)
    {
        if (!CanDisplay || !CanAnimate) { SnapToTarget(); return; }
        AdvancePhase();
        InvalidateVisual();
    }

    private void AdvancePhase()
    {
        var elapsed = Stopwatch.GetElapsedTime(_transitionStarted).TotalMilliseconds;
        var t = Math.Clamp(elapsed / _transitionMilliseconds, 0, 1);
        _phase = InterpolatePhase(_from, _target, t);
        if (t >= 1) { _phase = _target; _timer.Stop(); }
    }

    private static double PhaseAngle(double phase) => Math.Acos(Math.Clamp(2 * phase - 1, -1, 1));

    private static double InterpolatePhase(double from, double target, double t)
    {
        // Interpolating illuminated area makes the light direction's derivative
        // singular at full/new moon. Ease the physical angle instead, with zero
        // velocity and acceleration at both endpoints; disk radius never changes.
        t = Math.Clamp(t, 0, 1);
        var eased = t * t * t * (t * (t * 6 - 15) + 10);
        var angle = PhaseAngle(from) + (PhaseAngle(target) - PhaseAngle(from)) * eased;
        return (Math.Cos(angle) + 1) / 2;
    }

    private string InformationText
    {
        get
        {
            // A presentation owner supplies the truthful semantics (including decorative
            // idle moons and non-rounded completion). Do not prepend a second percentage.
            var progress = IsIndeterminate ? "进度待确定" : $"进度 {(Math.Floor(Progress * 10) / 10).ToString("0.#", CultureInfo.CurrentCulture)}%";
            return string.Join(Environment.NewLine, new[] { StatusText, string.IsNullOrWhiteSpace(DetailsText) ? progress : DetailsText }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }

    private void UpdateInformation()
    {
        _tooltip.Content = InformationText;
        AutomationProperties.SetName(this, "月相进度");
        AutomationProperties.SetHelpText(this, InformationText);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (CanDisplay) _tooltip.IsOpen = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _tooltip.IsOpen = false;
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _tooltip.IsOpen = false; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        if (_moonBounds.IsEmpty || _moonBounds.Width == 0) return null;
        var point = hitTestParameters.HitPoint;
        var x = (point.X - (_moonBounds.X + _moonBounds.Width / 2)) / (_moonBounds.Width / 2 + 6);
        var y = (point.Y - (_moonBounds.Y + _moonBounds.Height / 2)) / (_moonBounds.Height / 2 + 6);
        return x * x + y * y <= 1 ? new PointHitTestResult(this, point) : null;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _layoutDirty = true;
        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _layoutDirty = true;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        // An offscreen/re-attached surface may not have received Loaded/theme events.
        // Never render a new palette/layout with the previous skin's artwork.
        if (_artwork is null || _artworkThemeId != ThemeManager.Current.SelectedTheme.Id) ReloadArtwork();
        var artwork = _artwork;
        if (artwork is null) return;
        RenderCount++;
        if (SystemParameters.HighContrast)
        {
            drawingContext.DrawRectangle(SystemColors.WindowBrush, null, new Rect(RenderSize));
            return;
        }
        if (_layoutDirty) BuildGeometry();
        var energy = .72 + .28 * Math.Pow(_phase, 1.2);
        drawingContext.DrawRectangle(new SolidColorBrush(artwork.Background), null, new Rect(RenderSize));
        drawingContext.DrawImage(artwork.Plate, _artBounds);
        if (artwork.Kind is not "paper")
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * (1 - energy)), artwork.Background.R, artwork.Background.G, artwork.Background.B)), null, new Rect(RenderSize));
        if (_moonBitmap is not null)
        {
            if (_phase != _lastRasterPhase) RasterizeMoon(energy);
            drawingContext.DrawImage(_moonBitmap, _bitmapBounds);
        }
        if (artwork.Kind is not "paper")
        {
            drawingContext.DrawRectangle(_leftShade, null, new Rect(RenderSize));
            drawingContext.DrawRectangle(_bottomShade, null, new Rect(RenderSize));
        }
    }

    private void BuildGeometry()
    {
        var images = _artwork ?? throw new InvalidOperationException("Lunar artwork is not loaded.");
        _layoutDirty = false;
        var scale = Math.Max(ActualWidth / images.SourceWidth, ActualHeight / images.SourceHeight);
        var imageWidth = images.SourceWidth * scale;
        var offsetX = (ActualWidth - imageWidth) / 2;
        var radiusX = images.MoonRadiusX * scale;
        var radiusY = images.MoonRadiusY * scale;
        var centerX = images.MoonX * scale + offsetX;
        var centerY = images.MoonY * scale;
        // A compact host still keeps the moon safely inside the scene, without changing its proportions.
        if (centerX + radiusX + 32 > ActualWidth) offsetX -= centerX + radiusX + 32 - ActualWidth;
        centerX = images.MoonX * scale + offsetX;
        _artBounds = new Rect(offsetX, 0, imageWidth, images.SourceHeight * scale);
        if (images.Kind == "stone")
        {
            // Vertical scenic rail: cover its full height, but keep the moon's
            // size/location independent of window height, as in the approved design.
            centerX = ActualWidth / 2;
            centerY = 136.5;
            radiusX = radiusY = 51.5;
        }
        _moonBounds = new Rect(centerX - radiusX, centerY - radiusY, radiusX * 2, radiusY * 2);
        _tooltip.PlacementRectangle = _moonBounds;

        var dpi = VisualTreeHelper.GetDpi(this);
        // Bounded CPU raster; 384 physical pixels is ample even for the high-DPI hero.
        var rasterScale = Math.Min(dpi.DpiScaleX, 384 / Math.Max(1, radiusX * 2 + 2));
        var width = Math.Max(2, (int)Math.Ceiling(radiusX * 2 * rasterScale) + 2);
        var height = Math.Max(2, (int)Math.Ceiling(radiusY * 2 * rasterScale) + 2);
        _bitmapBounds = new Rect(centerX - width / rasterScale / 2, centerY - height / rasterScale / 2, width / rasterScale, height / rasterScale);
        _moonBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        _output = new byte[width * height * 4];
        var samples = new List<Sample>(width * height);
        var edge = .55 / Math.Min(radiusX * rasterScale, radiusY * rasterScale);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var nx = (x + .5 - width / 2.0) / (radiusX * rasterScale);
            var ny = (y + .5 - height / 2.0) / (radiusY * rasterScale);
            var rho = Math.Sqrt(nx * nx + ny * ny);
            if (rho > 1 + edge) continue;
            var sx = images.MoonX + nx * images.MoonRadiusX;
            var sy = images.MoonY + ny * images.MoonRadiusY;
            var body = SampleRgb(images.TexturePixels, images.SourceWidth, images.SourceHeight, sx, sy);
            var airX = images.Kind == "stone" ? (centerX + nx * radiusX - offsetX) / scale : sx;
            var airY = images.Kind == "stone" ? (centerY + ny * radiusY) / scale : sy;
            var air = SampleRgb(images.PlatePixels, images.SourceWidth, images.SourceHeight, airX, airY);
            samples.Add(new Sample((y * width + x) * 4, nx, ny, Math.Sqrt(Math.Max(0, 1 - rho * rho)),
                LinearValue(body.R), LinearValue(body.G), LinearValue(body.B), air.R, air.G, air.B,
                1 - Smooth(1 - edge, 1 + edge, rho)));
        }
        _samples = samples.ToArray();
        _lastRasterPhase = double.NaN;
        _leftShade = images.Kind == "stone"
            ? new LinearGradientBrush(Color.FromArgb(0, 18, 24, 27), Color.FromArgb(0, 18, 24, 27), 0)
            : new LinearGradientBrush(Color.FromArgb(165, 8, 27, 36), Color.FromArgb(0, 8, 27, 36), new Point(0, 0), new Point(.56, 0));
        _leftShade.Freeze();
        var bottomStart = Math.Min(ActualHeight * .76, _artBounds.Height - 65) / ActualHeight;
        var bottomEnd = Math.Min(ActualHeight, _artBounds.Height) / ActualHeight;
        _bottomShade = new LinearGradientBrush(Color.FromArgb(0, images.Background.R, images.Background.G, images.Background.B), images.Background, new Point(0, bottomStart), new Point(0, bottomEnd));
        _bottomShade.Freeze();
    }

    private void RasterizeMoon(double energy)
    {
        if (_moonBitmap is null) return;
        var lz = 2 * _phase - 1;
        var lateral = Math.Sqrt(Math.Max(0, 1 - lz * lz));
        var lx = lateral * Math.Cos(-12 * Math.PI / 180);
        var ly = lateral * Math.Sin(-12 * Math.PI / 180);
        var earthshine = .00012 * Math.Pow(1 - _phase, 2) * Smooth(0, .035, _phase);
        var exposure = Math.Pow(energy, 2.2);
        foreach (var sample in _samples)
        {
            // Subpixel limb samples need a finite reference normal. Normalizing by
            // that same normal reaches the original artwork continuously at full
            // moon, without crossfading an entire extra disk above 94%.
            var referenceZ = Math.Max(sample.Z, .025);
            var dot = sample.X * lx + sample.Y * ly + referenceZ * lz;
            var positive = Math.Max(0, dot);
            var gain = Math.Clamp(2 * positive / (positive + referenceZ), 0, 1) * Smooth(-.009, .017, dot);
            // The opaque unlit surface has nearly the sky's airlight. It is not a black
            // cutout; phase never changes alpha and no pixels beyond the limb emit light.
            byte Channel(double body, double air, double overlay)
            {
                var shadow = LinearValue(air * energy + overlay * (1 - energy)) + body * earthshine;
                var surface = shadow * (1 - gain) + body * gain * exposure;
                var value = Gamma[(int)Math.Round(Math.Clamp(surface, 0, 1) * 8192)];
                return (byte)Math.Round(value * sample.Coverage);
            }
            _output[sample.Offset] = Channel(sample.Blue, sample.AirBlue, 14);
            _output[sample.Offset + 1] = Channel(sample.Green, sample.AirGreen, 8);
            _output[sample.Offset + 2] = Channel(sample.Red, sample.AirRed, 0);
            _output[sample.Offset + 3] = (byte)Math.Round(sample.Coverage * 255);
        }
        _moonBitmap.WritePixels(new Int32Rect(0, 0, _moonBitmap.PixelWidth, _moonBitmap.PixelHeight), _output, _moonBitmap.PixelWidth * 4, 0);
        _lastRasterPhase = _phase;
    }

    private static double Smooth(double start, double end, double value)
    {
        var t = Math.Clamp((value - start) / (end - start), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double ToLinear(int value)
    {
        var v = value / 255.0;
        return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
    }

    private static double LinearValue(double value)
    {
        var v = Math.Clamp(value, 0, 255);
        var index = (int)v;
        return Linear[index] + (Linear[Math.Min(255, index + 1)] - Linear[index]) * (v - index);
    }

    private static byte ToGamma(double value) => (byte)Math.Round(255 * (value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - .055));

    private static (double R, double G, double B) SampleRgb(byte[] source, double width, double height, double x, double y)
    {
        x = Math.Clamp(x, 0, width - 1); y = Math.Clamp(y, 0, height - 1);
        var ix = (int)x; var iy = (int)y; var fx = x - ix; var fy = y - iy;
        double red = 0, green = 0, blue = 0;
        for (var yy = 0; yy < 2; yy++)
        for (var xx = 0; xx < 2; xx++)
        {
            var weight = (xx == 1 ? fx : 1 - fx) * (yy == 1 ? fy : 1 - fy);
            var offset = (Math.Min((int)height - 1, iy + yy) * (int)width + Math.Min((int)width - 1, ix + xx)) * 4;
            blue += source[offset] * weight; green += source[offset + 1] * weight; red += source[offset + 2] * weight;
        }
        return (red, green, blue);
    }

    private static Artwork LoadArtwork(string themeId)
    {
        static BitmapSource Load(string resource)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri($"pack://application:,,,/Zxtract;component/Resources/{resource}", UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        }
        var (kind, plateName, textureName, moonX, moonY, moonRadiusX, moonRadiusY, background) = themeId switch
        {
            "lunar-paper" => ("paper", "paper-sky.jpg", "paper-full.jpg", 1068d, 132d, 99d, 99d, Color.FromRgb(244, 240, 230)),
            // The integrated aurora painting's lunar disk is centered at roughly
            // (730, 158) in the 1440×480 source. Keeping the phase texture aligned
            // with that disk prevents the former upper-sky strip from appearing as
            // a dark cap during rendering.
            "lunar-aurora" => ("aurora", "aurora-sky.jpg", "aurora-full.jpg", 730d, 158d, 84d, 84d, Color.FromRgb(17, 18, 32)),
            "lunar-stone" => ("stone", "stone-sky.jpg", "stone-full.jpg", 433.5d, 159.8d, 74d, 74d, Color.FromRgb(18, 24, 27)),
            _ => ("night", "night-clear-plate.png", "night-moon-source.png", 1628d, 244d, 86.5d, 85d, NightColor)
        };
        var plate = Load(plateName);
        var texture = Load(textureName);
        if (plate.PixelWidth != texture.PixelWidth || plate.PixelHeight != texture.PixelHeight)
            throw new InvalidOperationException("Lunar skin artwork dimensions do not match.");
        var sourceWidth = plate.PixelWidth;
        var sourceHeight = plate.PixelHeight;
        var platePixels = new byte[sourceWidth * sourceHeight * 4];
        var texturePixels = new byte[platePixels.Length];
        plate.CopyPixels(platePixels, sourceWidth * 4, 0);
        texture.CopyPixels(texturePixels, sourceWidth * 4, 0);
        return new Artwork(kind, plate, platePixels, texturePixels, sourceWidth, sourceHeight, moonX, moonY, moonRadiusX, moonRadiusY, background);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LunarSceneAutomationPeer(this);

    private sealed class LunarSceneAutomationPeer(LunarScene owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => nameof(LunarScene);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
        protected override string GetHelpTextCore() => owner.InformationText;
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue && !owner.IsIndeterminate ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => true;
        public double LargeChange => double.NaN;
        public double SmallChange => double.NaN;
        public double Maximum => 100;
        public double Minimum => 0;
        public double Value => owner.Progress;
        public void SetValue(double value) => throw new InvalidOperationException("The lunar progress indicator is read-only.");
    }

    private sealed record Artwork(string Kind, BitmapSource Plate, byte[] PlatePixels, byte[] TexturePixels,
        double SourceWidth, double SourceHeight, double MoonX, double MoonY, double MoonRadiusX, double MoonRadiusY, Color Background);
    private readonly record struct Sample(int Offset, double X, double Y, double Z, double Red, double Green, double Blue,
        double AirRed, double AirGreen, double AirBlue, double Coverage);
}
