using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MonitorControl.App.Localization;

namespace MonitorControl.App.UI.Settings;

/// <summary>Settings window with the five pages of the macOS app.</summary>
public partial class SettingsWindow
{
    private readonly NavigationItem[] _items;

    internal SettingsWindow(SettingsContext context)
    {
        InitializeComponent();
        General = new GeneralViewModel(context);
        AppMenu = new AppMenuViewModel(context);
        Keyboard = new KeyboardViewModel(context);
        Displays = new DisplaysViewModel(context);
        var about = new AboutViewModel(context);
        General.SettingsReset += (_, _) =>
        {
            AppMenu.Load();
            Keyboard.Load();
            Displays.LoadDisplayList();
        };

        _items =
        [
            new("Settings.Tab.General", Icons.Settings, new GeneralPage { DataContext = General }),
            new("Settings.Tab.AppMenu", "", new AppMenuPage { DataContext = AppMenu }),
            new("Settings.Tab.Keyboard", "", new KeyboardPage { DataContext = Keyboard }),
            new("Settings.Tab.Displays", Icons.Display, new DisplaysPage { DataContext = Displays }),
            new("Settings.Tab.About", "", new AboutPage { DataContext = about }),
        ];
        Navigation.ItemsSource = _items;
        Navigation.SelectedIndex = 0;
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
        PreviewKeyDown += (_, e) =>
        {
            // Ctrl+W closes, like Cmd+W in the macOS app.
            if (e.Key == Key.W && System.Windows.Input.Keyboard.Modifiers == ModifierKeys.Control)
            {
                Close();
            }
        };
        UpdateTitle();
    }

    internal GeneralViewModel General { get; }

    internal AppMenuViewModel AppMenu { get; }

    internal KeyboardViewModel Keyboard { get; }

    internal DisplaysViewModel Displays { get; }

    public void ShowPage(int index) => Navigation.SelectedIndex = Math.Clamp(index, 0, _items.Length - 1);

    private void OnNavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is NavigationItem item)
        {
            PageHost.Content = item.Page;
            Scroller.ScrollToTop();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in _items)
        {
            item.Refresh();
        }

        Displays.LoadDisplayList();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Title = Loc.T("App.Name");
        TitleBar.Title = Title;
    }

    /// <summary>A navigation entry.</summary>
    public sealed class NavigationItem(string titleKey, string glyph, FrameworkElement page) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Title => Loc.T(titleKey);

        public string Glyph { get; } = glyph;

        public FrameworkElement Page { get; } = page;

        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
    }
}
