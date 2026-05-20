using System.Text.Json;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Dotnet10AiChatApp;

/// <summary>
/// Persists and restores chat history to/from JSON files.
/// Stateless — callers handle file paths and chat history objects.
/// </summary>
public static class ChatHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Loads user + assistant messages from a JSON file into the given ChatHistory.
    /// Returns the number of messages loaded. Returns 0 if the file doesn't exist.
    /// </summary>
    public static int Load(ChatHistory chatHistory, string filePath)
    {
        if (!File.Exists(filePath)) return 0;

        var json = File.ReadAllText(filePath);
        var messages = JsonSerializer.Deserialize<List<ChatMessageDto>>(json);
        if (messages is null) return 0;

        int count = 0;
        foreach (var msg in messages)
        {
            if (msg.Role == "user")
            {
                chatHistory.AddUserMessage(msg.Content);
                count++;
            }
            else if (msg.Role == "assistant")
            {
                chatHistory.AddAssistantMessage(msg.Content);
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Saves user + assistant messages to a JSON file. System messages are excluded.
    /// Returns the number of messages saved.
    /// </summary>
    public static int Save(ChatHistory chatHistory, string filePath)
    {
        var messages = new List<ChatMessageDto>();
        foreach (var msg in chatHistory)
        {
            if (msg.Role == AuthorRole.System) continue;
            messages.Add(new ChatMessageDto(
                msg.Role == AuthorRole.User ? "user" : "assistant",
                msg.Content ?? ""));
        }
        var json = JsonSerializer.Serialize(messages, JsonOptions);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(filePath, json);
        return messages.Count;
    }

    /// <summary>
    /// Deletes the history file if it exists. Returns true if a file was deleted.
    /// </summary>
    public static bool Delete(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return true;
        }
        return false;
    }
}

public record ChatMessageDto(string Role, string Content);
