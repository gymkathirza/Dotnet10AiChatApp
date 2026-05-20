using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Dotnet10AiChatApp;

public static class ChatCommandHandler
{
    public static bool TryHandleCommand(string input,
        ref ChatHistory chatHistory,
        ref IChatCompletionService chatService,
        ref Kernel kernel,
        ref string provider,
        ref string modelDisplay,
        ref string systemPrompt,
        ref string chatHistoryFile,
        ref string? activeBranch,
        ref Dictionary<string, ChatHistory> branches,
        IConfiguration config)
    {
        switch (input)
        {
            case "/clear":
                chatHistory = new ChatHistory();
                chatHistory.AddSystemMessage(systemPrompt);
                string clearFile = activeBranch is not null
                    ? BranchManager.BranchFilePath(activeBranch)
                    : chatHistoryFile;
                try { ChatHistoryStore.Delete(clearFile); } catch { }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  🗑  Chat history cleared.");
                Console.ResetColor();
                Console.WriteLine();
                return true;

            case "/history":
                ConsoleHelpers.PrintHistory(chatHistory);
                return true;

            case "/branches":
                BranchManager.PrintBranches(branches, activeBranch);
                return true;

            case string s when s.StartsWith("/save "):
            {
                var fileName = input[6..].Trim();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    ConsoleHelpers.Warn("Usage: /save <filename>");
                    return true;
                }
                try
                {
                    int count = ChatHistoryStore.Save(chatHistory, fileName);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  💾 Saved {count} message(s) to {fileName}");
                    Console.ResetColor();
                    Console.WriteLine();
                }
                catch (Exception ex) { ConsoleHelpers.Warn($"Save failed: {ex.Message}"); }
                return true;
            }

            case string s when s.StartsWith("/load "):
            {
                var fileName = input[6..].Trim();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    ConsoleHelpers.Warn("Usage: /load <filename>");
                    return true;
                }
                try
                {
                    int count = ChatHistoryStore.Load(chatHistory, fileName);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  📂 Loaded {count} message(s) from {fileName}");
                    Console.ResetColor();
                    Console.WriteLine();
                }
                catch (Exception ex) { ConsoleHelpers.Warn($"Load failed: {ex.Message}"); }
                return true;
            }

            case string s when s.StartsWith("/model "):
            {
                var newModel = input[7..].Trim();
                if (string.IsNullOrWhiteSpace(newModel))
                {
                    ConsoleHelpers.Warn("Usage: /model <model-id>");
                    return true;
                }
                try
                {
                    var (newBuilder, newDisplay) = ProviderSetup.SetupProvider(provider, config, newModel);
                    kernel = newBuilder.Build();
                    chatService = kernel.GetRequiredService<IChatCompletionService>();
                    chatHistory = new ChatHistory();
                    chatHistory.AddSystemMessage(systemPrompt);
                    string modelClearFile = activeBranch is not null
                        ? BranchManager.BranchFilePath(activeBranch)
                        : chatHistoryFile;
                    try { ChatHistoryStore.Delete(modelClearFile); } catch { }
                    modelDisplay = newDisplay;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  🔄 Switched model to {newDisplay}. History cleared.");
                    Console.ResetColor();
                    Console.WriteLine();
                }
                catch (Exception ex) { ConsoleHelpers.Warn($"Model switch failed: {ex.Message}"); }
                return true;
            }

            case string s when s.StartsWith("/branch "):
            {
                var branchName = input[8..].Trim();
                if (string.IsNullOrWhiteSpace(branchName))
                {
                    ConsoleHelpers.Warn("Usage: /branch <name>");
                    return true;
                }
                if (!BranchManager.IsValidBranchName(branchName, out var validationError))
                {
                    ConsoleHelpers.Warn(validationError ?? "Branch name is invalid.");
                    return true;
                }
                BranchManager.SaveCurrentBranch(chatHistory, activeBranch, chatHistoryFile);
                if (activeBranch is not null)
                    branches[activeBranch] = chatHistory;
                branches[branchName] = chatHistory;
                activeBranch = branchName;
                chatHistory = new ChatHistory();
                chatHistory.AddSystemMessage(systemPrompt);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  🌿 Branched to '{branchName}'. Starting fresh conversation.");
                Console.ResetColor();
                Console.WriteLine();
                return true;
            }

            case string s when s.StartsWith("/switch "):
            {
                var branchName = input[8..].Trim();
                if (string.IsNullOrWhiteSpace(branchName))
                {
                    ConsoleHelpers.Warn("Usage: /switch <name>");
                    return true;
                }
                if (branchName != "main" && !BranchManager.IsValidBranchName(branchName, out var validationError))
                {
                    ConsoleHelpers.Warn(validationError ?? "Branch name is invalid.");
                    return true;
                }
                if (string.Equals(branchName, activeBranch ?? "main", StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  Already on branch '{branchName}'.");
                    Console.ResetColor();
                    Console.WriteLine();
                    return true;
                }
                bool isMain = string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase);
                if (!branches.ContainsKey(branchName))
                {
                    string loadFile = isMain ? chatHistoryFile : BranchManager.BranchFilePath(branchName);
                    if (File.Exists(loadFile))
                    {
                        var restored = new ChatHistory();
                        restored.AddSystemMessage(systemPrompt);
                        try { ChatHistoryStore.Load(restored, loadFile); }
                        catch (Exception ex) { ConsoleHelpers.Warn($"Could not load branch: {ex.Message}"); return true; }
                        branches[branchName] = restored;
                    }
                    else if (!isMain)
                    {
                        ConsoleHelpers.Warn($"Branch '{branchName}' not found. Use /branch to create it.");
                        return true;
                    }
                    else
                    {
                        var empty = new ChatHistory();
                        empty.AddSystemMessage(systemPrompt);
                        branches[branchName] = empty;
                    }
                }
                BranchManager.SaveCurrentBranch(chatHistory, activeBranch, chatHistoryFile);
                if (activeBranch is not null)
                    branches[activeBranch] = chatHistory;
                chatHistory = branches[branchName];
                activeBranch = isMain ? null : branchName;
                int msgCount = chatHistory.Count(m => m.Role != AuthorRole.System);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  🔀 Switched to branch '{(isMain ? "main" : branchName)}' ({msgCount} messages).");
                Console.ResetColor();
                Console.WriteLine();
                return true;
            }

            case string s when s.StartsWith("/delete "):
            {
                var branchName = input[8..].Trim();
                if (string.IsNullOrWhiteSpace(branchName))
                {
                    ConsoleHelpers.Warn("Usage: /delete <branch-name>");
                    return true;
                }
                if (!BranchManager.IsValidBranchName(branchName, out var validationError))
                {
                    ConsoleHelpers.Warn(validationError ?? "Branch name is invalid.");
                    return true;
                }
                BranchManager.DeleteBranch(branchName, ref chatHistory, ref activeBranch, ref branches);
                return true;
            }

            case string s when s.StartsWith("/rename "):
            {
                var parts = input[8..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    ConsoleHelpers.Warn("Usage: /rename <old-name> <new-name>");
                    return true;
                }
                if (!BranchManager.IsValidBranchName(parts[0], out var oldNameError))
                {
                    ConsoleHelpers.Warn(oldNameError ?? "Branch name is invalid.");
                    return true;
                }
                if (!BranchManager.IsValidBranchName(parts[1], out var newNameError))
                {
                    ConsoleHelpers.Warn(newNameError ?? "Branch name is invalid.");
                    return true;
                }
                BranchManager.RenameBranch(parts[0], parts[1], ref chatHistory, ref activeBranch, ref branches, systemPrompt);
                return true;
            }
        }

        return false;
    }
}
