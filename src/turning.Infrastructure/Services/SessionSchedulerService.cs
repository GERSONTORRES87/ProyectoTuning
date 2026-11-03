using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Turning.Application.Features.ExperimentSessions;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Turning.Infrastructure.Persistence;

namespace Turning.Infrastructure.Services;

public sealed class SessionOptions
{
    public int DurationSeconds { get; set; } = 300;
    public int InactivitySeconds { get; set; } = 120;
}

public sealed class SessionSchedulerService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<SessionSchedulerService> _log;
    private readonly SessionOptions _opts;

    public SessionSchedulerService(IServiceProvider sp, ILogger<SessionSchedulerService> log, IOptions<SessionOptions> opts)
    {
        _sp = sp;
        _log = log;
        _opts = opts.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await RecoverAsync(ct);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
            await ExpireAsync(ct);
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TurningDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IExperimentSessionRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IExperimentEventPublisher>();
        var actives = await db.ExperimentSessions.Where(s => !s.IsDeleted && s.Status == ExperimentSessionStatus.Active).ToListAsync(ct);
        var recovered = 0; var timedOut = 0;
        foreach (var s in actives)
        {
            using var operation = await SessionOperationLock.AcquireAsync(s.Id, ct);
            await db.Entry(s).ReloadAsync(ct);
            if (s.IsDeleted || s.Status != ExperimentSessionStatus.Active) continue;
            var now = DateTime.UtcNow;
            if ((s.ExpiresAtUtc.HasValue && now >= s.ExpiresAtUtc.Value) || (s.LastActivityAtUtc.HasValue && (now - s.LastActivityAtUtc.Value).TotalSeconds >= _opts.InactivitySeconds && (now - s.ActivatedAtUtc!.Value).TotalSeconds > 60))
            {
                var prev = s.Status;
                s.Expire(now);
                await SessionTransitionRecorder.RecordAsync(s, prev, null, "Scheduler", repository, publisher, "RecoveredTimedOut", ct);
                await db.SaveChangesAsync(ct);
                timedOut++;
            }
            else recovered++;
        }
        _log.LogInformation("Session recovery: {Recovered} active preserved, {TimedOut} timed out", recovered, timedOut);
    }

    private async Task ExpireAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TurningDbContext>();
            var repository = scope.ServiceProvider.GetRequiredService<IExperimentSessionRepository>();
            var publisher = scope.ServiceProvider.GetRequiredService<IExperimentEventPublisher>();
            var actives = await db.ExperimentSessions.Where(s => !s.IsDeleted && s.Status == ExperimentSessionStatus.Active).ToListAsync(ct);
            foreach (var s in actives)
            {
                // shortcut: espera secuencial al candado de cada sesion; una generacion de IA en curso
                // (hasta TotalBudgetMs, 10 s) retrasa el cierre de las demas. Aceptable con una sola
                // instancia; pasar a un intento sin espera por sesion si crece la concurrencia.
                using var operation = await SessionOperationLock.AcquireAsync(s.Id, ct);
                await db.Entry(s).ReloadAsync(ct);
                if (s.IsDeleted || s.Status != ExperimentSessionStatus.Active) continue;
                var now = DateTime.UtcNow;
                var expired = s.ExpiresAtUtc.HasValue && now >= s.ExpiresAtUtc.Value;
                var inactive = s.LastActivityAtUtc.HasValue && (now - s.LastActivityAtUtc.Value).TotalSeconds >= _opts.InactivitySeconds && s.ActivatedAtUtc.HasValue && (now - s.ActivatedAtUtc.Value).TotalSeconds > 60;
                if (expired || inactive)
                {
                    var prev = s.Status;
                    s.Expire(now);
                    await SessionTransitionRecorder.RecordAsync(s, prev, null, "Scheduler", repository, publisher,
                        expired ? "DurationExpired" : "Inactivity", ct);
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        catch (Exception ex) { _log.LogError(ex, "Scheduler tick failed"); }
    }
}
