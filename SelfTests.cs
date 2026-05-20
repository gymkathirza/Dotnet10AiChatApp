using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Dotnet10AiChatApp;

/// <summary>
/// Self-contained test runner. Run with: dotnet run -- --test
/// </summary>
public static class SelfTests
{
    public static int RunAll()
    {
        int passed = 0;
        int failed = 0;
        var results = new List<(string Name, bool Passed, string? Error)>();

        RunTest("Load_NonExistentFile_ReturnsZero", Test_Load_NonExistentFile_ReturnsZero, results);
        RunTest("Save_Then_Load_Roundtrip_PreservesMessages", Test_Save_Then_Load_Roundtrip, results);
        RunTest("Save_ExcludesSystemMessages", Test_Save_ExcludesSystemMessages, results);
        RunTest("Save_EmptyHistory_SavesZeroMessages", Test_Save_EmptyHistory_SavesZeroMessages, results);
        RunTest("Save_HistoryWithOnlySystemMessage_SavesZeroMessages", Test_Save_HistoryWithOnlySystemMessage, results);
        RunTest("Delete_ExistingFile_ReturnsTrueAndRemovesFile", Test_Delete_ExistingFile, results);
        RunTest("Delete_NonExistentFile_ReturnsFalse", Test_Delete_NonExistentFile, results);
        RunTest("Load_IntoNonEmptyHistory_AppendsMessages", Test_Load_IntoNonEmptyHistory, results);
        RunTest("Save_MultipleSaves_OverwritesFile", Test_Save_MultipleSaves_OverwritesFile, results);
        RunTest("Load_ValidJson_ExtraUnknownRoles_AreSkipped", Test_Load_UnknownRolesSkipped, results);
        RunTest("Branch_SaveAndLoad_InSubdirectory", Test_BranchSaveInSubdirectory, results);
        RunTest("Branch_Delete_RemovesFromDisk", Test_BranchDeleteFromDisk, results);
        RunTest("Branch_Delete_NonExistentBranch", Test_BranchDeleteNonExistent, results);
        RunTest("Branch_Rename_RenamesOnDisk", Test_BranchRenameOnDisk, results);
        RunTest("Branch_Rename_OverwriteFails_WhenTargetExists", Test_BranchRenameOverwriteFails, results);

        foreach (var r in results)
        {
            if (r.Passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✓ {r.Name}");
                passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ {r.Name}: {r.Error}");
                failed++;
            }
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.WriteLine($"  {passed} passed, {failed} failed, {results.Count} total");
        return failed;
    }

    private static void RunTest(string name, Action test, List<(string, bool, string?)> results)
    {
        try
        {
            test();
            results.Add((name, true, null));
        }
        catch (Exception ex)
        {
            results.Add((name, false, ex.Message));
        }
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test-branches-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"test-chat-{Guid.NewGuid()}.json");

    // ── Tests ───────────────────────────────────────────────────────────

    static void Test_Load_NonExistentFile_ReturnsZero()
    {
        var dir = TempDir();
        try
        {
            var history = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
            var file = Path.Combine(dir, "nonexistent-file.json");
            Check(!File.Exists(file), "File should not exist before load");
            int count = ChatHistoryStore.Load(history, file);
            Check(count == 0, $"Expected 0, got {count}");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    static void Test_Save_Then_Load_Roundtrip()
    {
        var file = TempFile();
        try
        {
            var history = new ChatHistory();
            history.AddUserMessage("Hello");
            history.AddAssistantMessage("Hi there!");
            history.AddUserMessage("How are you?");
            history.AddAssistantMessage("I'm doing great!");

            int saved = ChatHistoryStore.Save(history, file);
            Check(saved == 4, $"Expected 4 saved, got {saved}");
            Check(File.Exists(file), "File should exist");

            var loaded = new ChatHistory();
            int loadedCount = ChatHistoryStore.Load(loaded, file);
            Check(loadedCount == 4, $"Expected 4 loaded, got {loadedCount}");
            Check(loaded.Count == 4, $"Expected 4 messages, got {loaded.Count}");

            var messages = loaded.ToList();
            Check(messages[0].Role == AuthorRole.User, "msg[0] role");
            Check(messages[0].Content == "Hello", "msg[0] content");
            Check(messages[3].Role == AuthorRole.Assistant, "msg[3] role");
            Check(messages[3].Content == "I'm doing great!", "msg[3] content");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    static void Test_Save_ExcludesSystemMessages()
    {
        var file = TempFile();
        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage("You are a helpful assistant.");
            history.AddUserMessage("Hello");
            history.AddAssistantMessage("Hi!");

            int saved = ChatHistoryStore.Save(history, file);
            Check(saved == 2, $"Expected 2 saved, got {saved}");

            var json = File.ReadAllText(file);
            Check(!json.Contains("\"system\""), "JSON should not contain system role");
            Check(json.Contains("\"user\""), "JSON should contain user role");
            Check(json.Contains("\"assistant\""), "JSON should contain assistant role");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    static void Test_Save_EmptyHistory_SavesZeroMessages()
    {
        var file = TempFile();
        try
        {
            var history = new ChatHistory();
            int saved = ChatHistoryStore.Save(history, file);
            Check(saved == 0, $"Expected 0 saved, got {saved}");
            Check(File.Exists(file), "File should exist even for empty history");

            var loaded = new ChatHistory();
            int loadedCount = ChatHistoryStore.Load(loaded, file);
            Check(loadedCount == 0, $"Expected 0 loaded, got {loadedCount}");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    static void Test_Save_HistoryWithOnlySystemMessage()
    {
        var file = TempFile();
        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage("System prompt only");
            int saved = ChatHistoryStore.Save(history, file);
            Check(saved == 0, $"Expected 0 saved (system-only), got {saved}");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    static void Test_Delete_ExistingFile()
    {
        var file = TempFile();
        File.WriteAllText(file, "{}");
        Check(File.Exists(file), "File should exist before delete");

        bool deleted = ChatHistoryStore.Delete(file);
        Check(deleted, "Delete should return true");
        Check(!File.Exists(file), "File should not exist after delete");
    }

    static void Test_Delete_NonExistentFile()
    {
        var dir = TempDir();
        try
        {
            var file = Path.Combine(dir, "no-such-file.json");
            Check(!File.Exists(file), "File should not exist before delete");
            bool deleted = ChatHistoryStore.Delete(file);
            Check(!deleted, "Delete should return false for nonexistent file");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    static void Test_Load_IntoNonEmptyHistory()
    {
        var file = TempFile();
        try
        {
            var original = new ChatHistory();
            original.AddUserMessage("First user message");
            original.AddAssistantMessage("First assistant response");
            ChatHistoryStore.Save(original, file);

            var history = new ChatHistory();
            history.AddSystemMessage("Existing system prompt");
            history.AddUserMessage("Pre-existing user message");

            int loaded = ChatHistoryStore.Load(history, file);
            Check(loaded == 2, $"Expected 2 loaded, got {loaded}");

            var messages = history.ToList();
            Check(messages.Count == 4, $"Expected 4 total messages, got {messages.Count}");
            Check(messages[0].Role == AuthorRole.System, "msg[0] should be system");
            Check(messages[1].Content == "Pre-existing user message", "msg[1] content");
            Check(messages[2].Content == "First user message", "msg[2] content");
            Check(messages[3].Content == "First assistant response", "msg[3] content");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    static void Test_Save_MultipleSaves_OverwritesFile()
    {
        var file = TempFile();
        try
        {
            var history1 = new ChatHistory();
            history1.AddUserMessage("Session 1 message");
            ChatHistoryStore.Save(history1, file);

            var history2 = new ChatHistory();
            history2.AddUserMessage("A");
            history2.AddAssistantMessage("B");
            ChatHistoryStore.Save(history2, file);

            var loaded = new ChatHistory();
            int count = ChatHistoryStore.Load(loaded, file);
            Check(count == 2, $"Expected 2 (overwritten), got {count}");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    static void Test_Load_UnknownRolesSkipped()
    {
        var file = TempFile();
        try
        {
            var json = """
            [
                {"Role": "user", "Content": "Hello"},
                {"Role": "unknown", "Content": "Should be skipped"},
                {"Role": "assistant", "Content": "Hi back"}
            ]
            """;
            File.WriteAllText(file, json);

            var history = new ChatHistory();
            int count = ChatHistoryStore.Load(history, file);
            Check(count == 2, $"Expected 2 (unknown skipped), got {count}");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    // ── Branching tests ──────────────────────────────────────────────────

    static void Test_BranchSaveInSubdirectory()
    {
        var dir = TempDir();
        try
        {
            string branchDir = Path.Combine(dir, "branches");
            Directory.CreateDirectory(branchDir);
            string branchFile = Path.Combine(branchDir, "feature.json");

            var history = new ChatHistory();
            history.AddUserMessage("Feature discussion");
            history.AddAssistantMessage("Let's plan it out.");

            int saved = ChatHistoryStore.Save(history, branchFile);
            Check(saved == 2, $"Expected 2 saved, got {saved}");
            Check(File.Exists(branchFile), "Branch file should exist");

            var loaded = new ChatHistory();
            int count = ChatHistoryStore.Load(loaded, branchFile);
            Check(count == 2, $"Expected 2 loaded, got {count}");
            Check(loaded.ToList()[0].Content == "Feature discussion", "msg[0] content");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    static void Test_BranchDeleteFromDisk()
    {
        var dir = TempDir();
        try
        {
            string branchDir = Path.Combine(dir, "branches");
            Directory.CreateDirectory(branchDir);
            string branchFile = Path.Combine(branchDir, "test-branch.json");
            File.WriteAllText(branchFile, "[]");
            Check(File.Exists(branchFile), "Branch file should exist before delete");

            bool deleted = ChatHistoryStore.Delete(branchFile);
            Check(deleted, "Delete should return true");
            Check(!File.Exists(branchFile), "Branch file should not exist after delete");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    static void Test_BranchDeleteNonExistent()
    {
        var dir = TempDir();
        try
        {
            string branchFile = Path.Combine(dir, "branches", "no-such-branch.json");
            bool deleted = ChatHistoryStore.Delete(branchFile);
            Check(!deleted, "Delete should return false for nonexistent branch file");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    static void Test_BranchRenameOnDisk()
    {
        var dir = TempDir();
        try
        {
            string branchDir = Path.Combine(dir, "branches");
            Directory.CreateDirectory(branchDir);
            string oldFile = Path.Combine(branchDir, "old-name.json");
            File.WriteAllText(oldFile, $"[{{\"Role\": \"user\", \"Content\": \"Hello\"}}]");

            string newFile = Path.Combine(branchDir, "new-name.json");
            File.Move(oldFile, newFile);

            Check(!File.Exists(oldFile), "Old file should not exist after rename");
            Check(File.Exists(newFile), "New file should exist after rename");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    static void Test_BranchRenameOverwriteFails()
    {
        var dir = TempDir();
        try
        {
            string branchDir = Path.Combine(dir, "branches");
            Directory.CreateDirectory(branchDir);
            string oldFile = Path.Combine(branchDir, "source.json");
            string newFile = Path.Combine(branchDir, "target.json");

            File.WriteAllText(oldFile, "[{\"Role\": \"user\", \"Content\": \"Source\"}]");
            File.WriteAllText(newFile, "[{\"Role\": \"user\", \"Content\": \"Target exists\"}]");

            // File.Move with overwrite:false should throw when target exists
            bool threw = false;
            try { File.Move(oldFile, newFile, overwrite: false); }
            catch (IOException) { threw = true; }
            Check(threw, "Move with overwrite:false should throw IOException when target exists");
            Check(File.Exists(oldFile), "Source file should still exist after failed move");
            Check(File.Exists(newFile), "Target file should still exist after failed move");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
