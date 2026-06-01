using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class GitHubProjectsPlugin
{
    private readonly string _workspaceRoot;
    private readonly string _defaultUsername;
    private readonly string _defaultTokenLabel;
    private readonly string _defaultBaseBranch;

    public GitHubProjectsPlugin(string workspaceRoot, IConfiguration configuration)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _defaultUsername = configuration["GitHub:DefaultUsername"] ?? string.Empty;
        _defaultTokenLabel = configuration["GitHub:TokenLabel"] ?? string.Empty;
        _defaultBaseBranch = configuration["GitHub:DefaultBaseBranch"] ?? "main";
    }

    [KernelFunction("build_load_github_project_execution_request")]
    [Description("Build a policy-aware execution request summary for loading a GitHub project by cloning its repository into the workspace.")]
    public string BuildLoadGitHubProjectExecutionRequest(
        [Description("GitHub repository URL, for example 'https://github.com/owner/repo.git'.")] string repositoryUrl,
        [Description("Optional target folder name under the workspace. Leave empty to derive from the repository URL.")] string? targetFolder = null,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
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

        var arguments = $"clone \"{repositoryUrl}\" \"{safeFolder}\"";
        return BuildExecutionRequestSummary("git", arguments, userApproved, _workspaceRoot, "This will load the GitHub project into the local workspace.");
    }

    [KernelFunction("build_list_issues_execution_request")]
    [Description("Build a policy-aware execution request summary for listing GitHub issues with the GitHub CLI inside a loaded repository.")]
    public string BuildListIssuesExecutionRequest(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Issue state filter: open, closed, or all.")] string state = "open",
        [Description("Maximum number of issues to return.")] int limit = 20,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = ResolveRepositoryPath(repositoryFolder);
        if (repoPath.StartsWith("Repository folder", StringComparison.Ordinal))
        {
            return repoPath;
        }

        if (!IsSafeIssueState(state))
        {
            return "Issue state must be one of: open, closed, all.";
        }

        if (limit <= 0 || limit > 200)
        {
            return "Limit must be between 1 and 200.";
        }

        var arguments = $"issue list --state \"{state}\" --limit {limit}";
        return BuildExecutionRequestSummary("gh", arguments, userApproved, repoPath, "Use this to inspect issues before choosing one to process.");
    }

    [KernelFunction("build_view_issue_execution_request")]
    [Description("Build a policy-aware execution request summary for viewing a specific GitHub issue in a loaded repository.")]
    public string BuildViewIssueExecutionRequest(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Issue number to inspect.")] int issueNumber,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = ResolveRepositoryPath(repositoryFolder);
        if (repoPath.StartsWith("Repository folder", StringComparison.Ordinal))
        {
            return repoPath;
        }

        if (issueNumber <= 0)
        {
            return "Issue number must be greater than zero.";
        }

        var arguments = $"issue view {issueNumber}";
        return BuildExecutionRequestSummary("gh", arguments, userApproved, repoPath, "Use this to gather the issue description, comments, and acceptance details.");
    }

    [KernelFunction("build_process_issue_plan")]
    [Description("Build a structured text plan for processing a GitHub issue after the repository has been loaded and the issue details have been reviewed.")]
    public string BuildProcessIssuePlan(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Issue number to process.")] int issueNumber,
        [Description("Short issue title or summary.")] string issueTitle)
    {
        var repoPath = ResolveRepositoryPath(repositoryFolder);
        if (repoPath.StartsWith("Repository folder", StringComparison.Ordinal))
        {
            return repoPath;
        }

        if (issueNumber <= 0)
        {
            return "Issue number must be greater than zero.";
        }

        var safeTitle = string.IsNullOrWhiteSpace(issueTitle) ? "Issue work item" : issueTitle.Trim();

        return string.Join(Environment.NewLine, new[]
        {
            $"Process issue #{issueNumber}: {safeTitle}",
            $"Repository: {repoPath}",
            "Suggested workflow:",
            "1. Inspect the repository structure and relevant source files.",
            "2. Read the issue details and clarify expected behavior.",
            "3. Create a focused implementation plan.",
            "4. Make minimal code changes in the loaded repository.",
            "5. Run safe validation commands if execution is enabled and approved.",
            "6. Summarize changes, risks, and possible follow-up actions.",
            "7. Optionally prepare branch, commit, and pull request steps."
        });
    }

    [KernelFunction("build_issue_comment_execution_request")]
    [Description("Build a policy-aware execution request summary for posting a comment to a GitHub issue with the GitHub CLI.")]
    public string BuildIssueCommentExecutionRequest(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Issue number to comment on.")] int issueNumber,
        [Description("Comment body to post.")] string body,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = ResolveRepositoryPath(repositoryFolder);
        if (repoPath.StartsWith("Repository folder", StringComparison.Ordinal))
        {
            return repoPath;
        }

        if (issueNumber <= 0)
        {
            return "Issue number must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return "Comment body is required.";
        }

        var escapedBody = EscapeForDoubleQuotes(body);
        var arguments = $"issue comment {issueNumber} --body \"{escapedBody}\"";
        return BuildExecutionRequestSummary("gh", arguments, userApproved, repoPath, "Use with care because this would modify remote GitHub state.");
    }

    [KernelFunction("build_issue_assign_execution_request")]
    [Description("Build a policy-aware execution request summary for assigning a GitHub issue to a specified user.")]
    public string BuildIssueAssignExecutionRequest(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Issue number to assign.")] int issueNumber,
        [Description("GitHub username to assign to the issue. Leave empty to use the configured default username.")] string assignee = "",
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = ResolveRepositoryPath(repositoryFolder);
        if (repoPath.StartsWith("Repository folder", StringComparison.Ordinal))
        {
            return repoPath;
        }

        if (issueNumber <= 0)
        {
            return "Issue number must be greater than zero.";
        }

        var effectiveAssignee = string.IsNullOrWhiteSpace(assignee) ? _defaultUsername : assignee.Trim();
        if (!IsSafeGitHubIdentity(effectiveAssignee))
        {
            return "Assignee is missing or contains unsupported characters.";
        }

        var arguments = $"issue edit {issueNumber} --add-assignee \"{effectiveAssignee}\"";
        return BuildExecutionRequestSummary("gh", arguments, userApproved, repoPath, $"Use with care because this would modify remote GitHub state by assigning the issue to '{effectiveAssignee}'.");
    }

    [KernelFunction("build_issue_assign_with_token_plan")]
    [Description("Build a structured plan for assigning a specified user or token identity to process an issue and prepare a pull request.")]
    public string BuildIssueAssignWithTokenPlan(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Issue number to process.")] int issueNumber,
        [Description("GitHub username or external worker token label responsible for the issue. Leave empty to use configured defaults.")] string assigneeOrToken = "",
        [Description("Short issue title or summary.")] string issueTitle = "")
    {
        var repoPath = ResolveRepositoryPath(repositoryFolder);
        if (repoPath.StartsWith("Repository folder", StringComparison.Ordinal))
        {
            return repoPath;
        }

        if (issueNumber <= 0)
        {
            return "Issue number must be greater than zero.";
        }

        var effectiveIdentity = string.IsNullOrWhiteSpace(assigneeOrToken)
            ? (!string.IsNullOrWhiteSpace(_defaultTokenLabel) ? _defaultTokenLabel : _defaultUsername)
            : assigneeOrToken.Trim();

        if (string.IsNullOrWhiteSpace(effectiveIdentity))
        {
            return "Assignee or token is required, and no configured default was found.";
        }

        var safeTitle = string.IsNullOrWhiteSpace(issueTitle) ? "Issue work item" : issueTitle.Trim();
        var safeIdentity = effectiveIdentity;
        var branchName = BuildIssueBranchName(issueNumber, safeTitle);

        return string.Join(Environment.NewLine, new[]
        {
            $"Assign and process issue #{issueNumber}: {safeTitle}",
            $"Repository: {repoPath}",
            $"Assigned processor: {safeIdentity}",
            $"Suggested branch name: {branchName}",
            "Suggested workflow:",
            "1. Assign the GitHub issue to the specified GitHub user if remote assignment is desired.",
            "2. Record the external worker token or identity in a task note or issue comment if it is not a GitHub username.",
            "3. Inspect the issue details, acceptance criteria, and affected files.",
            "4. Create a focused branch for the issue work.",
            "5. Implement the fix with minimal code changes.",
            "6. Run approved validation commands.",
            "7. Prepare commit and pull request content that references the issue.",
            "8. Optionally comment on the issue with implementation status and PR link."
        });
    }

    [KernelFunction("build_create_issue_branch_execution_request")]
    [Description("Build a policy-aware execution request summary for creating and switching to a local git branch for an issue.")]
    public string BuildCreateIssueBranchExecutionRequest(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Issue number to process.")] int issueNumber,
        [Description("Short issue title or summary.")] string issueTitle,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = ResolveRepositoryPath(repositoryFolder);
        if (repoPath.StartsWith("Repository folder", StringComparison.Ordinal))
        {
            return repoPath;
        }

        if (issueNumber <= 0)
        {
            return "Issue number must be greater than zero.";
        }

        var branchName = BuildIssueBranchName(issueNumber, issueTitle);
        var arguments = $"checkout -b \"{branchName}\"";
        return BuildExecutionRequestSummary("git", arguments, userApproved, repoPath, "This prepares a focused local branch for issue work.");
    }

    [KernelFunction("build_prepare_pull_request_plan")]
    [Description("Build a structured pull request preparation summary for a processed GitHub issue.")]
    public string BuildPreparePullRequestPlan(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Issue number addressed by the pull request.")] int issueNumber,
        [Description("GitHub username or token label that processed the issue. Leave empty to use configured defaults.")] string assigneeOrToken = "",
        [Description("Short issue title or summary.")] string issueTitle = "")
    {
        var repoPath = ResolveRepositoryPath(repositoryFolder);
        if (repoPath.StartsWith("Repository folder", StringComparison.Ordinal))
        {
            return repoPath;
        }

        if (issueNumber <= 0)
        {
            return "Issue number must be greater than zero.";
        }

        var effectiveIdentity = string.IsNullOrWhiteSpace(assigneeOrToken)
            ? (!string.IsNullOrWhiteSpace(_defaultTokenLabel) ? _defaultTokenLabel : _defaultUsername)
            : assigneeOrToken.Trim();

        if (string.IsNullOrWhiteSpace(effectiveIdentity))
        {
            return "Assignee or token is required, and no configured default was found.";
        }

        var safeTitle = string.IsNullOrWhiteSpace(issueTitle) ? "Issue work item" : issueTitle.Trim();
        var branchName = BuildIssueBranchName(issueNumber, safeTitle);
        var prTitle = $"Fix #{issueNumber}: {safeTitle}";
        var prBody = $"## Summary\n- Addresses issue #{issueNumber}\n- Implemented by: {effectiveIdentity}\n\n## Validation\n- Describe the validation performed here\n\n## Issue Link\n- Closes #{issueNumber}";

        return string.Join(Environment.NewLine, new[]
        {
            $"Prepare pull request for issue #{issueNumber}: {safeTitle}",
            $"Repository: {repoPath}",
            $"Suggested branch: {branchName}",
            $"Suggested PR title: {prTitle}",
            "Suggested PR body:",
            prBody
        });
    }

    [KernelFunction("build_pr_create_for_issue_execution_request")]
    [Description("Build a policy-aware execution request summary for creating a pull request for a processed issue.")]
    public string BuildPrCreateForIssueExecutionRequest(
        [Description("Repository folder under the workspace.")] string repositoryFolder,
        [Description("Issue number addressed by the pull request.")] int issueNumber,
        [Description("Short issue title or summary.")] string issueTitle = "",
        [Description("GitHub username or token label that processed the issue. Leave empty to use configured defaults.")] string assigneeOrToken = "",
        [Description("Base branch to merge into. Leave empty to use the configured default base branch.")] string baseBranch = "",
        [Description("Whether to open the PR as a draft.")] bool draft = false,
        [Description("Whether the user has explicitly approved command execution.")] bool userApproved = false)
    {
        var repoPath = ResolveRepositoryPath(repositoryFolder);
        if (repoPath.StartsWith("Repository folder", StringComparison.Ordinal))
        {
            return repoPath;
        }

        if (issueNumber <= 0)
        {
            return "Issue number must be greater than zero.";
        }

        var effectiveBaseBranch = string.IsNullOrWhiteSpace(baseBranch) ? _defaultBaseBranch : baseBranch.Trim();
        if (!IsSafeRevision(effectiveBaseBranch))
        {
            return "Base branch is missing or contains unsupported characters.";
        }

        var effectiveIdentity = string.IsNullOrWhiteSpace(assigneeOrToken)
            ? (!string.IsNullOrWhiteSpace(_defaultTokenLabel) ? _defaultTokenLabel : _defaultUsername)
            : assigneeOrToken.Trim();

        if (string.IsNullOrWhiteSpace(effectiveIdentity))
        {
            return "Assignee or token is required, and no configured default was found.";
        }

        var safeTitle = string.IsNullOrWhiteSpace(issueTitle) ? "Issue work item" : issueTitle.Trim();
        var branchName = BuildIssueBranchName(issueNumber, safeTitle);
        var prTitle = EscapeForDoubleQuotes($"Fix #{issueNumber}: {safeTitle}");
        var prBody = EscapeForDoubleQuotes($"## Summary\n- Addresses issue #{issueNumber}\n- Implemented by: {effectiveIdentity}\n\n## Validation\n- Describe the validation performed here\n\n## Issue Link\n- Closes #{issueNumber}");
        var draftArgument = draft ? " --draft" : string.Empty;
        var arguments = $"pr create --base \"{effectiveBaseBranch}\" --head \"{branchName}\" --title \"{prTitle}\" --body \"{prBody}\"{draftArgument}";
        return BuildExecutionRequestSummary("gh", arguments, userApproved, repoPath, $"Use with care because this would create a remote pull request targeting '{effectiveBaseBranch}'.");
    }

    private static string BuildIssueBranchName(int issueNumber, string? issueTitle)
    {
        var title = string.IsNullOrWhiteSpace(issueTitle) ? "issue-work" : issueTitle.Trim().ToLowerInvariant();
        var slug = Regex.Replace(title, "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "issue-work";
        }

        if (slug.Length > 40)
        {
            slug = slug[..40].Trim('-');
        }

        return $"issue/{issueNumber}-{slug}";
    }

    private static bool IsSafeGitHubIdentity(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(value, "^[A-Za-z0-9-]+$");
    }

    private static bool IsSafeRevision(string revision)
    {
        return !string.IsNullOrWhiteSpace(revision)
            && Regex.IsMatch(revision, "^[A-Za-z0-9._/-]+$");
    }

    private string ResolveRepositoryPath(string repositoryFolder)
    {
        var safeFolder = SanitizeFolderName(repositoryFolder);
        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return "Repository folder name is invalid.";
        }

        var repoPath = Path.Combine(_workspaceRoot, safeFolder);
        return repoPath;
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

        owner = SanitizeFolderName(segments[0]);
        repositoryName = segments[1];
        if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repositoryName = repositoryName[..^4];
        }

        repositoryName = SanitizeFolderName(repositoryName);
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repositoryName);
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

    private static bool IsSafeIssueState(string value)
    {
        return Regex.IsMatch(value ?? string.Empty, "^(open|closed|all)$", RegexOptions.IgnoreCase);
    }

    private static string BuildExecutionRequestSummary(string fileName, string arguments, bool userApproved, string workingDirectory, string note)
    {
        var lines = new List<string>
        {
            $"Execution request prepared for: {fileName}",
            $"UserApproved: {userApproved}",
            "This plugin builds a request summary only and does not execute commands.",
            $"Suggested arguments: {arguments}",
            $"Expected working directory: {workingDirectory}",
            note
        };

        if (!userApproved)
        {
            lines.Add("Approval may be required before execution depending on the configured execution policy.");
        }

        lines.Add($"To evaluate this with the execution plugin, call request_command_execution_in_directory with fileName='{fileName}', arguments='<the built arguments>', workingDirectory='<the expected working directory>', and the desired approval flag.");
        return string.Join(Environment.NewLine, lines);
    }
}
