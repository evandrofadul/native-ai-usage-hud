namespace AiUsageBar.Core.Models;

/// <summary>
/// A single usage window — generic enough that every vendor with a notion of
/// "% used vs. when does it reset" can express itself with it. Port of the Rust
/// <c>UsageWindow</c>.
/// </summary>
/// <param name="UtilizationPct">Integer percent, 0..=100.</param>
/// <param name="ResetsAt">When the window rolls over; null if the vendor omits it.</param>
/// <param name="WindowDuration">Total window length (used for pacing math).</param>
public readonly record struct UsageWindow(
    int UtilizationPct,
    DateTimeOffset? ResetsAt,
    TimeSpan WindowDuration);
