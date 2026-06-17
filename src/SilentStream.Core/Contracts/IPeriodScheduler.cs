using SilentStream.Core.Models;

namespace SilentStream.Core.Contracts;

/// <summary>
/// Wall-clock scheduler that fires one event when a school period begins and one when it
/// ends, resolving the current day's timetable from <see cref="IPeriodScheduleStore"/>.
/// The VOD pipeline subscribes to <see cref="PeriodEnded"/> to cut and upload each period
/// (확장계획서 §4.0/§5). Periods already past at <see cref="Start"/> are not back-fired.
/// </summary>
public interface IPeriodScheduler
{
    /// <summary>Raised at a period's start time exactly once.</summary>
    event EventHandler<PeriodBoundary>? PeriodStarted;

    /// <summary>Raised at a period's end time exactly once.</summary>
    event EventHandler<PeriodBoundary>? PeriodEnded;

    /// <summary>Starts the background wall-clock loop. Idempotent; stops on <paramref name="ct"/>.</summary>
    void Start(CancellationToken ct);
}
