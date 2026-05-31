using AiUsageBar.Core.Models;

namespace AiUsageBar.Core.Abstractions;

/// <summary>
/// What a vendor returns from a fetch — snapshot + metadata. Port of the Rust
/// <c>VendorOutcome</c> / <c>FetchOutcome</c>.
/// </summary>
public sealed record VendorOutcome(
    VendorSnapshot Snapshot,
    bool Stale,
    (int Code, string Message)? LastError,
    TimeSpan? CacheAge);

/// <summary>A vendor that can produce a usage snapshot.</summary>
public interface IVendorFetcher
{
    VendorId Id { get; }
    Task<VendorOutcome> FetchAsync(CancellationToken ct = default);
}
