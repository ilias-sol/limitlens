using System.Globalization;
using System.Text.Json;
using LimitLens.Core.Settings;

namespace LimitLens.Indexing.Parsing;

internal sealed record RateLimitHistoryParseResult(
    IReadOnlyList<UsageHistorySample> Samples,
    long CompletedOffset);

internal static class RateLimitHistoryParser
{
    public static async Task<RateLimitHistoryParseResult> ParseAsync(
        string fullPath,
        long startOffset,
        CancellationToken cancellationToken = default)
    {
        var information = new FileInfo(fullPath);
        var completedOffset = startOffset >= 0 && startOffset <= information.Length
            ? startOffset
            : 0;
        var samples = new List<UsageHistorySample>();

        await foreach (var line in JsonlRecordReader.ReadCompletedLinesAsync(
            fullPath,
            completedOffset,
            cancellationToken).ConfigureAwait(false))
        {
            completedOffset = line.EndOffset;
            if (!line.Text.Contains("\"token_count\"", StringComparison.Ordinal) ||
                !line.Text.Contains("\"rate_limits\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line.Text);
                if (TryReadSample(document.RootElement, out var sample))
                {
                    samples.Add(sample);
                }
            }
            catch (JsonException)
            {
                // Malformed and future records are isolated without retaining their contents.
            }
        }

        return new RateLimitHistoryParseResult(samples, completedOffset);
    }

    private static bool TryReadSample(JsonElement root, out UsageHistorySample sample)
    {
        sample = new UsageHistorySample();
        if (!HasStringValue(root, "type", "event_msg") ||
            !root.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !HasStringValue(payload, "type", "token_count") ||
            !payload.TryGetProperty("rate_limits", out var limits) ||
            limits.ValueKind != JsonValueKind.Object ||
            !limits.TryGetProperty("primary", out var primary) ||
            primary.ValueKind != JsonValueKind.Object ||
            !TryReadTimestamp(root, "timestamp", out var timestamp) ||
            !TryReadDouble(primary, "used_percent", out var usedPercent) ||
            !TryReadUnixTimestamp(primary, "resets_at", out var resetAt) ||
            usedPercent is < 0 or > 100 ||
            resetAt <= timestamp)
        {
            return false;
        }

        if (TryReadDouble(primary, "window_minutes", out var windowMinutes) && windowMinutes > 0)
        {
            var windowStart = resetAt - TimeSpan.FromMinutes(windowMinutes);
            if (timestamp < windowStart - TimeSpan.FromMinutes(2) ||
                timestamp > resetAt + TimeSpan.FromMinutes(1))
            {
                return false;
            }
        }

        sample = new UsageHistorySample
        {
            Timestamp = timestamp,
            RemainingPercent = Math.Clamp(
                100 - (int)Math.Round(usedPercent, MidpointRounding.AwayFromZero),
                0,
                100),
            ResetAt = resetAt,
        };
        return true;
    }

    private static bool HasStringValue(JsonElement element, string propertyName, string expected) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expected, StringComparison.Ordinal);

    private static bool TryReadTimestamp(
        JsonElement element,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(
                   property.GetString(),
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out value);
    }

    private static bool TryReadDouble(JsonElement element, string propertyName, out double value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value) &&
               double.IsFinite(value);
    }

    private static bool TryReadUnixTimestamp(
        JsonElement element,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var seconds))
        {
            return false;
        }

        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
