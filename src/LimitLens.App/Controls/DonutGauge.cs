using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace LimitLens.App.Controls;

public sealed class DonutGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(DonutGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(DonutGauge),
        new FrameworkPropertyMetadata(Brushes.DarkSlateGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush),
        typeof(Brush),
        typeof(DonutGauge),
        new FrameworkPropertyMetadata(Brushes.MediumAquamarine, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(DonutGauge),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ValueBrush
    {
        get => (Brush)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var thickness = Math.Max(1, StrokeThickness);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var trackPen = CreatePen(TrackBrush, thickness);
        var valuePen = CreatePen(ValueBrush, thickness);
        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        var value = Math.Clamp(Value, 0, 100);
        if (value <= 0)
        {
            return;
        }

        if (value >= 99.999)
        {
            drawingContext.DrawEllipse(null, valuePen, center, radius, radius);
            return;
        }

        var startAngle = -90d;
        var endAngle = startAngle + value * 3.6d;
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, endAngle);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            value > 50,
            SweepDirection.Clockwise,
            true));
        drawingContext.DrawGeometry(null, valuePen, new PathGeometry([figure]));
    }

    private static Pen CreatePen(Brush brush, double thickness) => new(brush, thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    };

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var angle = angleDegrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }
}
