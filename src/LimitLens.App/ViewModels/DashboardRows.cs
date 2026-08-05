namespace LimitLens.App.ViewModels;

public sealed record LimitRowViewModel(
    string Id,
    string Name,
    string Plan,
    double PrimaryUsed,
    string PrimaryText,
    string PrimaryReset);

public sealed record DailyRowViewModel(
    string Date,
    long Tokens,
    string TokensText,
    int Tasks,
    string Completion,
    string Duration);

public sealed record ProjectRowViewModel(
    string Name,
    string Tokens,
    int Tasks,
    string Duration,
    double Share);

public sealed record ModelRowViewModel(string Name, string Tokens, int Tasks, double Share);

public sealed class DashboardCardViewModel(
    string id,
    string title,
    string value,
    string detail,
    double progress,
    bool hasProgress,
    bool isLimit)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Value { get; } = value;
    public string Detail { get; } = detail;
    public double Progress { get; } = progress;
    public bool HasProgress { get; } = hasProgress;
    public bool IsLimit { get; } = isLimit;
}

public sealed class CardPreferenceViewModel : ObservableObject
{
    private readonly Action<CardPreferenceViewModel> changed;
    private bool isVisible;
    private bool isCompact;

    public CardPreferenceViewModel(
        string id,
        string name,
        bool visible,
        bool compact,
        Action<CardPreferenceViewModel> changed)
    {
        Id = id;
        Name = name;
        isVisible = visible;
        isCompact = compact;
        this.changed = changed;
    }

    public string Id { get; }
    public string Name { get; }

    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (SetProperty(ref isVisible, value))
            {
                changed(this);
            }
        }
    }

    public bool IsCompact
    {
        get => isCompact;
        set
        {
            if (SetProperty(ref isCompact, value))
            {
                changed(this);
            }
        }
    }

    internal void SetCompactSilently(bool value) => SetProperty(ref isCompact, value, nameof(IsCompact));
}
