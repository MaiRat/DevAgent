using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CodingAgent;

public static partial class QwenToolingCompatibility
{
    private const int MaxToolIterations = 8;
    private const int MaxLogSummaryLength = 180;
    private static readonly NullabilityInfoContext NullabilityContext = new();

    public static bool IsEnabled(string provider, string? codingModelId, string? toolingModelId, IConfiguration configuration)
    {
        if (TryGetConfiguredCompatibilityOverride(configuration, out var configuredValue))
        {
            return configuredValue;
        }

        return string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildSystemPrompt(IEnumerable<(string Name, object Instance)> plugins)
    {
        var tools = DescribeTools(plugins);
        var builder = new StringBuilder();
        builder.AppendLine("You are running in local model tool compatibility mode.");
        builder.AppendLine("When you need a tool, respond with XML only using this exact shape:");
        builder.AppendLine("<tool_call>");
        builder.AppendLine("  <PluginName.function_name>");
        builder.AppendLine("    <argumentName>value</argumentName>");
        builder.AppendLine("  </PluginName.function_name>");
        builder.AppendLine("</tool_call>");
        builder.AppendLine("Use one <tool_call> block per tool invocation and include every required parameter.");
        builder.AppendLine("If the user asks for local code, documentation, or file changes, inspect the workspace and use write/edit tools to apply those changes before your final answer. Do not stop at git hygiene advice alone.");
        builder.AppendLine("If no tool is needed, answer normally.");
        builder.AppendLine("After a <tool_response> message is returned, either issue another <tool_call> block or provide the final answer.");
        builder.AppendLine();
        builder.AppendLine("Available tools:");

        foreach (var tool in tools)
        {
            builder.AppendLine($"- {tool.Name}: {tool.Description}");

            if (tool.Parameters.Count == 0)
            {
                builder.AppendLine("  Parameters: none");
                continue;
            }

            builder.AppendLine("  Parameters:");
            foreach (var parameter in tool.Parameters)
            {
                var requiredText = parameter.Required ? "required" : "optional";
                builder.AppendLine($"  - {parameter.Name} ({requiredText}): {parameter.Description}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static async Task<string> RunTurnAsync(
        IChatCompletionService chat,
        ChatHistory history,
        Kernel kernel,
        IEnumerable<(string Name, object Instance)> plugins,
        Action<string>? verboseLog = null,
        CancellationToken cancellationToken = default)
    {
        var toolDefinitions = DescribeTools(plugins);
        var toolIndex = BuildToolIndex(toolDefinitions);
        var settings = new OpenAIPromptExecutionSettings();
        var initialUserRequest = history.LastOrDefault(message => message.Role == AuthorRole.User)?.Content ?? string.Empty;
        var expectsWorkspaceChanges = LooksLikeWorkspaceChangeRequest(initialUserRequest);
        var appliedWorkspaceChange = false;
        var promptedForWorkspaceChanges = false;

        Log(verboseLog, $"Starting local model tooling compatibility loop. Advertised tools: {toolDefinitions.Count}.");
        Log(verboseLog, $"Initial user request: {SummarizeForLog(initialUserRequest)}");

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            Log(verboseLog, $"Iteration {iteration + 1}/{MaxToolIterations}: requesting local model response.");
            var result = await chat.GetChatMessageContentAsync(history, executionSettings: settings, kernel: kernel, cancellationToken: cancellationToken);
            var content = (result.Content ?? string.Empty).Trim();

            history.AddMessage(result.Role, content);
            Log(verboseLog, $"Iteration {iteration + 1}/{MaxToolIterations}: model response: {SummarizeForLog(content)}");

            var toolCalls = ParseToolCalls(content, toolIndex);
            Log(verboseLog, toolCalls.Count == 0
                ? $"Iteration {iteration + 1}/{MaxToolIterations}: no tool calls detected."
                : $"Iteration {iteration + 1}/{MaxToolIterations}: parsed {toolCalls.Count} tool call(s): {string.Join(", ", toolCalls.Select(toolCall => toolCall.Tool.Name))}");
            if (toolCalls.Count == 0)
            {
                if (expectsWorkspaceChanges
                    && !appliedWorkspaceChange
                    && !promptedForWorkspaceChanges
                    && ShouldPromptForWorkspaceChanges(content))
                {
                    Log(verboseLog, "Advisory-only response detected for a workspace change request. Prompting the local model to apply the change with tools.");
                    history.AddUserMessage("Your last reply only gave git or workflow advice, but the user asked for a local workspace change. Inspect files if needed and use Workspace or CodeEditor write/edit tools to apply the requested change locally before your final answer.");
                    promptedForWorkspaceChanges = true;
                    continue;
                }

                Log(verboseLog, "Returning final assistant response without additional tool calls.");
                return content;
            }

            foreach (var toolCall in toolCalls)
            {
                var toolResponse = await InvokeToolAsync(kernel, toolCall, cancellationToken, verboseLog);
                appliedWorkspaceChange |= IsWorkspaceMutationTool(toolCall.Tool);
                history.AddUserMessage(BuildToolResponse(toolCall.Tool.Name, toolResponse));
                Log(verboseLog, $"Captured tool response for {toolCall.Tool.Name}: {SummarizeForLog(toolResponse)}");
            }
        }

        const string limitMessage = "Stopped after reaching the local model tool iteration limit.";
        Log(verboseLog, limitMessage);
        history.AddAssistantMessage(limitMessage);
        return limitMessage;
    }

    public static IReadOnlyList<QwenParsedToolCall> ParseToolCalls(
        string content,
        IEnumerable<(string Name, object Instance)> plugins)
    {
        var toolIndex = BuildToolIndex(DescribeTools(plugins));
        return ParseToolCalls(content, toolIndex);
    }

    private static IReadOnlyList<QwenToolDefinition> DescribeTools(IEnumerable<(string Name, object Instance)> plugins)
    {
        var tools = new List<QwenToolDefinition>();

        foreach (var (pluginName, instance) in plugins)
        {
            var methods = instance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => new
                {
                    Method = method,
                    KernelFunction = method.GetCustomAttribute<KernelFunctionAttribute>(),
                    Description = method.GetCustomAttribute<DescriptionAttribute>()
                })
                .Where(entry => entry.KernelFunction is not null);

            foreach (var entry in methods)
            {
                var functionName = entry.KernelFunction!.Name ?? entry.Method.Name;
                var description = entry.Description?.Description ?? "No description provided.";
                var parameters = entry.Method.GetParameters()
                    .Select(parameter => new QwenToolParameterDefinition(
                        parameter.Name ?? "value",
                        parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description provided.",
                        !parameter.HasDefaultValue && !IsNullableParameter(parameter)))
                    .ToList();

                tools.Add(new QwenToolDefinition(pluginName, functionName, description, parameters));
            }
        }

        return tools.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, QwenToolDefinition> BuildToolIndex(IEnumerable<QwenToolDefinition> tools)
    {
        var index = new Dictionary<string, QwenToolDefinition>(StringComparer.OrdinalIgnoreCase);
        var shortNameCounts = tools
            .GroupBy(tool => tool.FunctionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var tool in tools)
        {
            index[tool.Name] = tool;

            if (shortNameCounts[tool.FunctionName] == 1)
            {
                index[tool.FunctionName] = tool;
            }
        }

        return index;
    }

    private static List<QwenParsedToolCall> ParseToolCalls(string content, IReadOnlyDictionary<string, QwenToolDefinition> toolIndex)
    {
        var xmlCalls = ParseXmlToolCalls(content, toolIndex);
        if (xmlCalls.Count > 0)
        {
            return xmlCalls;
        }

        return ParseJsonToolCalls(content, toolIndex);
    }

    private static bool LooksLikeWorkspaceChangeRequest(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return Regex.IsMatch(
            content,
            @"\b(fix|update|modify|change|edit|write|rewrite|create|add|remove|delete|refactor|implement|rename|document|improve|patch)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ShouldPromptForWorkspaceChanges(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var normalized = content.Trim();
        return !normalized.Contains('?', StringComparison.Ordinal)
            && !Regex.IsMatch(
                normalized,
                @"\b(cannot|can't|unable|failed|error|not found|missing|which file|need more information|permission denied)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            && !Regex.IsMatch(
                normalized,
                @"\b(updated|modified|created|wrote|inserted|deleted|restored|changed)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsWorkspaceMutationTool(QwenToolDefinition tool)
    {
        if (string.Equals(tool.PluginName, "CodeEditor", StringComparison.OrdinalIgnoreCase))
        {
            return !tool.FunctionName.StartsWith("preview_", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(tool.FunctionName, "list_backups", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(tool.PluginName, "Workspace", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(tool.FunctionName, "write_file", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool.FunctionName, "append_file", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool.FunctionName, "create_task_note", StringComparison.OrdinalIgnoreCase);
    }

    private static List<QwenParsedToolCall> ParseXmlToolCalls(string content, IReadOnlyDictionary<string, QwenToolDefinition> toolIndex)
    {
        var toolCalls = new List<QwenParsedToolCall>();

        foreach (Match match in ToolCallRegex().Matches(content))
        {
            var xml = match.Value.Trim();
            if (string.IsNullOrWhiteSpace(xml))
            {
                continue;
            }

            try
            {
                var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                var toolCallElement = document.Root;
                var functionElement = toolCallElement?.Elements().FirstOrDefault();
                if (toolCallElement is null || functionElement is null)
                {
                    continue;
                }

                var toolName = functionElement.Name.LocalName;
                if (!toolIndex.TryGetValue(toolName, out var tool))
                {
                    continue;
                }

                var arguments = functionElement.Elements()
                    .ToDictionary(
                        element => element.Name.LocalName,
                        element => element.Value.Trim(),
                        StringComparer.OrdinalIgnoreCase);

                toolCalls.Add(new QwenParsedToolCall(tool, arguments));
            }
            catch
            {
                // Ignore malformed XML fragments so the model can still recover with a normal text reply.
            }
        }

        return toolCalls;
    }

    private static List<QwenParsedToolCall> ParseJsonToolCalls(string content, IReadOnlyDictionary<string, QwenToolDefinition> toolIndex)
    {
        var json = ExtractJsonCandidate(content);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<QwenParsedToolCall>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var toolCalls = ParseJsonToolCalls(document.RootElement, toolIndex);
            return toolCalls;
        }
        catch
        {
            // Ignore malformed JSON payloads and fall back to treating the response as plain assistant text.
            return new List<QwenParsedToolCall>();
        }
    }

    private static List<QwenParsedToolCall> ParseJsonToolCalls(JsonElement element, IReadOnlyDictionary<string, QwenToolDefinition> toolIndex)
    {
        if (TryParseJsonToolCallArray(element, "tool_calls", toolIndex, out var toolCalls)
            || TryParseJsonToolCallArray(element, "function_calls", toolIndex, out toolCalls))
        {
            return toolCalls;
        }

        if (TryParseJsonToolCallProperty(element, "tool_call", toolIndex, out var toolCall)
            || TryParseJsonToolCallProperty(element, "function_call", toolIndex, out toolCall)
            || TryParseJsonToolCallElement(element, toolIndex, out toolCall))
        {
            return [toolCall!];
        }

        return new List<QwenParsedToolCall>();
    }

    private static bool TryParseJsonToolCallArray(
        JsonElement element,
        string propertyName,
        IReadOnlyDictionary<string, QwenToolDefinition> toolIndex,
        out List<QwenParsedToolCall> toolCalls)
    {
        toolCalls = [];
        if (!element.TryGetProperty(propertyName, out var toolCallsElement) || toolCallsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in toolCallsElement.EnumerateArray())
        {
            if (TryParseJsonToolCallElement(item, toolIndex, out var toolCall) && toolCall is not null)
            {
                toolCalls.Add(toolCall);
            }
        }

        return true;
    }

    private static bool TryParseJsonToolCallProperty(
        JsonElement element,
        string propertyName,
        IReadOnlyDictionary<string, QwenToolDefinition> toolIndex,
        out QwenParsedToolCall? toolCall)
    {
        toolCall = null;
        return element.TryGetProperty(propertyName, out var toolCallElement)
            && TryParseJsonToolCallElement(toolCallElement, toolIndex, out toolCall);
    }

    private static bool TryParseJsonToolCallElement(
        JsonElement element,
        IReadOnlyDictionary<string, QwenToolDefinition> toolIndex,
        out QwenParsedToolCall? toolCall)
    {
        toolCall = null;

        JsonElement functionElement;
        if (element.TryGetProperty("function", out var nestedFunctionElement))
        {
            functionElement = nestedFunctionElement;
        }
        else
        {
            functionElement = element;
        }

        if (!functionElement.TryGetProperty("name", out var nameElement))
        {
            return false;
        }

        var toolName = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(toolName) || !toolIndex.TryGetValue(toolName, out var tool))
        {
            return false;
        }

        toolCall = new QwenParsedToolCall(tool, ParseJsonArguments(functionElement));
        return true;
    }

    private static Dictionary<string, string> ParseJsonArguments(JsonElement functionElement)
    {
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!functionElement.TryGetProperty("arguments", out var argumentsElement))
        {
            return arguments;
        }

        if (argumentsElement.ValueKind == JsonValueKind.String)
        {
            var rawArguments = argumentsElement.GetString();
            if (!string.IsNullOrWhiteSpace(rawArguments))
            {
                using var argsDocument = JsonDocument.Parse(rawArguments);
                foreach (var property in argsDocument.RootElement.EnumerateObject())
                {
                    arguments[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }
            }
        }
        else if (argumentsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in argumentsElement.EnumerateObject())
            {
                arguments[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }
        }

        return arguments;
    }

    private static string? ExtractJsonCandidate(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return content[start..(end + 1)];
    }

    private static async Task<string> InvokeToolAsync(Kernel kernel, QwenParsedToolCall toolCall, CancellationToken cancellationToken, Action<string>? verboseLog)
    {
        try
        {
            Log(verboseLog, $"Invoking tool {toolCall.Tool.Name} with arguments: {FormatArgumentsForLog(toolCall.Arguments)}");
            var arguments = new KernelArguments();
            foreach (var argument in toolCall.Arguments)
            {
                arguments[argument.Key] = argument.Value;
            }

            var result = await kernel.InvokeAsync(
                pluginName: toolCall.Tool.PluginName,
                functionName: toolCall.Tool.FunctionName,
                arguments: arguments,
                cancellationToken: cancellationToken);

            var resultText = result.GetValue<object>()?.ToString() ?? result.ToString() ?? string.Empty;
            Log(verboseLog, $"Tool completed successfully: {toolCall.Tool.Name}");
            return resultText;
        }
        catch (Exception ex)
        {
            Log(verboseLog, $"Tool execution failed for {toolCall.Tool.Name}: {ex}");
            return $"Tool execution failed for {toolCall.Tool.Name}: {ex.Message}";
        }
    }

    private static string BuildToolResponse(string toolName, string toolResponse)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Tool result:");
        builder.AppendLine($"<tool_response name=\"{toolName}\">");
        builder.AppendLine(toolResponse);
        builder.AppendLine("</tool_response>");
        builder.AppendLine("Use this result to continue. Either emit another <tool_call> block or answer the user directly.");
        return builder.ToString().TrimEnd();
    }

    private static bool IsNullableParameter(ParameterInfo parameter)
    {
        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return true;
        }

        if (parameter.ParameterType.IsValueType)
        {
            return false;
        }

        return NullabilityContext.Create(parameter).WriteState == NullabilityState.Nullable;
    }

    [GeneratedRegex(@"<tool_call>[\s\S]*?</tool_call>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ToolCallRegex();

    private static void Log(Action<string>? verboseLog, string message)
    {
        verboseLog?.Invoke(message);
    }

    private static string FormatArgumentsForLog(IReadOnlyDictionary<string, string> arguments)
    {
        if (arguments.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", arguments.Select(argument => $"{argument.Key}={SummarizeForLog(argument.Value)}"));
    }

    private static string SummarizeForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        var normalized = LogWhitespaceRegex().Replace(value, " ").Trim();
        return normalized.Length <= MaxLogSummaryLength
            ? normalized
            : $"{normalized[..MaxLogSummaryLength]}...";
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex LogWhitespaceRegex();

    private static bool TryGetConfiguredCompatibilityOverride(IConfiguration configuration, out bool configuredValue)
    {
        if (bool.TryParse(configuration["AI:EnableLocalToolCompatibility"], out configuredValue))
        {
            return true;
        }

        return bool.TryParse(configuration["AI:EnableQwenToolCompatibility"], out configuredValue);
    }
}

public sealed record QwenToolDefinition(
    string PluginName,
    string FunctionName,
    string Description,
    IReadOnlyList<QwenToolParameterDefinition> Parameters)
{
    public string Name => $"{PluginName}.{FunctionName}";
}

public sealed record QwenToolParameterDefinition(string Name, string Description, bool Required);

public sealed record QwenParsedToolCall(QwenToolDefinition Tool, IReadOnlyDictionary<string, string> Arguments);
