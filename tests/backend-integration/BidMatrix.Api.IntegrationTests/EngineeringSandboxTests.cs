using System.Diagnostics;
using BidMatrix.Application.Tools;
using BidMatrix.Infrastructure.Engineering;
using BidMatrix.Infrastructure.Tools;

namespace BidMatrix.Api.IntegrationTests;

public sealed class EngineeringSandboxTests
{
    [Fact]
    public void PolicyRequiresOwnerEngineeringTaskAndActiveWriteSwitch()
    {
        var engine = new DeterministicPolicyEngine();
        var tool = new ToolDefinitionSnapshot(
            Guid.CreateVersion7(), "repo.writeFile", "yellow", "internal_write", true, "policy");
        var controls = new Dictionary<string, bool>
        {
            ["allAgentsEnabled"] = true,
            ["engineeringWritesEnabled"] = true,
            ["externalToolExecutionEnabled"] = false,
        };

        Assert.Equal("engineering_task_required", engine.Evaluate(new PolicyEvaluationContext(
            tool, "engineering", "engineering", false, controls, "Development")).ReasonCode);

        controls["engineeringWritesEnabled"] = false;
        Assert.Equal("engineering_writes_disabled", engine.Evaluate(new PolicyEvaluationContext(
            tool, "engineering", "engineering", true, controls, "Development")).ReasonCode);

        controls["engineeringWritesEnabled"] = true;
        Assert.Equal(ToolDecisions.Allowed, engine.Evaluate(new PolicyEvaluationContext(
            tool, "engineering", "engineering", true, controls, "Development")).Decision);
    }

    [Fact]
    public async Task SandboxBlocksEscapesSecretsAndArbitraryCommandsAndProducesDiff()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"bidmatrix-sandbox-test-{Guid.NewGuid():N}");
        var repository = Path.Combine(testRoot, "repository");
        var worktrees = Path.Combine(testRoot, "worktrees");
        Directory.CreateDirectory(repository);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# Fixture\n\nOriginal.\n");
            await File.WriteAllTextAsync(Path.Combine(repository, ".env"), "SHOULD_NOT_BE_READ=true\n");
            RunGit(repository, "init", "--initial-branch=main");
            RunGit(repository, "config", "user.email", "sandbox@example.invalid");
            RunGit(repository, "config", "user.name", "Sandbox Test");
            RunGit(repository, "add", "README.md", ".env");
            RunGit(repository, "commit", "-m", "Initial fixture");

            var service = new EngineeringSandboxService(new EngineeringSandboxOptions
            {
                BaseRepositoryPath = repository,
                WorktreeRootPath = worktrees,
                CommandTimeoutSeconds = 10,
                MaximumOutputBytes = 16_384,
            });
            var organizationId = Guid.CreateVersion7();
            var taskId = Guid.CreateVersion7();

            var created = await service.CreateWorktreeAsync(organizationId, taskId, "HEAD");
            Assert.Equal("active", created.Status);
            Assert.Equal(40, created.HeadRevision.Length);

            var read = await service.ReadFileAsync(organizationId, taskId, "README.md");
            Assert.Contains("Original", read.Content, StringComparison.Ordinal);
            await service.WriteFileAsync(
                organizationId,
                taskId,
                "README.md",
                "# Fixture\n\nChanged only inside the isolated worktree.\n");

            var command = await service.RunAllowlistedCommandAsync(
                organizationId, taskId, "git", ["diff", "--check"]);
            Assert.Equal(0, command.ExitCode);
            var diff = await service.GetDiffAsync(organizationId, taskId);
            Assert.Contains("Changed only inside", diff.Diff, StringComparison.Ordinal);
            Assert.Matches("^[0-9a-f]{64}$", diff.Sha256);
            Assert.Equal("# Fixture\n\nOriginal.\n", await File.ReadAllTextAsync(Path.Combine(repository, "README.md")));

            await Assert.ThrowsAsync<ToolGatewayException>(() =>
                service.ReadFileAsync(organizationId, taskId, "../repository/README.md"));
            await Assert.ThrowsAsync<ToolGatewayException>(() =>
                service.ReadFileAsync(organizationId, taskId, ".env"));
            await Assert.ThrowsAsync<ToolGatewayException>(() =>
                service.RunAllowlistedCommandAsync(organizationId, taskId, "git", ["push"]));

            var outside = Path.Combine(testRoot, "outside");
            Directory.CreateDirectory(outside);
            await File.WriteAllTextAsync(Path.Combine(outside, "outside.txt"), "outside");
            var worktree = Path.Combine(
                worktrees,
                organizationId.ToString("N"),
                taskId.ToString("N"),
                "worktree");
            var escapeLink = Path.Combine(worktree, "escape-link");
            CreateDirectoryLink(escapeLink, outside);
            await Assert.ThrowsAsync<ToolGatewayException>(() =>
                service.ReadFileAsync(organizationId, taskId, "escape-link/outside.txt"));
            Directory.Delete(escapeLink);
        }
        finally
        {
            if (Directory.Exists(testRoot)) DeleteTestDirectory(testRoot);
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git did not start.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }

    private static void DeleteTestDirectory(string testRoot)
    {
        foreach (var path in Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        Directory.Delete(testRoot, recursive: true);
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", linkPath, targetPath })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Junction creation did not start.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }
}
