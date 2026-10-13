namespace OpenOd.Core;

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write(ConsoleColor.Gray, message);
    public static void Warn(string message) => Write(ConsoleColor.Yellow, message);
    public static void Error(string message) => Write(ConsoleColor.Red, message);

    private static void Write(ConsoleColor color, string message)
    {
        lock (Gate)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            Console.ResetColor();
        }
    }
}
