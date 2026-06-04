using AiUsageHud.Core.Caching;
using AiUsageHud.Core.Errors;

namespace AiUsageHud.Core.Tests;

public class CacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-usage-hud-test-" + Guid.NewGuid().ToString("N"));
    private Cache NewCache()
    {
        var c = new Cache(Path.Combine(_root, "anthropic"));
        c.EnsureDir();
        return c;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void EnsureDirIsIdempotent()
    {
        var c = NewCache();
        c.EnsureDir();
        c.EnsureDir();
        Assert.True(Directory.Exists(c.Dir));
    }

    [Fact]
    public void WriteThenReadRoundTrip()
    {
        var c = NewCache();
        c.WritePayload("hello world"u8.ToArray());
        Assert.Equal("hello world"u8.ToArray(), c.MaybePayload());
    }

    [Fact]
    public void MaybePayloadReturnsNullWhenMissing() =>
        Assert.Null(NewCache().MaybePayload());

    [Fact]
    public void FreshPayloadRespectsTtl()
    {
        var c = NewCache();
        c.WritePayload("x"u8.ToArray());
        Assert.NotNull(c.FreshPayload(TimeSpan.FromSeconds(10)));
        Assert.Null(c.FreshPayload(TimeSpan.Zero));
    }

    [Fact]
    public void WriteClearsStaleMarkerAndLastError()
    {
        var c = NewCache();
        c.MarkStale();
        c.WriteLastError(429, "rate limited");
        Assert.True(c.IsStale());
        Assert.NotNull(c.ReadLastError());

        c.WritePayload("fresh"u8.ToArray());
        Assert.False(c.IsStale());
        Assert.Null(c.ReadLastError());
    }

    [Fact]
    public void LastErrorRoundTrip()
    {
        var c = NewCache();
        c.WriteLastError(503, "service unavailable");
        var le = c.ReadLastError();
        Assert.Equal((503, "service unavailable"), le);
    }

    [Fact]
    public void LastErrorWithEmptyMessageRoundTrips()
    {
        var c = NewCache();
        c.WriteLastError(429, "");
        Assert.Equal((429, ""), c.ReadLastError());
    }

    [Fact]
    public void LockSerializesConcurrentAcquirers()
    {
        var c = NewCache();
        using var first = c.AcquireLock(TimeSpan.FromMilliseconds(500));
        Assert.Throws<OtherException>(() => c.AcquireLock(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void AtomicWriteCreatesParentDirs()
    {
        var nested = Path.Combine(_root, "a", "b", "c", "file.txt");
        Cache.AtomicWrite(nested, "abc"u8.ToArray());
        Assert.Equal("abc"u8.ToArray(), File.ReadAllBytes(nested));
    }
}
