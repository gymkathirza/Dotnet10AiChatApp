using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Dotnet10AiChatApp;

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

var sessionState = new ChatSessionState(
    chatHistory,
    kernel,
    chatService,
    provider,
    modelDisplay,
    systemPrompt,
    chatHistoryFile,
    activeBranch,
    branches,
    config);

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

await ChatSession.RunAsync(sessionState);

