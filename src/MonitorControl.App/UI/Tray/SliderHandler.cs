using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MonitorControl.App.UI.Controls;
using MonitorControl.Core;
using MonitorControl.Core.Displays;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Mathematics;
using MonitorControl.Core.Platform;
using MonitorControl.Core.Settings;

namespace MonitorControl.App.UI.Tray;

/// <summary>A flyout slider row (icon, slider, optional percentage) controlling one or more displays. Port of SliderHandler.swift.</summary>
internal sealed class SliderHandler : ISliderHandle
{
    private readonly Prefs _prefs;
    private readonly AppState _state;
    private readonly Dictionary<uint, float> _values = [];
    private readonly TextBlock? _percentage;
    private bool _settingValue;

    public SliderHandler(Prefs prefs, AppState state, Display? display, Command command, string title)
    {
        _prefs = prefs;
        _state = state;
        Command = command;
        Title = title;
        Slider = new McSlider { Width = 200, VerticalAlignment = VerticalAlignment.Center, ToolTip = title };
        Slider.SetNumOfCustomTickMarks(prefs.Bool(PrefKey.ShowTickMarks) ? 5 : 0);
        System.Windows.Automation.AutomationProperties.SetName(Slider, title);
        Slider.ValueChanged += (_, _) =>
        {
            if (!_settingValue)
            {
                ValueChanged();
            }
        };

        var icon = new TextBlock
        {
            Text = command switch
            {
                Command.AudioSpeakerVolume => Icons.Volume,
                Command.Contrast => Icons.Contrast,
                _ => Icons.Brightness,
            },
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = 14,
            Width = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(icon);
        row.Children.Add(Slider);
        if (prefs.Bool(PrefKey.EnableSliderPercent))
        {
            _percentage = new TextBlock
            {
                Width = 44,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7,
                FontSize = 12,
            };
            _percentage.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            row.Children.Add(_percentage);
        }

        View = row;
        if (display is not null)
        {
            AddDisplay(display);
        }
    }

    public McSlider Slider { get; }

    public FrameworkElement View { get; }

    public List<Display> Displays { get; } = [];

    public Command Command { get; }

    public string Title { get; }

    public void AddDisplay(Display display)
    {
        Displays.Add(display);
        if (display is ExternalDisplay external)
        {
            SetValue(external.SetupSliderCurrentValue(Command), external.Id);
        }
        else if (display is InternalDisplay builtIn && Command == Command.Brightness)
        {
            SetValue(builtIn.GetInternalBrightness(), builtIn.Id);
        }
    }

    public void SetValue(float value, uint displayId = 0)
    {
        if (displayId != 0)
        {
            _values[displayId] = value;
            Slider.SetHighlightItem(displayId, value);
        }

        _settingValue = true;
        try
        {
            Slider.Value = value;
        }
        finally
        {
            _settingValue = false;
        }

        var spread = _values.Count > 0 ? _values.Values.Max() - _values.Values.Min() : 0;
        Slider.SetDisplayHighlightItems(spread > 0.001f);
        UpdatePercentage(value);
    }

    private void ValueChanged()
    {
        if (!_state.IsActive)
        {
            return;
        }

        var value = (float)Slider.Value;
        if (_prefs.Bool(PrefKey.EnableSliderSnap))
        {
            var snapped = StepMath.Snap(value);
            if (snapped != value)
            {
                value = snapped;
                _settingValue = true;
                Slider.Value = value;
                _settingValue = false;
            }
        }

        UpdatePercentage(value);
        foreach (var display in Displays)
        {
            Slider.SetHighlightItem(display.Id, value);
            _values[display.Id] = value;
            if (Command == Command.Brightness && display is InternalDisplay builtIn)
            {
                builtIn.SetBrightness(value);
            }
            else if (display is ExternalDisplay external)
            {
                ValueChangedExternalDisplay(external, value);
            }
        }

        Slider.SetDisplayHighlightItems(false);
    }

    private void ValueChangedExternalDisplay(ExternalDisplay display, float value)
    {
        // For the volume slider, also set/unset mute when the value is changed from/to 0.
        var muted = display.ReadPrefAsInt(command: Command.AudioMuteScreenBlank) == 1;
        if (Command == Command.AudioSpeakerVolume && ((muted && value > 0) || (!muted && value == 0)))
        {
            display.ToggleMute(fromVolumeSlider: true);
        }

        if (Command == Command.Brightness)
        {
            display.SetBrightness(value);
            return;
        }

        if (display.IsSw())
        {
            return;
        }

        if (Command != Command.AudioSpeakerVolume || !display.ReadPrefAsBool(PrefKey.EnableMuteUnmute) || value != 0)
        {
            display.WriteDdcValues(Command, display.ConvValueToDdc(Command, value));
        }

        display.SavePref(value, command: Command);
    }

    private void UpdatePercentage(float value)
    {
        if (_percentage is not null)
        {
            _percentage.Text = (value * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
        }
    }
}
