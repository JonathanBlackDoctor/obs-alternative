using SilentStream.Core.Models;

namespace SilentStream.Core.Contracts;

/// <summary>
/// Persists and resolves the class timetable (교시 시간표). A schedule has a per-weekday
/// default plus optional per-date overrides; <see cref="ResolveForDate"/> picks the override
/// when present, else the weekday default (확장계획서 §5, decision D5). Backed by config.json.
/// </summary>
public interface IPeriodScheduleStore
{
    /// <summary>Returns the default schedule for the given weekday (empty if none set).</summary>
    DaySchedule GetWeekdayDefault(DayOfWeek day);

    /// <summary>Replaces the default schedule for the given weekday.</summary>
    void SetWeekdayDefault(DayOfWeek day, DaySchedule schedule);

    /// <summary>Returns the override for the given date, or null when none is set.</summary>
    DaySchedule? GetOverride(DateOnly date);

    /// <summary>Sets (or replaces) the override for a single date ("오늘만 덮어쓰기").</summary>
    void SetOverride(DateOnly date, DaySchedule schedule);

    /// <summary>Removes the override for a date so it falls back to the weekday default.</summary>
    void ClearOverride(DateOnly date);

    /// <summary>Resolved schedule for a date: override if present, otherwise weekday default.</summary>
    DaySchedule ResolveForDate(DateOnly date);
}
