using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MonitorControl.App.Localization;
using MonitorControl.Core;
using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Settings;
using UiButton = Wpf.Ui.Controls.Button;

namespace MonitorControl.App.UI.Tray;

/// <summary>Actions available from the flyout.</summary>
internal sealed record FlyoutActions(Action OpenSettings, Action CheckForUpdates, Action Quit);

/// <summary>Builds the flyout content: slider blocks per display and the bottom row. Port of MenuHandler.swift.</summary>
internal sealed class FlyoutBuilder
{
    private readonly FlyoutWindow _window;
    private readonly DisplayManager _displayManager;
    private readonly Prefs _prefs;
    private readonly AppState _state;
    private readonly FlyoutActions _actions;
    private readonly Action<bool> _setIconVisible;
    private readonly Dictionary<Command, SliderHandler> _combinedSliderHandler = [];
    private uint _lastMenuRelevantDisplayId;

    public FlyoutBuilder(FlyoutWindow window, DisplayManager displayManager, AppState state, FlyoutActions actions, Action<bool> setIconVisible)
    {
        _window = window;
        _displayManager = displayManager;
        _prefs = displayManager.Prefs;
        _state = state;
        _actions = actions;
        _setIconVisible = setIconVisible;
        _window.Opening += (_, _) => UpdateMenuRelevantDisplay();
    }

    public void UpdateMenus(bool dontClose = false)
    {
        Core.Log.Info("Menu update initiated");
        if (!dontClose)
        {
            _window.HideFlyout();
        }

        var showIcon = _prefs.MenuIcon switch
        {
            MenuIcon.Show => true,
            MenuIcon.ExternalOnly => _displayManager.Displays.Any(d => !d.IsBuiltIn),
            _ => false,
        };
        _setIconVisible(showIcon);
        ClearMenu();

        var currentDisplay = _displayManager.GetCurrentDisplay();
        var displays = _displayManager.SortDisplaysByFriendlyName()
            .Where(d => !(d is InternalDisplay && _prefs.Bool(PrefKey.HideBuiltInFromMenu)))
            .Where(d => !d.IsDummy)
            .ToList();
        var relevant = _prefs.MultiSliders == MultiSliders.Relevant;
        var combine = _prefs.MultiSliders == MultiSliders.Combine;
        var numOfDisplays = displays.Count;
        if (relevant && currentDisplay is not null)
        {
            var effectiveCurrent = _displayManager.ResolveEffectiveDisplayId(currentDisplay.Id);
            displays = displays.Where(d => _displayManager.ResolveEffectiveDisplayId(d.Id) == effectiveCurrent).ToList();
            _lastMenuRelevantDisplayId = currentDisplay.Id;
        }

        foreach (var display in displays)
        {
            UpdateDisplayMenu(display, numOfDisplays);
        }

        if (combine)
        {
            AddCombinedDisplayMenuBlock();
        }

        AddDefaultMenuOptions();
    }

    private void ClearMenu()
    {
        _window.Blocks.Children.Clear();
        _window.BottomRow.Children.Clear();
        _combinedSliderHandler.Clear();
    }

    private void UpdateMenuRelevantDisplay()
    {
        if (_prefs.MultiSliders != MultiSliders.Relevant)
        {
            return;
        }

        if (_displayManager.GetCurrentDisplay() is { } display && display.Id != _lastMenuRelevantDisplayId)
        {
            Core.Log.Info("Menu must be refreshed as relevant display changed since last time.");
            _lastMenuRelevantDisplayId = display.Id;
            UpdateMenus(dontClose: true);
        }
    }

    private SliderHandler SetupMenuSliderHandler(Command command, Display display, string title)
    {
        if (_prefs.MultiSliders == MultiSliders.Combine && _combinedSliderHandler.TryGetValue(command, out var combined))
        {
            combined.AddDisplay(display);
            display.SliderHandler[command] = combined;
            return combined;
        }

        var handler = new SliderHandler(_prefs, _state, display, command, title);
        if (_prefs.MultiSliders == MultiSliders.Combine)
        {
            _combinedSliderHandler[command] = handler;
        }

        display.SliderHandler[command] = handler;
        return handler;
    }

    private void UpdateDisplayMenu(Display display, int numOfDisplays)
    {
        Core.Log.Info($"Adding menu items for display {display.Id}");
        var added = new List<SliderHandler>();
        var external = display as ExternalDisplay;

        display.SliderHandler.Remove(Command.Brightness);
        if (!display.ReadPrefAsBool(PrefKey.UnavailableDdc, Command.Brightness) && !_prefs.Bool(PrefKey.HideBrightness))
        {
            added.Add(SetupMenuSliderHandler(Command.Brightness, display, Loc.T("Menu.Brightness")));
        }

        display.SliderHandler.Remove(Command.Contrast);
        if (external is not null && !external.IsSw() && !display.ReadPrefAsBool(PrefKey.UnavailableDdc, Command.Contrast) && _prefs.Bool(PrefKey.ShowContrast))
        {
            added.Add(SetupMenuSliderHandler(Command.Contrast, display, Loc.T("Menu.Contrast")));
        }

        display.SliderHandler.Remove(Command.AudioSpeakerVolume);
        if (external is not null && !external.IsSw() && !display.ReadPrefAsBool(PrefKey.UnavailableDdc, Command.AudioSpeakerVolume) && !_prefs.Bool(PrefKey.HideVolume))
        {
            added.Add(SetupMenuSliderHandler(Command.AudioSpeakerVolume, display, Loc.T("Menu.Volume")));
        }

        if (_prefs.MultiSliders != MultiSliders.Combine)
        {
            AddDisplayMenuBlock(added, display.FriendlyName, numOfDisplays);
        }

        if (added.Count > 0 && _prefs.MenuIcon == MenuIcon.SliderOnly)
        {
            _setIconVisible(true);
        }
    }

    private void AddDisplayMenuBlock(List<SliderHandler> handlers, string blockName, int numOfDisplays)
    {
        if (handlers.Count == 0)
        {
            return;
        }

        var rows = new StackPanel();
        foreach (var handler in handlers)
        {
            rows.Children.Add(handler.View);
        }

        if (numOfDisplays > 1 && _prefs.MultiSliders != MultiSliders.Relevant)
        {
            var title = new TextBlock
            {
                Text = blockName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Opacity = 0.6,
                Margin = new Thickness(2, 0, 0, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            rows.Children.Insert(0, title);
            var block = new Border
            {
                Child = rows,
                Padding = new Thickness(10, 8, 10, 6),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(_prefs.Bool(PrefKey.ShowTickMarks) ? 4 : 11),
                BorderThickness = new Thickness(1),
            };
            block.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            block.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            _window.Blocks.Children.Add(block);
        }
        else
        {
            _window.Blocks.Children.Add(rows);
        }
    }

    private void AddCombinedDisplayMenuBlock()
    {
        foreach (var command in new[] { Command.Brightness, Command.Contrast, Command.AudioSpeakerVolume })
        {
            if (_combinedSliderHandler.TryGetValue(command, out var handler))
            {
                _window.Blocks.Children.Add(handler.View);
            }
        }
    }

    private void AddDefaultMenuOptions()
    {
        var style = _prefs.MenuItemStyle;
        if (style == MenuItemStyle.Hide)
        {
            _window.BottomRow.Visibility = Visibility.Collapsed;
            return;
        }

        _window.BottomRow.Visibility = Visibility.Visible;
        if (style == MenuItemStyle.Icon)
        {
            var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            icons.Children.Add(IconButton(Icons.Settings, Loc.T("Menu.Settings"), _actions.OpenSettings));
            icons.Children.Add(IconButton(Icons.Update, Loc.T("Menu.CheckForUpdates"), _actions.CheckForUpdates));
            icons.Children.Add(IconButton(Icons.Quit, Loc.T("Menu.Quit"), _actions.Quit));
            _window.BottomRow.Children.Add(icons);
        }
        else
        {
            var items = new StackPanel();
            items.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 4) });
            items.Children.Add(TextButton(Loc.T("Menu.Settings"), _actions.OpenSettings));
            items.Children.Add(TextButton(Loc.T("Menu.CheckForUpdates"), _actions.CheckForUpdates));
            items.Children.Add(TextButton(Loc.T("Menu.Quit"), _actions.Quit));
            _window.BottomRow.Children.Add(items);
        }
    }

    private UiButton IconButton(string glyph, string text, Action action)
    {
        var button = new UiButton
        {
            Content = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 16 },
            Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
            Padding = new Thickness(6),
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = text,
            Opacity = 0.7,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) => Run(action);
        return button;
    }

    private UiButton TextButton(string text, Action action)
    {
        var button = new UiButton
        {
            Content = text,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 5, 8, 5),
        };
        button.Click += (_, _) => Run(action);
        return button;
    }

    private void Run(Action action)
    {
        _window.HideFlyout();
        action();
    }
}
