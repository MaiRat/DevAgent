using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class AIConfigurationPlugin
{
    private readonly string _provider;
    private readonly string _toolingModelId;
    private readonly string _codingModelId;
    private readonly string _openAIApiKey;
    private readonly string _ollamaEndpoint;

    public AIConfigurationPlugin(IConfiguration configuration)
    {
        _provider = configuration["AI:Provider"] ?? "OpenAI";
        _toolingModelId = configuration["AI:ToolingModelId"]
            ?? configuration[_provider + ":ToolingModelId"]
            ?? configuration[_provider + ":ModelId"]
            ?? (string.Equals(_provider, "Ollama", StringComparison.OrdinalIgnoreCase) ? "llama3.1" : "gpt-5.4-mini");
        _codingModelId = configuration["AI:CodingModelId"]
            ?? configuration[_provider + ":CodingModelId"]
            ?? configuration[_provider + ":ModelId"]
            ?? (string.Equals(_provider, "Ollama", StringComparison.OrdinalIgnoreCase) ? "llama3.1" : "gpt-5.4");
        _openAIApiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
        _ollamaEndpoint = configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
    }

    [KernelFunction("describe_ai_configuration")]
    [Description("Describe the active AI provider, coding model, tooling model, and safe provider-specific configuration status.")]
    public string DescribeAIConfiguration()
    {
        var providerSummary = string.Equals(_provider, "Ollama", StringComparison.OrdinalIgnoreCase)
            ? $"Provider: {_provider}; CodingModel: {_codingModelId}; ToolingModel: {_toolingModelId}; OllamaEndpoint: {_ollamaEndpoint}"
            : $"Provider: {_provider}; CodingModel: {_codingModelId}; ToolingModel: {_toolingModelId}; OpenAIApiKeyConfigured: {!string.IsNullOrWhiteSpace(_openAIApiKey)}";

        return providerSummary;
    }

    [KernelFunction("get_active_ai_provider")]
    [Description("Return the configured active AI provider.")]
    public string GetActiveAIProvider()
    {
        return _provider;
    }

    [KernelFunction("get_active_coding_model")]
    [Description("Return the configured active coding model id.")]
    public string GetActiveCodingModel()
    {
        return _codingModelId;
    }

    [KernelFunction("get_active_tooling_model")]
    [Description("Return the configured active tooling model id.")]
    public string GetActiveToolingModel()
    {
        return _toolingModelId;
    }

    [KernelFunction("get_openai_api_key_status")]
    [Description("Return whether an OpenAI API key is configured, without revealing the secret.")]
    public string GetOpenAIApiKeyStatus()
    {
        return string.IsNullOrWhiteSpace(_openAIApiKey)
            ? "OpenAI API key is not configured."
            : "OpenAI API key is configured.";
    }

    [KernelFunction("get_ollama_endpoint")]
    [Description("Return the configured Ollama endpoint.")]
    public string GetOllamaEndpoint()
    {
        return _ollamaEndpoint;
    }
}
