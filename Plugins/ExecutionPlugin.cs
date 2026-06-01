using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class ExecutionPlugin
{
    private readonly string _workspaceRoot;
    private readonly bool _enabled;
    private readonly bool _requireApproval;
    private readonly int _timeoutSeconds;
    private readonly string[] _allowedCommands;

    public ExecutionPlugin(string workspaceRoot, IConfiguration configuration)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _enabled = bool.TryParse(configuration["Execution:Enabled"], out var enabled) && enabled;
        _requireApproval = !bool.TryParse(configuration["Execution:RequireApproval"], out var requireApproval) || requireApproval;
        _timeoutSeconds = int.TryParse(configuration["Execution:TimeoutSeconds"], out var timeoutSeconds) ? timeoutSeconds : 60;

        var configuredCommands = configuration
            .GetSection("Execution:AllowedCommands")
            .GetChildren()
            .Select(section => section.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _allowedCommands = configuredCommands.Length > 0
            ? configuredCommands
            : new[] { "dotnet", "git", "gh" };
    }

    [KernelFunction("describe_execution_policy")]
    [Description("Describe the configured execution policy for this agent.")]
    public string DescribeExecutionPolicy()
    {
        return $"Execution enabled: {_enabled}; Require approval: {_requireApproval}; TimeoutSeconds: {_timeoutSeconds}; AllowedCommands: {string.Join(", ", _allowedCommands)}; WorkspaceRoot: {_workspaceRoot}";
    }

    [KernelFunction("request_command_execution")]
    [Description("Prepare a command execution request. This scaffold does not run commands; it returns the policy decision and request summary.")]
    public string RequestCommandExecution(
        [Description("Executable name, for example 'dotnet'.")] string fileName,
        [Description("Arguments to pass to the executable.")] string arguments,
        [Description("Whether the user explicitly approved this execution request.")] bool userApproved = false)
    {
        if (!_enabled)
        {
            return "Execution is disabled by configuration. No command was run.";
        }

        if (!_allowedCommands.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return $"Command not allowed by policy: {fileName}";
        }

        if (_requireApproval && !userApproved)
        {
            return $"Approval required before execution. Command: {fileName} {arguments}";
        }

        return $"Execution scaffold accepted request. Command would run in '{_workspaceRoot}' with timeout {_timeoutSeconds}s: {fileName} {arguments}";
    }

    [KernelFunction("request_command_execution_in_directory")]
    [Description("Prepare a command execution request for a specific working directory under the workspace. This scaffold does not run commands; it returns the policy decision and request summary.")]
    public string RequestCommandExecutionInDirectory(
        [Description("Executable name, for example 'git'.")] string fileName,
        [Description("Arguments to pass to the executable.")] string arguments,
        [Description("Working directory inside the workspace. Can be absolute if it remains under the workspace root.")] string workingDirectory,
        [Description("Whether the user explicitly approved this execution request.")] bool userApproved = false)
    {
        if (!_enabled)
        {
            return "Execution is disabled by configuration. No command was run.";
        }

        if (!_allowedCommands.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return $"Command not allowed by policy: {fileName}";
        }

        var resolvedWorkingDirectory = ResolveWorkingDirectory(workingDirectory);
        if (_requireApproval && !userApproved)
        {
            return $"Approval required before execution. Command: {fileName} {arguments}; WorkingDirectory: {resolvedWorkingDirectory}";
        }

        return $"Execution scaffold accepted request. Command would run in '{resolvedWorkingDirectory}' with timeout {_timeoutSeconds}s: {fileName} {arguments}";
    }

    [KernelFunction("build_execution_configuration_example")]
    [Description("Print an appsettings.json example for execution policy configuration.")]
    public string BuildExecutionConfigurationExample()
    {
        return """
{
  "Execution": {
    "Enabled": false,
    "RequireApproval": true,
    "TimeoutSeconds": 60,
    "AllowedCommands": [ "dotnet", "git", "gh" ]
  }
}
""";
    }

    [KernelFunction("print_execution_plugin_snippet")]
    [Description("Print a C# snippet showing how a real guarded execution plugin could be implemented.")]
    public string PrintExecutionPluginSnippet()
    {
        return """
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.SemanticKernel;

public class ExecutionPlugin
{
    private readonly string _workspaceRoot;
    private readonly HashSet<string> _allowedCommands = new(StringComparer.OrdinalIgnoreCase) { "dotnet" };

    public ExecutionPlugin(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    [KernelFunction("run_command")]
    [Description("Run an allowlisted command in the workspace.")]
    public async Task<string> RunCommandAsync(string fileName, string arguments, int timeoutSeconds = 60)
    {
        if (!_allowedCommands.Contains(fileName))
        {
            return $"Command not allowed: {fileName}";
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = _workspaceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return $"ExitCode: {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}";
    }
}
""";
    }
    private string ResolveWorkingDirectory(string workingDirectory)
    {
        var candidate = string.IsNullOrWhiteSpace(workingDirectory)
            ? _workspaceRoot
            : workingDirectory;

        var combined = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(Path.Combine(_workspaceRoot, candidate));

        if (!combined.StartsWith(_workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Working directory escapes the workspace root.");
        }

        return combined;
    }
}
