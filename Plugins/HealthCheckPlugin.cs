using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class HealthCheckPlugin
{
    private readonly string _workspaceRoot;
    private readonly IConfiguration _configuration;

    public HealthCheckPlugin(string workspaceRoot, IConfiguration configuration)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _configuration = configuration;
    }

    [KernelFunction("run_startup_health_check")]
    [Description("Return a compact startup health report for AI provider configuration, execution policy, GitHub defaults, and workspace status.")]
    public string RunStartupHealthCheck()
    {
        var lines = new List<string>();

        var provider = _configuration["AI:Provider"] ?? "OpenAI";
        var validProvider = string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase);
        lines.Add($"AI Provider: {(validProvider ? "OK" : "WARN")} - {provider}");

        if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var apiKeyPresent = !string.IsNullOrWhiteSpace(_configuration["OpenAI:ApiKey"]);
            lines.Add($"OpenAI API Key: {(apiKeyPresent ? "OK" : "WARN")} - {(apiKeyPresent ? "configured" : "missing")}");
        }
        else if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = _configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
            var endpointValid = Uri.TryCreate(endpoint, UriKind.Absolute, out _);
            lines.Add($"Ollama Endpoint: {(endpointValid ? "OK" : "WARN")} - {endpoint}");
        }

        var executionEnabled = bool.TryParse(_configuration["Execution:Enabled"], out var enabled) && enabled;
        var requireApproval = !bool.TryParse(_configuration["Execution:RequireApproval"], out var approval) || approval;
        lines.Add($"Execution Policy: OK - Enabled={executionEnabled}, RequireApproval={requireApproval}");

        var defaultUsername = _configuration["GitHub:DefaultUsername"];
        var tokenLabel = _configuration["GitHub:TokenLabel"];
        var defaultBaseBranch = _configuration["GitHub:DefaultBaseBranch"] ?? "main";
        lines.Add($"GitHub Default Username: {(string.IsNullOrWhiteSpace(defaultUsername) ? "WARN - not set" : "OK - configured")}");
        lines.Add($"GitHub Token Label: {(string.IsNullOrWhiteSpace(tokenLabel) ? "WARN - not set" : "OK - configured")}");
        lines.Add($"GitHub Base Branch: OK - {defaultBaseBranch}");

        var workspaceExists = Directory.Exists(_workspaceRoot);
        lines.Add($"Workspace Root: {(workspaceExists ? "OK" : "WARN")} - {_workspaceRoot}");

        return string.Join(Environment.NewLine, lines);
    }
}
