using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MonitorControl.App.UI.Controls;

/// <summary>
/// The MonitorControl slider: rounded bar with the knob inside it, optional tick marks and markers showing the
/// values of other displays when a combined slider controls displays with different values.
/// Drawn in <see cref="OnRender"/> (port of MCSliderCell.drawBar); dragging is handled by a transparent Track.
/// </summary>
public sealed class McSlider : Slider
{
    private const double Inset = 0;
    private const double TickMarkKnobExtraInset = 4;
    private const double TickMarkKnobExtraRadiusMultiplier = 0.25;

    private static readonly Brush BarFill = Frozen(Color.FromArgb(51, 142, 142, 147));
    private static readonly Pen BarStroke = FrozenPen(Color.FromArgb(128, 142, 142, 147));
    private static readonly Brush BarFilledFill = Frozen(Colors.White);
    private static readonly Color KnobFill = Colors.White;
    private static readonly Color KnobFillTracking = Color.FromRgb(204, 204, 204);
    private static readonly Color KnobStroke = Color.FromArgb(128, 142, 142, 147);
    private static readonly Color KnobShadow = Color.FromArgb(8, 0, 0, 0);
    private static readonly Color HighlightIndicator = Color.FromRgb(217, 217, 217);
    private static readonly Brush TickMarkFill = Frozen(Color.FromArgb(128, 142, 142, 147));

    private readonly Dictionary<uint, float> _highlightItems = [];
    private bool _isHighlightDisplayItems;
    private int _numOfTickMarks;

    static McSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(McSlider), new FrameworkPropertyMetadata(typeof(McSlider)));
    }

    public McSlider()
    {
        Minimum = 0;
        Maximum = 1;
        SmallChange = 0.01;
        LargeChange = 1.0 / 16;
        IsMoveToPointEnabled = true;
        Height = 22;
        Focusable = true;
    }

    public bool IsTracking { get; private set; }

    public void SetNumOfCustomTickMarks(int count)
    {
        _numOfTickMarks = count;
        InvalidateVisual();
    }

    public void SetDisplayHighlightItems(bool isHighlightDisplayItems)
    {
        _isHighlightDisplayItems = isHighlightDisplayItems;
        InvalidateVisual();
    }

    public void SetHighlightItem(uint displayId, float value)
    {
        _highlightItems[displayId] = value;
        InvalidateVisual();
    }

    public void RemoveHighlightItem(uint displayId) => _highlightItems.Remove(displayId);

    public void ResetHighlightItems() => _highlightItems.Clear();

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
    }

    protected override void OnThumbDragStarted(DragStartedEventArgs e)
    {
        IsTracking = true;
        InvalidateVisual();
        base.OnThumbDragStarted(e);
    }

    protected override void OnThumbDragCompleted(DragCompletedEventArgs e)
    {
        IsTracking = false;
        InvalidateVisual();
        base.OnThumbDragCompleted(e);
    }

    /// <summary>Scroll to adjust (credits @thompsonate, Scrollable-NSSlider).</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        var range = Maximum - Minimum;
        Value = Math.Clamp(Value + (range * (e.Delta / 120.0) * 0.03), Minimum, Maximum);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var rect = new Rect(0, Inset, ActualWidth, Math.Max(0, ActualHeight - (2 * Inset)));
        if (rect.Width <= rect.Height)
        {
            return;
        }

        var value = (float)Value;
        var maxValue = value;
        var minValue = value;
        if (_isHighlightDisplayItems && _highlightItems.Count > 0)
        {
            maxValue = Math.Max(_highlightItems.Values.Max(), maxValue);
            minValue = Math.Min(_highlightItems.Values.Min(), minValue);
        }

        var hasTicks = _numOfTickMarks > 0;
        var barRadius = rect.Height * 0.5 * (hasTicks ? TickMarkKnobExtraRadiusMultiplier : 1);
        drawingContext.DrawRoundedRectangle(BarFill, null, rect, barRadius, barRadius);

        var barFilledWidth = ((rect.Width - rect.Height) * maxValue) + rect.Height;
        drawingContext.DrawRoundedRectangle(BarFilledFill, null, new Rect(rect.X, rect.Y, barFilledWidth, rect.Height), barRadius, barRadius);

        var extraInset = hasTicks ? TickMarkKnobExtraInset : 0;
        var knobMinX = rect.X + ((rect.Width - rect.Height) * minValue);
        var knobMaxX = rect.X + ((rect.Width - rect.Height) * maxValue);
        var knobRect = new Rect(knobMinX + extraInset, rect.Y, rect.Height + (knobMaxX - knobMinX), rect.Height);
        knobRect.Inflate(-extraInset, 0);
        if (knobRect.Width < 0)
        {
            knobRect.Width = 0;
        }

        var knobRadius = knobRect.Height * 0.5 * (hasTicks ? TickMarkKnobExtraRadiusMultiplier : 1);

        if (hasTicks)
        {
            for (var i = 1; i <= _numOfTickMarks - 2; i++)
            {
                var location = 1.0 / (_numOfTickMarks - 1) * i;
                var tick = new Rect(
                    rect.X + rect.Height + TickMarkKnobExtraInset - knobRect.Height + (TickMarkKnobExtraInset * 2) + ((rect.Width - (TickMarkKnobExtraInset * 5)) * location),
                    rect.Y + (rect.Height / 3),
                    4,
                    rect.Height / 3);
                drawingContext.DrawRoundedRectangle(TickMarkFill, null, tick, 1, 1);
            }
        }

        var knobAlpha = Math.Clamp((minValue - 0.08) * 5, 0, 1);
        for (var i = 1; i <= 3; i++)
        {
            var shadow = knobRect;
            shadow.Offset(-2 * i, 0);
            drawingContext.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(KnobShadow, KnobShadow.A / 255.0 * knobAlpha)), null, shadow, knobRadius, knobRadius);
        }

        var knobColor = IsTracking ? KnobFillTracking : KnobFill;
        var knobPen = new Pen(new SolidColorBrush(WithAlpha(KnobStroke, KnobStroke.A / 255.0 * knobAlpha)), 1);
        drawingContext.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(knobColor, knobAlpha)), knobPen, knobRect, knobRadius, knobRadius);

        if (_isHighlightDisplayItems && _highlightItems.Count > 2)
        {
            foreach (var location in _highlightItems.Values)
            {
                var x = rect.X + ((rect.Width - rect.Height) * location);
                var highlight = new Rect(x + extraInset, rect.Y, rect.Height, rect.Height);
                highlight.Inflate(-(extraInset + (hasTicks ? 3 : 6)), -6);
                if (highlight.Width <= 0 || highlight.Height <= 0)
                {
                    continue;
                }

                var radius = highlight.Height * 0.5 * (hasTicks ? TickMarkKnobExtraRadiusMultiplier : 1);
                var alpha = Math.Clamp((location - 0.08) * 5, 0, 1);
                drawingContext.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(HighlightIndicator, alpha)), null, highlight, radius, radius);
            }
        }

        drawingContext.DrawRoundedRectangle(null, BarStroke, rect, barRadius, barRadius);
    }

    private static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color)
    {
        var pen = new Pen(Frozen(color), 1);
        pen.Freeze();
        return pen;
    }
}
