using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Dotnet10AiChatApp;

// ── Self-test mode ────────────────────────────────────────────────────
if (args.Length > 0 && args[0] == "--test")
{
    Console.WriteLine("Running self-tests...\n");
    int failures = SelfTests.RunAll();
    Environment.Exit(failures > 0 ? 1 : 0);
}

// ── Load configuration ─────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

string provider = (config["Provider"] ?? "ollama").ToLowerInvariant();
string systemPrompt = config["SystemPrompt"]
    ?? "You are a helpful, friendly AI assistant. Keep your responses concise and to the point.";
string chatHistoryFile = config["ChatHistoryFile"] ?? "chat-history.json";

// ── Initialize Kernel (with health checks for all providers) ───────────
IKernelBuilder builder;
string modelDisplay;
(builder, modelDisplay) = await SetupProviderAsync(provider, config);

var kernel = builder.Build();
var chatService = kernel.GetRequiredService<IChatCompletionService>();
var chatHistory = new ChatHistory();
chatHistory.AddSystemMessage(systemPrompt);

// ── Branch system ──────────────────────────────────────────────────────
var branches = new Dictionary<string, ChatHistory>(StringComparer.OrdinalIgnoreCase);
string? activeBranch = null; // null = default/main

// ── Display banner & load persisted history ────────────────────────────
Console.Clear();
Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║      .NET AI Chat App               ║");
Console.WriteLine("╠══════════════════════════════════════╣");
Console.WriteLine($"║  Provider: {provider,-23} ║");
Console.WriteLine($"║  Model:    {modelDisplay,-23} ║");
if (activeBranch is not null)
    Console.WriteLine($"║  Branch:   {activeBranch,-23} ║");
Console.WriteLine("║  Type 'exit' or 'quit' to stop      ║");
Console.WriteLine("║  /clear /history /model /save /load ║");
Console.WriteLine("║  /branch /switch /branches          ║");
Console.WriteLine("║  /delete /rename                    ║");
Console.WriteLine("╚══════════════════════════════════════╝");

int loadedMessages = 0;
try
{
    loadedMessages = ChatHistoryStore.Load(chatHistory, chatHistoryFile);
}
catch (Exception ex)
{
    Console.WriteLine($"  ⚠  Could not load history ({ex.Message}), starting fresh.");
}
if (loadedMessages > 0)
    Console.WriteLine($"  📝 Loaded {loadedMessages} message(s) from {chatHistoryFile}");
Console.WriteLine();

// ── Chat loop ──────────────────────────────────────────────────────────
while (true)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("You > ");
    Console.ResetColor();

    string? input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input is "exit" or "quit") break;

    // ── Slash commands ─────────────────────────────────────────────────
    if (TryHandleCommand(input, ref chatHistory, ref chatService, ref kernel,
            ref provider, ref modelDisplay, ref systemPrompt,
            ref chatHistoryFile, ref activeBranch, ref branches, config))
        continue;

    chatHistory.AddUserMessage(input);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("AI  > ");
    Console.ResetColor();

    try
    {
        var fullResponse = new System.Text.StringBuilder();
        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(chatHistory))
        {
            Console.Write(chunk.Content);
            fullResponse.Append(chunk.Content);
        }
        Console.WriteLine();
        chatHistory.AddAssistantMessage(fullResponse.ToString());

        // Auto-save current branch
        string saveFile = activeBranch is not null
            ? BranchFilePath(activeBranch)
            : chatHistoryFile;
        try { ChatHistoryStore.Save(chatHistory, saveFile); }
        catch (Exception ex) { Warn($"Could not save history: {ex.Message}"); }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
    }
}

// ═══════════════════════════════════════════════════════════════════════
// ── Slash command handler ──────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════

static bool TryHandleCommand(string input,
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
                ? BranchFilePath(activeBranch)
                : chatHistoryFile;
            try { ChatHistoryStore.Delete(clearFile); } catch { }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  🗑  Chat history cleared.");
            Console.ResetColor();
            Console.WriteLine();
            return true;

        case "/history":
            PrintHistory(chatHistory);
            return true;

        case "/branches":
            PrintBranches(branches, activeBranch);
            return true;

        case string s when s.StartsWith("/save "):
        {
            var fileName = input[6..].Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Warn("Usage: /save <filename>");
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
            catch (Exception ex) { Warn($"Save failed: {ex.Message}"); }
            return true;
        }

        case string s when s.StartsWith("/load "):
        {
            var fileName = input[6..].Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Warn("Usage: /load <filename>");
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
            catch (Exception ex) { Warn($"Load failed: {ex.Message}"); }
            return true;
        }

        case string s when s.StartsWith("/model "):
        {
            var newModel = input[7..].Trim();
            if (string.IsNullOrWhiteSpace(newModel))
            {
                Warn("Usage: /model <model-id>");
                return true;
            }
            try
            {
                var (newBuilder, newDisplay) = SetupProvider(provider, config, newModel);
                kernel = newBuilder.Build();
                chatService = kernel.GetRequiredService<IChatCompletionService>();
                chatHistory = new ChatHistory();
                chatHistory.AddSystemMessage(systemPrompt);
                string modelClearFile = activeBranch is not null
                    ? BranchFilePath(activeBranch)
                    : chatHistoryFile;
                try { ChatHistoryStore.Delete(modelClearFile); } catch { }
                modelDisplay = newDisplay;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  🔄 Switched model to {newDisplay}. History cleared.");
                Console.ResetColor();
                Console.WriteLine();
            }
            catch (Exception ex) { Warn($"Model switch failed: {ex.Message}"); }
            return true;
        }

        case string s when s.StartsWith("/branch "):
        {
            var branchName = input[8..].Trim();
            if (string.IsNullOrWhiteSpace(branchName))
            {
                Warn("Usage: /branch <name>");
                return true;
            }
            // Save current branch state
            SaveCurrentBranch(chatHistory, activeBranch, chatHistoryFile);
            // Update old branch's dictionary entry before switching
            if (activeBranch is not null)
                branches[activeBranch] = chatHistory;
            // Store current in dictionary under new name and start fresh
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
                Warn("Usage: /switch <name>");
                return true;
            }
            // Early return if already on this branch
            if (string.Equals(branchName, activeBranch ?? "main", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Already on branch '{branchName}'.");
                Console.ResetColor();
                Console.WriteLine();
                return true;
            }
            // Switching to "main" — load from default chatHistoryFile
            bool isMain = string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase);
            if (!branches.ContainsKey(branchName))
            {
                string loadFile = isMain ? chatHistoryFile : BranchFilePath(branchName);
                if (File.Exists(loadFile))
                {
                    var restored = new ChatHistory();
                    restored.AddSystemMessage(systemPrompt);
                    try { ChatHistoryStore.Load(restored, loadFile); }
                    catch (Exception ex) { Warn($"Could not load branch: {ex.Message}"); return true; }
                    branches[branchName] = restored;
                }
                else if (!isMain)
                {
                    Warn($"Branch '{branchName}' not found. Use /branch to create it.");
                    return true;
                }
                else
                {
                    // "main" doesn't exist yet — start empty
                    var empty = new ChatHistory();
                    empty.AddSystemMessage(systemPrompt);
                    branches[branchName] = empty;
                }
            }
            // Save current branch state
            SaveCurrentBranch(chatHistory, activeBranch, chatHistoryFile);
            // Switch
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
                Warn("Usage: /delete <branch-name>");
                return true;
            }
            DeleteBranch(branchName, ref chatHistory, ref activeBranch, ref branches);
            return true;
        }

        case string s when s.StartsWith("/rename "):
        {
            var parts = input[8..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                Warn("Usage: /rename <old-name> <new-name>");
                return true;
            }
            RenameBranch(parts[0], parts[1], ref chatHistory, ref activeBranch, ref branches);
            return true;
        }
    }
    return false;
}

// ═══════════════════════════════════════════════════════════════════════
// ── Branch helpers ─────────────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════

static string BranchFilePath(string name) => Path.Combine("branches", $"{name}.json");

static void SaveCurrentBranch(ChatHistory chatHistory, string? activeBranch, string defaultFile)
{
    string file = activeBranch is not null ? BranchFilePath(activeBranch) : defaultFile;
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        ChatHistoryStore.Save(chatHistory, file);
    }
    catch { /* best effort */ }
}

static void PrintHistory(ChatHistory chatHistory)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("  ── Chat History ──");
    Console.ResetColor();

    int i = 0;
    foreach (var msg in chatHistory)
    {
        if (msg.Role == AuthorRole.System) continue;
        i++;
        var prefix = msg.Role == AuthorRole.User ? "You" : "AI ";
        var color = msg.Role == AuthorRole.User ? ConsoleColor.Green : ConsoleColor.Cyan;
        Console.ForegroundColor = color;
        Console.WriteLine($"  [{i}] {prefix}: {msg.Content}");
    }
    Console.ResetColor();
    if (i == 0)
        Console.WriteLine("  (no messages yet)");
    Console.WriteLine();
}

static void DeleteBranch(string branchName,
    ref ChatHistory chatHistory,
    ref string? activeBranch,
    ref Dictionary<string, ChatHistory> branches)
{
    if (string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase))
    {
        Warn("Cannot delete the main branch.");
        return;
    }

    if (string.Equals(branchName, activeBranch, StringComparison.OrdinalIgnoreCase))
    {
        Warn($"Cannot delete the active branch '{branchName}'. Switch to another branch first.");
        return;
    }

    // Check in-memory first, then on-disk
    bool inDict = branches.Remove(branchName, out _);
    string filePath = BranchFilePath(branchName);
    bool onDisk = File.Exists(filePath);

    if (!inDict && !onDisk)
    {
        Warn($"Branch '{branchName}' not found.");
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

static void RenameBranch(string oldName, string newName,
    ref ChatHistory chatHistory,
    ref string? activeBranch,
    ref Dictionary<string, ChatHistory> branches)
{
    if (string.Equals(oldName, "main", StringComparison.OrdinalIgnoreCase))
    {
        Warn("Cannot rename the main branch.");
        return;
    }

    if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
    {
        Warn($"'{oldName}' and '{newName}' are the same name.");
        return;
    }

    if (branches.ContainsKey(newName) || File.Exists(BranchFilePath(newName)))
    {
        Warn($"Branch '{newName}' already exists. Choose a different name.");
        return;
    }

    // Load from disk if not in memory
    if (!branches.TryGetValue(oldName, out var history))
    {
        string oldFile = BranchFilePath(oldName);
        if (!File.Exists(oldFile))
        {
            Warn($"Branch '{oldName}' not found.");
            return;
        }
        history = new ChatHistory();
        try { ChatHistoryStore.Load(history, oldFile); }
        catch (Exception ex) { Warn($"Could not load branch: {ex.Message}"); return; }
    }

    branches.Remove(oldName);
    branches[newName] = history;

    // Rename disk file
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

static void PrintBranches(Dictionary<string, ChatHistory> branches, string? activeBranch)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("  ── Branches ──");
    Console.ResetColor();

    // Show default branch
    var marker = activeBranch is null ? " ◀ active" : "";
    Console.WriteLine($"  {'*',-2} (main)   {marker}");

    foreach (var (name, history) in branches)
    {
        int count = history.Count(m => m.Role != AuthorRole.System);
        marker = string.Equals(name, activeBranch, StringComparison.OrdinalIgnoreCase) ? " ◀ active" : "";
        Console.WriteLine($"  {' ',2} {name,-12} ({count} messages){marker}");
    }            // Check disk for branches not in memory
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

// ═══════════════════════════════════════════════════════════════════════
// ── Provider setup helpers ─────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════

static async Task<(IKernelBuilder Builder, string ModelDisplay)> SetupProviderAsync(
    string provider, IConfiguration config)
{
    var builder = Kernel.CreateBuilder();
    string modelDisplay;

    switch (provider)
    {
        case "openai":
            modelDisplay = await SetupOpenAIAsync(builder, config);
            break;
        case "azure":
        case "azureopenai":
            modelDisplay = await SetupAzureOpenAIAsync(builder, config);
            break;
        default: // ollama
            modelDisplay = await SetupOllamaAsync(builder, config);
            break;
    }
    return (builder, modelDisplay);
}

static (IKernelBuilder Builder, string ModelDisplay) SetupProvider(
    string provider, IConfiguration config, string? overrideModel)
{
    IKernelBuilder builder;
    string modelDisplay;

    switch (provider)
    {
        case "openai":
            modelDisplay = SetupOpenAI(out builder, config, overrideModel);
            break;
        case "azure":
        case "azureopenai":
            modelDisplay = SetupAzureOpenAI(out builder, config, overrideModel);
            break;
        default:
            modelDisplay = SetupOllamaSync(out builder, config, overrideModel);
            break;
    }
    return (builder, modelDisplay);
}

// ── Ollama ─────────────────────────────────────────────────────────────

static string SetupOllamaSync(out IKernelBuilder builder, IConfiguration config, string? overrideModel)
{
    string modelId = overrideModel ?? config["Ollama:ModelId"] ?? "gemma4:latest";
    var endpoint = new Uri(config["Ollama:Endpoint"] ?? "http://localhost:11434");

    builder = Kernel.CreateBuilder();
    builder.AddOllamaChatCompletion(modelId, endpoint);
    return modelId;
}

static async Task<string> SetupOllamaAsync(IKernelBuilder builder, IConfiguration config)
{
    string modelId = config["Ollama:ModelId"] ?? "gemma4:latest";
    var endpoint = new Uri(config["Ollama:Endpoint"] ?? "http://localhost:11434");

    Console.Write("Checking Ollama... ");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        var response = await http.GetAsync($"{endpoint}/api/tags");
        if (!response.IsSuccessStatusCode)
            Fail($"Ollama returned HTTP {(int)response.StatusCode}.");
    }
    catch (Exception ex)
    {
        Fail($"Cannot reach Ollama: {ex.Message}\n  Ensure Ollama is running at {endpoint}");
    }
    Ok();

    builder.AddOllamaChatCompletion(modelId, endpoint);
    return modelId;
}

// ── OpenAI ─────────────────────────────────────────────────────────────

static string SetupOpenAI(out IKernelBuilder builder, IConfiguration config, string? overrideModel)
{
    string? apiKey = config["OpenAI:ApiKey"];
    string modelId = overrideModel ?? config["OpenAI:ModelId"] ?? "gpt-4o-mini";

    if (string.IsNullOrWhiteSpace(apiKey))
        Fail("OpenAI API key not configured.\n  Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"sk-...\"");

    builder = Kernel.CreateBuilder();
    builder.AddOpenAIChatCompletion(modelId, apiKey!);
    return modelId;
}

static async Task<string> SetupOpenAIAsync(IKernelBuilder builder, IConfiguration config)
{
    string? apiKey = config["OpenAI:ApiKey"];
    string modelId = config["OpenAI:ModelId"] ?? "gpt-4o-mini";

    if (string.IsNullOrWhiteSpace(apiKey))
        Fail("OpenAI API key not configured.\n  Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"sk-...\"");

    Console.Write("Checking OpenAI... ");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    try
    {
        var response = await http.GetAsync("https://api.openai.com/v1/models");
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode is 401 or 403)
                Fail($"OpenAI returned HTTP {(int)response.StatusCode}. Check your API key.");
            else
                Warn($"OpenAI health check returned HTTP {(int)response.StatusCode} — proceeding anyway.");
        }
    }
    catch (Exception ex)
    {
        Fail($"Cannot reach OpenAI: {ex.Message}\n  Check your network and API key.");
    }
    Ok();

    builder.AddOpenAIChatCompletion(modelId, apiKey!);
    return modelId;
}

// ── Azure OpenAI ───────────────────────────────────────────────────────

static string SetupAzureOpenAI(out IKernelBuilder builder, IConfiguration config, string? overrideModel)
{
    string? deploymentName = overrideModel ?? config["Azure:Deployment"];
    string? endpoint = config["Azure:Endpoint"];
    string? apiKey = config["Azure:ApiKey"];

    if (string.IsNullOrWhiteSpace(deploymentName))
        Fail("Azure deployment name not configured.\n  Set it with: dotnet user-secrets set \"Azure:Deployment\" \"gpt-4o\"");
    if (string.IsNullOrWhiteSpace(endpoint))
        Fail("Azure endpoint not configured.\n  Set it with: dotnet user-secrets set \"Azure:Endpoint\" \"https://your-resource.openai.azure.com/\"");
    if (string.IsNullOrWhiteSpace(apiKey))
        Fail("Azure API key not configured.\n  Set it with: dotnet user-secrets set \"Azure:ApiKey\" \"your-key\"");

    builder = Kernel.CreateBuilder();
    builder.AddAzureOpenAIChatCompletion(deploymentName!, endpoint!, apiKey!);
    return deploymentName!;
}

static async Task<string> SetupAzureOpenAIAsync(IKernelBuilder builder, IConfiguration config)
{
    string? deploymentName = config["Azure:Deployment"];
    string? endpoint = config["Azure:Endpoint"];
    string? apiKey = config["Azure:ApiKey"];

    if (string.IsNullOrWhiteSpace(deploymentName))
        Fail("Azure deployment name not configured.\n  Set it with: dotnet user-secrets set \"Azure:Deployment\" \"gpt-4o\"");
    if (string.IsNullOrWhiteSpace(endpoint))
        Fail("Azure endpoint not configured.\n  Set it with: dotnet user-secrets set \"Azure:Endpoint\" \"https://your-resource.openai.azure.com/\"");
    if (string.IsNullOrWhiteSpace(apiKey))
        Fail("Azure API key not configured.\n  Set it with: dotnet user-secrets set \"Azure:ApiKey\" \"your-key\"");

    Console.Write("Checking Azure OpenAI... ");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    http.DefaultRequestHeaders.Add("api-key", apiKey);
    try
    {
        var checkUrl = $"{endpoint!.TrimEnd('/')}/openai/models?api-version=2024-10-21";
        var response = await http.GetAsync(checkUrl);
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode is 401 or 403)
                Fail($"Azure OpenAI returned HTTP {(int)response.StatusCode}. Check your API key.");
            else
                Warn($"Azure OpenAI health check returned HTTP {(int)response.StatusCode} — proceeding anyway.");
        }
    }
    catch (Exception ex)
    {
        Fail($"Cannot reach Azure OpenAI: {ex.Message}\n  Check your endpoint: {endpoint}");
    }
    Ok();

    builder.AddAzureOpenAIChatCompletion(deploymentName!, endpoint!, apiKey!);
    return deploymentName!;
}

// ═══════════════════════════════════════════════════════════════════════
// ── Helpers ────────────────────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════

static void Ok()
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✓");
    Console.ResetColor();
}

static void Warn(string message)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"  ⚠  {message}");
    Console.ResetColor();
}

static void Fail(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("✗");
    Console.WriteLine($"Error: {message}");
    Console.ResetColor();
    Environment.Exit(1);
}
