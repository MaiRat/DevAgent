using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class GitAnalysisPlugin
{
    private readonly string _workspaceRoot;

    public GitAnalysisPlugin(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    [KernelFunction("detect_git_repository")]
    [Description("Detect whether the workspace appears to be inside a Git repository.")]
    public string DetectGitRepository()
    {
        var current = _workspaceRoot;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var gitDirectory = Path.Combine(current, ".git");
            if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory))
            {
                return $"Git repository detected at: {current}";
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        return "No Git repository detected from the workspace root upward.";
    }

    [KernelFunction("find_gitignore_files")]
    [Description("Find .gitignore files in the workspace.")]
    public string FindGitignoreFiles()
    {
        var files = Directory
            .EnumerateFiles(_workspaceRoot, ".gitignore", SearchOption.AllDirectories)
            .OrderBy(x => x)
            .Select(ToRelativePath)
            .ToList();

        return files.Count == 0 ? "No .gitignore files found." : string.Join(Environment.NewLine, files);
    }

    [KernelFunction("summarize_gitignore")]
    [Description("Summarize non-empty, non-comment entries in a .gitignore file.")]
    public string SummarizeGitignore(
        [Description("Relative path to the .gitignore file.")] string relativePath = ".gitignore")
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath))
        {
            return $"File not found: {relativePath}";
        }

        var entries = File.ReadAllLines(fullPath)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .ToList();

        return entries.Count == 0 ? "No active .gitignore entries found." : string.Join(Environment.NewLine, entries);
    }

    [KernelFunction("find_git_related_files")]
    [Description("Find common Git-related files such as .gitignore, .gitattributes, and GitHub workflow files.")]
    public string FindGitRelatedFiles()
    {
        var results = new List<string>();

        results.AddRange(Directory.EnumerateFiles(_workspaceRoot, ".gitignore", SearchOption.AllDirectories));
        results.AddRange(Directory.EnumerateFiles(_workspaceRoot, ".gitattributes", SearchOption.AllDirectories));

        var githubDirectory = Path.Combine(_workspaceRoot, ".github");
        if (Directory.Exists(githubDirectory))
        {
            results.AddRange(Directory.EnumerateFiles(githubDirectory, "*.*", SearchOption.AllDirectories));
        }

        var distinct = results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .Select(ToRelativePath)
            .ToList();

        return distinct.Count == 0 ? "No common Git-related files found." : string.Join(Environment.NewLine, distinct);
    }

    [KernelFunction("suggest_git_hygiene_steps")]
    [Description("Suggest practical Git hygiene steps based on files visible in the workspace.")]
    public string SuggestGitHygieneSteps()
    {
        var suggestions = new List<string>();

        if (!File.Exists(Path.Combine(_workspaceRoot, ".gitignore")))
        {
            suggestions.Add("Add a root .gitignore file.");
        }

        if (Directory.Exists(Path.Combine(_workspaceRoot, ".agent-backups")))
        {
            suggestions.Add("Ensure .agent-backups/ is ignored in .gitignore.");
        }

        if (!Directory.Exists(Path.Combine(_workspaceRoot, ".github")))
        {
            suggestions.Add("Consider adding .github workflows or repository templates if this project will be shared.");
        }

        suggestions.Add("Review generated files, secrets, and local tooling artifacts before committing.");
        suggestions.Add("Prefer small, focused commits after validating changes.");

        return string.Join(Environment.NewLine, suggestions.Distinct());
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
