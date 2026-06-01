using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class ShellCommandPlugin
{
    private readonly string _workspaceRoot;

    public ShellCommandPlugin(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    [KernelFunction("describe_shell_command_policy")]
    [Description("Describe the shell command safety policy for this agent.")]
    public string DescribeShellCommandPolicy()
    {
        return "Shell commands are not enabled in this scaffold. Add guarded process execution with an allowlist, working-directory restrictions, timeout handling, output capture, and explicit user approval for destructive commands.";
    }

    [KernelFunction("build_command_for_dotnet_restore")]
    [Description("Return a suggested dotnet restore command for the workspace.")]
    public string BuildCommandForDotnetRestore()
    {
        return $"cd \"{_workspaceRoot}\" && dotnet restore";
    }

    [KernelFunction("build_command_for_dotnet_build")]
    [Description("Return a suggested dotnet build command for the workspace.")]
    public string BuildCommandForDotnetBuild()
    {
        return $"cd \"{_workspaceRoot}\" && dotnet build";
    }

    [KernelFunction("build_command_for_dotnet_run")]
    [Description("Return a suggested dotnet run command for the workspace.")]
    public string BuildCommandForDotnetRun()
    {
        return $"cd \"{_workspaceRoot}\" && dotnet run";
    }

    [KernelFunction("build_command_for_tests")]
    [Description("Return a suggested dotnet test command for the workspace.")]
    public string BuildCommandForTests()
    {
        return $"cd \"{_workspaceRoot}\" && dotnet test";
    }

    [KernelFunction("build_command_for_git_clone")]
    [Description("Return a suggested git clone command for a GitHub repository into a target folder under the workspace.")]
    public string BuildCommandForGitClone(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Target folder name under the workspace.")] string targetFolder)
    {
        if (!IsSafeGitHubRepositoryUrl(repositoryUrl))
        {
            return "Only https://github.com/... repository URLs are supported.";
        }

        var safeFolder = SanitizeFolderName(targetFolder);
        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return "Target folder name is invalid.";
        }

        return $"cd /d \"{_workspaceRoot}\" && git clone \"{repositoryUrl}\" \"{safeFolder}\"";
    }

    [KernelFunction("build_command_for_git_checkout")]
    [Description("Return a suggested git checkout command for a repository folder under the workspace.")]
    public string BuildCommandForGitCheckout(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Branch, tag, or commit to checkout.")] string revision)
    {
        var safeFolder = SanitizeFolderName(repositoryFolder);
        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return "Repository folder name is invalid.";
        }

        if (!IsSafeRevision(revision))
        {
            return "Revision contains unsupported characters.";
        }

        return $"cd /d \"{Path.Combine(_workspaceRoot, safeFolder)}\" && git checkout \"{revision}\"";
    }

    [KernelFunction("print_guarded_shell_command_snippet")]
    [Description("Print a C# snippet showing how to implement guarded shell command execution for a Semantic Kernel plugin.")]
    public string PrintGuardedShellCommandSnippet()
    {
        return """
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.SemanticKernel;

public class ShellCommandPlugin
{
    private readonly string _workspaceRoot;
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet"
    };

    public ShellCommandPlugin(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    [KernelFunction("run_command")]
    [Description("Run an allowlisted shell command in the workspace.")]
    public async Task<string> RunCommandAsync(string fileName, string arguments)
    {
        if (!AllowedCommands.Contains(fileName))
        {
            return $"Command not allowed: {fileName}";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = _workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return $"ExitCode: {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}";
    }
}
""";
    }
    private static bool IsSafeGitHubRepositoryUrl(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(uri.AbsolutePath)
            && uri.AbsolutePath.Count(ch => ch == '/') >= 2;
    }

    private static string SanitizeFolderName(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return string.Empty;
        }

        var trimmed = folderName.Trim();
        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains("..", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return string.Concat(trimmed.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
    }

    private static bool IsSafeRevision(string revision)
    {
        return !string.IsNullOrWhiteSpace(revision)
            && Regex.IsMatch(revision, "^[A-Za-z0-9._/-]+$");
    }
}
