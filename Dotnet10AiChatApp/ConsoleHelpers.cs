using Microsoft.SemanticKernel.ChatCompletion;

namespace Dotnet10AiChatApp;

public static class ConsoleHelpers
{
    public static void Ok()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓");
        Console.ResetColor();
    }

    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  ⚠  {message}");
        Console.ResetColor();
    }

    public static void Fail(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("✗");
        Console.WriteLine($"Error: {message}");
        Console.ResetColor();
        Environment.Exit(1);
    }

    public static void PrintHistory(ChatHistory chatHistory)
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
}
