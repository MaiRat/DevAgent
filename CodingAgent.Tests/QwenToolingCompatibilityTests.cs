using System.ComponentModel;
using CodingAgent;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Xunit;

namespace CodingAgent.Tests;

public sealed class QwenToolingCompatibilityTests
{
    private static readonly (string Name, object Instance)[] SamplePlugins = [("Sample", new SamplePlugin())];

    [Fact]
    public void BuildSystemPrompt_DescribesAvailableTools()
    {
        var prompt = QwenToolingCompatibility.BuildSystemPrompt(SamplePlugins);

        Assert.Contains("Sample.lookup_value", prompt);
        Assert.Contains("query (required)", prompt);
        Assert.Contains("mode (optional)", prompt);
        Assert.Contains("<tool_call>", prompt);
    }

    [Fact]
    public void ParseToolCalls_ParsesXmlToolCalls()
    {
        const string content = """
            <tool_call>
              <Sample.lookup_value>
                <query>status</query>
                <mode>brief</mode>
              </Sample.lookup_value>
            </tool_call>
            """;

        var toolCalls = QwenToolingCompatibility.ParseToolCalls(content, SamplePlugins);

        var toolCall = Assert.Single(toolCalls);
        Assert.Equal("Sample.lookup_value", toolCall.Tool.Name);
        Assert.Equal("status", toolCall.Arguments["query"]);
        Assert.Equal("brief", toolCall.Arguments["mode"]);
    }

    [Fact]
    public void ParseToolCalls_ParsesJsonToolCalls()
    {
        const string content = """
            {
              "tool_calls": [
                {
                  "function": {
                    "name": "Sample.lookup_value",
                    "arguments": "{\"query\":\"status\"}"
                  }
                }
              ]
            }
            """;

        var toolCalls = QwenToolingCompatibility.ParseToolCalls(content, SamplePlugins);

        var toolCall = Assert.Single(toolCalls);
        Assert.Equal("Sample.lookup_value", toolCall.Tool.Name);
        Assert.Equal("status", toolCall.Arguments["query"]);
    }

    [Fact]
    public void IsEnabled_UsesExplicitConfigurationOverride()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:EnableQwenToolCompatibility"] = "true" })
            .Build();

        var enabled = QwenToolingCompatibility.IsEnabled("OpenAI", "gpt-5.4", "gpt-5.4-mini", configuration);

        Assert.True(enabled);
    }

    private sealed class SamplePlugin
    {
        [KernelFunction("lookup_value")]
        [Description("Look up a value for a given query.")]
        public string LookupValue(
            [Description("The query to look up.")] string query,
            [Description("Optional display mode.")] string mode = "full") =>
            $"{query}:{mode}";
    }
}
