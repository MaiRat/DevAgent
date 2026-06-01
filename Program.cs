using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using CodingAgent.Plugins;

#pragma warning disable SKEXP0010

internal class Program
{
    private static async Task Main(string[] args)
    {
        var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build();

        var provider = configuration["AI:Provider"] ?? "Ollama";
        var toolingModelId = configuration["AI:ToolingModelId"]
            ?? configuration[provider + ":ToolingModelId"]
            ?? configuration[provider + ":ModelId"]
            ?? (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) ? "qwen3:14b" : "gpt-5.4-mini");
        var codingModelId = configuration["AI:CodingModelId"]
            ?? configuration[provider + ":CodingModelId"]
            ?? configuration[provider + ":ModelId"]
            ?? (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase) ? "qwen3:14b" : "gpt-5.4");
        var openAIApiKey = "";
        var ollamaEndpoint = configuration["Ollama:Endpoint"] ?? "http://localhost:11434/v1";
        var workspaceRoot = configuration["Agent:WorkspaceRoot"] ?? "c:\\coding-agent";
        var systemPrompt = configuration["Agent:SystemPrompt"] ?? "You are a careful coding agent. Inspect files before editing, prefer minimal changes, use available tools when needed, and summarize actions clearly.";

        var isOpenAI = string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase);
        var isOllama = string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase);

        if (!isOpenAI && !isOllama)
        {
            Console.WriteLine($"Unsupported AI provider: {provider}");
            Console.WriteLine("Supported providers are: OpenAI, Ollama.");
            return;
        }

        Uri? validatedOllamaUri = null;
        if (isOllama && !Uri.TryCreate(ollamaEndpoint, UriKind.Absolute, out validatedOllamaUri))
        {
            Console.WriteLine($"Invalid Ollama endpoint: {ollamaEndpoint}");
            Console.WriteLine("Set Ollama:Endpoint to a valid absolute URL, for example http://localhost:11434");
            return;
        }

        var builder = Kernel.CreateBuilder();

        if (isOllama)
        {
            var endpoint = validatedOllamaUri!;
            var httpClient = new HttpClient { BaseAddress = endpoint };
            builder.AddOpenAIChatCompletion(modelId: codingModelId, apiKey: "ollama", endpoint: endpoint, httpClient: httpClient);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(openAIApiKey))
            {
                Console.WriteLine("Missing OpenAI API key.");
                Console.WriteLine("Set OpenAI:ApiKey in appsettings.json or environment variables, or set AI:Provider to Ollama.");
                return;
            }

            builder.AddOpenAIChatCompletion(modelId: codingModelId, apiKey: openAIApiKey);
        }
        builder.Plugins.AddFromObject(new WorkspacePlugin(workspaceRoot), "Workspace");
        builder.Plugins.AddFromObject(new CodeEditingPlugin(workspaceRoot), "CodeEditor");
        builder.Plugins.AddFromObject(new TaskPlanningPlugin(workspaceRoot), "TaskPlanner");
        builder.Plugins.AddFromObject(new ShellCommandPlugin(workspaceRoot), "Shell");
        builder.Plugins.AddFromObject(new ProjectAnalysisPlugin(workspaceRoot), "ProjectAnalysis");
        builder.Plugins.AddFromObject(new ExecutionPlugin(workspaceRoot, configuration), "Execution");
        builder.Plugins.AddFromObject(new GitAnalysisPlugin(workspaceRoot), "GitAnalysis");
        builder.Plugins.AddFromObject(new GitCheckoutPlugin(workspaceRoot), "GitCheckout");
        builder.Plugins.AddFromObject(new GitHubProjectsPlugin(workspaceRoot, configuration), "GitHubProjects");
        builder.Plugins.AddFromObject(new GitHubAuthPlugin(configuration), "GitHubAuth");
        builder.Plugins.AddFromObject(new AIConfigurationPlugin(configuration), "AIConfiguration");
        builder.Plugins.AddFromObject(new HealthCheckPlugin(workspaceRoot, configuration), "HealthCheck");

        var kernel = builder.Build();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        history.AddSystemMessage($"Model routing guidance: prefer '{toolingModelId}' for tool-heavy workspace inspection, file listing, planning, and repo analysis; prefer '{codingModelId}' for code synthesis, refactoring, and final implementation responses. Keep tool use concise to reduce token usage. Do not output tool calls as JSON text. Only call tools via the tool_calls mechanism. For simple questions, answer directly.");

        Console.WriteLine($"Coding Agent with {provider} is ready.");
        Console.WriteLine($"Provider: {provider}");
        if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Provider validation: OpenAI configuration selected.");
        }
        else
        {
            Console.WriteLine("Provider validation: Ollama configuration selected.");
        }
        Console.WriteLine($"Coding model: {codingModelId}");
        Console.WriteLine($"Tooling model suggestion: {toolingModelId}");
        if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Ollama endpoint: {ollamaEndpoint}");
        }
        Console.WriteLine($"Workspace: {workspaceRoot}");
        Console.WriteLine("Loaded plugins: Workspace, CodeEditor, TaskPlanner, Shell, ProjectAnalysis, Execution, GitAnalysis, GitCheckout, GitHubProjects, GitHubAuth, AIConfiguration, HealthCheck");
        Console.WriteLine("Automatic function calling: enabled");
        Console.WriteLine("Type a prompt, or press Enter on an empty line to exit.\n");

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                break;
            }

            history.AddUserMessage(input);
            var result = await chat.GetChatMessageContentAsync(history, executionSettings: settings, kernel: kernel);
            history.AddMessage(result.Role, result.Content ?? string.Empty);

            Console.WriteLine();
            Console.WriteLine(result.Content);
            Console.WriteLine();
        }
    }
}