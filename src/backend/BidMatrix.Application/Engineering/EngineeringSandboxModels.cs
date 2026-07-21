namespace BidMatrix.Application.Engineering;

public sealed record EngineeringWorktreeResult(
    string SandboxId,
    string BaseRevision,
    string HeadRevision,
    string Status);

public sealed record EngineeringFileResult(string Path, string Content, long SizeBytes);

public sealed record EngineeringSearchMatch(string Path, int LineNumber, string Line);

public sealed record EngineeringCommandResult(
    string Executable,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    long DurationMilliseconds);

public sealed record EngineeringDiffResult(string Diff, string Sha256, int SizeBytes);

public interface IEngineeringSandboxService
{
    Task<EngineeringWorktreeResult> CreateWorktreeAsync(Guid organizationId, Guid taskId, string baseRevision, CancellationToken cancellationToken = default);
    Task<EngineeringFileResult> ReadFileAsync(Guid organizationId, Guid taskId, string relativePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EngineeringSearchMatch>> SearchAsync(Guid organizationId, Guid taskId, string query, string? relativePath, CancellationToken cancellationToken = default);
    Task<EngineeringCommandResult> GetStatusAsync(Guid organizationId, Guid taskId, CancellationToken cancellationToken = default);
    Task<EngineeringDiffResult> GetDiffAsync(Guid organizationId, Guid taskId, CancellationToken cancellationToken = default);
    Task<EngineeringFileResult> WriteFileAsync(Guid organizationId, Guid taskId, string relativePath, string content, CancellationToken cancellationToken = default);
    Task<EngineeringCommandResult> RunAllowlistedCommandAsync(Guid organizationId, Guid taskId, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}
