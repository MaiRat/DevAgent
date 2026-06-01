using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class ProjectAnalysisPlugin
{
    private readonly string _workspaceRoot;

    public ProjectAnalysisPlugin(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    [KernelFunction("find_solution_and_project_files")]
    [Description("Find .sln and common .NET project files in the workspace.")]
    public string FindSolutionAndProjectFiles()
    {
        var files = Directory
            .EnumerateFiles(_workspaceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .Select(ToRelativePath)
            .ToList();

        return files.Count == 0 ? "No solution or project files found." : string.Join(Environment.NewLine, files);
    }

    [KernelFunction("summarize_project_file")]
    [Description("Summarize key information from a .csproj, .fsproj, or .vbproj file.")]
    public string SummarizeProjectFile(
        [Description("Relative path to the project file.")] string relativePath)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath))
        {
            return $"File not found: {relativePath}";
        }

        var content = File.ReadAllText(fullPath);
        var sb = new StringBuilder();
        sb.AppendLine($"Project: {relativePath}");
        AppendIfFound(sb, content, "<TargetFramework>", "TargetFramework");
        AppendIfFound(sb, content, "<TargetFrameworks>", "TargetFrameworks");
        AppendIfFound(sb, content, "<OutputType>", "OutputType");
        AppendPackageReferences(sb, content);
        return sb.ToString().TrimEnd();
    }

    [KernelFunction("list_csharp_source_files")]
    [Description("List C# source files in the workspace.")]
    public string ListCSharpSourceFiles()
    {
        var files = Directory
            .EnumerateFiles(_workspaceRoot, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path)
            .Select(ToRelativePath)
            .ToList();

        return files.Count == 0 ? "No C# source files found." : string.Join(Environment.NewLine, files);
    }

    [KernelFunction("summarize_directory_tree")]
    [Description("Summarize the directory tree up to a limited depth.")]
    public string SummarizeDirectoryTree(
        [Description("Relative path to start from. Use '.' for workspace root.")] string relativePath = ".",
        [Description("Maximum recursion depth. Recommended range: 1 to 4.")] int maxDepth = 2)
    {
        var root = ResolveSafePath(relativePath);
        if (!Directory.Exists(root))
        {
            return $"Directory not found: {relativePath}";
        }

        maxDepth = Math.Clamp(maxDepth, 0, 6);
        var sb = new StringBuilder();
        AppendDirectoryTree(sb, root, 0, maxDepth);
        return sb.ToString().TrimEnd();
    }

    [KernelFunction("find_files_by_name")]
    [Description("Find files by wildcard pattern in the workspace, for example '*.json' or 'Program.cs'.")]
    public string FindFilesByName(
        [Description("Wildcard search pattern.")] string pattern)
    {
        var files = Directory
            .EnumerateFiles(_workspaceRoot, pattern, SearchOption.AllDirectories)
            .OrderBy(path => path)
            .Select(ToRelativePath)
            .ToList();

        return files.Count == 0 ? $"No files found matching pattern: {pattern}" : string.Join(Environment.NewLine, files);
    }

    private void AppendDirectoryTree(StringBuilder sb, string directory, int depth, int maxDepth)
    {
        var indent = new string(' ', depth * 2);
        var name = depth == 0 ? ToRelativePath(directory) : Path.GetFileName(directory);
        sb.AppendLine($"{indent}[DIR] {name}");

        if (depth >= maxDepth)
        {
            return;
        }

        foreach (var subDirectory in Directory.GetDirectories(directory).OrderBy(x => x))
        {
            AppendDirectoryTree(sb, subDirectory, depth + 1, maxDepth);
        }

        foreach (var file in Directory.GetFiles(directory).OrderBy(x => x))
        {
            sb.AppendLine($"{indent}  [FILE] {Path.GetFileName(file)}");
        }
    }

    private static void AppendIfFound(StringBuilder sb, string content, string tag, string label)
    {
        var start = content.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return;

        start += tag.Length;
        var endTag = "</" + tag.Trim('<', '>') + ">";
        var end = content.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return;

        var value = content[start..end].Trim();
        sb.AppendLine($"{label}: {value}");
    }

    private static void AppendPackageReferences(StringBuilder sb, string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var packages = lines
            .Where(line => line.Contains("<PackageReference ", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .ToList();

        if (packages.Count == 0)
        {
            sb.AppendLine("PackageReferences: none found");
            return;
        }

        sb.AppendLine("PackageReferences:");
        foreach (var package in packages)
        {
            sb.AppendLine("- " + package);
        }
    }

    private string ResolveSafePath(string relativePath)
    {
        relativePath ??= ".";
        var combined = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));
        if (!combined.StartsWith(_workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path escapes the workspace root.");
        }

        return combined;
    }

    private string ToRelativePath(string fullPath)
    {
        var relative = Path.GetRelativePath(_workspaceRoot, fullPath);
        return string.IsNullOrWhiteSpace(relative) ? "." : relative;
    }
}
