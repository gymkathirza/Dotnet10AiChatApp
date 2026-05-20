using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace Dotnet10AiChatApp;

public static class ProviderSetup
{
    public static async Task<(IKernelBuilder Builder, string ModelDisplay)> SetupProviderAsync(
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
            default:
                modelDisplay = await SetupOllamaAsync(builder, config);
                break;
        }

        return (builder, modelDisplay);
    }

    public static (IKernelBuilder Builder, string ModelDisplay) SetupProvider(
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

    private static string SetupOllamaSync(out IKernelBuilder builder, IConfiguration config, string? overrideModel)
    {
        string modelId = overrideModel ?? config["Ollama:ModelId"] ?? "gemma4:latest";
        var endpoint = new Uri(config["Ollama:Endpoint"] ?? "http://localhost:11434");

        builder = Kernel.CreateBuilder();
        builder.AddOllamaChatCompletion(modelId, endpoint);
        return modelId;
    }

    private static async Task<string> SetupOllamaAsync(IKernelBuilder builder, IConfiguration config)
    {
        string modelId = config["Ollama:ModelId"] ?? "gemma4:latest";
        var endpoint = new Uri(config["Ollama:Endpoint"] ?? "http://localhost:11434");

        Console.Write("Checking Ollama... ");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await http.GetAsync($"{endpoint}/api/tags");
            if (!response.IsSuccessStatusCode)
                ConsoleHelpers.Fail($"Ollama returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            ConsoleHelpers.Fail($"Cannot reach Ollama: {ex.Message}\n  Ensure Ollama is running at {endpoint}");
        }
        ConsoleHelpers.Ok();

        builder.AddOllamaChatCompletion(modelId, endpoint);
        return modelId;
    }

    private static string SetupOpenAI(out IKernelBuilder builder, IConfiguration config, string? overrideModel)
    {
        string? apiKey = config["OpenAI:ApiKey"];
        string modelId = overrideModel ?? config["OpenAI:ModelId"] ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(apiKey))
            ConsoleHelpers.Fail("OpenAI API key not configured.\n  Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"sk-...\"");

        builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId, apiKey!);
        return modelId;
    }

    private static async Task<string> SetupOpenAIAsync(IKernelBuilder builder, IConfiguration config)
    {
        string? apiKey = config["OpenAI:ApiKey"];
        string modelId = config["OpenAI:ModelId"] ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(apiKey))
            ConsoleHelpers.Fail("OpenAI API key not configured.\n  Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"sk-...\"");

        Console.Write("Checking OpenAI... ");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        try
        {
            var response = await http.GetAsync("https://api.openai.com/v1/models");
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode is 401 or 403)
                    ConsoleHelpers.Fail($"OpenAI returned HTTP {(int)response.StatusCode}. Check your API key.");
                else
                    ConsoleHelpers.Warn($"OpenAI health check returned HTTP {(int)response.StatusCode} — proceeding anyway.");
            }
        }
        catch (Exception ex)
        {
            ConsoleHelpers.Fail($"Cannot reach OpenAI: {ex.Message}\n  Check your network and API key.");
        }
        ConsoleHelpers.Ok();

        builder.AddOpenAIChatCompletion(modelId, apiKey!);
        return modelId;
    }

    private static string SetupAzureOpenAI(out IKernelBuilder builder, IConfiguration config, string? overrideModel)
    {
        string? deploymentName = overrideModel ?? config["Azure:Deployment"];
        string? endpoint = config["Azure:Endpoint"];
        string? apiKey = config["Azure:ApiKey"];

        if (string.IsNullOrWhiteSpace(deploymentName))
            ConsoleHelpers.Fail("Azure deployment name not configured.\n  Set it with: dotnet user-secrets set \"Azure:Deployment\" \"gpt-4o\"");
        if (string.IsNullOrWhiteSpace(endpoint))
            ConsoleHelpers.Fail("Azure endpoint not configured.\n  Set it with: dotnet user-secrets set \"Azure:Endpoint\" \"https://your-resource.openai.azure.com/\"");
        if (string.IsNullOrWhiteSpace(apiKey))
            ConsoleHelpers.Fail("Azure API key not configured.\n  Set it with: dotnet user-secrets set \"Azure:ApiKey\" \"your-key\"");

        builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(deploymentName!, endpoint!, apiKey!);
        return deploymentName!;
    }

    private static async Task<string> SetupAzureOpenAIAsync(IKernelBuilder builder, IConfiguration config)
    {
        string? deploymentName = config["Azure:Deployment"];
        string? endpoint = config["Azure:Endpoint"];
        string? apiKey = config["Azure:ApiKey"];

        if (string.IsNullOrWhiteSpace(deploymentName))
            ConsoleHelpers.Fail("Azure deployment name not configured.\n  Set it with: dotnet user-secrets set \"Azure:Deployment\" \"gpt-4o\"");
        if (string.IsNullOrWhiteSpace(endpoint))
            ConsoleHelpers.Fail("Azure endpoint not configured.\n  Set it with: dotnet user-secrets set \"Azure:Endpoint\" \"https://your-resource.openai.azure.com/\"");
        if (string.IsNullOrWhiteSpace(apiKey))
            ConsoleHelpers.Fail("Azure API key not configured.\n  Set it with: dotnet user-secrets set \"Azure:ApiKey\" \"your-key\"");

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
                    ConsoleHelpers.Fail($"Azure OpenAI returned HTTP {(int)response.StatusCode}. Check your API key.");
                else
                    ConsoleHelpers.Warn($"Azure OpenAI health check returned HTTP {(int)response.StatusCode} — proceeding anyway.");
            }
        }
        catch (Exception ex)
        {
            ConsoleHelpers.Fail($"Cannot reach Azure OpenAI: {ex.Message}\n  Check your endpoint: {endpoint}");
        }
        ConsoleHelpers.Ok();

        builder.AddAzureOpenAIChatCompletion(deploymentName!, endpoint!, apiKey!);
        return deploymentName!;
    }
}
