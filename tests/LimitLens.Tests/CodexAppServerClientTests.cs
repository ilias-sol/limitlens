using System.Reflection;
using System.Text.Json;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;
using LimitLens.Indexing.AppServer;

namespace LimitLens.Tests;

public sealed class CodexAppServerClientTests
{
    [Fact]
    public async Task ParsesDynamicBucketsOptionalUsageAndUnknownFields()
    {
        await using var client = new CodexAppServerClient(new DashboardSettings());
        Assert.True((bool)Invoke(client, "ApplyAccount", Element("""
            {"account":{"type":"chatgpt","planType":"plus"}}
            """))!);
        _ = Invoke(client, "ApplyUsage", Element("""
            {
              "summary":{"lifetimeTokens":123456,"peakDailyTokens":4567,"currentStreakDays":4,"longestStreakDays":9,"longestRunningTurnSec":321},
              "dailyUsageBuckets":[{"startDate":"2026-08-04","tokens":2345}],
              "futureUsageField":{"kept":true}
            }
            """));
        _ = Invoke(client, "ApplyRateLimits", Element("""
            {
              "rateLimitsByLimitId":{
                "codex":{"limitId":"codex","limitName":"Codex","primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1785888000},"credits":{"hasCredits":true,"unlimited":false,"balance":"12.5"},"futureBucketField":17},
                "review":{"limitId":"review","limitName":"Review","primary":{"usedPercent":10}}
              },
              "rateLimitResetCredits":{"availableCount":2},
              "futureRateRoot":"preserved"
            }
            """));
        _ = Invoke(client, "PublishSnapshot");

        Assert.Equal("plus", client.Current.PlanType);
        Assert.Equal(123456, client.Current.Summary.LifetimeTokens);
        Assert.Equal(2345, Assert.Single(client.Current.DailyUsage).Tokens);
        Assert.Equal(2, client.Current.RateLimits.Count);
        Assert.Equal(2, client.Current.ResetCredits?.AvailableCount);
        Assert.True(client.Current.Extensions?.ContainsKey("usage.futureUsageField"));
        Assert.True(client.Current.Extensions?.ContainsKey("rateLimits.futureRateRoot"));
        Assert.Equal(17, client.Current.RateLimits.Single(bucket => bucket.Id == "codex").Extensions?["futureBucketField"].GetInt32());
    }

    [Fact]
    public async Task SparseNotificationKeepsTheLatestCompleteWindow()
    {
        await using var client = new CodexAppServerClient(new DashboardSettings());
        _ = Invoke(client, "ApplyRateLimits", Element("""
            {"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"usedPercent":40,"resetsAt":1785888000}}}}
            """));
        _ = Invoke(client, "PublishSnapshot");

        _ = Invoke(client, "ApplyNotification", "account/rateLimits/updated", Element("""
            {"rateLimits":{"limitId":"codex","secondary":{"usedPercent":75,"resetsAt":1786147200}}}
            """));

        var bucket = Assert.Single(client.Current.RateLimits);
        Assert.Equal(40, bucket.Primary?.UsedPercent);
        Assert.Equal(75, bucket.Secondary?.UsedPercent);
    }

    [Theory]
    [InlineData("{\"account\":null}", SourceConnectionState.SignedOut)]
    [InlineData("{\"account\":{\"type\":\"apiKey\"}}", SourceConnectionState.LocalOnly)]
    public async Task ReportsUnavailableAccountModesWithoutZeroingLocalAnalytics(
        string payload,
        SourceConnectionState expectedState)
    {
        await using var client = new CodexAppServerClient(new DashboardSettings());

        var available = (bool)Invoke(client, "ApplyAccount", Element(payload))!;

        Assert.False(available);
        Assert.Equal(expectedState, client.Health.State);
    }

    private static object? Invoke(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        try
        {
            return method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
