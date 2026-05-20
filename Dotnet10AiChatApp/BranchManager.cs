using Microsoft.SemanticKernel.ChatCompletion;

namespace Dotnet10AiChatApp;

public static class BranchManager
{
    public static bool IsValidBranchName(string name, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Branch name cannot be empty.";
            return false;
        }
        if (name.Contains("..", StringComparison.Ordinal))
        {
            error = "Branch name cannot contain '..' path segments.";
            return false;
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
            name.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
            name.IndexOf(Path.VolumeSeparatorChar) >= 0)
        {
            error = "Branch name contains invalid characters or directory separators.";
            return false;
        }
        if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal))
        {
            error = "Branch name cannot start or end with a dot.";
            return false;
        }
        return true;
    }

    public static string BranchFilePath(string name)
    {
        if (!IsValidBranchName(name, out var error))
        {
            throw new ArgumentException(error, nameof(name));
        }

        string branchesDir = Path.GetFullPath("branches");
        string file = Path.GetFullPath(Path.Combine(branchesDir, $"{name}.json"));
        if (!file.StartsWith(branchesDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Branch name resolves outside the branches directory.", nameof(name));
        }
        return file;
    }

    public static void SaveCurrentBranch(ChatHistory chatHistory, string? activeBranch, string defaultFile)
    {
        string file = activeBranch is not null ? BranchFilePath(activeBranch) : defaultFile;
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            ChatHistoryStore.Save(chatHistory, file);
        }
        catch { /* best effort */ }
    }

    public static void DeleteBranch(string branchName,
        ref ChatHistory chatHistory,
        ref string? activeBranch,
        ref Dictionary<string, ChatHistory> branches)
    {
        if (string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleHelpers.Warn("Cannot delete the main branch.");
            return;
        }

        if (string.Equals(branchName, activeBranch, StringComparison.OrdinalIgnoreCase))
        {
            ConsoleHelpers.Warn($"Cannot delete the active branch '{branchName}'. Switch to another branch first.");
            return;
        }

        bool inDict = branches.Remove(branchName, out _);
        string filePath = BranchFilePath(branchName);
        bool onDisk = File.Exists(filePath);

        if (!inDict && !onDisk)
        {
            ConsoleHelpers.Warn($"Branch '{branchName}' not found.");
            return;
        }

        if (onDisk)
        {
            try { File.Delete(filePath); } catch { /* best effort */ }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  🗑  Deleted branch '{branchName}'.");
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void RenameBranch(string oldName, string newName,
        ref ChatHistory chatHistory,
        ref string? activeBranch,
        ref Dictionary<string, ChatHistory> branches,
        string systemPrompt)
    {
        if (string.Equals(oldName, "main", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleHelpers.Warn("Cannot rename the main branch.");
            return;
        }

        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            ConsoleHelpers.Warn($"'{oldName}' and '{newName}' are the same name.");
            return;
        }

        if (branches.ContainsKey(newName) || File.Exists(BranchFilePath(newName)))
        {
            ConsoleHelpers.Warn($"Branch '{newName}' already exists. Choose a different name.");
            return;
        }

        if (!branches.TryGetValue(oldName, out var history))
        {
            string oldFile = BranchFilePath(oldName);
            if (!File.Exists(oldFile))
            {
                ConsoleHelpers.Warn($"Branch '{oldName}' not found.");
                return;
            }
            history = new ChatHistory();
            history.AddSystemMessage(systemPrompt);
            try { ChatHistoryStore.Load(history, oldFile); }
            catch (Exception ex) { ConsoleHelpers.Warn($"Could not load branch: {ex.Message}"); return; }
        }

        branches.Remove(oldName);
        branches[newName] = history;

        string oldFilePath = BranchFilePath(oldName);
        if (File.Exists(oldFilePath))
        {
            string dir = Path.GetDirectoryName(oldFilePath)!;
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Move(oldFilePath, BranchFilePath(newName), overwrite: false);
        }

        bool wasActive = string.Equals(oldName, activeBranch, StringComparison.OrdinalIgnoreCase);
        if (wasActive)
            activeBranch = newName;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✏  Renamed branch '{oldName}' → '{newName}'.");
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void PrintBranches(Dictionary<string, ChatHistory> branches, string? activeBranch)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  ── Branches ──");
        Console.ResetColor();

        var marker = activeBranch is null ? " ◀ active" : string.Empty;
        Console.WriteLine($"  {'*',-2} (main)   {marker}");

        foreach (var (name, history) in branches)
        {
            int count = history.Count(m => m.Role != AuthorRole.System);
            marker = string.Equals(name, activeBranch, StringComparison.OrdinalIgnoreCase) ? " ◀ active" : string.Empty;
            Console.WriteLine($"  {' ',2} {name,-12} ({count} messages){marker}");
        }

        if (Directory.Exists("branches"))
        {
            foreach (var file in Directory.GetFiles("branches", "*.json"))
            {
                var diskName = Path.GetFileNameWithoutExtension(file);
                if (diskName is not null && !branches.ContainsKey(diskName)
                    && !string.Equals(diskName, activeBranch, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  {' ',2} {diskName,-12} (on disk)");
                }
            }
        }
        Console.WriteLine();
    }
}
