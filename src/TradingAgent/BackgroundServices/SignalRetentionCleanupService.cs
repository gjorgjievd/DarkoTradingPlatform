using Microsoft.EntityFrameworkCore;
using TradingAgent.Configuration;
using TradingAgent.Data;

namespace TradingAgent.BackgroundServices;

public sealed class SignalRetentionCleanupService(
    IServiceScopeFactory serviceScopeFactory,
    AppSettings settings,
    ILogger<SignalRetentionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));

        await CleanupAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingAgentDbContext>();
            var cutoffDate = DateTime.UtcNow.AddDays(-settings.RetentionDays);

            var deletedRows = await dbContext.TradingSignals
                .Where(signal => signal.CreatedAtUtc < cutoffDate)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows > 0)
            {
                logger.LogInformation("Removed {DeletedRows} expired trading signals.", deletedRows);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Failed to clean up expired trading signals.");
        }
    }
}
