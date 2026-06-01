# Coding Agent

A bootstrap C# coding agent workspace using Semantic Kernel with either OpenAI or a local Ollama server, including a local model tooling compatibility path.

## Contents
- `CodingAgent.csproj` - .NET project file
- `Program.cs` - app entry point and kernel bootstrap
- `Plugins/WorkspacePlugin.cs` - workspace and task management plugin
- `Plugins/CodeEditingPlugin.cs` - code editing, diff, backup, and undo plugin
- `Plugins/TaskPlanningPlugin.cs` - task planning and checklist plugin
- `Plugins/ShellCommandPlugin.cs` - shell command policy and command scaffold helpers
- `Plugins/ProjectAnalysisPlugin.cs` - solution, project, file, and directory analysis helpers
- `Plugins/ExecutionPlugin.cs` - execution policy and approval scaffold
- `Plugins/GitAnalysisPlugin.cs` - Git repository and hygiene analysis helpers
- `Plugins/GitCheckoutPlugin.cs` - GitHub clone, checkout, shallow clone, and archive download command helpers
- `appsettings.json` - OpenAI, workspace, and execution configuration
- `agent.md` - agent operating instructions
- `run.ps1` - simple helper script
- `tasks/` - task notes and work items

## Prerequisites
- .NET 8 SDK
- OpenAI API key for OpenAI mode, or a local Ollama server for Ollama mode

## Configuration
Set your API key in one of these ways:

To use a local Ollama server instead of OpenAI, set `AI:Provider` to `Ollama` and configure the `Ollama:Endpoint` and model ids. In Ollama mode, no OpenAI API key is required.

For local Ollama models, the agent automatically enables the local tooling compatibility fallback so tool use continues to work even when the selected model has limited native tool-calling support. You can also force it on or off with `AI:EnableLocalToolCompatibility` (the older `AI:EnableQwenToolCompatibility` key is still accepted for compatibility).

### appsettings.json
```json
{
  "AI": {
    "Provider": "OpenAI",
    "EnableLocalToolCompatibility": false
  },
  "OpenAI": {
    "ModelId": "gpt-4o-mini",
    "ToolingModelId": "gpt-4o-mini",
    "CodingModelId": "gpt-4.1-mini",
    "ApiKey": "your-api-key"
  },
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "ModelId": "llama3.1",
    "ToolingModelId": "llama3.1",
    "CodingModelId": "llama3.1"
  },
  "Agent": {
    "WorkspaceRoot": "c:\\coding-agent",
    "SystemPrompt": "You are a careful coding agent. Inspect files before editing, prefer minimal changes, use available tools when needed, and summarize actions clearly."
  },
  "Execution": {
    "Enabled": false,
    "RequireApproval": true,
    "TimeoutSeconds": 60,
    "AllowedCommands": [ "dotnet", "git", "gh" ]
  }
}
```

Example GitHub defaults:
```json
{
  "GitHub": {
    "DefaultUsername": "octocat",
    "Token": "set-via-environment-or-secret-store",
    "TokenLabel": "worker-a17",
    "DefaultBaseBranch": "main"
  }
}
```

### Environment variable (PowerShell)
```powershell
$env:AI__Provider="OpenAI"
$env:AI__EnableLocalToolCompatibility="false"
$env:OpenAI__ApiKey="your-api-key"
$env:OpenAI__ModelId="gpt-4o-mini"
$env:OpenAI__ToolingModelId="gpt-4o-mini"
$env:OpenAI__CodingModelId="gpt-4.1-mini"
$env:Ollama__Endpoint="http://localhost:11434"
$env:Ollama__ModelId="llama3.1"
$env:Ollama__ToolingModelId="llama3.1"
$env:Ollama__CodingModelId="llama3.1"
$env:Agent__WorkspaceRoot="c:\coding-agent"
$env:Agent__SystemPrompt="You are a careful coding agent."
$env:Execution__Enabled="false"
$env:Execution__RequireApproval="true"
$env:Execution__TimeoutSeconds="60"
$env:GitHub__DefaultUsername="octocat"
$env:GitHub__Token="your-github-token"
$env:GitHub__TokenLabel="worker-a17"
$env:GitHub__DefaultBaseBranch="main"
```


## Run
```powershell
cd c:\coding-agent
dotnet restore
dotnet run
```

Then type prompts interactively.

## Automatic Function Calling
The agent keeps chat history and enables automatic kernel function invocation for compatible OpenAI tool-calling models.

When using Ollama, the agent adds an explicit tool manifest and uses a local-model fallback loop that:
- advertises each plugin function as `PluginName.function_name`
- accepts XML tool calls such as `<tool_call><Workspace.list_files><relativePath>.</relativePath></Workspace.list_files></tool_call>`
- also accepts OpenAI-style JSON `tool_calls` payloads when the local server emits them
- returns tool outputs in a `<tool_response>` block so the model can continue tool use or produce a final answer
- writes verbose local-model logs for setup, tool calls, tool outputs, and tool failures

### Extending Local Model Tooling
Add new tool functions by decorating public plugin methods with `[KernelFunction("function_name")]` and `[Description("...")]`.
Parameter descriptions are taken from `[Description]` attributes on method arguments and are included in the compatibility manifest automatically.
Use unique function names when possible; if two plugins share the same function name, the local model should call the fully qualified `PluginName.function_name`.

Example compatible tool call:
```xml
<tool_call>
  <Workspace.list_files>
    <relativePath>.</relativePath>
  </Workspace.list_files>
</tool_call>
```

Example compatible tool response injected by the agent:
```xml
<tool_response name="Workspace.list_files">
[DIR]  Plugins
[FILE] Program.cs
</tool_response>
```

## AI Configuration Notes
- The `AIConfiguration` plugin exposes safe runtime inspection helpers for the active AI provider and model configuration.
- It can report the active provider, coding model, tooling model, OpenAI API key presence, and configured Ollama endpoint.
- It does not expose the raw OpenAI API key.

## Health Check Notes
- The `HealthCheck` plugin can generate a compact startup report for AI provider configuration, execution policy, GitHub defaults, and workspace availability.
- It is intended as a quick environment sanity check before asking the agent to perform larger tasks.

## Token Optimization
- Configure a cheaper tooling model for workspace inspection, planning, file reads, and repository analysis.
- Configure a separate coding model for code generation, refactoring, and final answers.
- `ToolingModelId` and `CodingModelId` fall back to `ModelId` if not set.
- The current bootstrap uses the coding model for the active chat session and records tooling-model guidance in the system prompt for future routing-aware evolution.

## Plugins
### WorkspacePlugin
- `summarize_workspace`
- `suggest_next_step`
- `list_files`
- `read_file`
- `write_file`
- `append_file`
- `create_task_note`

### CodeEditingPlugin
Basic editing:
- `replace_text`
- `insert_text_after`
- `insert_text_before`
- `delete_text`
- `preview_replace_text`

Safer diff-style editing:
- `preview_replace_with_diff`
- `replace_text_with_diff`
- `replace_line`
- `insert_block_after_with_diff`

Backup and undo support:
- `list_backups`
- `restore_backup`
- `undo_last_change`

### TaskPlanningPlugin
- `create_plan`
- `list_plans`
- `read_plan`
- `append_plan_note`
- `mark_checklist_item`

### ShellCommandPlugin
- `describe_shell_command_policy`
- `build_command_for_dotnet_restore`
- `build_command_for_dotnet_build`
- `build_command_for_dotnet_run`
- `build_command_for_tests`
- `build_command_for_git_clone`
- `build_command_for_git_checkout`
- `print_guarded_shell_command_snippet`

### ProjectAnalysisPlugin
- `find_solution_and_project_files`
- `summarize_project_file`
- `list_csharp_source_files`
- `summarize_directory_tree`
- `find_files_by_name`

### ExecutionPlugin
- `describe_execution_policy`
- `request_command_execution`
- `request_command_execution_in_directory`
- `build_execution_configuration_example`
- `print_execution_plugin_snippet`

### GitAnalysisPlugin
- `detect_git_repository`
- `find_gitignore_files`
- `summarize_gitignore`
- `find_git_related_files`
- `suggest_git_hygiene_steps`

### GitCheckoutPlugin
- `derive_folder_name_from_repo_url`
- `build_git_clone_arguments`
- `build_git_checkout_arguments`
- `build_git_pull_arguments`
- `build_git_fetch_arguments`
- `build_gh_pr_create_arguments`
- `build_git_shallow_clone_arguments`
- `get_expected_repo_path`
- `build_clone_execution_request`
- `build_checkout_execution_request`
- `build_pull_execution_request`
- `build_fetch_execution_request`
- `build_pr_create_execution_request`
- `build_clone_and_checkout_command`
- `build_shallow_clone_command`
- `build_github_archive_download_command`
- `build_clone_and_checkout_execution_request`
- `build_shallow_clone_execution_request`

### GitHubProjectsPlugin
- `build_load_github_project_execution_request`
- `build_list_issues_execution_request`
- `build_view_issue_execution_request`
- `build_process_issue_plan`
- `build_issue_comment_execution_request`
- `build_issue_assign_execution_request`
- `build_issue_assign_with_token_plan`
- `build_create_issue_branch_execution_request`
- `build_prepare_pull_request_plan`
- `build_pr_create_for_issue_execution_request`

### GitHubAuthPlugin
- `describe_github_auth_configuration`
- `get_default_github_username`
- `get_default_github_token_status`
- `get_default_github_token_label`
- `get_default_github_base_branch`
- `build_github_configuration_example`
- `build_github_environment_variable_example`

### AIConfigurationPlugin
- `describe_ai_configuration`
- `get_active_ai_provider`
- `get_active_coding_model`
- `get_active_tooling_model`
- `get_openai_api_key_status`
- `get_ollama_endpoint`

### HealthCheckPlugin
- `run_startup_health_check`

## Execution Notes
- The execution plugin is a scaffold only and does not run commands.
- It models policy decisions such as enablement, approval, timeout, allowlisted executables, and workspace-scoped working directories.
- This is safer than enabling arbitrary command execution by default.
- If you later implement live execution, keep allowlists, workspace restrictions, timeout handling, output capture, and approval requirements.

## Shell Command Notes
- The shell plugin is also a scaffold and does not execute commands.
- It provides suggested commands and a C# snippet for implementing guarded command execution.

## GitHub Checkout Notes
- GitHub checkout support is provided as command builders for `git clone`, `git checkout`, `git pull`, `git fetch`, shallow clone, and archive download flows.
- Repository URLs are limited to `https://github.com/...` format.
- Target folder names are sanitized to stay under the workspace.
- Checkout revisions are restricted to a conservative character set.
- The dedicated `GitCheckout` plugin can derive default folder names from repository URLs.
- It can also build git argument strings that fit `request_command_execution(fileName, arguments, userApproved)`.
- It can return the expected repository path under the workspace after clone.
- It can also build separate policy-aware execution request summaries for clone, checkout, pull, and fetch flows.
- It can also build policy-aware execution request summaries for clone and shallow clone flows.
- Actual execution still depends on the execution policy configuration and user approval flow.

## GitHub Project and Issue Notes
- The `GitHubProjects` plugin adds helpers for loading GitHub repositories into the workspace and planning issue processing work.
- It can build policy-aware execution request summaries for cloning a project, listing issues, viewing issue details, and commenting on issues with the GitHub CLI.
- It can also produce a structured workflow plan for processing a selected issue inside a loaded repository.
- These helpers are scaffolds only and do not execute commands by themselves.
- Remote-changing actions such as issue comments should still require approval and an enabled execution policy.
- It also supports assigning a GitHub issue to a specified user, capturing an external worker/token identity in a planning flow, creating an issue branch, and preparing pull request content.
- Pull request preparation helpers generate issue-linked branch, title, and body suggestions that can be used with the GitHub CLI.
- Assignment and pull request creation are remote-changing actions and should require approval.

## GitHub Auth and Defaults Notes
- The `GitHubAuth` plugin exposes safe configuration helpers for default GitHub username, token presence, token label, and default pull request base branch.
- It never returns the raw token value.
- Prefer storing GitHub tokens in environment variables or an external secret store rather than directly in repository files.
- The token label is intended for identifying the worker or automation identity without exposing the secret.
- The `GitHubProjects` plugin now falls back to configured defaults when assignee, token label, or PR base branch are omitted.
- Fallback order for processor identity is: explicit value, configured token label, then configured default username.
- Fallback order for PR base branch is: explicit value, then configured default base branch.

## Backup Behavior
- Before a write-style edit is applied, the current file content is saved under `.agent-backups/`.
- Backups are organized by a hash of the relative file path.
- `.agent-backups/` is ignored in `.gitignore`.
- You can list backups for a file and restore a specific one.
- `undo_last_change` restores the most recent backup for a file.

## Notes
- Plugin code is split into separate `.cs` files for maintainability.
- File operations are restricted to the configured workspace root.
- Relative paths that try to escape the workspace are blocked.
- Code editing functions are text-based and work best with exact matches.
- Diff-style functions return a simple before/after summary to make edits easier to inspect.
- Automatic function calling depends on the selected model supporting tool use.
