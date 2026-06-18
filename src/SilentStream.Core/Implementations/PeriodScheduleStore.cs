using System.Globalization;
using SilentStream.Core.Contracts;
using SilentStream.Core.Models;

namespace SilentStream.Core.Implementations;

/// <summary>
/// Timetable persistence over config.json (확장계획서 §5/§6): per-weekday defaults plus
/// per-date overrides, with <see cref="ResolveForDate"/> preferring an override (D5).
/// Times are stored as "HH:mm:ss" strings and surfaced as <see cref="TimeOnly"/>.
/// </summary>
public sealed class PeriodScheduleStore(IConfigStore configStore) : IPeriodScheduleStore
{
    private static readonly string[] WeekdayKeys =
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]; // index = (int)DayOfWeek

    private static string WeekdayKey(DayOfWeek day) => WeekdayKeys[(int)day];

    private static string DateKey(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public DaySchedule GetWeekdayDefault(DayOfWeek day)
    {
        var config = configStore.Load();
        return config.Periods.WeekdayDefaults.TryGetValue(WeekdayKey(day), out var entries)
            ? ToDaySchedule(entries)
            : DaySchedule.Empty;
    }

    public void SetWeekdayDefault(DayOfWeek day, DaySchedule schedule)
    {
        var config = configStore.Load();
        config.Periods.WeekdayDefaults[WeekdayKey(day)] = ToEntries(schedule);
        configStore.Save(config);
    }

    public DaySchedule? GetOverride(DateOnly date)
    {
        var config = configStore.Load();
        return config.Periods.Overrides.TryGetValue(DateKey(date), out var entries)
            ? ToDaySchedule(entries)
            : null;
    }

    public void SetOverride(DateOnly date, DaySchedule schedule)
    {
        var config = configStore.Load();
        config.Periods.Overrides[DateKey(date)] = ToEntries(schedule);
        configStore.Save(config);
    }

    public void ClearOverride(DateOnly date)
    {
        var config = configStore.Load();
        if (config.Periods.Overrides.Remove(DateKey(date)))
        {
            configStore.Save(config);
        }
    }

    public DaySchedule ResolveForDate(DateOnly date) =>
        GetOverride(date) ?? GetWeekdayDefault(date.DayOfWeek);

    private static DaySchedule ToDaySchedule(IEnumerable<PeriodEntry> entries)
    {
        var periods = entries
            .Select(e => new SchoolPeriod(e.No, ParseTime(e.Start), ParseTime(e.End)))
            .OrderBy(p => p.Number)
            .ToList();
        return new DaySchedule(periods);
    }

    private static List<PeriodEntry> ToEntries(DaySchedule schedule) =>
        schedule.Periods
            .Select(p => new PeriodEntry
            {
                No = p.Number,
                Start = p.Start.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                End = p.End.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            })
            .ToList();

    private static TimeOnly ParseTime(string value) =>
        TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var t) ? t : TimeOnly.MinValue;
}
