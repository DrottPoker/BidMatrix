namespace BidMatrix.Infrastructure.Engineering;

public sealed class EngineeringSandboxOptions
{
    public string BaseRepositoryPath { get; init; } = string.Empty;
    public string WorktreeRootPath { get; init; } = Path.Combine(Path.GetTempPath(), "bidmatrix-engineering");
    public int CommandTimeoutSeconds { get; init; } = 60;
    public int MaximumOutputBytes { get; init; } = 65_536;
    public int MaximumFileBytes { get; init; } = 262_144;
    public int MaximumFileCount { get; init; } = 5_000;
}
