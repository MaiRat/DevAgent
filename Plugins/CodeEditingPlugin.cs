using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.SemanticKernel;

namespace CodingAgent.Plugins;

public class CodeEditingPlugin
{
    private readonly string _workspaceRoot;
    private readonly string _backupRoot;

    public CodeEditingPlugin(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _backupRoot = Path.Combine(_workspaceRoot, ".agent-backups");
        Directory.CreateDirectory(_backupRoot);
    }

    [KernelFunction("replace_text")]
    [Description("Replace exact text in a workspace file. Fails if the target text does not exist.")]
    public string ReplaceText(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("Exact text to find.")] string oldText,
        [Description("Replacement text.")] string newText)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";

        var content = File.ReadAllText(fullPath);
        if (!content.Contains(oldText, StringComparison.Ordinal))
        {
            return "Target text not found. No changes made.";
        }

        CreateBackup(relativePath, content);
        content = content.Replace(oldText, newText, StringComparison.Ordinal);
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        return $"Updated file: {relativePath}";
    }

    [KernelFunction("insert_text_after")]
    [Description("Insert text immediately after an exact marker in a workspace file. Fails if the marker does not exist.")]
    public string InsertTextAfter(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("Exact marker text to search for.")] string marker,
        [Description("Text to insert after the marker.")] string textToInsert)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";

        var content = File.ReadAllText(fullPath);
        var index = content.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return "Marker text not found. No changes made.";
        }

        CreateBackup(relativePath, content);
        var insertIndex = index + marker.Length;
        content = content.Insert(insertIndex, textToInsert);
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        return $"Inserted text into file: {relativePath}";
    }

    [KernelFunction("insert_text_before")]
    [Description("Insert text immediately before an exact marker in a workspace file. Fails if the marker does not exist.")]
    public string InsertTextBefore(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("Exact marker text to search for.")] string marker,
        [Description("Text to insert before the marker.")] string textToInsert)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";

        var content = File.ReadAllText(fullPath);
        var index = content.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return "Marker text not found. No changes made.";
        }

        CreateBackup(relativePath, content);
        content = content.Insert(index, textToInsert);
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        return $"Inserted text into file: {relativePath}";
    }

    [KernelFunction("delete_text")]
    [Description("Delete exact text from a workspace file. Fails if the target text does not exist.")]
    public string DeleteText(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("Exact text to remove.")] string textToDelete)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";

        var content = File.ReadAllText(fullPath);
        if (!content.Contains(textToDelete, StringComparison.Ordinal))
        {
            return "Target text not found. No changes made.";
        }

        CreateBackup(relativePath, content);
        content = content.Replace(textToDelete, string.Empty, StringComparison.Ordinal);
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        return $"Deleted text from file: {relativePath}";
    }

    [KernelFunction("preview_replace_text")]
    [Description("Preview a text replacement without writing the file. Returns a short preview.")]
    public string PreviewReplaceText(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("Exact text to find.")] string oldText,
        [Description("Replacement text.")] string newText)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";

        var content = File.ReadAllText(fullPath);
        if (!content.Contains(oldText, StringComparison.Ordinal))
        {
            return "Target text not found. No preview available.";
        }

        var updated = content.Replace(oldText, newText, StringComparison.Ordinal);
        return updated.Length <= 4000 ? updated : updated[..4000] + "\n...<truncated preview>";
    }

    [KernelFunction("preview_replace_with_diff")]
    [Description("Preview an exact text replacement and return a simple diff-style view without modifying the file.")]
    public string PreviewReplaceWithDiff(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("Exact text to find.")] string oldText,
        [Description("Replacement text.")] string newText)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";

        var content = File.ReadAllText(fullPath);
        if (!content.Contains(oldText, StringComparison.Ordinal))
        {
            return "Target text not found. No diff available.";
        }

        return BuildReplacementDiff(oldText, newText);
    }

    [KernelFunction("replace_text_with_diff")]
    [Description("Replace exact text in a workspace file and return a simple diff-style summary of the applied change.")]
    public string ReplaceTextWithDiff(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("Exact text to find.")] string oldText,
        [Description("Replacement text.")] string newText)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";

        var content = File.ReadAllText(fullPath);
        if (!content.Contains(oldText, StringComparison.Ordinal))
        {
            return "Target text not found. No changes made.";
        }

        CreateBackup(relativePath, content);
        content = content.Replace(oldText, newText, StringComparison.Ordinal);
        File.WriteAllText(fullPath, content, Encoding.UTF8);

        return $"Updated file: {relativePath}\n\n{BuildReplacementDiff(oldText, newText)}";
    }

    [KernelFunction("replace_line")]
    [Description("Replace one exact line in a file with another line and return a simple diff-style summary.")]
    public string ReplaceLine(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("The exact full line to replace.")] string oldLine,
        [Description("The new full line.")] string newLine)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";

        var originalContent = File.ReadAllText(fullPath);
        var lines = File.ReadAllLines(fullPath).ToList();
        var index = lines.FindIndex(line => string.Equals(line, oldLine, StringComparison.Ordinal));
        if (index < 0)
        {
            return "Target line not found. No changes made.";
        }

        CreateBackup(relativePath, originalContent);
        lines[index] = newLine;
        File.WriteAllText(fullPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);

        return $"Updated file: {relativePath}\n\n--- before\n- {oldLine}\n+++ after\n+ {newLine}";
    }

    [KernelFunction("insert_block_after_with_diff")]
    [Description("Insert a block of text after an exact marker and return a simple diff-style summary.")]
    public string InsertBlockAfterWithDiff(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("Exact marker text to search for.")] string marker,
        [Description("Block of text to insert after the marker.")] string block)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath)) return $"File not found: {relativePath}";

        var content = File.ReadAllText(fullPath);
        var index = content.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return "Marker text not found. No changes made.";
        }

        CreateBackup(relativePath, content);
        var insertIndex = index + marker.Length;
        content = content.Insert(insertIndex, block);
        File.WriteAllText(fullPath, content, Encoding.UTF8);

        return $"Updated file: {relativePath}\n\n--- marker\n{marker}\n+++ inserted after marker\n{block}";
    }

    [KernelFunction("list_backups")]
    [Description("List available backups for a workspace file.")]
    public string ListBackups(
        [Description("Relative file path inside the workspace.")] string relativePath)
    {
        var backupDirectory = GetBackupDirectory(relativePath);
        if (!Directory.Exists(backupDirectory))
        {
            return $"No backups found for: {relativePath}";
        }

        var files = Directory.GetFiles(backupDirectory, "*.bak")
            .OrderByDescending(x => x)
            .Select(Path.GetFileName)
            .ToList();

        return files.Count == 0
            ? $"No backups found for: {relativePath}"
            : string.Join(Environment.NewLine, files);
    }

    [KernelFunction("restore_backup")]
    [Description("Restore a file from a specific backup file name previously created by the agent.")]
    public string RestoreBackup(
        [Description("Relative file path inside the workspace.")] string relativePath,
        [Description("Backup file name returned by list_backups.")] string backupFileName)
    {
        var fullPath = ResolveSafePath(relativePath);
        var backupDirectory = GetBackupDirectory(relativePath);
        var backupPath = Path.Combine(backupDirectory, backupFileName);

        if (!File.Exists(backupPath))
        {
            return $"Backup not found: {backupFileName}";
        }

        var currentContent = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
        CreateBackup(relativePath, currentContent);

        var restoredContent = File.ReadAllText(backupPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, restoredContent, Encoding.UTF8);

        return $"Restored file: {relativePath} from backup: {backupFileName}";
    }

    [KernelFunction("undo_last_change")]
    [Description("Undo the last agent-made change to a file by restoring the most recent backup.")]
    public string UndoLastChange(
        [Description("Relative file path inside the workspace.")] string relativePath)
    {
        var backupDirectory = GetBackupDirectory(relativePath);
        if (!Directory.Exists(backupDirectory))
        {
            return $"No backups found for: {relativePath}";
        }

        var latestBackup = Directory.GetFiles(backupDirectory, "*.bak")
            .OrderByDescending(x => x)
            .FirstOrDefault();

        if (latestBackup is null)
        {
            return $"No backups found for: {relativePath}";
        }

        return RestoreBackup(relativePath, Path.GetFileName(latestBackup));
    }

    private void CreateBackup(string relativePath, string content)
    {
        var backupDirectory = GetBackupDirectory(relativePath);
        Directory.CreateDirectory(backupDirectory);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var backupPath = Path.Combine(backupDirectory, $"{timestamp}.bak");
        File.WriteAllText(backupPath, content, Encoding.UTF8);
    }

    private string GetBackupDirectory(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(_backupRoot, hash);
    }

    private static string BuildReplacementDiff(string oldText, string newText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- before");
        foreach (var line in SplitLinesForDiff(oldText))
        {
            sb.AppendLine("- " + line);
        }

        sb.AppendLine("+++ after");
        foreach (var line in SplitLinesForDiff(newText))
        {
            sb.AppendLine("+ " + line);
        }

        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<string> SplitLinesForDiff(string text)
    {
        return text.Replace("\r\n", "\n").Split('\n');
    }

    private string ResolveSafePath(string relativePath)
    {
        relativePath ??= ".";
        var combined = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));
        if (!combined.StartsWith(_workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path escapes the workspace root.");
        return combined;
    }
}
