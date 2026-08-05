using System.Collections;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace LimitLens.App.Controls;

public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.MediumAquamarine, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var values = Values?.Cast<object>()
            .Select(value => System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray() ?? [];
        if (values.Length < 2 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 75, 90, 86)), 1);
        for (var index = 1; index <= 3; index++)
        {
            var y = ActualHeight * index / 4;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(ActualWidth, y));
        }

        var minimum = values.Min();
        var maximum = values.Max();
        var range = Math.Max(1, maximum - minimum);
        var padding = 4d;
        var points = values.Select((value, index) => new Point(
            padding + index * (ActualWidth - padding * 2) / (values.Length - 1),
            padding + (maximum - value) / range * (ActualHeight - padding * 2))).ToArray();

        var lineFigure = new PathFigure { StartPoint = points[0], IsClosed = false };
        lineFigure.Segments.Add(new PolyLineSegment(points.Skip(1), true));
        var lineGeometry = new PathGeometry([lineFigure]);

        var areaFigure = new PathFigure { StartPoint = new Point(points[0].X, ActualHeight), IsClosed = true };
        areaFigure.Segments.Add(new LineSegment(points[0], true));
        areaFigure.Segments.Add(new PolyLineSegment(points.Skip(1), true));
        areaFigure.Segments.Add(new LineSegment(new Point(points[^1].X, ActualHeight), true));
        drawingContext.DrawGeometry(FillBrush, null, new PathGeometry([areaFigure]));

        var linePen = new Pen(LineBrush, 2.25)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        drawingContext.DrawGeometry(null, linePen, lineGeometry);
    }
}
