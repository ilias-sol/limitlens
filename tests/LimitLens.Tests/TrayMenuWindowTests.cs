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
