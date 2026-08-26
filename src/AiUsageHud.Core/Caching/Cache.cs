using AiUsageHud.Core.Errors;

namespace AiUsageHud.Core.Caching;

/// <summary>
/// Per-vendor on-disk cache with atomic writes, TTL checks, and a stale/last-error
/// sidecar. Port of the Rust <c>cache.rs</c>. Layout under the vendor dir:
/// <c>usage.json</c>, <c>.stale</c>, <c>.last_error</c>, <c>.fetch.lock</c>.
/// </summary>
public sealed class Cache
{
    /// <summary>Default TTL — claudebar's CACHE_TTL=60s.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Oldest a cached payload may be and still be served on a failure path.
    /// Beyond this the caller must surface the real error instead of presenting
    /// week-old numbers as if they were current.
    /// </summary>
    public static readonly TimeSpan MaxStale = TimeSpan.FromDays(7);

    private readonly string _dir;

    public Cache(string dir) => _dir = dir;

    public static Cache ForVendor(string vendorSlug) =>
        new(Config.AppPaths.CacheDirFor(vendorSlug));

    public string Dir => _dir;
    public string PayloadPath => Path.Combine(_dir, "usage.json");
    public string StalePath => Path.Combine(_dir, ".stale");
    public string LastErrorPath => Path.Combine(_dir, ".last_error");
    public string LockPath => Path.Combine(_dir, ".fetch.lock");

    public void EnsureDir() => Directory.CreateDirectory(_dir);

    /// <summary>Age of the payload, or null if it doesn't exist.</summary>
    public TimeSpan? PayloadAge()
    {
        if (!File.Exists(PayloadPath)) return null;
        var mtime = File.GetLastWriteTimeUtc(PayloadPath);
        var age = DateTime.UtcNow - mtime;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    /// <summary>Cached payload only if younger than <paramref name="ttl"/>.</summary>
    public byte[]? FreshPayload(TimeSpan ttl)
    {
        if (!File.Exists(PayloadPath)) return null;
        // Raw (unclamped) age: a future mtime (clock skew, DST, a stray file)
        // must never look "freshly written" — that would pin the cache forever
        // since a clamped-to-zero age is always < ttl. Treat it as expired instead.
        var rawAge = DateTime.UtcNow - File.GetLastWriteTimeUtc(PayloadPath);
        return rawAge >= TimeSpan.Zero && rawAge < ttl ? File.ReadAllBytes(PayloadPath) : null;
    }

    /// <summary>
    /// Read the payload regardless of age; null if it doesn't exist. Prefer
    /// <see cref="FallbackPayload"/> on failure paths — this one imposes no age
    /// limit, so it will happily hand back a month-old figure.
    /// </summary>
    public byte[]? MaybePayload() =>
        File.Exists(PayloadPath) ? File.ReadAllBytes(PayloadPath) : null;

    /// <summary>
    /// Payload for the failure path: the last good value, but only while it is
    /// still worth showing. Beyond <paramref name="maxStale"/> this returns null
    /// so the caller surfaces the real error instead of presenting week-old
    /// numbers as if they were current.
    /// </summary>
    public byte[]? FallbackPayload(TimeSpan maxStale)
    {
        var age = PayloadAge();
        if (age is null || age > maxStale) return null;
        return File.ReadAllBytes(PayloadPath);
    }

    /// <summary>Atomically write a new payload; clears stale + last-error markers.</summary>
    public void WritePayload(byte[] bytes)
    {
        EnsureDir();
        AtomicWrite(PayloadPath, bytes);
        TryDelete(StalePath);
        TryDelete(LastErrorPath);
    }

    public void MarkStale()
    {
        EnsureDir();
        try { File.WriteAllBytes(StalePath, []); } catch { /* best effort */ }
    }

    public bool IsStale() => File.Exists(StalePath);

    /// <summary>Write the <c>.last_error</c> marker (line 1 = code, line 2 = message). Best-effort.</summary>
    public void WriteLastError(int code, string message)
    {
        try
        {
            EnsureDir();
            AtomicWrite(LastErrorPath, System.Text.Encoding.UTF8.GetBytes($"{code}\n{message}"));
        }
        catch { /* best effort — matches claudebar */ }
    }

    public (int Code, string Message)? ReadLastError()
    {
        if (!File.Exists(LastErrorPath)) return null;
        var raw = File.ReadAllText(LastErrorPath);
        var lines = raw.Split('\n');
        if (lines.Length == 0 || !int.TryParse(lines[0], out var code)) return null;
        var msg = lines.Length > 1 ? lines[1] : "";
        return (code, msg);
    }

    /// <summary>
    /// Acquire an exclusive lock on the vendor's lock file, blocking up to
    /// <paramref name="timeout"/>. Replaces the Rust flock; on Windows we open the
    /// file with <see cref="FileShare.None"/> and retry. Dispose to release.
    /// </summary>
    public IDisposable AcquireLock(TimeSpan timeout)
    {
        EnsureDir();
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var fs = new FileStream(LockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                return fs;
            }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new OtherException($"cache lock timeout after {timeout}");
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>Atomic write via temp file + move. Public for sidecar writers (config).</summary>
    public static void AtomicWrite(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path)
            ?? throw new OtherException($"atomic_write: path has no parent: {path}");
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".tmp.{Guid.NewGuid():N}");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
