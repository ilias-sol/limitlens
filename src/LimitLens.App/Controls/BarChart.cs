using System.Collections;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace LimitLens.App.Controls;

public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable),
        typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush),
        typeof(Brush),
        typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.DarkSlateGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
        nameof(HighlightBrush),
        typeof(Brush),
        typeof(BarChart),
        new FrameworkPropertyMetadata(Brushes.MediumAquamarine, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public Brush HighlightBrush
    {
        get => (Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var values = Values?.Cast<object>()
            .Select(value => System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray() ?? [];
        if (values.Length == 0)
        {
            return;
        }

        var maximum = Math.Max(1, values.Max());
        var gap = 7d;
        var barWidth = Math.Max(3, (ActualWidth - gap * (values.Length - 1)) / values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            var height = Math.Max(3, values[index] / maximum * ActualHeight);
            var rectangle = new Rect(
                index * (barWidth + gap),
                ActualHeight - height,
                barWidth,
                height);
            drawingContext.DrawRoundedRectangle(
                index == values.Length - 1 ? HighlightBrush : BarBrush,
                null,
                rectangle,
                5,
                5);
        }
    }
}
