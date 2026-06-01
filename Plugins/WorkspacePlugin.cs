using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class WorkspacePlugin
{
    private readonly string _workspaceRoot;
    private readonly string[] _writableRoots;

    public WorkspacePlugin(string workspaceRoot, IConfiguration? configuration = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _writableRoots = [.. GetWritableRoots(configuration)];
    }

    [KernelFunction("summarize_workspace")]
    [Description("Summarize the purpose of the current coding workspace.")]
    public string SummarizeWorkspace()
    {
        return $"This workspace at '{_workspaceRoot}' hosts a bootstrap coding agent project built with C# and Semantic Kernel using OpenAI chat completion.";
    }

    [KernelFunction("suggest_next_step")]
    [Description("Suggest the next practical step for setting up the coding agent.")]
    public string SuggestNextStep()
    {
        return "Use the file and code editing functions to inspect files, create task notes, and apply focused text-based changes.";
    }

    [KernelFunction("list_files")]
    [Description("List files and directories in a relative workspace path. Use '.' for the workspace root.")]
    public string ListFiles([Description("Relative path inside the workspace.")] string relativePath = ".")
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!Directory.Exists(fullPath)) return $"Directory not found: {relativePath}";

        var entries = Directory.GetFileSystemEntries(fullPath)
            .OrderBy(x => x)
            .Select(path => Directory.Exists(path) ? $"[DIR]  {Path.GetFileName(path)}" : $"[FILE] {Path.GetFileName(path)}");

        return string.Join(Environment.NewLine, entries);
    }

    [KernelFunction("read_file")]
    [Description("Read a text file from the workspace using a relative path.")]
    public string ReadFile([Description("Relative file path inside the workspace.")] string relativePath)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";
        return File.ReadAllText(fullPath);
    }

    [KernelFunction("write_file")]
    [Description("Write text content to a file path. Relative paths stay inside the workspace root; absolute paths are allowed only when they match the workspace root or an explicitly configured writable root. Creates directories if needed.")]
    public string WriteFile(
        [Description("Relative file path inside the workspace, or an absolute path under the workspace root or an allowed writable root.")] string relativePath,
        [Description("The full text content to write into the file.")] string content)
    {
        var fullPath = ResolveWritablePath(relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        return $"Wrote file: {relativePath}";
    }

    [KernelFunction("append_file")]
    [Description("Append text content to a file path. Relative paths stay inside the workspace root; absolute paths are allowed only when they match the workspace root or an explicitly configured writable root. Creates the file if needed.")]
    public string AppendFile(
        [Description("Relative file path inside the workspace, or an absolute path under the workspace root or an allowed writable root.")] string relativePath,
        [Description("The text content to append.")] string content)
    {
        var fullPath = ResolveWritablePath(relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.AppendAllText(fullPath, content, Encoding.UTF8);
        return $"Appended file: {relativePath}";
    }

    [KernelFunction("create_task_note")]
    [Description("Create a markdown task note under the tasks folder.")]
    public string CreateTaskNote(
        [Description("Task file name without extension.")] string name,
        [Description("Task description or plan in markdown.")] string description)
    {
        var safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch)).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "task";

        var relativePath = Path.Combine("tasks", safeName + ".md");
        var content = $"# {safeName}\n\n{description}\n";
        return WriteFile(relativePath, content);
    }

    private string ResolveSafePath(string relativePath)
    {
        relativePath ??= ".";
        var combined = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));
        if (!IsPathWithinRoot(combined, _workspaceRoot))
            throw new InvalidOperationException("Path escapes the workspace root.");
        return combined;
    }

    private string ResolveWritablePath(string path)
    {
        path ??= ".";
        var combined = Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : ResolveSafePath(path);

        if (_writableRoots.All(root => !IsPathWithinRoot(combined, root)))
        {
            throw new InvalidOperationException("Path is not under the workspace root or an allowed writable root.");
        }

        return combined;
    }

    private IEnumerable<string> GetWritableRoots(IConfiguration? configuration)
    {
        yield return _workspaceRoot;

        if (configuration is null)
        {
            yield break;
        }

        foreach (var configuredRoot in configuration.GetSection("Agent:WritableRoots").GetChildren())
        {
            var root = configuredRoot.Value;
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (!Path.IsPathFullyQualified(root))
            {
                throw new InvalidOperationException($"Configured writable root must be an absolute path: {root}");
            }

            yield return Path.GetFullPath(root);
        }
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedPath = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(root);

        if (string.Equals(normalizedPath, normalizedRoot, comparison))
        {
            return true;
        }

        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        return normalizedPath.StartsWith(normalizedRoot, comparison);
    }
}
