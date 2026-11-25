using System.Threading.Channels;
using Azure;
using Azure.Storage.Blobs;

namespace PolskiYol.Features.Materials;

public sealed class MaterialBlobCleanup(ILogger<MaterialBlobCleanup> logger) : BackgroundService
{
    private const int RetryAttempts = 3;
    private readonly Channel<BlobClient> _pending = Channel.CreateBounded<BlobClient>(
        new BoundedChannelOptions(256) { SingleReader = true });

    public async Task DeleteOrQueueAsync(BlobContainerClient container, string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return;

        var blob = container.GetBlobClient(storageKey);
        try
        {
            await blob.DeleteIfExistsAsync();
        }
        catch (RequestFailedException ex)
        {
            if (IsTransient(ex) && _pending.Writer.TryWrite(blob))
                logger.LogWarning("Queued material blob deletion after Azure status {Status}.", ex.Status);
            else
                logger.LogError("Could not delete or queue a material blob. Azure status {Status}.", ex.Status);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var blob in _pending.Reader.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; attempt <= RetryAttempts; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), stoppingToken);

                try
                {
                    await blob.DeleteIfExistsAsync(cancellationToken: stoppingToken);
                    break;
                }
                catch (RequestFailedException ex)
                {
                    if (attempt == RetryAttempts || !IsTransient(ex))
                    {
                        logger.LogError(
                            "Material blob deletion stopped after {Attempt} retries. Azure status {Status}.",
                            attempt, ex.Status);
                        break;
                    }
                }
            }
        }
    }

    private static bool IsTransient(RequestFailedException error) =>
        error.Status is 0 or 408 or 429 or >= 500;
}
