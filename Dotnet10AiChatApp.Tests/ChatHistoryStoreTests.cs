using System;
using System.IO;
using System.Linq;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace Dotnet10AiChatApp.Tests;

public class ChatHistoryStoreTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test-branches-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"test-chat-{Guid.NewGuid()}.json");

    [Fact]
    public void Load_NonExistentFile_ReturnsZero()
    {
        var history = new ChatHistory();
        var file = Path.Combine(TempDir(), "nonexistent-file.json");
        Assert.False(File.Exists(file));

        int count = ChatHistoryStore.Load(history, file);

        Assert.Equal(0, count);
    }

    [Fact]
    public void Save_Then_Load_Roundtrip_PreservesMessages()
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
            Assert.Equal(4, saved);
            Assert.True(File.Exists(file));

            var loaded = new ChatHistory();
            int loadedCount = ChatHistoryStore.Load(loaded, file);
            Assert.Equal(4, loadedCount);
            Assert.Equal(4, loaded.Count);

            var messages = loaded.ToList();
            Assert.Equal(AuthorRole.User, messages[0].Role);
            Assert.Equal("Hello", messages[0].Content);
            Assert.Equal(AuthorRole.Assistant, messages[3].Role);
            Assert.Equal("I'm doing great!", messages[3].Content);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Save_ExcludesSystemMessages()
    {
        var file = TempFile();
        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage("You are a helpful assistant.");
            history.AddUserMessage("Hello");
            history.AddAssistantMessage("Hi!");

            int saved = ChatHistoryStore.Save(history, file);
            Assert.Equal(2, saved);

            var json = File.ReadAllText(file);
            Assert.DoesNotContain("\"system\"", json);
            Assert.Contains("\"user\"", json);
            Assert.Contains("\"assistant\"", json);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Save_EmptyHistory_SavesZeroMessages()
    {
        var file = TempFile();
        try
        {
            var history = new ChatHistory();
            int saved = ChatHistoryStore.Save(history, file);
            Assert.Equal(0, saved);
            Assert.True(File.Exists(file));

            var loaded = new ChatHistory();
            int loadedCount = ChatHistoryStore.Load(loaded, file);
            Assert.Equal(0, loadedCount);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Save_HistoryWithOnlySystemMessage()
    {
        var file = TempFile();
        try
        {
            var history = new ChatHistory();
            history.AddSystemMessage("System prompt only");
            int saved = ChatHistoryStore.Save(history, file);
            Assert.Equal(0, saved);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Delete_ExistingFile_ReturnsTrueAndRemovesFile()
    {
        var file = TempFile();
        File.WriteAllText(file, "{}");
        Assert.True(File.Exists(file));

        bool deleted = ChatHistoryStore.Delete(file);
        Assert.True(deleted);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Delete_NonExistentFile_ReturnsFalse()
    {
        var dir = TempDir();
        try
        {
            var file = Path.Combine(dir, "no-such-file.json");
            Assert.False(File.Exists(file));

            bool deleted = ChatHistoryStore.Delete(file);
            Assert.False(deleted);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_IntoNonEmptyHistory_AppendsMessages()
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
            Assert.Equal(2, loaded);

            var messages = history.ToList();
            Assert.Equal(4, messages.Count);
            Assert.Equal(AuthorRole.System, messages[0].Role);
            Assert.Equal("Pre-existing user message", messages[1].Content);
            Assert.Equal("First user message", messages[2].Content);
            Assert.Equal("First assistant response", messages[3].Content);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Save_MultipleSaves_OverwritesFile()
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
            Assert.Equal(2, count);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Load_ValidJson_ExtraUnknownRoles_AreSkipped()
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
            Assert.Equal(2, count);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Branch_SaveInSubdirectory()
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
            Assert.Equal(2, saved);
            Assert.True(File.Exists(branchFile));

            var loaded = new ChatHistory();
            int count = ChatHistoryStore.Load(loaded, branchFile);
            Assert.Equal(2, count);
            Assert.Equal("Feature discussion", loaded.ToList()[0].Content);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Branch_DeleteFromDisk()
    {
        var dir = TempDir();
        try
        {
            string branchDir = Path.Combine(dir, "branches");
            Directory.CreateDirectory(branchDir);
            string branchFile = Path.Combine(branchDir, "test-branch.json");
            File.WriteAllText(branchFile, "[]");
            Assert.True(File.Exists(branchFile));

            bool deleted = ChatHistoryStore.Delete(branchFile);
            Assert.True(deleted);
            Assert.False(File.Exists(branchFile));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Branch_Delete_NonExistentBranch()
    {
        var dir = TempDir();
        try
        {
            string branchFile = Path.Combine(dir, "branches", "no-such-branch.json");
            bool deleted = ChatHistoryStore.Delete(branchFile);
            Assert.False(deleted);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Branch_Rename_OnDisk()
    {
        var dir = TempDir();
        try
        {
            string branchDir = Path.Combine(dir, "branches");
            Directory.CreateDirectory(branchDir);
            string oldFile = Path.Combine(branchDir, "old-name.json");
            File.WriteAllText(oldFile, "[{\"Role\": \"user\", \"Content\": \"Hello\"}]\n");

            string newFile = Path.Combine(branchDir, "new-name.json");
            File.Move(oldFile, newFile);

            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(newFile));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Branch_Rename_OverwriteFails_WhenTargetExists()
    {
        var dir = TempDir();
        try
        {
            string branchDir = Path.Combine(dir, "branches");
            Directory.CreateDirectory(branchDir);
            string oldFile = Path.Combine(branchDir, "source.json");
            string newFile = Path.Combine(branchDir, "target.json");

            File.WriteAllText(oldFile, "[{\"Role\": \"user\", \"Content\": \"Source\"}]\n");
            File.WriteAllText(newFile, "[{\"Role\": \"user\", \"Content\": \"Target exists\"}]\n");

            bool threw = false;
            try { File.Move(oldFile, newFile, overwrite: false); }
            catch (IOException) { threw = true; }

            Assert.True(threw);
            Assert.True(File.Exists(oldFile));
            Assert.True(File.Exists(newFile));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
