using System.Windows;
using System.Windows.Controls;
using ExtractUtil.App.ViewModels;
using ExtractUtil.App.Themes;
using ExtractUtil.App.Presentation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Reflection;
using Button = System.Windows.Controls.Button;

namespace ExtractUtil.App;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty LunarStatusProperty = DependencyProperty.Register(
        nameof(LunarStatus), typeof(LunarStatusPresenter), typeof(MainWindow));

    public LunarStatusPresenter? LunarStatus
    {
        get => (LunarStatusPresenter?)GetValue(LunarStatusProperty);
        private set => SetValue(LunarStatusProperty, value);
    }

    public MainWindow()
    {
        // Load presentation preferences before creating controls; extraction state stays separate.
        _ = ThemeManager.Current;
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.Current.ApplyWindowChrome(this);
        DataContextChanged += (_, _) =>
        {
            LunarStatus?.Dispose();
            LunarStatus = DataContext is MainViewModel model ? new LunarStatusPresenter(model) : null;
        };
        Closed += (_, _) => { LunarStatus?.Dispose(); LunarStatus = null; };
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"版本 {version?.Major}.{version?.Minor}.{version?.Build} · Windows 桌面版";
        DataContext = new MainViewModel();
        NightOptions.CloseRequested += (_, _) => CloseNightOptions();
        NightOptions.PasswordsRequested += (_, _) =>
        {
            CloseNightOptions();
            if (DataContext is MainViewModel model) model.IsPasswordPanelOpen = true;
        };
        ThemeManager.Current.PropertyChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Current.PropertyChanged -= OnThemeChanged;
        ApplyComposition();
    }

    private void OnThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ThemeManager.SelectedTheme)) return;
        // Changing a skin is a committed choice; dismiss the picker immediately
        // so the newly composed surface is visible without a second click.
        SettingsToggle.IsChecked = false;
        CloseNightOptions();
        if (OptionsToggle is not null) OptionsToggle.IsChecked = false;
        if (DataContext is MainViewModel model) model.IsPasswordPanelOpen = false;
        ApplyComposition();
    }

    private void ApplyComposition()
    {
        var theme = ThemeManager.Current.SelectedTheme;
        var stone = theme.Id == "lunar-stone";
        // Recompose the SAME controls. Commands, selection, virtualization, progress,
        // context menus and keyboard bindings remain intact across layout changes.
        foreach (UIElement child in WorkspaceRoot.Children) Grid.SetColumn(child, 1);
        ScenicColumn.Width = new GridLength(stone ? 296 : 0);
        Grid.SetColumn(NightHero, stone ? 0 : 1);
        Grid.SetRowSpan(NightHero, stone ? 6 : 1);
        WorkspaceRoot.RowDefinitions[0].Height = stone ? new GridLength(0) :
            theme.HasLunarScene ? (GridLength)FindResource("LunarHeroHeight") : new GridLength(120);
        WorkspaceRoot.RowDefinitions[3].Height = new GridLength(stone ? 108 : 56);
        Grid.SetColumn(WorkspaceFooter, 0);
        Grid.SetColumnSpan(WorkspaceFooter, 2);
        TaskGrid.Columns[0].MinWidth = stone ? 180 : 240;
        TaskGrid.Columns[1].Width = stone ? 185 : theme.HasLunarScene ? 210 : 182;
        TaskGrid.Columns[2].Width = theme.HasLunarScene ? 138 : 176;
        // Drop the previous composition's cached star display width when its
        // fixed columns and available viewport change together during a skin swap.
        TaskGrid.Columns[0].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        TaskGrid.Columns[1].Header = stone ? "状态 / 进度" : theme.HasLunarScene ? "状态" : "状态 / 进度";
        if (stone)
        {
            LunarCopy.Width = 244;
            LunarCopy.Margin = new Thickness(26, 238, 26, 0);
            LunarCopy.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            NightActions.Margin = new Thickness(26, 352, 0, 0);
            NightActions.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            LunarSummary.Margin = new Thickness(26, 418, 26, 0);
            LunarSummary.VerticalAlignment = VerticalAlignment.Top;
            LunarSummary.RowDefinitions.Clear();
            LunarSummary.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            LunarSummary.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            LunarSummary.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(NightOptionsButton, 0);
            Grid.SetRow(NightOptionsButton, 1);
            NightOptionsButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            NightOptionsButton.Margin = new Thickness(0, 12, 0, 0);
            SummaryCopy.Margin = new Thickness(0, 18, 0, 0);
            SummaryMain.TextWrapping = SummarySecondary.TextWrapping = TextWrapping.Wrap;
            TaskToolbarLayout.Margin = new Thickness(27, 12, 27, 0);
            TaskToolbarLayout.RowDefinitions[1].Height = new GridLength(42);
            TaskToolbarLayout.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(TaskSearch, 0);
            Grid.SetRow(TaskSearch, 1);
            TaskArea.Margin = new Thickness(27, 0, 27, 12);
            TaskGrid.RowHeight = 75;
        }
        else
        {
            LunarCopy.ClearValue(WidthProperty);
            LunarCopy.ClearValue(MarginProperty);
            LunarCopy.ClearValue(HorizontalAlignmentProperty);
            NightActions.ClearValue(MarginProperty);
            NightActions.ClearValue(HorizontalAlignmentProperty);
            LunarSummary.SetResourceReference(MarginProperty, "LunarHeroBottomMargin");
            LunarSummary.VerticalAlignment = VerticalAlignment.Bottom;
            LunarSummary.RowDefinitions.Clear();
            LunarSummary.ColumnDefinitions[1].Width = GridLength.Auto;
            Grid.SetColumn(NightOptionsButton, 1);
            Grid.SetRow(NightOptionsButton, 0);
            NightOptionsButton.ClearValue(HorizontalAlignmentProperty);
            NightOptionsButton.ClearValue(MarginProperty);
            SummaryCopy.Margin = new Thickness(0);
            SummaryMain.TextWrapping = SummarySecondary.TextWrapping = TextWrapping.NoWrap;
            TaskToolbarLayout.ClearValue(MarginProperty);
            TaskToolbarLayout.RowDefinitions[1].Height = new GridLength(0);
            TaskToolbarLayout.ColumnDefinitions[1].Width = new GridLength(220);
            Grid.SetColumn(TaskSearch, 1);
            Grid.SetRow(TaskSearch, 0);
            TaskArea.ClearValue(MarginProperty);
            TaskGrid.ClearValue(System.Windows.Controls.DataGrid.RowHeightProperty);
        }
        LogPopup.HorizontalOffset = theme.HasLunarScene ? 0 : -720;
    }

    private void OnNightOptions(object sender, RoutedEventArgs e)
    {
        SettingsToggle.IsChecked = OptionsToggle.IsChecked = LogToggle.IsChecked = false;
        if (DataContext is MainViewModel model) model.IsPasswordPanelOpen = false;
        NightOptionsOverlay.Visibility = Visibility.Visible;
        WorkspaceRoot.IsHitTestVisible = false;
        NightOptions.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void CloseNightOptions()
    {
        if (NightOptionsOverlay.Visibility != Visibility.Visible) return;
        NightOptionsOverlay.Visibility = Visibility.Collapsed;
        WorkspaceRoot.IsHitTestVisible = true;
        NightOptionsButton.Focus();
    }

    private void OnNightOptionsKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F5 || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.O or Key.S or Key.E)
        {
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape) return;
        CloseNightOptions();
        e.Handled = true;
    }

    private void OnJobMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnSettingsOpened(object sender, RoutedEventArgs e)
    {
        if (NightOptionsOverlay is not null) CloseNightOptions();
        if (OptionsToggle is not null) OptionsToggle.IsChecked = false;
        if (DataContext is MainViewModel model) model.IsPasswordPanelOpen = false;
    }

    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is Popup { Child: UIElement child })
            child.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnFlyoutKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        UIElement target = ThemeManager.Current.SelectedTheme.HasLunarScene && !SettingsPopup.IsOpen
            ? NightOptionsButton : SettingsPopup.IsOpen ? SettingsToggle : PasswordPopup.IsOpen ? PasswordToggle : OptionsToggle;
        SettingsToggle.IsChecked = PasswordToggle.IsChecked = OptionsToggle.IsChecked = false;
        target.Focus();
        e.Handled = true;
    }

    private void OnPreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (NightOptionsOverlay.Visibility == Visibility.Visible) { e.Handled = true; return; }
        if (DataContext is not MainViewModel viewModel ||
            !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
            e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        viewModel.AddPathsFromDrop(paths);
        e.Handled = true;
    }

    private void OnOpenPasswordAssistant(object sender, RoutedEventArgs e)
    {
        var window = new PasswordAssistantWindow
        {
            Owner = this,
            DataContext = DataContext
        };
        window.ShowDialog();
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.JobSearchText = string.Empty;
            JobSearchBox.Focus();
        }
    }

    private void OnPasswordOpened(object sender, RoutedEventArgs e)
    {
        CloseNightOptions();
        var night = ThemeManager.Current.SelectedTheme.HasLunarScene;
        PasswordPopup.PlacementTarget = night ? WorkspaceRoot : PasswordToggle;
        PasswordPopup.Placement = night ? PlacementMode.Center : PlacementMode.Bottom;
        PasswordPopup.HorizontalOffset = 0;
        PasswordPopup.VerticalOffset = night ? 0 : 8;
        SettingsToggle.IsChecked = false;
        if (OptionsToggle is not null) OptionsToggle.IsChecked = false;
    }

    private void OnOptionsOpened(object sender, RoutedEventArgs e)
    {
        SettingsToggle.IsChecked = false;
        if (DataContext is MainViewModel viewModel) viewModel.IsPasswordPanelOpen = false;
    }
}
