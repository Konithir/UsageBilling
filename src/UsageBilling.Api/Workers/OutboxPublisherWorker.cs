using UsageBilling.Domain;
using UsageBilling.Domain.Models;

namespace UsageBilling.Api.Workers;

public sealed class OutboxPublisherWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxPublisherWorker> logger, TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IBillingRepository>();
                var publisher = scope.ServiceProvider.GetRequiredService<IUsageMessagePublisher>();
                var messages = (await repository.ListOutboxAsync(50, stoppingToken))
                    .Where(x => x.Status != OutboxStatus.Published && x.Attempts < 5)
                    .OrderBy(x => x.CreatedAt)
                    .ToList();
                foreach (var message in messages)
                {
                    try { await publisher.PublishAsync(message, stoppingToken); message.MarkPublished(timeProvider.GetUtcNow()); }
                    catch (Exception exception) { message.MarkFailed(exception.Message); logger.LogWarning(exception, "Outbox message {MessageId} could not be published", message.Id); }
                }
                if (messages.Count > 0) await repository.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Outbox processing failed"); }
        }
    }
}