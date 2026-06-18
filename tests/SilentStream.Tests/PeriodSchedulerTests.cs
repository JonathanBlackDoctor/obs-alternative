using SilentStream.Core.Implementations;
using SilentStream.Core.Models;
using Xunit;

namespace SilentStream.Tests;

public class PeriodSchedulerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sstream-sched-").FullName;
    private readonly ConfigStore _configStore;
    private readonly PeriodScheduleStore _store;

    private readonly List<int> _started = [];
    private readonly List<int> _ended = [];

    public PeriodSchedulerTests()
    {
        _configStore = new ConfigStore(Path.Combine(_dir, "config.json"));
        _configStore.Save(AppConfig.CreateDefault());
        _store = new PeriodScheduleStore(_configStore);
    }

    /// <summary>
    /// Virtual clock: a "delay" advances the clock by the requested span and yields, so the loop
    /// races through the day deterministically. The long wait-until-midnight at end of day parks
    /// the loop (no clock advance) so it doesn't spin into the next day during assertions.
    /// </summary>
    private sealed class VirtualClock(DateTime start)
    {
        private readonly object _lock = new();
        private DateTime _now = start;

        public DateTime Now { get { lock (_lock) { return _now; } } }

        public async Task Delay(TimeSpan span, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (span > TimeSpan.FromHours(6))
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false); // park at midnight
                return;
            }
            lock (_lock) { _now = _now.Add(span); }
            await Task.Yield();
        }
    }

    [Fact]
    public async Task Fires_start_and_end_once_per_period_at_their_boundaries()
    {
        // 2026-06-15 Monday, two periods. Clock starts a minute before period 1.
        var date = new DateOnly(2026, 6, 15);
        _store.SetWeekdayDefault(DayOfWeek.Monday, new DaySchedule(
        [
            new SchoolPeriod(1, new TimeOnly(9, 0), new TimeOnly(9, 50)),
            new SchoolPeriod(2, new TimeOnly(10, 0), new TimeOnly(10, 50)),
        ]));

        var clock = new VirtualClock(date.ToDateTime(new TimeOnly(8, 59)));
        var scheduler = new PeriodScheduler(_store, new LogService(), () => clock.Now, clock.Delay);
        scheduler.PeriodStarted += (_, b) => { lock (_started) { _started.Add(b.PeriodNumber); } };
        scheduler.PeriodEnded += (_, b) => { lock (_ended) { _ended.Add(b.PeriodNumber); } };

        using var cts = new CancellationTokenSource();
        scheduler.Start(cts.Token);

        await WaitUntilAsync(() => Count(_ended) >= 2, TimeSpan.FromSeconds(5));
        cts.Cancel();

        lock (_started) { Assert.Equal([1, 2], _started); }
        lock (_ended) { Assert.Equal([1, 2], _ended); }
    }

    [Fact]
    public async Task Periods_already_past_at_start_are_not_back_fired()
    {
        var date = new DateOnly(2026, 6, 15);
        _store.SetWeekdayDefault(DayOfWeek.Monday, new DaySchedule(
        [
            new SchoolPeriod(1, new TimeOnly(9, 0), new TimeOnly(9, 50)),   // fully in the past
            new SchoolPeriod(2, new TimeOnly(10, 0), new TimeOnly(10, 50)), // future
        ]));

        // Launch at 09:55 — period 1 is already over, only period 2 should fire.
        var clock = new VirtualClock(date.ToDateTime(new TimeOnly(9, 55)));
        var scheduler = new PeriodScheduler(_store, new LogService(), () => clock.Now, clock.Delay);
        scheduler.PeriodStarted += (_, b) => { lock (_started) { _started.Add(b.PeriodNumber); } };
        scheduler.PeriodEnded += (_, b) => { lock (_ended) { _ended.Add(b.PeriodNumber); } };

        using var cts = new CancellationTokenSource();
        scheduler.Start(cts.Token);

        await WaitUntilAsync(() => Count(_ended) >= 1, TimeSpan.FromSeconds(5));
        cts.Cancel();

        lock (_started) { Assert.Equal([2], _started); }
        lock (_ended) { Assert.Equal([2], _ended); }
    }

    private int Count(List<int> list) { lock (list) { return list.Count; } }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
