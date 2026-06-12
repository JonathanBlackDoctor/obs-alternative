using SilentStream.Core.Contracts;

namespace SilentStream.App;

/// <summary>
/// Boot-time automation (plan §4.2): single-instance check, 30s warmup, then the
/// auto stream+record sequence. Filled in by Phase 5 — currently a placeholder so
/// the UI phase stands alone.
/// </summary>
public static class StartupSequence
{
    public static Task RunAsync(IServiceProvider services, System.Windows.Application app)
    {
        // Phase 5: SingleInstanceGuard, 30초 대기, orchestrator.StartAsync, 세션 종료 후킹.
        return Task.CompletedTask;
    }
}
