using System.Text.Json;
using System.Text.Json.Serialization;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Settings;

namespace LimitLens.Indexing.Storage;

public sealed class JsonSettingsStore(AppStoragePaths paths) : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim gate = new(1, 1);

    public DashboardSettings Current { get; private set; } = new();

    public async Task<DashboardSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureCreated();
            if (!File.Exists(paths.SettingsPath))
            {
                Current = Normalize(new DashboardSettings());
                await WriteCoreAsync(Current, cancellationToken).ConfigureAwait(false);
                return Current;
            }

            try
            {
                await using var stream = new FileStream(
                    paths.SettingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                Current = Normalize(
                    await JsonSerializer.DeserializeAsync<DashboardSettings>(
                        stream,
                        SerializerOptions,
                        cancellationToken).ConfigureAwait(false)
                    ?? new DashboardSettings());
            }
            catch (JsonException)
            {
                Current = Normalize(new DashboardSettings());
            }

            // Persist migrations and repaired values (especially the privacy salt) so normalization
            // remains stable across launches instead of generating a new identity on every load.
            await WriteCoreAsync(Current, cancellationToken).ConfigureAwait(false);
            return Current;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        DashboardSettings settings,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Current = Normalize(settings);
            await WriteCoreAsync(Current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task WriteCoreAsync(
        DashboardSettings settings,
        CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        var temporaryPath = Path.Combine(
            paths.RootDirectory,
            $"settings.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, paths.SettingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static DashboardSettings Normalize(DashboardSettings settings)
    {
        settings.TaskbarPositionPercent = Math.Clamp(settings.TaskbarPositionPercent, 0, DashboardSettings.MaxTaskbarPosition);
        if (!Enum.IsDefined(settings.TaskbarTextColorMode)) settings.TaskbarTextColorMode = TaskbarColorMode.Automatic;
        if (!Enum.IsDefined(settings.TaskbarBarColorMode)) settings.TaskbarBarColorMode = TaskbarColorMode.Automatic;
        TaskbarColors.TryNormalizeHex(settings.TaskbarCustomTextColor, out var textColor);
        TaskbarColors.TryNormalizeHex(settings.TaskbarCustomBarColor, out var barColor);
        settings.TaskbarCustomTextColor = textColor;
        settings.TaskbarCustomBarColor = barColor;
        settings.CompactPlacement ??= new WindowPlacementSettings();
        settings.ExpandedPlacement ??= new WindowPlacementSettings();
        settings.AlertThresholds ??= [];
        var previousSchema = settings.SchemaVersion;
        if (previousSchema < 7)
        {
            settings.Theme = DashboardTheme.Light;
            settings.CompactPlacement.Width = 340;
            settings.CompactPlacement.Height = 56;
            settings.ExpandedPlacement.Width = 340;
            settings.ExpandedPlacement.Height = 420;
        }

        if (previousSchema < 8 && settings.AlertThresholds.Order().SequenceEqual(new[] { 80, 95 }))
        {
            settings.AlertThresholds = [25, 10, 0];
            settings.AlertDedupeKeys = [];
        }

        settings.SchemaVersion = DashboardSettings.CurrentSchemaVersion;
        if (!Enum.IsDefined(settings.FlyoutPosition))
        {
            settings.FlyoutPosition = FlyoutPosition.Right;
        }
        settings.AccountRefreshSeconds = Math.Clamp(settings.AccountRefreshSeconds, 30, 900);
        settings.WidgetOpacity = Math.Clamp(settings.WidgetOpacity, 0.65, 1);
        settings.UsageHistory = (settings.UsageHistory ?? [])
            .Where(sample => sample.Timestamp != default &&
                             sample.ResetAt > sample.Timestamp &&
                             (previousSchema >= 10 || sample.WindowDurationMinutes is > 0) &&
                             sample.RemainingPercent is >= 0 and <= 100)
            .OrderBy(sample => sample.Timestamp)
            .TakeLast(512)
            .ToList();
        settings.AlertThresholds = settings.AlertThresholds
            .Where(value => value is >= 0 and <= 100)
            .Distinct()
            .Order()
            .DefaultIfEmpty(10)
            .ToArray();
        settings.AlertDedupeKeys = (settings.AlertDedupeKeys ?? [])
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .TakeLast(256)
            .ToList();

        if (settings.AlertsMutedUntil <= DateTimeOffset.Now)
        {
            settings.AlertsMutedUntil = null;
        }

        settings.CardOrder = NormalizeCardList(settings.CardOrder, includeMissingDefaults: true);
        settings.VisibleCards = NormalizeCardList(settings.VisibleCards, includeMissingDefaults: false);
        settings.CompactCards = NormalizeCardList(settings.CompactCards, includeMissingDefaults: false)
            .Take(4)
            .ToList();

        if (!IsValidPrivacySalt(settings.PrivacySalt))
        {
            settings.PrivacySalt = Convert.ToHexString(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        }

        return settings;
    }

    private static bool IsValidPrivacySalt(string? value)
    {
        return value is { Length: 32 } && value.All(static character =>
            character is >= '0' and <= '9' or
                >= 'A' and <= 'F' or
                >= 'a' and <= 'f');
    }

    private static List<string> NormalizeCardList(
        IEnumerable<string>? values,
        bool includeMissingDefaults)
    {
        var known = DashboardCardIds.DefaultOrder.ToHashSet(StringComparer.Ordinal);
        var normalized = (values ?? [])
            .Where(known.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (includeMissingDefaults)
        {
            normalized.AddRange(DashboardCardIds.DefaultOrder.Where(card => !normalized.Contains(card)));
        }

        return normalized;
    }
}
