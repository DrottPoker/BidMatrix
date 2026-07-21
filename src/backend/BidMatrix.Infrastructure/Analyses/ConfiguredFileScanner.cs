using BidMatrix.Application.Analyses;

namespace BidMatrix.Infrastructure.Analyses;

public sealed class ConfiguredFileScanner(AnalysisOptions options) : IFileScanner
{
    public bool DevelopmentBypassEnabled => options.ScanMode == "development_bypass";

    public Task<FileScanResult> ScanAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(options.ScanMode == "development_bypass"
            ? new FileScanResult("development_bypass", "Development scan bypass is visibly active.")
            : new FileScanResult("failed", "No production malware scanner is configured."));
    }
}
