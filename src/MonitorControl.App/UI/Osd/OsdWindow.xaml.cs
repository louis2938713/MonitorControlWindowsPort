using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MonitorControl.App.Platform;
using MonitorControl.Core.Mathematics;
using MonitorControl.Core.Platform;

namespace MonitorControl.App.UI.Osd;

/// <summary>One OSD window per display: icon and value bar, non-activating and click-through.</summary>
public partial class OsdWindow
{
    private const int ChicletCount = (int)StepMath.ChicletCount;
    private readonly DispatcherTimer _hideTimer = new();
    private readonly Border[] _chiclets = new Border[ChicletCount];

    public OsdWindow()
    {
        InitializeComponent();
        for (var i = 0; i < ChicletCount; i++)
        {
            _chiclets[i] = new Border { CornerRadius = new CornerRadius(1.5), Margin = new Thickness(1, 0, 1, 0) };
            Chiclets.Children.Add(_chiclets[i]);
        }

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) =>
            {
                if (Opacity == 0)
                {
                    Hide();
                }
            };
            BeginAnimation(OpacityProperty, fade);
        };
        SourceInitialized += (_, _) => WindowStyles.MakeOverlay(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Shows the OSD on a display for <paramref name="duration"/>.</summary>
    /// <param name="filled">Filled part (chiclets when <paramref name="chiclets"/>, otherwise a 0..1 ratio). Null hides the bar.</param>
    public void Present(PixelRect workArea, string glyph, double? filled, bool chiclets, bool dimmed, TimeSpan duration)
    {
        IconGlyph.Text = glyph;
        IconGlyph.Opacity = dimmed ? 0.4 : 1;
        Chiclets.Visibility = chiclets && filled is not null ? Visibility.Visible : Visibility.Collapsed;
        ContinuousBar.Visibility = !chiclets && filled is not null ? Visibility.Visible : Visibility.Collapsed;
        if (filled is { } amount)
        {
            if (chiclets)
            {
                var filledCount = (int)Math.Round(amount);
                for (var i = 0; i < ChicletCount; i++)
                {
                    _chiclets[i].SetResourceReference(Border.BackgroundProperty, i < filledCount ? "AccentFillColorDefaultBrush" : "ControlStrongFillColorDefaultBrush");
                }
            }
            else
            {
                ContinuousBar.UpdateLayout();
                ContinuousFill.Width = Math.Clamp(amount, 0, 1) * Math.Max(0, ContinuousBar.ActualWidth > 0 ? ContinuousBar.ActualWidth : 160);
            }
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible)
        {
            Show();
        }

        WindowPlacement.Place(this, workArea, (width, height) => (
            workArea.Left + ((workArea.Width - width) / 2),
            workArea.Bottom - height - (int)(48 * VisualTreeHelper.GetDpi(this).DpiScaleY)));
        _hideTimer.Stop();
        _hideTimer.Interval = duration;
        _hideTimer.Start();
    }
}
