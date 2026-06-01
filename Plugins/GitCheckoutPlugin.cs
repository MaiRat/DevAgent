using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class GitCheckoutPlugin
{
    private readonly string _workspaceRoot;

    public GitCheckoutPlugin(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    [KernelFunction("derive_folder_name_from_repo_url")]
    [Description("Derive a safe default local folder name from a GitHub repository URL.")]
    public string DeriveFolderNameFromRepoUrl(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl)
    {
        if (!TryParseGitHubRepositoryUrl(repositoryUrl, out _, out var repositoryName))
        {
            return "Only https://github.com/owner/repo or https://github.com/owner/repo.git URLs are supported.";
        }

        return repositoryName;
    }

    [KernelFunction("build_git_clone_arguments")]
    [Description("Build git clone arguments for a GitHub repository into a target folder under the workspace.")]
    public string BuildGitCloneArguments(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null)
    {
        if (!TryParseGitHubRepositoryUrl(repositoryUrl, out _, out var repositoryName))
        {
            return "Only https://github.com/owner/repo or https://github.com/owner/repo.git URLs are supported.";
        }

        var safeFolder = string.IsNullOrWhiteSpace(targetFolder)
            ? repositoryName
            : SanitizeFolderName(targetFolder);

        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return "Target folder name is invalid.";
        }

        return $"clone \"{repositoryUrl}\" \"{safeFolder}\"";
    }

    [KernelFunction("build_git_checkout_arguments")]
    [Description("Build git checkout arguments for a revision.")]
    public string BuildGitCheckoutArguments(
        [Description("Branch, tag, or commit to checkout.")] string revision)
    {
        if (!IsSafeRevision(revision))
        {
            return "Revision contains unsupported characters.";
        }

        return $"checkout \"{revision}\"";
    }

    [KernelFunction("build_git_pull_arguments")]
    [Description("Build git pull arguments for a branch from a remote.")]
    public string BuildGitPullArguments(
        [Description("Remote name, for example 'origin'.")] string remoteName = "origin",
        [Description("Branch name to pull, for example 'main'.")] string branch = "main")
    {
        if (!IsSafeGitName(remoteName))
        {
            return "Remote name contains unsupported characters.";
        }

        if (!IsSafeRevision(branch))
        {
            return "Branch contains unsupported characters.";
        }

        return $"pull \"{remoteName}\" \"{branch}\"";
    }

    [KernelFunction("build_git_fetch_arguments")]
    [Description("Build git fetch arguments for a remote.")]
    public string BuildGitFetchArguments(
        [Description("Remote name, for example 'origin'.")] string remoteName = "origin")
    {
        if (!IsSafeGitName(remoteName))
        {
            return "Remote name contains unsupported characters.";
        }

        return $"fetch \"{remoteName}\"";
    }

    [KernelFunction("build_gh_pr_create_arguments")]
    [Description("Build GitHub CLI arguments for creating a pull request from the current repository.")]
    public string BuildGhPrCreateArguments(
        [Description("Pull request title.")] string title,
        [Description("Pull request body.")] string body,
        [Description("Base branch to merge into, for example 'main'.")] string baseBranch = "main",
        [Description("Head branch containing the changes.")] string headBranch = "",
        [Description("Whether to open the PR as a draft.")] bool draft = false)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Pull request title is required.";
        }

        if (!IsSafeRevision(baseBranch))
        {
            return "Base branch contains unsupported characters.";
        }

        if (!string.IsNullOrWhiteSpace(headBranch) && !IsSafeRevision(headBranch))
        {
            return "Head branch contains unsupported characters.";
        }

        var escapedTitle = EscapeForDoubleQuotes(title);
        var escapedBody = EscapeForDoubleQuotes(body ?? string.Empty);
        var headArgument = string.IsNullOrWhiteSpace(headBranch) ? string.Empty : $" --head \"{headBranch}\"";
        var draftArgument = draft ? " --draft" : string.Empty;

        return $"pr create --base \"{baseBranch}\"{headArgument} --title \"{escapedTitle}\" --body \"{escapedBody}\"{draftArgument}";
    }

    [KernelFunction("build_git_shallow_clone_arguments")]
    [Description("Build git shallow clone arguments for a GitHub repository into a target folder under the workspace.")]
    public string BuildGitShallowCloneArguments(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Optional branch to clone. Leave empty to use the default branch.")] string? branch = null,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null)
    {
        if (!TryParseGitHubRepositoryUrl(repositoryUrl, out _, out var repositoryName))
        {
            return "Only https://github.com/owner/repo or https://github.com/owner/repo.git URLs are supported.";
        }

        var safeFolder = string.IsNullOrWhiteSpace(targetFolder)
            ? repositoryName
            : SanitizeFolderName(targetFolder);

        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return "Target folder name is invalid.";
        }

        if (!string.IsNullOrWhiteSpace(branch) && !IsSafeRevision(branch))
        {
            return "Branch contains unsupported characters.";
        }

        var branchArgument = string.IsNullOrWhiteSpace(branch) ? string.Empty : $" --branch \"{branch}\"";
        return $"clone --depth 1{branchArgument} \"{repositoryUrl}\" \"{safeFolder}\"";
    }

    [KernelFunction("get_expected_repo_path")]
    [Description("Return the expected repository path under the workspace for a GitHub repository URL and optional target folder.")]
    public string GetExpectedRepoPath(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null)
    {
        if (!TryParseGitHubRepositoryUrl(repositoryUrl, out _, out var repositoryName))
        {
            return "Only https://github.com/owner/repo or https://github.com/owner/repo.git URLs are supported.";
        }

        var safeFolder = string.IsNullOrWhiteSpace(targetFolder)
            ? repositoryName
            : SanitizeFolderName(targetFolder);

        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return "Target folder name is invalid.";
        }

        return Path.Combine(_workspaceRoot, safeFolder);
    }

    [KernelFunction("build_clone_execution_request")]
    [Description("Build a policy-aware execution request summary for cloning a GitHub repository.")]
    public string BuildCloneExecutionRequest(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var arguments = BuildGitCloneArguments(repositoryUrl, targetFolder);
        if (!arguments.StartsWith("clone ", StringComparison.Ordinal))
        {
            return arguments;
        }

        return BuildExecutionRequestSummary("git", arguments, userApproved);
    }

    [KernelFunction("build_checkout_execution_request")]
    [Description("Build a policy-aware execution request summary for checking out a revision inside an existing cloned repository.")]
    public string BuildCheckoutExecutionRequest(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Branch, tag, or commit to checkout.")] string revision,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = GetExpectedRepoPath(repositoryUrl, targetFolder);
        if (!Path.IsPathRooted(repoPath))
        {
            return repoPath;
        }

        var arguments = BuildGitCheckoutArguments(revision);
        if (!arguments.StartsWith("checkout ", StringComparison.Ordinal))
        {
            return arguments;
        }

        return BuildExecutionRequestSummary("git", arguments, userApproved, repoPath);
    }

    [KernelFunction("build_pull_execution_request")]
    [Description("Build a policy-aware execution request summary for pulling a branch inside an existing cloned repository.")]
    public string BuildPullExecutionRequest(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Remote name, for example 'origin'.")] string remoteName = "origin",
        [Description("Branch name to pull, for example 'main'.")] string branch = "main",
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = GetExpectedRepoPath(repositoryUrl, targetFolder);
        if (!Path.IsPathRooted(repoPath))
        {
            return repoPath;
        }

        var arguments = BuildGitPullArguments(remoteName, branch);
        if (!arguments.StartsWith("pull ", StringComparison.Ordinal))
        {
            return arguments;
        }

        return BuildExecutionRequestSummary("git", arguments, userApproved, repoPath);
    }

    [KernelFunction("build_fetch_execution_request")]
    [Description("Build a policy-aware execution request summary for fetching from a remote inside an existing cloned repository.")]
    public string BuildFetchExecutionRequest(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Remote name, for example 'origin'.")] string remoteName = "origin",
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = GetExpectedRepoPath(repositoryUrl, targetFolder);
        if (!Path.IsPathRooted(repoPath))
        {
            return repoPath;
        }

        var arguments = BuildGitFetchArguments(remoteName);
        if (!arguments.StartsWith("fetch ", StringComparison.Ordinal))
        {
            return arguments;
        }

        return BuildExecutionRequestSummary("git", arguments, userApproved, repoPath);
    }

    [KernelFunction("build_pr_create_execution_request")]
    [Description("Build a policy-aware execution request summary for creating a GitHub pull request with gh inside an existing cloned repository.")]
    public string BuildPrCreateExecutionRequest(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Pull request title.")] string title,
        [Description("Pull request body.")] string body,
        [Description("Base branch to merge into, for example 'main'.")] string baseBranch = "main",
        [Description("Head branch containing the changes.")] string headBranch = "",
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null,
        [Description("Whether to open the PR as a draft.")] bool draft = false,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = GetExpectedRepoPath(repositoryUrl, targetFolder);
        if (!Path.IsPathRooted(repoPath))
        {
            return repoPath;
        }

        var arguments = BuildGhPrCreateArguments(title, body, baseBranch, headBranch, draft);
        if (!arguments.StartsWith("pr create ", StringComparison.Ordinal))
        {
            return arguments;
        }

        return BuildExecutionRequestSummary("gh", arguments, userApproved, repoPath);
    }

    [KernelFunction("build_clone_and_checkout_command")]
    [Description("Build a suggested command that clones a GitHub repository into the workspace and checks out a revision.")]
    public string BuildCloneAndCheckoutCommand(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Branch, tag, or commit to checkout after clone.")] string revision,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null)
    {
        if (!TryParseGitHubRepositoryUrl(repositoryUrl, out _, out var repositoryName))
        {
            return "Only https://github.com/owner/repo or https://github.com/owner/repo.git URLs are supported.";
        }

        var safeFolder = string.IsNullOrWhiteSpace(targetFolder)
            ? repositoryName
            : SanitizeFolderName(targetFolder);

        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return "Target folder name is invalid.";
        }

        if (!IsSafeRevision(revision))
        {
            return "Revision contains unsupported characters.";
        }

        return $"cd /d \"{_workspaceRoot}\" && git clone \"{repositoryUrl}\" \"{safeFolder}\" && cd /d \"{Path.Combine(_workspaceRoot, safeFolder)}\" && git checkout \"{revision}\"";
    }

    [KernelFunction("build_shallow_clone_command")]
    [Description("Build a suggested shallow git clone command for a GitHub repository into the workspace.")]
    public string BuildShallowCloneCommand(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Optional branch to clone. Leave empty to use the default branch.")] string? branch = null,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null)
    {
        if (!TryParseGitHubRepositoryUrl(repositoryUrl, out _, out var repositoryName))
        {
            return "Only https://github.com/owner/repo or https://github.com/owner/repo.git URLs are supported.";
        }

        var safeFolder = string.IsNullOrWhiteSpace(targetFolder)
            ? repositoryName
            : SanitizeFolderName(targetFolder);

        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return "Target folder name is invalid.";
        }

        if (!string.IsNullOrWhiteSpace(branch) && !IsSafeRevision(branch))
        {
            return "Branch contains unsupported characters.";
        }

        var branchArgument = string.IsNullOrWhiteSpace(branch) ? string.Empty : $" --branch \"{branch}\"";
        return $"cd /d \"{_workspaceRoot}\" && git clone --depth 1{branchArgument} \"{repositoryUrl}\" \"{safeFolder}\"";
    }

    [KernelFunction("build_github_archive_download_command")]
    [Description("Build a suggested PowerShell command to download a GitHub repository archive for a branch or tag into the workspace.")]
    public string BuildGithubArchiveDownloadCommand(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo' or 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Branch or tag name to download.")] string revision,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null)
    {
        if (!TryParseGitHubRepositoryUrl(repositoryUrl, out var owner, out var repositoryName))
        {
            return "Only https://github.com/owner/repo or https://github.com/owner/repo.git URLs are supported.";
        }

        if (!IsSafeRevision(revision))
        {
            return "Revision contains unsupported characters.";
        }

        var safeFolder = string.IsNullOrWhiteSpace(targetFolder)
            ? repositoryName
            : SanitizeFolderName(targetFolder);

        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return "Target folder name is invalid.";
        }

        var archiveUrl = $"https://github.com/{owner}/{repositoryName}/archive/refs/heads/{revision}.zip";
        var zipPath = Path.Combine(_workspaceRoot, safeFolder + ".zip");
        var extractPath = Path.Combine(_workspaceRoot, safeFolder);

        return $"powershell -NoProfile -Command \"Invoke-WebRequest -Uri '{archiveUrl}' -OutFile '{zipPath}'; Expand-Archive -Path '{zipPath}' -DestinationPath '{extractPath}' -Force\"";
    }

    [KernelFunction("build_clone_and_checkout_execution_request")]
    [Description("Build a policy-aware execution request summary for cloning a GitHub repository and checking out a revision.")]
    public string BuildCloneAndCheckoutExecutionRequest(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Branch, tag, or commit to checkout after clone.")] string revision,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var cloneArguments = BuildGitCloneArguments(repositoryUrl, targetFolder);
        if (!cloneArguments.StartsWith("clone ", StringComparison.Ordinal))
        {
            return cloneArguments;
        }

        var checkoutArguments = BuildGitCheckoutArguments(revision);
        if (!checkoutArguments.StartsWith("checkout ", StringComparison.Ordinal))
        {
            return checkoutArguments;
        }

        return BuildExecutionRequestSummary("git", $"{cloneArguments} && {checkoutArguments}", userApproved);
    }

    [KernelFunction("build_shallow_clone_execution_request")]
    [Description("Build a policy-aware execution request summary for shallow cloning a GitHub repository.")]
    public string BuildShallowCloneExecutionRequest(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Optional branch to clone. Leave empty to use the default branch.")] string? branch = null,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var arguments = BuildGitShallowCloneArguments(repositoryUrl, branch, targetFolder);
        if (!arguments.StartsWith("clone ", StringComparison.Ordinal))
        {
            return arguments;
        }

        return BuildExecutionRequestSummary("git", arguments, userApproved);
    }

    private static bool TryParseGitHubRepositoryUrl(string repositoryUrl, out string owner, out string repositoryName)
    {
        owner = string.Empty;
        repositoryName = string.Empty;

        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        owner = segments[0];
        repositoryName = segments[1];
        if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repositoryName = repositoryName[..^4];
        }

        owner = SanitizeFolderName(owner);
        repositoryName = SanitizeFolderName(repositoryName);

        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repositoryName);
    }

    private string BuildExecutionRequestSummary(string fileName, string arguments, bool userApproved, string? workingDirectory = null)
    {
        var policyLines = new List<string>
        {
            $"Execution request prepared for: {fileName}",
            $"UserApproved: {userApproved}",
            "This plugin builds a request summary only and does not execute commands.",
            $"Suggested arguments: {arguments}"
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            policyLines.Add($"Expected working directory: {workingDirectory}");
        }

        if (!userApproved)
        {
            policyLines.Add("Approval may be required before execution depending on the configured execution policy.");
        }

        policyLines.Add($"To evaluate this with the execution plugin, call request_command_execution with fileName='{fileName}', arguments='<the built arguments>', and the desired approval flag.");
        return string.Join(Environment.NewLine, policyLines);
    }

    private static string SanitizeFolderName(string? folderName)
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

    private static string EscapeForDoubleQuotes(string value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static bool IsSafeRevision(string revision)
    {
        return !string.IsNullOrWhiteSpace(revision)
            && Regex.IsMatch(revision, "^[A-Za-z0-9._/-]+$");
    }

    private static bool IsSafeGitName(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(value, "^[A-Za-z0-9._/-]+$");
    }
}
