using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodingAgent.Plugins;
using CodingAgent;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
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
        Assert.Contains("Do not stop at git hygiene advice alone.", prompt);
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

    [Fact]
    public async Task RunTurnAsync_RetriesAfterGitHintAndAppliesWorkspaceChange()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var plugins = new (string Name, object Instance)[]
            {
                ("Workspace", new WorkspacePlugin(workspaceRoot))
            };

            var builder = Kernel.CreateBuilder();
            builder.Plugins.AddFromObject(plugins[0].Instance, plugins[0].Name);
            var kernel = builder.Build();

            var chat = new FakeChatCompletionService(
                """
                Review generated files, secrets, and local tooling artifacts before committing.
                Prefer small, focused commits after validating changes.
                """,
                """
                <tool_call>
                  <Workspace.write_file>
                    <relativePath>notes.txt</relativePath>
                    <content>hello from qwen fallback</content>
                  </Workspace.write_file>
                </tool_call>
                """,
                "Created notes.txt with the requested content.");

            var history = new ChatHistory();
            history.AddUserMessage("Please update the local workspace by creating notes.txt.");

            var response = await QwenToolingCompatibility.RunTurnAsync(chat, history, kernel, plugins);

            Assert.Equal("Created notes.txt with the requested content.", response);
            Assert.Equal("hello from qwen fallback", File.ReadAllText(Path.Combine(workspaceRoot, "notes.txt")));
            Assert.Contains(
                chat.SeenHistories[1].Last(),
                message => message.Role == AuthorRole.User
                    && (message.Content?.Contains("use Workspace or CodeEditor write/edit tools", StringComparison.Ordinal) ?? false));
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
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

    private sealed class FakeChatCompletionService(params string[] responses) : IChatCompletionService
    {
        private readonly Queue<string> _responses = new(responses);

        public List<IReadOnlyList<ChatMessageContent>> SeenHistories { get; } = [];

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            SeenHistories.Add(chatHistory.Select(message => new ChatMessageContent(message.Role, message.Content ?? string.Empty)).ToList());

            var response = _responses.Dequeue();
            IReadOnlyList<ChatMessageContent> messages =
            [
                new ChatMessageContent(AuthorRole.Assistant, response)
            ];
            return Task.FromResult(messages);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
    }
}
