using System.Windows;
using System.Windows.Media;

namespace WorshipLoopStudio.App.Controls;

/// <summary>Draws a waveform from precomputed peaks and shades everything outside the selected loop region.</summary>
public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(float[]), typeof(WaveformView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionStartProperty = DependencyProperty.Register(
        nameof(SelectionStart), typeof(double), typeof(WaveformView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionEndProperty = DependencyProperty.Register(
        nameof(SelectionEnd), typeof(double), typeof(WaveformView),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush BackgroundFill = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x2A)));
    private static readonly Brush ShadeFill = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0x05, 0x05, 0x10)));
    private static readonly Pen WavePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8)), 1));
    private static readonly Pen MarkerPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)), 2));
    private static readonly Pen CenterPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5C)), 1));

    public float[]? Peaks
    {
        get => (float[]?)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public double SelectionStart
    {
        get => (double)GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public double SelectionEnd
    {
        get => (double)GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        dc.DrawRectangle(BackgroundFill, null, new Rect(0, 0, w, h));
        var mid = h / 2;
        dc.DrawLine(CenterPen, new Point(0, mid), new Point(w, mid));

        var peaks = Peaks;
        if (peaks is { Length: > 0 })
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var columns = (int)Math.Max(1, Math.Floor(w));
                for (var x = 0; x < columns; x++)
                {
                    var start = (int)((long)x * peaks.Length / columns);
                    var end = Math.Max(start + 1, (int)((long)(x + 1) * peaks.Length / columns));
                    var peak = 0f;
                    for (var i = start; i < end && i < peaks.Length; i++)
                    {
                        if (peaks[i] > peak) peak = peaks[i];
                    }

                    var half = Math.Max(0.5, peak * (mid - 2));
                    ctx.BeginFigure(new Point(x + 0.5, mid - half), false, false);
                    ctx.LineTo(new Point(x + 0.5, mid + half), true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, WavePen, geometry);
        }

        var s = Math.Clamp(SelectionStart, 0, 1) * w;
        var e = Math.Clamp(SelectionEnd, 0, 1) * w;
        if (s > 0)
        {
            dc.DrawRectangle(ShadeFill, null, new Rect(0, 0, s, h));
        }

        if (e < w)
        {
            dc.DrawRectangle(ShadeFill, null, new Rect(e, 0, w - e, h));
        }

        dc.DrawLine(MarkerPen, new Point(s, 0), new Point(s, h));
        dc.DrawLine(MarkerPen, new Point(e, 0), new Point(e, h));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
