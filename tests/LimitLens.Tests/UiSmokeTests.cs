using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LimitLens.App;
using LimitLens.App.Controls;
using LimitLens.App.Converters;
using LimitLens.App.Services;
using LimitLens.App.Taskbar;
using LimitLens.App.ViewModels;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;

namespace LimitLens.Tests;

public sealed class UiSmokeTests
{
    [Theory]
    [InlineData(26, 63, 71, 77)]
    [InlineData(25, 255, 157, 61)]
    [InlineData(10, 255, 99, 112)]
    public void UsageBarUsesAccessibleThresholdColours(double remaining, byte red, byte green, byte blue)
    {
        var converter = new UsageRemainingBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(converter.Convert(
            [remaining, DashboardTheme.Light],
            typeof(Brush),
            null!,
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(Color.FromRgb(red, green, blue), brush.Color);
    }

    [Fact]
    public void UsageBarUsesAContrastingLightFillInDarkMode()
    {
        var converter = new UsageRemainingBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(converter.Convert(
            [67d, DashboardTheme.DarkGlass],
            typeof(Brush),
            null!,
            System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(Color.FromRgb(193, 196, 203), brush.Color);
    }

    [Fact]
    public void UsageTrajectoryDrawsTheMeasuredCurveFromTheWindowStart()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var now = DateTimeOffset.Now;
                var reset = now.AddDays(6);
                var start = reset - TimeSpan.FromDays(7);
                var points = new[]
                {
                    new UsageHistorySample { Timestamp = start, ResetAt = reset, RemainingPercent = 100 },
                    new UsageHistorySample { Timestamp = start.AddHours(2), ResetAt = reset, RemainingPercent = 88 },
                    new UsageHistorySample { Timestamp = start.AddHours(5), ResetAt = reset, RemainingPercent = 73 },
                    new UsageHistorySample { Timestamp = start.AddHours(9), ResetAt = reset, RemainingPercent = 61 },
                    new UsageHistorySample { Timestamp = start.AddHours(15), ResetAt = reset, RemainingPercent = 50 },
                    new UsageHistorySample { Timestamp = now, ResetAt = reset, RemainingPercent = 41 },
                };
                var chart = new UsageForecastChart
                {
                    WindowStart = start,
                    ResetAt = reset,
                    RemainingPercent = 41,
                    ElapsedPercent = (now - start).TotalSeconds / (reset - start).TotalSeconds * 100,
                    ActualPoints = points,
                    ActualBrush = new SolidColorBrush(Color.FromRgb(0x68, 0xA4, 0xFF)),
                    CurrentPointBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB1, 0x8C)),
                    GridBrush = new SolidColorBrush(Color.FromArgb(0x20, 0x80, 0x80, 0x80)),
                    LabelBrush = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
                    TargetBrush = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)),
                };

                var bitmap = RenderElement(chart, 390, 190);
                var stride = bitmap.PixelWidth * 4;
                var pixels = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels(pixels, stride, 0);
                var measuredXs = new List<int>();
                for (var y = 0; y < bitmap.PixelHeight; y++)
                {
                    for (var x = 0; x < bitmap.PixelWidth; x++)
                    {
                        var offset = y * stride + x * 4;
                        if (pixels[offset] > 220 &&
                            pixels[offset + 1] > 120 &&
                            pixels[offset + 2] < 150 &&
                            pixels[offset + 3] > 100)
                        {
                            measuredXs.Add(x);
                        }
                    }
                }

                Assert.NotEmpty(measuredXs);
                Assert.InRange(measuredXs.Min(), 39, 48);
                Assert.True(measuredXs.Max() > 80, "The measured curve must reach the current point.");
                CaptureIfRequested(chart, "trajectory-history", 390, 190);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The trajectory render test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void TaskbarFlyoutLoadsWithoutBindingFailures()
    {
        Exception? failure = null;
        var step = "thread creation";
        var thread = new Thread(() =>
        {
            try
            {
                step = "application creation";
                var application = new LimitLens.App.App();
                application.InitializeComponent();
                step = "view-model creation";
                var settings = new DashboardSettings { Theme = DashboardTheme.Light };
                using var model = new DashboardViewModel(
                    new FakeAccountClient(AccountUsageSnapshot.Empty with { PlanType = "plus" }),
                    new FakeIndexer(),
                    new FakeSettingsStore(settings),
                    new FakeStartupService(),
                    settings,
                    "Data");
                step = "window creation";
                var window = new MainWindow(model);
                Assert.Equal("Limit Lens", window.Title);
                Assert.False(window.AllowsTransparency);
                Assert.True(window.Topmost);
                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
                Assert.Equal(new Thickness(0), chrome?.GlassFrameThickness);
                Assert.Equal(new CornerRadius(0), chrome?.CornerRadius);
                Assert.Equal(new CornerRadius(0), Assert.IsType<System.Windows.Controls.Border>(window.FindName("FlyoutRoot")).CornerRadius);
                Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(window));
                Assert.Equal(TextRenderingMode.Auto, TextOptions.GetTextRenderingMode(window));
                step = "layout and binding evaluation";
                window.Measure(new Size(420, 484));
                window.Arrange(new Rect(0, 0, 420, 484));
                window.UpdateLayout();
                UpdateBindings(window);
                var usageRemainingLabel = Assert.IsType<System.Windows.Controls.TextBlock>(window.FindName("UsageRemainingLabel"));
                var usageRemainingProgress = Assert.IsType<System.Windows.Controls.ProgressBar>(window.FindName("UsageRemainingProgress"));
                var usageBrushBinding = BindingOperations.GetBindingExpression(usageRemainingProgress, System.Windows.Controls.Control.ForegroundProperty);
                Assert.NotNull(usageBrushBinding);
                Assert.Equal(Color.FromRgb(63, 71, 77), Assert.IsType<SolidColorBrush>(model.UsageRemainingBrush).Color);
                Assert.Equal("Usage remaining", usageRemainingLabel.Text);
                Assert.NotNull(window.FindName("RefreshButton"));
                Assert.NotNull(window.FindName("SettingsButton"));
                var footerPlanText = Assert.IsType<System.Windows.Controls.TextBlock>(window.FindName("FooterPlanText"));
                Assert.NotNull(BindingOperations.GetBindingExpression(footerPlanText, System.Windows.Controls.TextBlock.TextProperty));
                Assert.Equal("Plus", model.PlanShortText);
                Assert.Null(window.FindName("HeaderPlanText"));
                CaptureIfRequested(Assert.IsAssignableFrom<FrameworkElement>(window.Content), "flyout", 420, 484);
                Assert.Equal(Color.FromArgb(0x64, 0xFF, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(window.Resources["FlyoutLayer"]).Color);
                Assert.Equal(Color.FromArgb(0x7C, 0xFF, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(window.Resources["FlyoutLayerStrong"]).Color);
                Assert.Equal(Color.FromArgb(0xA4, 0xF3, 0xF4, 0xF5), Assert.IsType<SolidColorBrush>(window.Resources["FlyoutSurface"]).Color);
                Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(window.Resources["FlyoutCardBorderThickness"]));
                model.UseLightTheme = false;
                UpdateBindings(window);
                Assert.Equal(DashboardTheme.DarkGlass, model.SelectedTheme);
                Assert.Equal(Color.FromRgb(193, 196, 203), Assert.IsType<SolidColorBrush>(model.UsageRemainingBrush).Color);
                Assert.Equal(Color.FromArgb(0x64, 0x39, 0x43, 0x4C), Assert.IsType<SolidColorBrush>(window.Resources["FlyoutLayer"]).Color);
                Assert.Equal(Color.FromArgb(0x7C, 0x3D, 0x47, 0x50), Assert.IsType<SolidColorBrush>(window.Resources["FlyoutLayerStrong"]).Color);
                Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(window.Resources["FlyoutCardBorderThickness"]));
                var expectedDarkText = Color.FromRgb(0xF4, 0xF5, 0xF7);
                Assert.Equal(expectedDarkText, Assert.IsType<SolidColorBrush>(Assert.IsType<System.Windows.Controls.TextBlock>(window.FindName("ShowCreditsLabel")).Foreground).Color);
                Assert.Equal(expectedDarkText, Assert.IsType<SolidColorBrush>(Assert.IsType<System.Windows.Controls.TextBlock>(window.FindName("StartWithWindowsLabel")).Foreground).Color);
                Assert.Equal(expectedDarkText, Assert.IsType<SolidColorBrush>(Assert.IsType<System.Windows.Controls.TextBlock>(window.FindName("UsageAlertsLabel")).Foreground).Color);
                CaptureIfRequested(Assert.IsAssignableFrom<FrameworkElement>(window.Content), "flyout-dark", 420, 484);
                model.ShowWidgetSettings = true;
                UpdateBindings(window);
                Assert.Equal(420, window.Height);
                Assert.NotNull(window.FindName("AppearanceLabel"));
                var positionLeft = Assert.IsType<System.Windows.Controls.RadioButton>(window.FindName("FlyoutPositionLeft"));
                var positionCenter = Assert.IsType<System.Windows.Controls.RadioButton>(window.FindName("FlyoutPositionCenter"));
                var positionRight = Assert.IsType<System.Windows.Controls.RadioButton>(window.FindName("FlyoutPositionRight"));
                Assert.True(model.IsFlyoutPositionRight);
                model.IsFlyoutPositionCenter = true;
                UpdateBindings(window);
                positionCenter.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
                window.UpdateLayout();
                Assert.Equal(FlyoutPosition.Center, model.SelectedFlyoutPosition);
                Assert.NotNull(BindingOperations.GetBindingExpression(positionLeft, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty));
                Assert.NotNull(BindingOperations.GetBindingExpression(positionCenter, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty));
                Assert.NotNull(BindingOperations.GetBindingExpression(positionRight, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty));
                var positionLeftLabel = Assert.IsType<System.Windows.Controls.TextBlock>(positionLeft.Content);
                var positionCenterLabel = Assert.IsType<System.Windows.Controls.TextBlock>(positionCenter.Content);
                var positionRightLabel = Assert.IsType<System.Windows.Controls.TextBlock>(positionRight.Content);
                Assert.Equal(expectedDarkText, Assert.IsType<SolidColorBrush>(positionLeftLabel.Foreground).Color);
                Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(positionCenterLabel.Foreground).Color);
                Assert.Equal(expectedDarkText, Assert.IsType<SolidColorBrush>(positionRightLabel.Foreground).Color);
                model.UseLightTheme = true;
                UpdateBindings(window);
                window.UpdateLayout();
                var expectedLightText = Color.FromRgb(0x25, 0x29, 0x32);
                Assert.Equal(expectedLightText, Assert.IsType<SolidColorBrush>(positionLeftLabel.Foreground).Color);
                Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(positionCenterLabel.Foreground).Color);
                Assert.Equal(expectedLightText, Assert.IsType<SolidColorBrush>(positionRightLabel.Foreground).Color);
                CaptureIfRequested(Assert.IsAssignableFrom<FrameworkElement>(window.Content), "settings", 420, 420);
                model.ShowWidgetSettings = false;
                Assert.Equal(484, window.Height);
                step = "taskbar indicator creation";
                var taskbarIndicator = new TaskbarWidgetView { DataContext = model };
                taskbarIndicator.ApplyTaskbarTheme(useDarkText: true);
                taskbarIndicator.Measure(new Size(134, 44));
                taskbarIndicator.Arrange(new Rect(0, 0, 134, 44));
                taskbarIndicator.UpdateLayout();
                UpdateBindings(taskbarIndicator);
                CaptureIfRequested(taskbarIndicator, "taskbar", 134, 44);
                var usageBar = Assert.IsType<System.Windows.Controls.ProgressBar>(taskbarIndicator.FindName("UsageBar"));
                usageBar.Value = 14;
                taskbarIndicator.UpdateLayout();
                var track = Assert.IsType<System.Windows.Controls.Border>(usageBar.Template.FindName("PART_Track", usageBar));
                var indicator = Assert.IsType<System.Windows.Controls.Border>(usageBar.Template.FindName("PART_Indicator", usageBar));
                var percentText = Assert.IsType<System.Windows.Controls.TextBlock>(taskbarIndicator.FindName("PercentText"));
                var trackRight = track.TranslatePoint(new Point(track.ActualWidth, 0), taskbarIndicator).X;
                var percentRight = percentText.TranslatePoint(new Point(percentText.ActualWidth, 0), taskbarIndicator).X;
                Assert.InRange(trackRight - percentRight, 1.5, 2.5);
                Assert.True(indicator.ActualWidth > 0, "A 14% remaining value must draw a visible indicator segment.");
                Assert.True(indicator.ActualWidth < track.ActualWidth, "A 14% remaining value must not fill the entire track.");
                step = "complete";
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(15)),
            $"The WPF smoke-test thread did not complete; last step: {step}; dispatcher failure: {failure}.");

        Assert.Null(failure);
    }

    private static void CaptureIfRequested(FrameworkElement element, string name, int width, int height)
    {
        var directory = Environment.GetEnvironmentVariable("LIMIT_LENS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var bitmap = RenderElement(element, width, height);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(stream);
    }

    private static RenderTargetBitmap RenderElement(
        FrameworkElement element,
        int width,
        int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void UpdateBindings(DependencyObject root)
    {
        var values = root.GetLocalValueEnumerator();
        while (values.MoveNext())
        {
            if (values.Current.Value is BindingExpressionBase binding)
            {
                binding.UpdateTarget();
            }
        }

        if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D)
        {
            return;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            UpdateBindings(VisualTreeHelper.GetChild(root, index));
        }
    }

    private sealed class FakeAccountClient(AccountUsageSnapshot? snapshot = null) : ICodexAppServerClient
    {
        public AccountUsageSnapshot Current { get; } = snapshot ?? AccountUsageSnapshot.Empty;
        public SourceHealth Health { get; } = SourceHealth.Starting("test");
        public event Action<AccountUsageSnapshot>? SnapshotChanged;
        public event Action<SourceHealth>? HealthChanged;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void RaiseSnapshot() => SnapshotChanged?.Invoke(Current);
        public void RaiseHealth() => HealthChanged?.Invoke(Health);
    }

    private sealed class FakeIndexer : ISessionLogIndexer
    {
        public LocalUsageAggregate Current { get; } = LocalUsageAggregate.Empty;
        public IReadOnlyList<UsageHistorySample> RateLimitHistory { get; } = [];
        public SourceHealth Health { get; } = SourceHealth.Starting("test");
        public event Action<LocalUsageAggregate>? SnapshotChanged;
        public event Action<SourceHealth>? HealthChanged;
        public event Action<double>? BackfillProgressChanged;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RebuildAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void RaiseSnapshot() => SnapshotChanged?.Invoke(Current);
        public void RaiseHealth() => HealthChanged?.Invoke(Health);
        public void RaiseProgress() => BackfillProgressChanged?.Invoke(1);
    }

    private sealed class FakeSettingsStore(DashboardSettings settings) : ISettingsStore
    {
        public DashboardSettings Current { get; private set; } = settings;
        public Task<DashboardSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(DashboardSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStartupService : IStartupRegistrationService
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }
}
