using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace PolskiYol.Features.Materials;

public interface IMaterialBlobStore
{
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream?> OpenAsync(string key, CancellationToken ct);
    Task DeleteOrQueueAsync(string? key);
}

public sealed class MaterialBlobStore(BlobContainerClient container, MaterialBlobCleanup cleanup) : IMaterialBlobStore
{
    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct) =>
        await container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, ct);

    public async Task<Stream?> OpenAsync(string key, CancellationToken ct)
    {
        var blob = await container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct);
        return blob.Value.Content;
    }

    public Task DeleteOrQueueAsync(string? key) => cleanup.DeleteOrQueueAsync(container, key);
}
