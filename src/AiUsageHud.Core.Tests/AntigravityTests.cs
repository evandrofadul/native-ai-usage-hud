using System.Text;
using System.Text.Json;
using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Models;
using AiUsageHud.Core.Vendors.Antigravity;

namespace AiUsageHud.Core.Tests;

public class AntigravityTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void ParsesUserStatusPlanAndAccount()
    {
        var status = AntigravityUserStatusResponse.Parse(B("""
            {
              "userStatus": {
                "email": "developer@google.com",
                "userTier": {
                  "name": "Google AI Pro",
                  "description": "Pro Tier"
                }
              }
            }
            """));

        Assert.Equal("Google AI Pro", status.PlanLabel());
        Assert.StartsWith("acct:", status.AccountKey());
        Assert.NotEqual("acct:unknown", status.AccountKey());
    }

    [Fact]
    public void FallbackPlanWhenEmpty()
    {
        var status = AntigravityUserStatusResponse.Parse(B("{}"));
        Assert.Equal("Antigravity", status.PlanLabel());
        Assert.Equal("acct:unknown", status.AccountKey());
    }

    [Fact]
    public void ParsesFullQuotaSummary()
    {
        var raw = """
            {
              "response": {
                "groups": [
                  {
                    "displayName": "Gemini",
                    "buckets": [
                      {
                        "bucketId": "gemini-5h",
                        "window": "5h",
                        "remainingFraction": 0.57,
                        "resetTime": "2026-07-22T14:00:00Z"
                      },
                      {
                        "bucketId": "gemini-weekly",
                        "window": "weekly",
                        "remainingFraction": 0.92,
                        "resetTime": "2026-07-28T17:39:58Z"
                      }
                    ]
                  },
                  {
                    "displayName": "Claude & GPT OSS",
                    "buckets": [
                      {
                        "bucketId": "3p-5h",
                        "window": "5h",
                        "remainingFraction": 0.25,
                        "resetTime": "2026-07-22T16:30:00Z"
                      },
                      {
                        "bucketId": "3p-weekly",
                        "window": "weekly",
                        "remainingFraction": 1.0,
                        "resetTime": "2026-07-29T12:47:00Z"
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var snap = AntigravityQuotaResponse.Parse(B(raw)).ToSnapshot("Google AI Pro", "acct:test");

        Assert.Equal("Google AI Pro", snap.Plan);
        Assert.Equal("acct:test", snap.Account);

        // Gemini 5h: 1.0 - 0.57 = 0.43 -> 43%
        Assert.Equal(43, snap.Session.UtilizationPct);
        Assert.Equal(TimeSpan.FromHours(5), snap.Session.WindowDuration);
        Assert.NotNull(snap.Session.ResetsAt);

        // Gemini weekly: 1.0 - 0.92 = 0.08 -> 8%
        Assert.Equal(8, snap.Weekly.UtilizationPct);
        Assert.Equal(TimeSpan.FromDays(7), snap.Weekly.WindowDuration);
        Assert.NotNull(snap.Weekly.ResetsAt);

        // 3P 5h: 1.0 - 0.25 = 0.75 -> 75%
        Assert.NotNull(snap.ThirdPartySession);
        Assert.Equal(75, snap.ThirdPartySession!.Value.UtilizationPct);
        Assert.Equal(TimeSpan.FromHours(5), snap.ThirdPartySession.Value.WindowDuration);

        // 3P weekly: 1.0 - 1.0 = 0.0 -> 0%
        Assert.NotNull(snap.ThirdPartyWeekly);
        Assert.Equal(0, snap.ThirdPartyWeekly!.Value.UtilizationPct);
        Assert.Equal(TimeSpan.FromDays(7), snap.ThirdPartyWeekly.Value.WindowDuration);
    }

    [Fact]
    public void ParsesQuotaSummaryWithoutThirdParty()
    {
        var raw = """
            {
              "groups": [
                {
                  "displayName": "Gemini",
                  "buckets": [
                    {
                      "bucketId": "gemini-5h",
                      "window": "5h",
                      "remainingFraction": 0.5
                    },
                    {
                      "bucketId": "gemini-weekly",
                      "window": "weekly",
                      "remainingFraction": 0.8
                    }
                  ]
                }
              ]
            }
            """;

        var snap = AntigravityQuotaResponse.Parse(B(raw)).ToSnapshot("Antigravity");
        Assert.Equal(50, snap.Session.UtilizationPct);
        Assert.Equal(20, snap.Weekly.UtilizationPct);
        Assert.Null(snap.ThirdPartySession);
        Assert.Null(snap.ThirdPartyWeekly);
    }

    [Fact]
    public void ThrowsOnMissingGeminiBuckets()
    {
        var rawMissingWeekly = """
            {
              "groups": [
                {
                  "displayName": "Gemini",
                  "buckets": [
                    { "bucketId": "gemini-5h", "window": "5h", "remainingFraction": 0.5 }
                  ]
                }
              ]
            }
            """;
        Assert.Throws<OtherException>(() => AntigravityQuotaResponse.Parse(B(rawMissingWeekly)).ToSnapshot("Antigravity"));
    }

    [Fact]
    public void ThrowsOnInvalidRemainingFraction()
    {
        var rawOutOfRange = """
            {
              "groups": [
                {
                  "displayName": "Gemini",
                  "buckets": [
                    { "bucketId": "gemini-5h", "window": "5h", "remainingFraction": 1.5 },
                    { "bucketId": "gemini-weekly", "window": "weekly", "remainingFraction": 0.5 }
                  ]
                }
              ]
            }
            """;
        Assert.Throws<SchemaException>(() => AntigravityQuotaResponse.Parse(B(rawOutOfRange)).ToSnapshot("Antigravity"));
    }

    [Fact]
    public void ThrowsOnDuplicateBucket()
    {
        var rawDuplicate = """
            {
              "groups": [
                {
                  "displayName": "Gemini",
                  "buckets": [
                    { "bucketId": "gemini-5h", "window": "5h", "remainingFraction": 0.5 },
                    { "bucketId": "gemini-5h", "window": "5h", "remainingFraction": 0.4 },
                    { "bucketId": "gemini-weekly", "window": "weekly", "remainingFraction": 0.5 }
                  ]
                }
              ]
            }
            """;
        Assert.Throws<SchemaException>(() => AntigravityQuotaResponse.Parse(B(rawDuplicate)).ToSnapshot("Antigravity"));
    }

    [Fact]
    public void CacheDtoRoundtripsSnapshot()
    {
        var original = new AntigravitySnapshot(
            "Google AI Pro",
            "acct:12345",
            new UsageWindow(40, DateTimeOffset.UtcNow.AddHours(2), TimeSpan.FromHours(5)),
            new UsageWindow(15, DateTimeOffset.UtcNow.AddDays(5), TimeSpan.FromDays(7)),
            new UsageWindow(70, DateTimeOffset.UtcNow.AddHours(4), TimeSpan.FromHours(5)),
            new UsageWindow(5, DateTimeOffset.UtcNow.AddDays(6), TimeSpan.FromDays(7)));

        var bytes = AntigravityCacheDto.Serialize(original);
        var restored = AntigravityCacheDto.Deserialize(bytes);

        Assert.Equal(original.Plan, restored.Plan);
        Assert.Equal(original.Account, restored.Account);
        Assert.Equal(original.Session.UtilizationPct, restored.Session.UtilizationPct);
        Assert.Equal(original.Weekly.UtilizationPct, restored.Weekly.UtilizationPct);
        Assert.Equal(original.ThirdPartySession?.UtilizationPct, restored.ThirdPartySession?.UtilizationPct);
        Assert.Equal(original.ThirdPartyWeekly?.UtilizationPct, restored.ThirdPartyWeekly?.UtilizationPct);
    }

    [Fact]
    public void NormalizeBaseHandlesVariousInputs()
    {
        Assert.Equal("http://127.0.0.1:8000", AntigravityDiscovery.NormalizeBase("127.0.0.1:8000"));
        Assert.Equal("http://localhost:9090", AntigravityDiscovery.NormalizeBase("http://localhost:9090/"));
        Assert.Equal("https://127.0.0.1:8443", AntigravityDiscovery.NormalizeBase("https://127.0.0.1:8443///"));
        Assert.Null(AntigravityDiscovery.NormalizeBase("   "));
        Assert.Null(AntigravityDiscovery.NormalizeBase("/"));
    }

    [Fact]
    public void IsAntigravityProcessMatchesKnownBinaries()
    {
        Assert.True(AntigravityDiscovery.IsAntigravityProcess("language_server.exe", @"C:\Users\test\.gemini\antigravity\bin\language_server.exe"));
        Assert.True(AntigravityDiscovery.IsAntigravityProcess("agy", "/usr/bin/agy"));
        Assert.True(AntigravityDiscovery.IsAntigravityProcess("antigravity.exe", null));
        Assert.True(AntigravityDiscovery.IsAntigravityProcess("node", @"C:\Program Files\Google\Antigravity\resources\app\node.exe"));
        Assert.False(AntigravityDiscovery.IsAntigravityProcess("chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
    }

    [Fact]
    public void ProbeOrderSortsDescendingPerPidAndInterleaves()
    {
        var perPid = new Dictionary<int, List<int>>
        {
            [100] = [8000, 8001], // Sorted: 8001, 8000
            [200] = [9000, 9002], // Sorted: 9002, 9000
        };

        var ordered = AntigravityDiscovery.ProbeOrder(perPid);
        // Rank 0: 8001, 9002
        // Rank 1: 8000, 9000
        Assert.Equal([8001, 9002, 8000, 9000], ordered);
    }

    [Fact]
    public void CandidateBasesWithPrecedesOverride()
    {
        var bases = AntigravityDiscovery.CandidateBasesWith("http://127.0.0.1:9999", [8000, 9999, 8001]);
        Assert.Equal("http://127.0.0.1:9999", bases[0]);
        Assert.Equal("http://127.0.0.1:8000", bases[1]);
        Assert.Equal("http://127.0.0.1:8001", bases[2]);
        Assert.Equal(3, bases.Count);
    }
}
