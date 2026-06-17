using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SilentStream.Core.Contracts;
using SilentStream.Core.Implementations;
using SilentStream.Core.Models;
using SilentStream.Core.Remote;

namespace SilentStream.App.Remote;

/// <summary>
/// Embedded ASP.NET Core (Kestrel) remote-control server (확장계획서 §4.4/§7). Serves a single
/// responsive mobile page and a token-authenticated REST + WebSocket API for editing the
/// timetable, toggling the live stream, and watching status. PIN pairing issues per-device
/// tokens whose SHA-256 hashes live in config (D11). Bind mode: off / lan / tailscale (D8).
/// </summary>
public sealed class RemoteControlServer : IRemoteControlServer
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, DayOfWeek> WeekdayMap = new Dictionary<string, DayOfWeek>
    {
        ["Sun"] = DayOfWeek.Sunday, ["Mon"] = DayOfWeek.Monday, ["Tue"] = DayOfWeek.Tuesday,
        ["Wed"] = DayOfWeek.Wednesday, ["Thu"] = DayOfWeek.Thursday, ["Fri"] = DayOfWeek.Friday,
        ["Sat"] = DayOfWeek.Saturday
    };

    private readonly IStreamOrchestrator _orchestrator;
    private readonly IPeriodScheduleStore _scheduleStore;
    private readonly PeriodScheduler _scheduler;
    private readonly IUploadQueue _uploadQueue;
    private readonly IRecordingManager _recording;
    private readonly IConfigStore _configStore;
    private readonly ILogService _log;

    private readonly object _socketsGate = new();
    private readonly List<WebSocket> _sockets = [];
    private readonly string _html;

    private WebApplication? _app;
    private MetricsSnapshot _lastMetrics = MetricsSnapshot.Empty;

    public RemoteControlServer(
        IStreamOrchestrator orchestrator,
        IPeriodScheduleStore scheduleStore,
        PeriodScheduler scheduler,
        IUploadQueue uploadQueue,
        IRecordingManager recording,
        IConfigStore configStore,
        ILogService log)
    {
        _orchestrator = orchestrator;
        _scheduleStore = scheduleStore;
        _scheduler = scheduler;
        _uploadQueue = uploadQueue;
        _recording = recording;
        _configStore = configStore;
        _log = log;
        _html = LoadEmbeddedHtml();
    }

    /// <summary>Current pairing PIN to show in the control window (null until the server starts).</summary>
    public string? CurrentPin { get; private set; }

    /// <summary>Raised when a new pairing PIN is generated (server start).</summary>
    public event Action<string>? PinChanged;

    public async Task StartAsync(RemoteBindMode mode, int port, CancellationToken ct)
    {
        if (mode == RemoteBindMode.Off)
        {
            _log.Info("원격 제어가 꺼져 있습니다(remote.mode=off).");
            return;
        }
        if (_app is not null)
        {
            return; // already running
        }

        var bindIp = ResolveBindAddress(mode);
        if (bindIp is null)
        {
            _log.Warn("Tailscale 인터페이스 IP를 찾지 못했습니다. Tailscale 연결 후 다시 시도하세요 (원격 미기동).");
            return;
        }

        if (mode == RemoteBindMode.Lan)
        {
            TryConfigureFirewall(port);
        }

        CurrentPin = RemoteAuth.NewPin();
        PinChanged?.Invoke(CurrentPin);

        var url = $"http://{bindIp}:{port}";
        var displayHost = mode == RemoteBindMode.Lan ? FirstLanIpv4() ?? bindIp : bindIp;
        _log.Info($"원격 제어 서버 시작: {url}  —  폰 브라우저로 http://{displayHost}:{port} 접속 후 PIN [{CurrentPin}] 입력.");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(url);
        var app = builder.Build();

        app.UseWebSockets();
        ConfigureAuth(app);
        MapEndpoints(app);

        _orchestrator.StateChanged += OnStateOrMetricsChanged;
        _orchestrator.MetricsUpdated += OnMetrics;

        await app.StartAsync(ct).ConfigureAwait(false);
        _app = app;
    }

    public async Task StopAsync()
    {
        var app = _app;
        if (app is null)
        {
            return;
        }
        _app = null;
        _orchestrator.StateChanged -= OnStateOrMetricsChanged;
        _orchestrator.MetricsUpdated -= OnMetrics;

        lock (_socketsGate)
        {
            _sockets.Clear();
        }

        try
        {
            await app.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Debug($"원격 서버 정지 중: {ex.Message}");
        }
        await app.DisposeAsync().ConfigureAwait(false);
        _log.Info("원격 제어 서버를 정지했습니다.");
    }

    // ---- auth ----

    private void ConfigureAuth(WebApplication app) =>
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            var protectedPath = path.StartsWithSegments("/api") || path.StartsWithSegments("/ws");
            var isPairing = path.StartsWithSegments("/api/pair");
            if (!protectedPath || isPairing)
            {
                await next().ConfigureAwait(false);
                return;
            }

            if (!RemoteAuth.IsKnownToken(KnownTokenHashes(), ExtractToken(ctx)))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next().ConfigureAwait(false);
        });

    private List<string> KnownTokenHashes() => _configStore.Load().Remote.DeviceTokens;

    private static string? ExtractToken(HttpContext ctx)
    {
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth["Bearer ".Length..].Trim();
        }
        if (ctx.Request.Headers.TryGetValue("X-Device-Token", out var header))
        {
            return header.ToString();
        }
        return ctx.Request.Query.TryGetValue("token", out var q) ? q.ToString() : null;
    }

    // ---- endpoints (확장계획서 §7) ----

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/", () => Results.Content(_html, "text/html; charset=utf-8"));

        app.MapPost("/api/pair", async ctx =>
        {
            var body = await ReadJsonAsync<PairRequest>(ctx).ConfigureAwait(false);
            if (body?.Pin is null || CurrentPin is null || body.Pin != CurrentPin)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { error = "PIN이 올바르지 않습니다." }).ConfigureAwait(false);
                return;
            }

            var token = RemoteAuth.NewToken();
            var config = _configStore.Load();
            config.Remote.DeviceTokens.Add(RemoteAuth.HashToken(token));
            _configStore.Save(config);
            _log.Info("새 기기가 페어링되었습니다(기기 토큰 발급).");
            await ctx.Response.WriteAsJsonAsync(new { token }).ConfigureAwait(false);
        });

        app.MapGet("/api/status", () => Results.Json(BuildStatus(), Json));

        app.MapGet("/api/schedule", () => Results.Json(GetWeekdayDefaults(), Json));
        app.MapPut("/api/schedule", async ctx =>
        {
            var body = await ReadJsonAsync<Dictionary<string, List<PeriodDto>>>(ctx).ConfigureAwait(false);
            if (body is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            foreach (var (key, rows) in body)
            {
                if (WeekdayMap.TryGetValue(key, out var day))
                {
                    _scheduleStore.SetWeekdayDefault(day, ToDaySchedule(rows));
                }
            }
            _scheduler.NotifyScheduleChanged();
            _log.Info("요일별 기본 시간표가 갱신되었습니다(원격).");
            await ctx.Response.WriteAsJsonAsync(new { ok = true }).ConfigureAwait(false);
        });

        app.MapGet("/api/schedule/today", () =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            return Results.Json(new
            {
                date = today.ToString("yyyy-MM-dd"),
                hasOverride = _scheduleStore.GetOverride(today) is not null,
                periods = ToDtos(_scheduleStore.ResolveForDate(today))
            }, Json);
        });
        app.MapPut("/api/schedule/today", async ctx =>
        {
            var rows = await ReadJsonAsync<List<PeriodDto>>(ctx).ConfigureAwait(false) ?? [];
            var today = DateOnly.FromDateTime(DateTime.Now);
            _scheduleStore.SetOverride(today, ToDaySchedule(rows));
            _scheduler.NotifyScheduleChanged();
            _log.Info($"오늘({today:yyyy-MM-dd}) 시간표 덮어쓰기가 적용되었습니다(원격).");
            await ctx.Response.WriteAsJsonAsync(new { ok = true }).ConfigureAwait(false);
        });
        app.MapDelete("/api/schedule/today", () =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            _scheduleStore.ClearOverride(today);
            _scheduler.NotifyScheduleChanged();
            _log.Info($"오늘({today:yyyy-MM-dd}) 시간표 덮어쓰기를 해제했습니다(원격).");
            return Results.Json(new { ok = true }, Json);
        });

        app.MapPost("/api/live/start", async () =>
        {
            await _orchestrator.StartAsync(CancellationToken.None).ConfigureAwait(false);
            return Results.Json(new { ok = true, state = _orchestrator.State.ToString() }, Json);
        });
        app.MapPost("/api/live/stop", async () =>
        {
            await _orchestrator.StopAsync().ConfigureAwait(false);
            return Results.Json(new { ok = true, state = _orchestrator.State.ToString() }, Json);
        });

        app.Map("/ws/status", HandleWebSocketAsync);
    }

    private async Task HandleWebSocketAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        lock (_socketsGate)
        {
            _sockets.Add(socket);
        }
        await SendStatusAsync(socket, ctx.RequestAborted).ConfigureAwait(false);

        try
        {
            var buffer = new byte[256];
            while (socket.State == WebSocketState.Open && !ctx.RequestAborted.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ctx.RequestAborted).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        finally
        {
            lock (_socketsGate)
            {
                _sockets.Remove(socket);
            }
        }
    }

    // ---- status push ----

    private void OnMetrics(object? sender, MetricsSnapshot metrics)
    {
        _lastMetrics = metrics;
        BroadcastStatus();
    }

    private void OnStateOrMetricsChanged(object? sender, StreamState state) => BroadcastStatus();

    private void BroadcastStatus()
    {
        List<WebSocket> sockets;
        lock (_socketsGate)
        {
            if (_sockets.Count == 0)
            {
                return;
            }
            sockets = _sockets.ToList();
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(BuildStatus(), Json);
        foreach (var socket in sockets)
        {
            _ = SendRawAsync(socket, payload);
        }
    }

    private async Task SendStatusAsync(WebSocket socket, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(BuildStatus(), Json);
        await SendRawAsync(socket, payload, ct).ConfigureAwait(false);
    }

    private async Task SendRawAsync(WebSocket socket, byte[] payload, CancellationToken ct = default)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
            lock (_socketsGate)
            {
                _sockets.Remove(socket);
            }
        }
    }

    // ---- status DTO ----

    private object BuildStatus()
    {
        var state = _orchestrator.State;
        var recording = _recording.GetStatus();
        var jobs = _uploadQueue.Snapshot();
        var today = DateOnly.FromDateTime(DateTime.Now);

        return new
        {
            state = state.ToString(),
            badge = StateBadge(state),
            live = state == StreamState.Live,
            metrics = new
            {
                bitrateKbps = _lastMetrics.UploadBitrateKbps,
                fps = _lastMetrics.Fps,
                cpu = _lastMetrics.CpuPercent,
                gpu = _lastMetrics.GpuPercent
            },
            recording = new
            {
                file = recording.CurrentFilePath is null ? null : Path.GetFileName(recording.CurrentFilePath),
                usedBytes = recording.TotalUsedBytes,
                freeBytes = recording.FreeDiskBytes
            },
            today = new
            {
                date = today.ToString("yyyy-MM-dd"),
                hasOverride = _scheduleStore.GetOverride(today) is not null,
                periods = ToDtos(_scheduleStore.ResolveForDate(today))
            },
            queue = new
            {
                pending = jobs.Count(j => j.Status == UploadJobStatus.Pending),
                uploading = jobs.Count(j => j.Status == UploadJobStatus.Uploading),
                completed = jobs.Count(j => j.Status == UploadJobStatus.Completed),
                failed = jobs.Count(j => j.Status == UploadJobStatus.Failed),
                items = jobs.TakeLast(20).Select(j => new
                {
                    period = j.PeriodNumber,
                    title = j.Title,
                    status = j.Status,
                    videoId = j.VideoId
                })
            }
        };
    }

    private static string StateBadge(StreamState state) => state switch
    {
        StreamState.Live => "LIVE",
        StreamState.Warmup => "준비 중",
        StreamState.ConnectingYouTube => "연결 중",
        StreamState.Retrying => "재시도 중",
        StreamState.Stopping => "중지 중",
        _ => "대기"
    };

    private Dictionary<string, List<PeriodDto>> GetWeekdayDefaults()
    {
        var result = new Dictionary<string, List<PeriodDto>>();
        foreach (var (key, day) in WeekdayMap)
        {
            result[key] = ToDtos(_scheduleStore.GetWeekdayDefault(day));
        }
        return result;
    }

    private static List<PeriodDto> ToDtos(DaySchedule schedule) =>
        schedule.Periods
            .Select(p => new PeriodDto(p.Number,
                p.Start.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                p.End.ToString("HH:mm:ss", CultureInfo.InvariantCulture)))
            .ToList();

    private static DaySchedule ToDaySchedule(IEnumerable<PeriodDto> rows) =>
        new(rows
            .Where(r => TimeOnly.TryParse(r.Start, out _) && TimeOnly.TryParse(r.End, out _))
            .Select(r => new SchoolPeriod(r.No, TimeOnly.Parse(r.Start), TimeOnly.Parse(r.End)))
            .OrderBy(p => p.Number)
            .ToList());

    private static async Task<T?> ReadJsonAsync<T>(HttpContext ctx)
    {
        try
        {
            return await ctx.Request.ReadFromJsonAsync<T>(Json).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    // ---- network / firewall ----

    private string? ResolveBindAddress(RemoteBindMode mode) =>
        mode == RemoteBindMode.Tailscale ? FindTailscaleIp() : "0.0.0.0";

    private static string? FindTailscaleIp()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var isTailscale = nic.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                              nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase);
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }
                // Tailscale uses the 100.64.0.0/10 CGNAT range.
                if (isTailscale || IsCgnat(ua.Address))
                {
                    return ua.Address.ToString();
                }
            }
        }
        return null;
    }

    private static bool IsCgnat(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    private static string? FirstLanIpv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IsCgnat(ua.Address))
                {
                    return ua.Address.ToString();
                }
            }
        }
        return null;
    }

    private void TryConfigureFirewall(int port)
    {
        var ruleName = $"SilentStream Remote {port}";
        try
        {
            // Replace any stale rule, then add a fresh inbound TCP allow (best-effort; needs admin).
            RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");
            var exit = RunNetsh(
                $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port}");
            _log.Info(exit == 0
                ? $"Windows 방화벽 인바운드 규칙을 추가했습니다(TCP {port})."
                : $"방화벽 규칙 추가 실패(관리자 권한 필요할 수 있음). 수동 허용: TCP {port}.");
        }
        catch (Exception ex)
        {
            _log.Warn($"방화벽 규칙 설정 실패(수동으로 TCP {port} 인바운드 허용 필요): {ex.Message}");
        }
    }

    private static int RunNetsh(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (process is null)
        {
            return -1;
        }
        process.WaitForExit(10_000);
        return process.HasExited ? process.ExitCode : -1;
    }

    private static string LoadEmbeddedHtml()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return "<html><body><h1>SilentStream 원격</h1><p>UI 리소스를 찾지 못했습니다.</p></body></html>";
        }
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed record PairRequest(string? Pin);

    private sealed record PeriodDto(int No, string Start, string End);
}
