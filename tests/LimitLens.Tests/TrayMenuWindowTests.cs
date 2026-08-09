using LimitLens.App.Views;

namespace LimitLens.Tests;

public sealed class TrayMenuWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dismiss_IsSafeWhenShutdownRequestsOverlap(bool useLightPalette)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var menu = new TrayMenuWindow(() => { }, () => { }, () => { }, useLightPalette);
                menu.Show();
                var expectedText = System.Windows.Media.Color.FromRgb(0x23, 0x27, 0x2F);
                var expectedBackground = System.Windows.Media.Color.FromArgb(0xFE, 0xFF, 0xFF, 0xFF);
                Assert.Equal(expectedText, Assert.IsType<System.Windows.Media.SolidColorBrush>(menu.Resources["TrayMenuTextBrush"]).Color);
                Assert.Equal(expectedBackground, Assert.IsType<System.Windows.Media.SolidColorBrush>(menu.Resources["TrayMenuBackgroundBrush"]).Color);
                foreach (var name in new[] { "OpenUsageButton", "SettingsMenuButton", "QuitButton" })
                {
                    var button = Assert.IsType<System.Windows.Controls.Button>(menu.FindName(name));
                    var label = Assert.IsType<System.Windows.Controls.TextBlock>(button.Content);
                    Assert.Equal(expectedText, Assert.IsType<System.Windows.Media.SolidColorBrush>(label.Foreground).Color);
                }
                menu.Dismiss();
                menu.Dismiss();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The tray-menu shutdown test did not finish.");
        Assert.Null(failure);
    }
}
