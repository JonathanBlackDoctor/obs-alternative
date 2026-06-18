namespace SilentStream.Core.Models;

/// <summary>
/// A single school period (교시): its number and the local wall-clock window it occupies.
/// Times are <see cref="TimeOnly"/> so a schedule is reusable across dates (확장계획서 §5).
/// </summary>
/// <param name="Number">1-based period number (1교시, 2교시 …).</param>
/// <param name="Start">Local start time (e.g. 09:00:00).</param>
/// <param name="End">Local end time (e.g. 09:50:00).</param>
public sealed record SchoolPeriod(int Number, TimeOnly Start, TimeOnly End);

/// <summary>
/// An ordered set of periods for one day (a weekday default or a date override). See 확장계획서 §5.
/// </summary>
/// <param name="Periods">Periods in display/number order.</param>
public sealed record DaySchedule(IReadOnlyList<SchoolPeriod> Periods)
{
    /// <summary>An empty schedule (no periods → scheduler fires nothing).</summary>
    public static DaySchedule Empty { get; } = new(Array.Empty<SchoolPeriod>());
}

/// <summary>
/// A concrete period boundary resolved against a calendar date: the period number plus
/// its absolute local start/end timestamps. Emitted by <c>IPeriodScheduler</c> and consumed
/// by the VOD cut + upload pipeline (확장계획서 §5).
/// </summary>
/// <param name="Date">The calendar date this boundary belongs to.</param>
/// <param name="PeriodNumber">1-based period number.</param>
/// <param name="StartLocal">Absolute local start timestamp.</param>
/// <param name="EndLocal">Absolute local end timestamp.</param>
public sealed record PeriodBoundary(
    DateOnly Date,
    int PeriodNumber,
    DateTime StartLocal,
    DateTime EndLocal);
