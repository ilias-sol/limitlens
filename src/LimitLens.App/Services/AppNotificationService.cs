namespace LimitLens.App.Services;

public interface IAppNotificationSink
{
    void Show(string title, string message);
}

public sealed class AppNotificationService : IAppNotificationSink, IDisposable
{
    private readonly TrayIconService tray;

    public AppNotificationService(TrayIconService tray)
    {
        this.tray = tray;
    }

    public void Show(string title, string message) => tray.ShowBalloon(title, message);

    public void Dispose() { }
}
