using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Dotnet10AiChatApp;

public static class ChatCommandHandler
{
    public static bool TryHandleCommand(string input, ChatSessionState state)
    {
        switch (input)
        {
            case "/clear":
                state.ChatHistory = new ChatHistory();
                state.ChatHistory.AddSystemMessage(state.SystemPrompt);
                string clearFile = state.ActiveBranch is not null
                    ? BranchManager.BranchFilePath(state.ActiveBranch)
                    : state.ChatHistoryFile;
                try { ChatHistoryStore.Delete(clearFile); } catch { }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  🗑  Chat history cleared.");
                Console.ResetColor();
                Console.WriteLine();
                return true;

            case "/history":
                ConsoleHelpers.PrintHistory(state.ChatHistory);
                return true;

            case "/branches":
                BranchManager.PrintBranches(state.Branches, state.ActiveBranch);
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
                    int count = ChatHistoryStore.Save(state.ChatHistory, fileName);
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
                    int count = ChatHistoryStore.Load(state.ChatHistory, fileName);
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
                    var (newBuilder, newDisplay) = ProviderSetup.SetupProvider(state.Provider, state.Config, newModel);
                    state.Kernel = newBuilder.Build();
                    state.ChatService = state.Kernel.GetRequiredService<IChatCompletionService>();
                    state.ChatHistory = new ChatHistory();
                    state.ChatHistory.AddSystemMessage(state.SystemPrompt);
                    string modelClearFile = state.ActiveBranch is not null
                        ? BranchManager.BranchFilePath(state.ActiveBranch)
                        : state.ChatHistoryFile;
                    try { ChatHistoryStore.Delete(modelClearFile); } catch { }
                    state.ModelDisplay = newDisplay;
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
                BranchManager.SaveCurrentBranch(state.ChatHistory, state.ActiveBranch, state.ChatHistoryFile);
                if (state.ActiveBranch is not null)
                    state.Branches[state.ActiveBranch] = state.ChatHistory;
                state.Branches[branchName] = state.ChatHistory;
                state.ActiveBranch = branchName;
                state.ChatHistory = new ChatHistory();
                state.ChatHistory.AddSystemMessage(state.SystemPrompt);
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
                if (string.Equals(branchName, state.ActiveBranch ?? "main", StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  Already on branch '{branchName}'.");
                    Console.ResetColor();
                    Console.WriteLine();
                    return true;
                }
                bool isMain = string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase);
                if (!state.Branches.ContainsKey(branchName))
                {
                    string loadFile = isMain ? state.ChatHistoryFile : BranchManager.BranchFilePath(branchName);
                    if (File.Exists(loadFile))
                    {
                        var restored = new ChatHistory();
                        restored.AddSystemMessage(state.SystemPrompt);
                        try { ChatHistoryStore.Load(restored, loadFile); }
                        catch (Exception ex) { ConsoleHelpers.Warn($"Could not load branch: {ex.Message}"); return true; }
                        state.Branches[branchName] = restored;
                    }
                    else if (!isMain)
                    {
                        ConsoleHelpers.Warn($"Branch '{branchName}' not found. Use /branch to create it.");
                        return true;
                    }
                    else
                    {
                        var empty = new ChatHistory();
                        empty.AddSystemMessage(state.SystemPrompt);
                        state.Branches[branchName] = empty;
                    }
                }
                BranchManager.SaveCurrentBranch(state.ChatHistory, state.ActiveBranch, state.ChatHistoryFile);
                if (state.ActiveBranch is not null)
                    state.Branches[state.ActiveBranch] = state.ChatHistory;
                state.ChatHistory = state.Branches[branchName];
                state.ActiveBranch = isMain ? null : branchName;
                int msgCount = state.ChatHistory.Count(m => m.Role != AuthorRole.System);
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
                var currentHistory = state.ChatHistory;
                var currentBranch = state.ActiveBranch;
                var currentBranches = state.Branches;
                BranchManager.DeleteBranch(branchName, ref currentHistory, ref currentBranch, ref currentBranches);
                state.ChatHistory = currentHistory;
                state.ActiveBranch = currentBranch;
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
                var currentHistory = state.ChatHistory;
                var currentBranch = state.ActiveBranch;
                var currentBranches = state.Branches;
                BranchManager.RenameBranch(parts[0], parts[1], ref currentHistory, ref currentBranch, ref currentBranches, state.SystemPrompt);
                state.ChatHistory = currentHistory;
                state.ActiveBranch = currentBranch;
                return true;
            }
        }

        return false;
    }
}
