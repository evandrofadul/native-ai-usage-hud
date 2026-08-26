using AiUsageHud.Core.Errors;

namespace AiUsageHud.Core.Http;

/// <summary>
/// Reads an HTTP response body with an upper bound so a misbehaving or
/// hijacked endpoint cannot exhaust memory — this HUD re-fetches every ~60s,
/// so an unbounded body is a standing resource-exhaustion risk. Every vendor
/// endpoint returns a small JSON document (a few KB at most), so the cap is
/// generous by three orders of magnitude while still bounding the damage.
/// <c>Content-Length</c> is checked up front when present, then the body is
/// read in chunks so a lying or absent length still cannot get past the cap.
/// Port of the Rust <c>vendor::read_body_capped</c>.
/// </summary>
public static class CappedBody
{
    /// <summary>Matches claudebar's <c>MAX_BODY_BYTES</c>.</summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    public static async Task<byte[]> ReadAsync(HttpContent content, CancellationToken ct, long maxBytes = MaxBytes)
    {
        if (content.Headers.ContentLength is { } declared && declared > maxBytes)
            throw TooBig(declared, maxBytes);

        using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > maxBytes) throw TooBig(total, maxBytes);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static SchemaException TooBig(long bytes, long max) =>
        new($"response body exceeds the {max}-byte limit ({bytes} bytes); refusing to buffer it");
}
