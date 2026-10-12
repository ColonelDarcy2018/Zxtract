using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace ExtractUtil.App.Themes;

public sealed record ThemeDefinition(string Id, string Name, string ResourcePath, bool IsDark, bool HasLunarScene = false);

// The presentation layer owns themes. No extraction services or business view models participate.
public sealed class ThemeManager : INotifyPropertyChanged
{
    private static ThemeManager? _current;
    private readonly ResourceDictionary _resources;
    private readonly AppearanceSettingsStore _settings;
    private readonly Dictionary<string, ResourceDictionary> _palettes = new();
    private readonly object[] _requiredKeys;
    private ResourceDictionary? _palette;
    private ThemeDefinition _selectedTheme;
    private string _statusText = "立即切换皮肤，不影响正在进行的任务";

    public static ThemeManager Current => _current ??= new ThemeManager(
        System.Windows.Application.Current.Resources, new AppearanceSettingsStore());

    public static ThemeManager Initialize(ResourceDictionary resources, AppearanceSettingsStore settings) =>
        _current ??= new ThemeManager(resources, settings);

    public void ApplyWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = SelectedTheme.IsDark && !SystemParameters.HighContrast ? 1 : 0;
        // Unsupported Windows versions simply keep the system title bar.
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    public IReadOnlyList<ThemeDefinition> AvailableThemes { get; } = new[]
    {
        new ThemeDefinition("ink-flow", "墨流 · 深色", "Themes/InkFlow.xaml", IsDark: true),
        new ThemeDefinition("clear-light", "清简 · 浅色", "Themes/ClearLight.xaml", IsDark: false),
        new ThemeDefinition("night-voyage", "夜航 · 山水月相", "Themes/NightVoyage.xaml", IsDark: true, HasLunarScene: true),
        new ThemeDefinition("lunar-paper", "月白 · 纸上月痕", "Themes/LunarPaper.xaml", IsDark: false, HasLunarScene: true),
        new ThemeDefinition("lunar-aurora", "流萤 · 极光抱月", "Themes/LunarAurora.xaml", IsDark: true, HasLunarScene: true),
        new ThemeDefinition("lunar-stone", "玄石 · 月下岩境", "Themes/LunarStone.xaml", IsDark: true, HasLunarScene: true)
    };

    public ThemeManager(ResourceDictionary resources, AppearanceSettingsStore settings)
    {
        _resources = resources;
        _settings = settings;
        _requiredKeys = LoadPalette(AvailableThemes[0]).Keys.Cast<object>().ToArray();
        var savedId = settings.LoadThemeId();
        _selectedTheme = AvailableThemes.FirstOrDefault(theme => theme.Id == savedId) ?? AvailableThemes[0];
        Apply(_selectedTheme, persist: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string StatusText => _statusText;

    public ThemeDefinition SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || value == _selectedTheme || !AvailableThemes.Contains(value)) return;
            Apply(value, persist: true);
            _selectedTheme = value;
            if (ReferenceEquals(_resources, System.Windows.Application.Current.Resources))
                foreach (Window window in System.Windows.Application.Current.Windows) ApplyWindowChrome(window);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTheme)));
        }
    }

    private void Apply(ThemeDefinition theme, bool persist)
    {
        System.Windows.Application.Current.Dispatcher.VerifyAccess();
        // Load and validate before changing the active palette. Common templates stay alive.
        var next = LoadPalette(theme);
        if (_requiredKeys.Any(key => !next.Contains(key)))
            throw new InvalidOperationException("The skin does not implement the full theme resource contract.");

        if (_palette is null) _resources.MergedDictionaries.Add(next);
        else _resources.MergedDictionaries[_resources.MergedDictionaries.IndexOf(_palette)] = next;
        _palette = next;
        _statusText = persist && !_settings.TrySave(theme.Id)
            ? "皮肤已切换，但无法保存偏好；下次启动可能恢复默认皮肤"
            : "立即切换皮肤，不影响正在进行的任务";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
    }

    private ResourceDictionary LoadPalette(ThemeDefinition theme)
    {
        if (_palettes.TryGetValue(theme.Id, out var cached)) return cached;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/Zxtract;component/{theme.ResourcePath}", UriKind.Relative)
        };
        _palettes.Add(theme.Id, palette);
        return palette;
    }
}
