# .NET AI Chat App

A multi-provider, interactive AI chat application built with **.NET 10** and **Microsoft Semantic Kernel**. Chat with AI models from Ollama (local), OpenAI, or Azure OpenAI — all from your terminal.

---

## 🏗 High-Level Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     Terminal (User)                      │
└─────────────────────┬───────────────────────────────────┘
                      │  stdin / stdout
                      ▼
┌─────────────────────────────────────────────────────────┐
│                    Program.cs                            │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐ │
│  │ SetupOllama  │  │ SetupOpenAI  │  │SetupAzureOpenAI│ │
│  │   (async)    │  │   (async)    │  │   (async)      │ │
│  └──────┬───────┘  └──────┬───────┘  └───────┬───────┘ │
│         │                 │                   │         │
│         └─────────────────┼───────────────────┘         │
│                           │                             │
│                    ┌──────▼──────┐                      │
│                    │  IKernel    │                      │
│                    │  Builder    │                      │
│                    └──────┬──────┘                      │
│                           │                             │
│                    ┌──────▼──────┐                      │
│                    │   Kernel    │                      │
│                    └──────┬──────┘                      │
│                           │                             │
│              ┌────────────▼────────────┐                │
│              │ IChatCompletionService  │                │
│              └────────────┬────────────┘                │
│                           │                             │
│              ┌────────────▼────────────┐                │
│              │     Chat Loop           │                │
│              │  (Streaming Responses)  │                │
│              └─────────────────────────┘                │
└─────────────────────────────────────────────────────────┘
         │                 │                   │
         ▼                 ▼                   ▼
┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐
│   Ollama    │  │   OpenAI    │  │   Azure OpenAI      │
│  (local)    │  │   (cloud)   │  │   (cloud)           │
│  :11434     │  │ api.openai  │  │ *.openai.azure.com  │
└─────────────┘  └─────────────┘  └─────────────────────┘
```

---

## 🔄 Application Flow Diagram

```
         ┌──────────┐
         │  START   │
         └────┬─────┘
              │
              ▼
    ┌─────────────────────┐
    │ Load Configuration  │
    │ (User Secrets)      │
    └─────────┬───────────┘
              │
              ▼
    ┌─────────────────────┐
    │ Read "Provider" key │
    │ (ollama|openai|     │
    │  azure|azureopenai) │
    └─────────┬───────────┘
              │
     ┌────────┼────────┐
     │        │        │
     ▼        ▼        ▼
┌────────┐┌───────┐┌──────────┐
│Ollama? ││OpenAI?││ Azure?   │
└───┬────┘└───┬───┘└────┬─────┘
    │         │         │
    ▼         ▼         ▼
┌────────┐┌───────┐┌──────────┐
│Health  ││Health ││Health    │
│Check   ││Check  ││Check     │
│Ollama  ││OpenAI ││Azure     │
│:11434  ││/v1/   ││OpenAI    │
│        ││models ││endpoint  │
└───┬────┘└───┬───┘└────┬─────┘
    │         │         │
    │    ┌────┘    ┌────┘
    │    │ FAIL?   │ FAIL?
    │    ▼         ▼
    │  ┌──────┐ ┌──────┐
    │  │EXIT 1│ │EXIT 1│
    │  └──────┘ └──────┘
    │
    ▼
┌─────────────────────┐
│ Register Chat       │
│ Completion Service  │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│ Build Kernel        │
│ Add System Prompt   │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│ Display Banner      │
│ (Provider + Model)  │
└─────────┬───────────┘
          │
          ▼
     ┌─────────┐
◄────│  Loop   │
│    └────┬────┘
│         │
│         ▼
│   ┌───────────┐    ┌───────────┐
│   │ Read User │───▶│ "exit" /  │───▶ EXIT
│   │  Input    │    │ "quit"?   │
│   └───────────┘    └───────────┘
│         │ (no)
│         ▼
│   ┌───────────────┐
│   │ Add to        │
│   │ ChatHistory   │
│   └───────┬───────┘
│           │
│           ▼
│   ┌───────────────────────┐     ┌──────────┐
│   │ Stream Response from  │────▶│  Error?  │
│   │ ChatCompletionService │     └────┬─────┘
│   └───────────────────────┘          │ (yes)
│           │ (no error)               ▼
│           ▼                    ┌──────────┐
│   ┌───────────────┐           │ Print    │
│   │ Add Assistant │           │ Error    │
│   │ to History    │           └──────────┘
│   └───────────────┘                │
│           │                        │
└───────────┴────────────────────────┘
```

---

## 📋 Prerequisites

| Requirement | Version |
|-------------|---------|
| .NET SDK | 10.0+ |
| Ollama (optional) | 0.24.0+ (for local models) |

- **Ollama**: Required only if using the `ollama` provider. Install from [ollama.com](https://ollama.com).
- **OpenAI / Azure OpenAI**: Requires an API key from the respective platform.

---

## ⚙️ Configuration

All settings are stored securely via **.NET User Secrets** (outside the project directory, never committed to git).

### Quick Start (Ollama — default, no API key needed)

```bash
# Pull a model (if not already done)
ollama pull gemma4:latest

# Optional: customize model or endpoint
dotnet user-secrets set "Ollama:ModelId" "gemma4:latest"
dotnet user-secrets set "Ollama:Endpoint" "http://localhost:11434"
```

### Switch Providers

```bash
# Use Ollama (default)
dotnet user-secrets set "Provider" "ollama"

# Use OpenAI
dotnet user-secrets set "Provider" "openai"
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-key"
dotnet user-secrets set "OpenAI:ModelId" "gpt-4o-mini"

# Use Azure OpenAI
dotnet user-secrets set "Provider" "azureopenai"
dotnet user-secrets set "Azure:Deployment" "gpt-4o"
dotnet user-secrets set "Azure:Endpoint" "https://your-resource.openai.azure.com/"
dotnet user-secrets set "Azure:ApiKey" "your-key"
```

### System Prompt (all providers)

```bash
dotnet user-secrets set "SystemPrompt" "You are a helpful, concise assistant."
```

### Chat History Persistence

Conversations are automatically saved to a local JSON file (`chat-history.json` by default). The history survives app restarts — your previous conversations are loaded on startup and the AI retains full context.

```bash
# Optional: customize the history file path
dotnet user-secrets set "ChatHistoryFile" "my-chats.json"
```

- **Load**: On startup, the app reads the JSON file and restores all previous messages.
- **Save**: After every AI response, the full conversation (excluding the system prompt) is saved.
- **Clear**: Type `/clear` during a chat to reset the conversation and delete the history file.

### Full Configuration Reference

| Key | Provider | Default | Description |
|-----|----------|---------|-------------|
| `Provider` | All | `ollama` | `ollama`, `openai`, `azureopenai` |
| `SystemPrompt` | All | *friendly assistant* | System-level prompt for AI personality |
| `ChatHistoryFile` | All | `chat-history.json` | Path to JSON file for persisting chat history |
| `Ollama:ModelId` | Ollama | `gemma4:latest` | Model name (e.g., `llama3.2`, `phi3`) |
| `Ollama:Endpoint` | Ollama | `http://localhost:11434` | Ollama server URL |
| `OpenAI:ApiKey` | OpenAI | *(required)* | OpenAI API key |
| `OpenAI:ModelId` | OpenAI | `gpt-4o-mini` | Model name |
| `Azure:Deployment` | Azure | *(required)* | Deployment name in Azure |
| `Azure:Endpoint` | Azure | *(required)* | Azure OpenAI resource URL |
| `Azure:ApiKey` | Azure | *(required)* | Azure API key |

---

## 🔨 Build & Run

```bash
# Build
dotnet build

# Run
dotnet run
```

The app will:
1. Show a health check for the selected provider
2. Load any previously saved chat history from the JSON file
3. Display a banner with provider, model info, and loaded message count
4. Start an interactive chat loop (type `exit` to quit, `/clear` to reset history)

**Example session (with persisted history):**
```
Checking Ollama... ✓

╔══════════════════════════════════════╗
║      .NET AI Chat App               ║
╠══════════════════════════════════════╣
║  Provider: ollama                   ║
║  Model:    gemma4:latest            ║
║  Type 'exit' or 'quit' to stop      ║
║  /clear /history /model /save /load ║
║  /branch /switch /delete /rename    ║
╚══════════════════════════════════════╝
  📝 Loaded 4 message(s) from chat-history.json

You > What's my name?
AI  > Your name is Alice.
You > /save my-chats.json
  💾 Saved 6 message(s) to my-chats.json

You > exit
```

### In-Chat Commands

| Command | Description |
|---------|-------------|
| `exit` / `quit` | Exit the chat app |
| `/clear` | Reset conversation and delete history file |
| `/history` | Display the full chat history in the terminal |
| `/save <file>` | Save current chat history to a specific JSON file |
| `/load <file>` | Load messages from a JSON file into current chat |
| `/model <id>` | Switch to a different model (e.g., `/model llama3.2`) |
| `/branch <name>` | Save current conversation and start a new named branch |
| `/switch <name>` | Switch to a named branch (`/switch main` to go back) |
| `/branches` | List all branches (in-memory and on-disk) |
| `/delete <name>` | Delete a named branch (cannot delete main or active branch) |
| `/rename <old> <new>` | Rename a branch (cannot rename main, new name must be unique) |

### Conversation Branching

Create multiple named conversation threads that persist independently:

```
You > /branch work
  🌿 Branched to 'work'. Starting fresh conversation.

You > Let's discuss the project timeline.
AI  > Sure! What's the current status?

You > /branch personal
  🌿 Branched to 'personal'. Starting fresh conversation.

You > What's a good recipe for pasta?
AI  > Here's a simple aglio e olio...

You > /branches
  ── Branches ──
  * (main)
    work       (2 messages)
    personal   (2 messages) ◀ active

You > /switch work
  🔀 Switched to branch 'work' (2 messages).

You > What were we discussing?
AI  > We were discussing the project timeline.
```

- **Auto-save**: Each branch auto-saves to `branches/<name>.json` after every AI response. The default branch saves to `chat-history.json`.
- **Disk persistence**: Branches survive app restarts — they're loaded from disk on `/switch`.
- **Switch to main**: Use `/switch main` to return to the default conversation.
- **Delete a branch**: `/delete <name>` removes the branch from memory and disk. Cannot delete the main branch or the currently active branch.
- **Rename a branch**: `/rename <old-name> <new-name>` changes the branch name in memory and on disk. The new name must not already exist.

```
You > /branches
  ── Branches ──
  * (main)
    work       (4 messages) ◀ active
    old-name   (2 messages)

You > /rename old-name feature-idea
  ✏  Renamed branch 'old-name' → 'feature-idea'.

You > /delete work
  🗑  Deleted branch 'work'.

You > /branches
  ── Branches ──
  * (main) ◀ active
    feature-idea   (2 messages)
```

---

## 🧪 Running Tests

```bash
dotnet run -- --test
```

Runs 15 self-tests covering the `ChatHistoryStore` persistence layer and branching:

- Load from nonexistent / empty / corrupted files
- Save-then-load round-trip with message integrity
- System message exclusion
- File delete behavior
- Message appending into non-empty history
- File overwrite on multiple saves
- Unknown role filtering
- Branch save/load in subdirectories
- Branch file delete and rename on disk
- Rename conflict detection (overwrite protection)

---

## 🛠 Technical Details

### Tech Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10.0 |
| AI Framework | Microsoft Semantic Kernel 1.76.0 |
| Local Models | Ollama (via `SemanticKernel.Connectors.Ollama`) |
| Cloud Models | OpenAI, Azure OpenAI (via `SemanticKernel.Connectors.AzureOpenAI`) |
| Config | `Microsoft.Extensions.Configuration.UserSecrets` |
| Streaming | `IAsyncEnumerable<T>` with `GetStreamingChatMessageContentsAsync` |

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.SemanticKernel` | 1.76.0 | Core AI orchestration framework |
| `Microsoft.SemanticKernel.Connectors.Ollama` | 1.76.0-alpha | Ollama local model connector |
| `Microsoft.SemanticKernel.Connectors.AzureOpenAI` | 1.76.0 | Azure OpenAI + OpenAI connector |
| `Microsoft.Extensions.Configuration.UserSecrets` | 10.0.8 | Secure configuration storage |

### Design Decisions

- **Shared Chat Loop**: All providers use the same `IChatCompletionService` abstraction and `ChatHistory` — only the kernel initialization differs.
- **Streaming Responses**: Tokens are streamed in real-time via `GetStreamingChatMessageContentsAsync` for responsive UX.
- **Health Checks**: ALL providers validate connectivity at startup. Ollama pings `/api/tags`, OpenAI calls `GET /v1/models`, Azure OpenAI hits its models endpoint. Non-authentication errors (e.g., 429 rate-limit) emit a warning rather than a fatal exit.
- **Conversation Branching**: Named branches with independent chat histories and disk-backed persistence to `branches/*.json`. Switch freely between contexts without losing any conversation.
- **Fail-Fast**: Missing required configuration causes an immediate exit with clear setup instructions.
- **Chat History Persistence**: Conversations are saved to `chat-history.json` after each exchange and loaded on startup, so context survives app restarts. The system prompt is intentionally excluded from the persisted file.
- **Secure by Default**: API keys are stored in .NET User Secrets (JSON file outside the project), never in source code.

### Project Structure

```
Dotnet10AiChatApp/
├── Program.cs              # Main application (multi-provider chat)
├── ChatHistoryStore.cs     # Chat history persistence (save/load/delete)
├── SelfTests.cs            # Self-contained test suite (15 tests)
├── Dotnet10AiChatApp.csproj # Project file (net10.0, package references)
├── Dotnet10AiChatApp.sln   # Solution file
├── chat-history.json       # Persisted chat history (auto-created)
├── branches/               # Named branch persistence files
├── sample_data.json        # Sample data (not used by chat app)
└── README.md               # This file
```

---

## 🔒 Security

- API keys are stored in `~/.microsoft/usersecrets/<id>/secrets.json` — **outside** the project directory.
- This prevents accidental commits of credentials to version control.
- Use `dotnet user-secrets list` to view all configured secrets.
- Use `dotnet user-secrets remove "Key"` to delete a specific secret.
