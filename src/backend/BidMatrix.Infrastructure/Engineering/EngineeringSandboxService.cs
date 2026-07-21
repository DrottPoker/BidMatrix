using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BidMatrix.Application.Engineering;
using BidMatrix.Application.Tools;

namespace BidMatrix.Infrastructure.Engineering;

public sealed partial class EngineeringSandboxService(EngineeringSandboxOptions options) : IEngineeringSandboxService
{
    private static readonly HashSet<string> AllowedCommandSignatures = new(StringComparer.Ordinal)
    {
        "git\0status\0--short",
        "git\0diff\0--no-ext-diff",
        "git\0diff\0--check",
        "dotnet\0restore",
        "dotnet\0build",
        "dotnet\0test",
        "python\0-m\0pytest",
        "python\0-m\0ruff\0check\0.",
        "python\0-m\0mypy",
        "npm\0ci\0--offline",
        "npm\0run\0lint",
        "npm\0run\0typecheck",
        "npm\0run\0test",
        "npm\0run\0build",
    };

    private static readonly HashSet<string> BlockedPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".ssh", ".aws", ".azure", ".kube", "credentials", "secrets",
    };

    public async Task<EngineeringWorktreeResult> CreateWorktreeAsync(
        Guid organizationId,
        Guid taskId,
        string baseRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifiers(organizationId, taskId);
        if (string.IsNullOrWhiteSpace(baseRevision) || baseRevision.Length > 200 ||
            baseRevision.StartsWith('-') || !RevisionPattern().IsMatch(baseRevision))
        {
            throw Invalid("invalid_base_revision", "The base revision contains unsupported characters.");
        }

        var baseRepository = Path.GetFullPath(options.BaseRepositoryPath);
        if (!Directory.Exists(baseRepository) || !Directory.Exists(Path.Combine(baseRepository, ".git")))
        {
            throw Invalid("base_repository_unavailable", "The configured engineering fixture repository is not a Git repository.", 409);
        }

        var sandboxRoot = GetSandboxRoot(organizationId, taskId);
        var mirrorPath = Path.Combine(sandboxRoot, "mirror.git");
        var worktreePath = Path.Combine(sandboxRoot, "worktree");
        if (Directory.Exists(worktreePath))
        {
            var head = await RunProcessAsync("git", ["rev-parse", "HEAD"], worktreePath, 30, cancellationToken);
            EnsureSuccess(head, "sandbox_head_failed");
            return new EngineeringWorktreeResult(taskId.ToString(), baseRevision, head.StandardOutput.Trim(), "active");
        }

        Directory.CreateDirectory(sandboxRoot);
        try
        {
            var clone = await RunProcessAsync(
                "git", ["clone", "--bare", "--no-hardlinks", baseRepository, mirrorPath], sandboxRoot, 30, cancellationToken);
            EnsureSuccess(clone, "sandbox_clone_failed");
            var add = await RunProcessAsync(
                "git", ["--git-dir", mirrorPath, "worktree", "add", "--detach", worktreePath, baseRevision], sandboxRoot, 30, cancellationToken);
            EnsureSuccess(add, "sandbox_worktree_failed");
            var head = await RunProcessAsync("git", ["rev-parse", "HEAD"], worktreePath, 30, cancellationToken);
            EnsureSuccess(head, "sandbox_head_failed");
            return new EngineeringWorktreeResult(taskId.ToString(), baseRevision, head.StandardOutput.Trim(), "active");
        }
        catch
        {
            DeleteSandboxRoot(sandboxRoot);
            throw;
        }
    }

    public async Task<EngineeringFileResult> ReadFileAsync(
        Guid organizationId,
        Guid taskId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var filePath = ResolveSafePath(organizationId, taskId, relativePath, mustExist: true);
        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length > options.MaximumFileBytes)
        {
            throw Invalid("sandbox_file_unavailable", "The sandbox file is missing or exceeds the read limit.", 404);
        }
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return new EngineeringFileResult(NormalizeRelativePath(relativePath), content, file.Length);
    }

    public async Task<IReadOnlyList<EngineeringSearchMatch>> SearchAsync(
        Guid organizationId,
        Guid taskId,
        string query,
        string? relativePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200)
        {
            throw Invalid("invalid_search_query", "The repository search query is invalid.");
        }
        var worktree = GetExistingWorktree(organizationId, taskId);
        var searchRoot = string.IsNullOrWhiteSpace(relativePath)
            ? worktree
            : ResolveSafePath(organizationId, taskId, relativePath, mustExist: true);
        var files = File.Exists(searchRoot)
            ? [searchRoot]
            : Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
                .Where(path => !ContainsReparsePoint(path, worktree))
                .Take(options.MaximumFileCount + 1)
                .ToArray();
        if (files.Length > options.MaximumFileCount)
        {
            throw Invalid("sandbox_file_limit", "The sandbox contains too many files to search.", 409);
        }

        var matches = new List<EngineeringSearchMatch>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if (info.Length > options.MaximumFileBytes || IsBlockedPath(Path.GetRelativePath(worktree, file))) continue;
            string[] lines;
            try { lines = await File.ReadAllLinesAsync(file, cancellationToken); }
            catch (DecoderFallbackException) { continue; }
            for (var index = 0; index < lines.Length && matches.Count < 200; index++)
            {
                if (lines[index].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new EngineeringSearchMatch(
                        Path.GetRelativePath(worktree, file).Replace('\\', '/'), index + 1,
                        lines[index].Length <= 500 ? lines[index] : lines[index][..500]));
                }
            }
            if (matches.Count >= 200) break;
        }
        return matches;
    }

    public Task<EngineeringCommandResult> GetStatusAsync(
        Guid organizationId,
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        RunAllowlistedCommandAsync(organizationId, taskId, "git", ["status", "--short"], cancellationToken);

    public async Task<EngineeringDiffResult> GetDiffAsync(
        Guid organizationId,
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAllowlistedCommandAsync(
            organizationId, taskId, "git", ["diff", "--no-ext-diff"], cancellationToken);
        EnsureSuccess(result, "sandbox_diff_failed");
        var bytes = Encoding.UTF8.GetBytes(result.StandardOutput);
        return new EngineeringDiffResult(
            result.StandardOutput,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bytes.Length);
    }

    public async Task<EngineeringFileResult> WriteFileAsync(
        Guid organizationId,
        Guid taskId,
        string relativePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (Encoding.UTF8.GetByteCount(content) > options.MaximumFileBytes)
        {
            throw Invalid("sandbox_file_too_large", "The sandbox write exceeds the file-size limit.");
        }
        var filePath = ResolveSafePath(organizationId, taskId, relativePath, mustExist: false);
        var parent = Path.GetDirectoryName(filePath) ?? throw Invalid("invalid_sandbox_path", "The sandbox path has no parent.");
        Directory.CreateDirectory(parent);
        if (ContainsReparsePoint(parent, GetExistingWorktree(organizationId, taskId)))
        {
            throw Invalid("sandbox_symlink_escape", "Symbolic links and reparse points are blocked.", 403);
        }
        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(false), cancellationToken);
        return new EngineeringFileResult(NormalizeRelativePath(relativePath), content, Encoding.UTF8.GetByteCount(content));
    }

    public async Task<EngineeringCommandResult> RunAllowlistedCommandAsync(
        Guid organizationId,
        Guid taskId,
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifiers(organizationId, taskId);
        if (arguments.Any(argument => argument.Contains('\0')) ||
            !AllowedCommandSignatures.Contains(string.Join('\0', [executable, .. arguments])))
        {
            throw Invalid("command_not_allowlisted", "The requested command and argument array is not allowlisted.", 403);
        }
        var worktree = GetExistingWorktree(organizationId, taskId);
        return await RunProcessAsync(executable, arguments, worktree, options.CommandTimeoutSeconds, cancellationToken);
    }

    private async Task<EngineeringCommandResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment["HTTP_PROXY"] = "http://127.0.0.1:9";
        startInfo.Environment["HTTPS_PROXY"] = "http://127.0.0.1:9";
        startInfo.Environment["ALL_PROXY"] = "http://127.0.0.1:9";
        startInfo.Environment["NO_PROXY"] = string.Empty;
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["NPM_CONFIG_OFFLINE"] = "true";

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start()) throw Invalid("sandbox_command_start_failed", "The sandbox command did not start.", 500);
        }
        catch (Exception exception) when (exception is not ToolGatewayException)
        {
            throw Invalid("sandbox_command_unavailable", $"The allowlisted executable is unavailable: {exception.GetType().Name}.", 409);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw Invalid("sandbox_command_timeout", "The allowlisted command exceeded its timeout.", 408);
        }
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        stopwatch.Stop();
        if (Encoding.UTF8.GetByteCount(standardOutput) + Encoding.UTF8.GetByteCount(standardError) > options.MaximumOutputBytes)
        {
            throw Invalid("sandbox_output_limit", "The command output exceeded the configured limit.", 409);
        }
        return new EngineeringCommandResult(
            executable, arguments, process.ExitCode, standardOutput, standardError, stopwatch.ElapsedMilliseconds);
    }

    private string ResolveSafePath(Guid organizationId, Guid taskId, string relativePath, bool mustExist)
    {
        ValidateIdentifiers(organizationId, taskId);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 500 ||
            Path.IsPathRooted(relativePath) || IsBlockedPath(relativePath))
        {
            throw Invalid("invalid_sandbox_path", "The repository path is not permitted.", 403);
        }
        var worktree = GetExistingWorktree(organizationId, taskId);
        var fullPath = Path.GetFullPath(Path.Combine(worktree, relativePath));
        EnsureContained(fullPath, worktree);
        if (ContainsReparsePoint(mustExist ? fullPath : Path.GetDirectoryName(fullPath)!, worktree))
        {
            throw Invalid("sandbox_symlink_escape", "Symbolic links and reparse points are blocked.", 403);
        }
        if (mustExist && !File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw Invalid("sandbox_path_not_found", "The requested sandbox path was not found.", 404);
        }
        return fullPath;
    }

    private string GetExistingWorktree(Guid organizationId, Guid taskId)
    {
        var worktree = Path.Combine(GetSandboxRoot(organizationId, taskId), "worktree");
        if (!Directory.Exists(worktree))
        {
            throw Invalid("sandbox_not_created", "Create the isolated worktree before using repository tools.", 409);
        }
        return worktree;
    }

    private string GetSandboxRoot(Guid organizationId, Guid taskId)
    {
        var root = Path.GetFullPath(options.WorktreeRootPath);
        var sandbox = Path.GetFullPath(Path.Combine(root, organizationId.ToString("N"), taskId.ToString("N")));
        EnsureContained(sandbox, root);
        return sandbox;
    }

    private static bool IsBlockedPath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment is "." or ".." || BlockedPathSegments.Contains(segment) ||
            segment.StartsWith(".env", StringComparison.OrdinalIgnoreCase) ||
            segment.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            segment.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            segment.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
            segment.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
            segment.EndsWith(".key", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsReparsePoint(string path, string boundary)
    {
        var current = Path.GetFullPath(path);
        var root = Path.GetFullPath(boundary);
        while (current.Length >= root.Length)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            if (string.Equals(current, root, PathComparison)) break;
            var parent = Path.GetDirectoryName(current);
            if (parent is null) break;
            current = parent;
        }
        return false;
    }

    private static void EnsureContained(string candidate, string root)
    {
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, PathComparison) && !string.Equals(candidate, Path.TrimEndingDirectorySeparator(root), PathComparison))
        {
            throw Invalid("sandbox_path_escape", "The repository path escapes the generated worktree.", 403);
        }
    }

    private void DeleteSandboxRoot(string sandboxRoot)
    {
        var configuredRoot = Path.GetFullPath(options.WorktreeRootPath);
        EnsureContained(Path.GetFullPath(sandboxRoot), configuredRoot);
        if (Directory.Exists(sandboxRoot)) Directory.Delete(sandboxRoot, recursive: true);
    }

    private static void EnsureSuccess(EngineeringCommandResult result, string code)
    {
        if (result.ExitCode != 0)
        {
            throw Invalid(code, $"The sandbox command failed with exit code {result.ExitCode}: {result.StandardError}", 409);
        }
    }

    private static void ValidateIdentifiers(Guid organizationId, Guid taskId)
    {
        if (organizationId == Guid.Empty || taskId == Guid.Empty)
            throw Invalid("invalid_sandbox_context", "Organization and task identifiers are required.");
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static ToolGatewayException Invalid(string code, string message, int statusCode = 400) => new(code, message, statusCode);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();
}
