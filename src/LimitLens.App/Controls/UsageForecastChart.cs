using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LimitLens.Core.Settings;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace LimitLens.App.Controls;

public sealed class UsageForecastChart : FrameworkElement
{
    private readonly List<RenderedSample> renderedSamples = [];

    public UsageForecastChart()
    {
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => ToolTip = null;
        ToolTipService.SetInitialShowDelay(this, 100);
        ToolTipService.SetBetweenShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, 15000);
    }

    public static readonly DependencyProperty RemainingPercentProperty = DependencyProperty.Register(
        nameof(RemainingPercent), typeof(double), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ElapsedPercentProperty = DependencyProperty.Register(
        nameof(ElapsedPercent), typeof(double), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProjectedRemainingProperty = DependencyProperty.Register(
        nameof(ProjectedRemaining), typeof(double), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WindowStartProperty = DependencyProperty.Register(
        nameof(WindowStart), typeof(DateTimeOffset?), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ResetAtProperty = DependencyProperty.Register(
        nameof(ResetAt), typeof(DateTimeOffset?), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProjectedEmptyAtProperty = DependencyProperty.Register(
        nameof(ProjectedEmptyAt), typeof(DateTimeOffset?), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActualPointsProperty = DependencyProperty.Register(
        nameof(ActualPoints), typeof(IReadOnlyList<UsageHistorySample>), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HasPredictionProperty = DependencyProperty.Register(
        nameof(HasPrediction), typeof(bool), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActualBrushProperty = DependencyProperty.Register(
        nameof(ActualBrush), typeof(Brush), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EstimateBrushProperty = DependencyProperty.Register(
        nameof(EstimateBrush), typeof(Brush), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TargetBrushProperty = DependencyProperty.Register(
        nameof(TargetBrush), typeof(Brush), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(Brushes.OliveDrab, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentPointBrushProperty = DependencyProperty.Register(
        nameof(CurrentPointBrush), typeof(Brush), typeof(UsageForecastChart),
        new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public double RemainingPercent { get => (double)GetValue(RemainingPercentProperty); set => SetValue(RemainingPercentProperty, value); }
    public double ElapsedPercent { get => (double)GetValue(ElapsedPercentProperty); set => SetValue(ElapsedPercentProperty, value); }
    public double ProjectedRemaining { get => (double)GetValue(ProjectedRemainingProperty); set => SetValue(ProjectedRemainingProperty, value); }
    public DateTimeOffset? WindowStart { get => (DateTimeOffset?)GetValue(WindowStartProperty); set => SetValue(WindowStartProperty, value); }
    public DateTimeOffset? ResetAt { get => (DateTimeOffset?)GetValue(ResetAtProperty); set => SetValue(ResetAtProperty, value); }
    public DateTimeOffset? ProjectedEmptyAt { get => (DateTimeOffset?)GetValue(ProjectedEmptyAtProperty); set => SetValue(ProjectedEmptyAtProperty, value); }
    public IReadOnlyList<UsageHistorySample>? ActualPoints { get => (IReadOnlyList<UsageHistorySample>?)GetValue(ActualPointsProperty); set => SetValue(ActualPointsProperty, value); }
    public bool HasPrediction { get => (bool)GetValue(HasPredictionProperty); set => SetValue(HasPredictionProperty, value); }
    public Brush ActualBrush { get => (Brush)GetValue(ActualBrushProperty); set => SetValue(ActualBrushProperty, value); }
    public Brush EstimateBrush { get => (Brush)GetValue(EstimateBrushProperty); set => SetValue(EstimateBrushProperty, value); }
    public Brush TargetBrush { get => (Brush)GetValue(TargetBrushProperty); set => SetValue(TargetBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public Brush CurrentPointBrush { get => (Brush)GetValue(CurrentPointBrushProperty); set => SetValue(CurrentPointBrushProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth < 160 || ActualHeight < 80)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double left = 42;
        var right = ActualWidth - 10;
        const double top = 10;
        var bottom = ActualHeight - 31;
        var plotWidth = Math.Max(1, right - left);
        var plotHeight = Math.Max(1, bottom - top);
        var gridPen = CreatePen(GridBrush, 0.8);

        foreach (var tick in new[] { 100d, 75d, 50d, 25d, 0d })
        {
            var y = Y(tick, top, plotHeight);
            drawingContext.DrawLine(gridPen, new Point(left, y), new Point(right, y));
            DrawText(drawingContext, $"{tick:0}%", new Point(0, y - 8), LabelBrush, 11, dpi);
        }

        DrawDayDividers(drawingContext, gridPen, left, right, top, bottom);

        var elapsed = Math.Clamp(ElapsedPercent, 0, 100);
        var remaining = Math.Clamp(RemainingPercent, 0, 100);
        var projected = Math.Clamp(ProjectedRemaining, 0, 100);
        var nowX = left + plotWidth * elapsed / 100d;
        var nowY = Y(remaining, top, plotHeight);

        var targetPen = CreatePen(TargetBrush, 1.2, DashStyles.Dot);
        drawingContext.DrawLine(targetPen, new Point(left, top), new Point(right, bottom));

        var actualPen = CreatePen(ActualBrush, 2.8);
        var measured = BuildMeasuredPoints(left, top, plotWidth, plotHeight);
        DrawSmoothMeasuredLine(drawingContext, measured, actualPen);
        if (measured.Count > 0)
        {
            drawingContext.DrawEllipse(CurrentPointBrush, new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(110, 255, 255, 255)), 1), measured[^1], 3.6, 3.6);
        }

        if (HasPrediction)
        {
            var estimatePen = CreatePen(EstimateBrush, 1.8, DashStyles.Dash);
            var estimateEnd = new Point(right, Y(projected, top, plotHeight));
            if (ProjectedEmptyAt is { } empty && empty > DateTimeOffset.Now &&
                WindowStart is { } start && ResetAt is { } reset && reset > start)
            {
                var emptyFraction = Math.Clamp((empty - start).TotalSeconds / (reset - start).TotalSeconds, 0, 1);
                estimateEnd = new Point(left + plotWidth * emptyFraction, bottom);
            }

            drawingContext.DrawLine(estimatePen, new Point(nowX, nowY), estimateEnd);
        }

        var startLabel = WindowStart?.LocalDateTime.ToString("MMM d", CultureInfo.CurrentCulture) ?? "Start";
        var resetLabel = ResetAt?.LocalDateTime.ToString("MMM d, HH:mm", CultureInfo.CurrentCulture) ?? "Reset";
        DrawText(drawingContext, startLabel, new Point(left, bottom + 9), LabelBrush, 11, dpi);
        var resetText = CreateText(resetLabel, LabelBrush, 11, dpi);
        drawingContext.DrawText(resetText, new Point(right - resetText.Width, bottom + 9));
    }

    private void DrawDayDividers(DrawingContext context, Pen pen, double left, double right, double top, double bottom)
    {
        if (WindowStart is not { } start || ResetAt is not { } reset || reset <= start)
        {
            return;
        }

        var totalDays = (reset - start).TotalDays;
        if (totalDays <= 1)
        {
            return;
        }

        var divisions = totalDays <= 14 ? (int)Math.Floor(totalDays) : 7;
        for (var index = 1; index < divisions; index++)
        {
            var fraction = totalDays <= 14 ? index / totalDays : index / (double)divisions;
            var x = left + (right - left) * fraction;
            context.DrawLine(pen, new Point(x, top), new Point(x, bottom));
        }
    }

    private static double Y(double remaining, double top, double height) =>
        top + height * (1 - Math.Clamp(remaining, 0, 100) / 100d);

    private List<Point> BuildMeasuredPoints(
        double left,
        double top,
        double plotWidth,
        double plotHeight)
    {
        renderedSamples.Clear();
        if (WindowStart is not { } start || ResetAt is not { } reset || reset <= start)
        {
            return [];
        }

        var points = new List<Point>();
        var samples = (ActualPoints ?? [])
            .Where(sample => sample.ResetAt == reset && sample.Timestamp >= start && sample.Timestamp <= reset)
            .OrderBy(sample => sample.Timestamp)
            .ToArray();
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            var fraction = Math.Clamp((sample.Timestamp - start).TotalSeconds / (reset - start).TotalSeconds, 0, 1);
            var point = new Point(left + plotWidth * fraction, Y(sample.RemainingPercent, top, plotHeight));
            if (points.Count == 0 || point.X > points[^1].X + 0.5)
            {
                points.Add(point);
                var usedSincePrevious = index == 0
                    ? 0
                    : Math.Max(0, samples[index - 1].RemainingPercent - sample.RemainingPercent);
                renderedSamples.Add(new RenderedSample(point, sample, usedSincePrevious));
            }
            else
            {
                points[^1] = point;
                var usedSincePrevious = index == 0
                    ? 0
                    : Math.Max(0, samples[index - 1].RemainingPercent - sample.RemainingPercent);
                renderedSamples[^1] = new RenderedSample(point, sample, usedSincePrevious);
            }
        }

        return points;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs args)
    {
        if (renderedSamples.Count == 0)
        {
            ToolTip = null;
            return;
        }

        var position = args.GetPosition(this);
        var nearest = renderedSamples
            .OrderBy(sample => Math.Abs(sample.Point.X - position.X))
            .First();
        if (Math.Abs(nearest.Point.X - position.X) > 14)
        {
            ToolTip = null;
            return;
        }

        var delta = nearest.UsedSincePrevious > 0
            ? $" · {nearest.UsedSincePrevious} pts used since previous"
            : string.Empty;
        ToolTip = $"{nearest.Sample.Timestamp.LocalDateTime:g} · {nearest.Sample.RemainingPercent}% remaining{delta}";
    }

    private sealed record RenderedSample(Point Point, UsageHistorySample Sample, int UsedSincePrevious);

    private static void DrawSmoothMeasuredLine(DrawingContext context, IReadOnlyList<Point> points, Pen pen)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(points[0], false, false);
            for (var index = 0; index < points.Count - 1; index++)
            {
                var current = points[index];
                var next = points[index + 1];
                var previous = index > 0 ? points[index - 1] : current;
                var following = index + 2 < points.Count ? points[index + 2] : next;
                var width = Math.Max(1, next.X - current.X);
                var slopeIn = (next.Y - previous.Y) / Math.Max(1, next.X - previous.X);
                var slopeOut = (following.Y - current.Y) / Math.Max(1, following.X - current.X);
                var minimumY = Math.Min(current.Y, next.Y);
                var maximumY = Math.Max(current.Y, next.Y);
                var control1 = new Point(current.X + width / 3, Math.Clamp(current.Y + slopeIn * width / 3, minimumY, maximumY));
                var control2 = new Point(next.X - width / 3, Math.Clamp(next.Y - slopeOut * width / 3, minimumY, maximumY));
                figure.BezierTo(control1, control2, next, true, false);
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private static Pen CreatePen(Brush brush, double thickness, DashStyle? dashStyle = null)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = dashStyle ?? DashStyles.Solid,
        };
        return pen;
    }

    private static void DrawText(DrawingContext context, string text, Point origin, Brush brush, double size, double dpi) =>
        context.DrawText(CreateText(text, brush, size, dpi), origin);

    private static FormattedText CreateText(string text, Brush brush, double size, double dpi) => new(
        text,
        CultureInfo.CurrentCulture,
        System.Windows.FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        size,
        brush,
        dpi);
}
