using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class GitHubAuthPlugin
{
    private readonly string _defaultUsername;
    private readonly string _defaultToken;
    private readonly string _defaultTokenLabel;
    private readonly string _defaultBaseBranch;

    public GitHubAuthPlugin(IConfiguration configuration)
    {
        _defaultUsername = configuration["GitHub:DefaultUsername"] ?? string.Empty;
        _defaultToken = configuration["GitHub:Token"] ?? string.Empty;
        _defaultTokenLabel = configuration["GitHub:TokenLabel"] ?? string.Empty;
        _defaultBaseBranch = configuration["GitHub:DefaultBaseBranch"] ?? "main";
    }

    [KernelFunction("describe_github_auth_configuration")]
    [Description("Describe the configured default GitHub user, token availability, token label, and default base branch without exposing secret values.")]
    public string DescribeGitHubAuthConfiguration()
    {
        var username = string.IsNullOrWhiteSpace(_defaultUsername) ? "(not set)" : _defaultUsername;
        var tokenLabel = string.IsNullOrWhiteSpace(_defaultTokenLabel) ? "(not set)" : _defaultTokenLabel;
        var tokenStatus = string.IsNullOrWhiteSpace(_defaultToken) ? "missing" : "present";

        return $"DefaultUsername: {username}; TokenStatus: {tokenStatus}; TokenLabel: {tokenLabel}; DefaultBaseBranch: {_defaultBaseBranch}";
    }

    [KernelFunction("get_default_github_username")]
    [Description("Return the configured default GitHub username, or a not-set message if absent.")]
    public string GetDefaultGitHubUsername()
    {
        return string.IsNullOrWhiteSpace(_defaultUsername) ? "Default GitHub username is not set." : _defaultUsername;
    }

    [KernelFunction("get_default_github_token_status")]
    [Description("Return whether a default GitHub token is configured, without revealing the token.")]
    public string GetDefaultGitHubTokenStatus()
    {
        return string.IsNullOrWhiteSpace(_defaultToken) ? "Default GitHub token is not configured." : "Default GitHub token is configured.";
    }

    [KernelFunction("get_default_github_token_label")]
    [Description("Return the configured default GitHub token label, or a not-set message if absent.")]
    public string GetDefaultGitHubTokenLabel()
    {
        return string.IsNullOrWhiteSpace(_defaultTokenLabel) ? "Default GitHub token label is not set." : _defaultTokenLabel;
    }

    [KernelFunction("get_default_github_base_branch")]
    [Description("Return the configured default GitHub base branch for pull requests.")]
    public string GetDefaultGitHubBaseBranch()
    {
        return _defaultBaseBranch;
    }

    [KernelFunction("build_github_configuration_example")]
    [Description("Print an appsettings.json example for GitHub defaults and token configuration.")]
    public string BuildGitHubConfigurationExample()
    {
        return """
{
  "GitHub": {
    "DefaultUsername": "octocat",
    "Token": "set-via-environment-or-secret-store",
    "TokenLabel": "worker-a17",
    "DefaultBaseBranch": "main"
  }
}
""";
    }

    [KernelFunction("build_github_environment_variable_example")]
    [Description("Print PowerShell environment variable examples for GitHub defaults and token configuration.")]
    public string BuildGitHubEnvironmentVariableExample()
    {
        return """
$env:GitHub__DefaultUsername="octocat"
$env:GitHub__Token="your-github-token"
$env:GitHub__TokenLabel="worker-a17"
$env:GitHub__DefaultBaseBranch="main"
""";
    }
}
