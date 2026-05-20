using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Dotnet10AiChatApp;

public sealed class ChatSessionState
{
    public ChatHistory ChatHistory { get; set; }
    public Kernel Kernel { get; set; }
    public IChatCompletionService ChatService { get; set; }
    public string Provider { get; set; }
    public string ModelDisplay { get; set; }
    public string SystemPrompt { get; set; }
    public string ChatHistoryFile { get; set; }
    public string? ActiveBranch { get; set; }
    public Dictionary<string, ChatHistory> Branches { get; }
    public IConfiguration Config { get; }

    public ChatSessionState(
        ChatHistory chatHistory,
        Kernel kernel,
        IChatCompletionService chatService,
        string provider,
        string modelDisplay,
        string systemPrompt,
        string chatHistoryFile,
        string? activeBranch,
        Dictionary<string, ChatHistory> branches,
        IConfiguration config)
    {
        ChatHistory = chatHistory;
        Kernel = kernel;
        ChatService = chatService;
        Provider = provider;
        ModelDisplay = modelDisplay;
        SystemPrompt = systemPrompt;
        ChatHistoryFile = chatHistoryFile;
        ActiveBranch = activeBranch;
        Branches = branches;
        Config = config;
    }
}

public static class ChatSession
{
    public static async Task RunAsync(ChatSessionState state)
    {
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("You > ");
            Console.ResetColor();

            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input is "exit" or "quit") break;

            if (ChatCommandHandler.TryHandleCommand(input, state))
            {
                continue;
            }

            state.ChatHistory.AddUserMessage(input);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("AI  > ");
            Console.ResetColor();

            try
            {
                var fullResponse = new System.Text.StringBuilder();
                await foreach (var chunk in state.ChatService.GetStreamingChatMessageContentsAsync(state.ChatHistory))
                {
                    Console.Write(chunk.Content);
                    fullResponse.Append(chunk.Content);
                }

                Console.WriteLine();
                state.ChatHistory.AddAssistantMessage(fullResponse.ToString());

                string saveFile = state.ActiveBranch is not null
                    ? BranchManager.BranchFilePath(state.ActiveBranch)
                    : state.ChatHistoryFile;

                try
                {
                    ChatHistoryStore.Save(state.ChatHistory, saveFile);
                }
                catch (Exception ex)
                {
                    ConsoleHelpers.Warn($"Could not save history: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}
