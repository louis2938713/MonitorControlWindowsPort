using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MonitorControl.App.UI.Tray;

/// <summary>Draws the notification area icon (sun glyph) with WPF and converts it to a Win32 icon.</summary>
internal static class TrayIconRenderer
{
    private const int Size = 32;

    public static System.Drawing.Icon Create(Color color)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var text = new FormattedText(
                Icons.Brightness,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily(Icons.InstalledFontName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                Size - 2,
                new SolidColorBrush(color),
                pixelsPerDip: 1);
            context.DrawText(text, new Point((Size - text.Width) / 2, (Size - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);

        // Single-image .ico with a PNG payload.
        var icon = new MemoryStream();
        using (var writer = new BinaryWriter(icon, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((byte)Size);
            writer.Write((byte)Size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)png.Length);
            writer.Write(6u + 16u);
            writer.Write(png.ToArray());
        }

        icon.Position = 0;
        return new System.Drawing.Icon(icon);
    }
}
