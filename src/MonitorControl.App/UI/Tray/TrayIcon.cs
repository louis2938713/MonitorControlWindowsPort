using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;
using Microsoft.Win32;
using MonitorControl.App.Localization;
using MonitorControl.App.Platform;

namespace MonitorControl.App.UI.Tray;

/// <summary>
/// Notification area icon (equivalent of the macOS status item): left click toggles the slider flyout,
/// right click shows a small menu that is always available.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly FlyoutWindow _flyout;
    private readonly MenuItem _settingsItem = new();
    private readonly MenuItem _updateItem = new();
    private readonly MenuItem _quitItem = new();

    public TrayIcon(FlyoutWindow flyout, FlyoutActions actions)
    {
        _flyout = flyout;
        _settingsItem.Click += (_, _) => actions.OpenSettings();
        _updateItem.Click += (_, _) => actions.CheckForUpdates();
        _quitItem.Click += (_, _) => actions.Quit();

        _icon = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            ContextMenu = new ContextMenu { Items = { _settingsItem, _updateItem, new Separator(), _quitItem } },
        };
        _icon.TrayLeftMouseUp += (_, _) => ToggleFlyout();
        UpdateIconImage();
        _icon.ForceCreate(enablesEfficiencyMode: false);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Loc.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    public bool IsVisible => _icon.Visibility == Visibility.Visible;

    public void Refresh()
    {
        _icon.ToolTipText = Loc.T("App.Name");
        _settingsItem.Header = Loc.T("Menu.Settings");
        _updateItem.Header = Loc.T("Menu.CheckForUpdates");
        _quitItem.Header = Loc.T("Menu.Quit");
        if (_icon.ContextMenu is { } menu)
        {
            menu.FlowDirection = Loc.FlowDirection;
        }
    }

    public void SetVisible(bool visible) => _icon.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Windows notification from the app (shown even when the icon is hidden in the overflow area).</summary>
    public void Notify(string title, string message) => _icon.ShowNotification(title, message);

    /// <summary>Opens the flyout on the display under the mouse.</summary>
    public void OpenFlyout()
    {
        var (x, y) = MonitorGeometry.CursorPosition();
        var (bounds, workArea) = MonitorGeometry.MonitorAt(x, y);
        _flyout.ShowFlyout(bounds, workArea, x, y);
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon.Dispose();
    }

    private void ToggleFlyout()
    {
        if (_flyout.IsVisible || _flyout.WasJustHidden)
        {
            _flyout.HideFlyout();
            return;
        }

        OpenFlyout();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color)
        {
            Application.Current?.Dispatcher.BeginInvoke(UpdateIconImage);
        }
    }

    private void UpdateIconImage() =>
        _icon.IconSource = new GeneratedIconSource
        {
            Text = Icons.Brightness,
            FontFamily = new FontFamily(Icons.InstalledFontName),
            FontSize = 26,
            Size = 32,
            Foreground = MonitorGeometry.TaskbarUsesLightTheme() ? Brushes.Black : Brushes.White,
        };
}
