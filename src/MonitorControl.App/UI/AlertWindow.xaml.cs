using System.Windows;
using MonitorControl.App.Localization;

namespace MonitorControl.App.UI;

/// <summary>Simple modal dialog with custom buttons (equivalent of NSAlert).</summary>
public partial class AlertWindow
{
    private int _result = -1;

    private AlertWindow(string title, string? message, IReadOnlyList<string> buttons)
    {
        InitializeComponent();
        FlowDirection = Loc.FlowDirection;
        Title = Loc.T("App.Name");
        TitleBar.Title = Title;
        HeaderText.Text = title;
        MessageText.Text = message ?? string.Empty;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Wpf.Ui.Controls.Button
            {
                Content = buttons[i],
                MinWidth = 96,
                Margin = new Thickness(8, 0, 0, 0),
                Appearance = i == 0 ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary,
                IsDefault = i == 0,
                IsCancel = i == buttons.Count - 1 && buttons.Count > 1,
            };
            button.Click += (_, _) =>
            {
                _result = index;
                Close();
            };
            Buttons.Children.Add(button);
        }
    }

    /// <summary>Shows the dialog and returns the index of the clicked button (-1 when closed).</summary>
    public static int Show(string title, string? message, params string[] buttons)
    {
        if (buttons.Length == 0)
        {
            buttons = [Loc.T("Common.Ok")];
        }

        var window = new AlertWindow(title, message, buttons);
        window.Topmost = true;
        window.ShowDialog();
        return window._result;
    }
}
