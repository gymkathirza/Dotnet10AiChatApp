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
(builder, modelDisplay) = await ProviderSetup.SetupProviderAsync(provider, config);

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
    if (ChatCommandHandler.TryHandleCommand(input, ref chatHistory, ref chatService, ref kernel,
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
            ? BranchManager.BranchFilePath(activeBranch)
            : chatHistoryFile;
        try { ChatHistoryStore.Save(chatHistory, saveFile); }
        catch (Exception ex) { ConsoleHelpers.Warn($"Could not save history: {ex.Message}"); }
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

