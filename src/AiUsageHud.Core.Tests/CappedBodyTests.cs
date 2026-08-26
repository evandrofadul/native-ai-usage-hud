using AiUsageHud.Core.Errors;
using AiUsageHud.Core.Http;

namespace AiUsageHud.Core.Tests;

public class CappedBodyTests
{
    [Fact]
    public async Task ReadsBodyUnderTheCap()
    {
        using var content = new ByteArrayContent("hello world"u8.ToArray());
        var bytes = await CappedBody.ReadAsync(content, CancellationToken.None, maxBytes: 1024);
        Assert.Equal("hello world"u8.ToArray(), bytes);
    }

    [Fact]
    public async Task RejectsUpfrontOnALyingContentLengthHeader()
    {
        using var content = new ByteArrayContent("small"u8.ToArray());
        // Content-Length is set correctly by ByteArrayContent, so drive the
        // over-declared case directly via a StreamContent with a forced header.
        content.Headers.ContentLength = 10_000;
        await Assert.ThrowsAsync<SchemaException>(() => CappedBody.ReadAsync(content, CancellationToken.None, maxBytes: 1024));
    }

    [Fact]
    public async Task RejectsAStreamThatExceedsTheCapWithNoHonestContentLength()
    {
        // No Content-Length at all — the cap must still be enforced while streaming.
        using var stream = new MemoryStream(new byte[5000]);
        using var content = new StreamContent(stream);
        await Assert.ThrowsAsync<SchemaException>(() => CappedBody.ReadAsync(content, CancellationToken.None, maxBytes: 1024));
    }

    [Fact]
    public async Task AllowsAStreamExactlyAtTheCap()
    {
        using var stream = new MemoryStream(new byte[1024]);
        using var content = new StreamContent(stream);
        var bytes = await CappedBody.ReadAsync(content, CancellationToken.None, maxBytes: 1024);
        Assert.Equal(1024, bytes.Length);
    }
}
