using System.Collections.Concurrent;
using BidMatrix.Application.Analyses;

namespace BidMatrix.Api.IntegrationTests;

public sealed class InMemoryObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<string, byte[]> objects = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, byte[]> Objects => objects;

    public Task PutAsync(ObjectWriteRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        objects[$"{request.Bucket}/{request.Key}"] = request.Content.ToArray();
        return Task.CompletedTask;
    }

    public Task<ReadOnlyMemory<byte>> GetAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return objects.TryGetValue($"{bucket}/{key}", out var content)
            ? Task.FromResult<ReadOnlyMemory<byte>>(content)
            : throw new FileNotFoundException("The requested test object was not found.", key);
    }
}
