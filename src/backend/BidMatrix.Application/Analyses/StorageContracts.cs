namespace BidMatrix.Application.Analyses;

public sealed record ObjectWriteRequest(
    string Bucket,
    string Key,
    string ContentType,
    string Sha256,
    ReadOnlyMemory<byte> Content);

public interface IObjectStorage
{
    Task PutAsync(ObjectWriteRequest request, CancellationToken cancellationToken = default);

    Task<ReadOnlyMemory<byte>> GetAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken = default);
}

public sealed record FileScanResult(string Status, string? Detail);

public interface IFileScanner
{
    bool DevelopmentBypassEnabled { get; }

    Task<FileScanResult> ScanAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
