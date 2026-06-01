using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class TaskPlanningPlugin
{
    private readonly string _workspaceRoot;
    private readonly string _tasksRoot;

    public TaskPlanningPlugin(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _tasksRoot = Path.Combine(_workspaceRoot, "tasks");
        Directory.CreateDirectory(_tasksRoot);
    }

    [KernelFunction("create_plan")]
    [Description("Create a markdown task plan file under the tasks folder.")]
    public string CreatePlan(
        [Description("Plan name without extension.")] string name,
        [Description("Goal or feature to plan.")] string goal,
        [Description("Optional implementation notes.")] string notes = "")
    {
        var safeName = SanitizeFileName(name);
        var path = Path.Combine(_tasksRoot, safeName + ".md");

        var content = new StringBuilder()
            .AppendLine($"# {safeName}")
            .AppendLine()
            .AppendLine("## Goal")
            .AppendLine(goal)
            .AppendLine()
            .AppendLine("## Checklist")
            .AppendLine("- [ ] Inspect relevant files")
            .AppendLine("- [ ] Define minimal change set")
            .AppendLine("- [ ] Implement changes")
            .AppendLine("- [ ] Validate behavior")
            .AppendLine("- [ ] Summarize results")
            .AppendLine()
            .AppendLine("## Notes")
            .AppendLine(string.IsNullOrWhiteSpace(notes) ? "- None yet" : notes)
            .ToString();

        File.WriteAllText(path, content, Encoding.UTF8);
        return $"Created plan: tasks/{safeName}.md";
    }

    [KernelFunction("list_plans")]
    [Description("List markdown task plans in the tasks folder.")]
    public string ListPlans()
    {
        var files = Directory.GetFiles(_tasksRoot, "*.md")
            .OrderBy(x => x)
            .Select(Path.GetFileName)
            .ToList();

        return files.Count == 0 ? "No task plans found." : string.Join(Environment.NewLine, files);
    }

    [KernelFunction("read_plan")]
    [Description("Read a markdown task plan from the tasks folder by file name or plan name.")]
    public string ReadPlan(
        [Description("Plan file name with or without .md extension.")] string name)
    {
        var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(name));
        var path = Path.Combine(_tasksRoot, safeName + ".md");

        return File.Exists(path)
            ? File.ReadAllText(path)
            : $"Plan not found: {safeName}.md";
    }

    [KernelFunction("append_plan_note")]
    [Description("Append a note to the Notes section of a markdown task plan.")]
    public string AppendPlanNote(
        [Description("Plan file name with or without .md extension.")] string name,
        [Description("Note text to append.")] string note)
    {
        var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(name));
        var path = Path.Combine(_tasksRoot, safeName + ".md");

        if (!File.Exists(path))
        {
            return $"Plan not found: {safeName}.md";
        }

        File.AppendAllText(path, $"{Environment.NewLine}- {note}", Encoding.UTF8);
        return $"Updated plan: tasks/{safeName}.md";
    }

    [KernelFunction("mark_checklist_item")]
    [Description("Mark a checklist item in a task plan as done by exact text match.")]
    public string MarkChecklistItem(
        [Description("Plan file name with or without .md extension.")] string name,
        [Description("Exact unchecked checklist line text, for example: '- [ ] Inspect relevant files'")] string item)
    {
        var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(name));
        var path = Path.Combine(_tasksRoot, safeName + ".md");

        if (!File.Exists(path))
        {
            return $"Plan not found: {safeName}.md";
        }

        var content = File.ReadAllText(path);
        if (!content.Contains(item, StringComparison.Ordinal))
        {
            return "Checklist item not found. No changes made.";
        }

        var updated = content.Replace(item, item.Replace("- [ ]", "- [x]"), StringComparison.Ordinal);
        File.WriteAllText(path, updated, Encoding.UTF8);
        return $"Marked checklist item complete in tasks/{safeName}.md";
    }

    private static string SanitizeFileName(string name)
    {
        var safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch)).Trim();
        return string.IsNullOrWhiteSpace(safeName) ? "task-plan" : safeName;
    }
}
