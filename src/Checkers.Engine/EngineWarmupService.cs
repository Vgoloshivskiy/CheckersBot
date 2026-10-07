using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Checkers.Engine;

/// <summary>
/// Starts and warms up the engine workers when the application starts. It runs in the background so the web server
/// comes up straight away (IIS gives an application a limited time to start); /healthz reports 503 until the workers
/// are ready, and if starting fails it keeps trying.
/// </summary>
public sealed class EngineWarmupService : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    private readonly EnginePool _pool;
    private readonly ILogger<EngineWarmupService> _logger;

    public EngineWarmupService(EnginePool pool, ILogger<EngineWarmupService> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _pool.StartAsync(stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The engine could not be started. Trying again in {Seconds} seconds.", RetryDelay.TotalSeconds);

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
