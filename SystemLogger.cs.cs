public static class SystemLogger
{
    private static readonly string logFilePath = "AutomationExecution.log";
    private static readonly object lockObj = new object();

    public enum LogLevel { INFO, WARNING, ERROR, DEBUG }

    public static void Log(string message, LogLevel level = LogLevel.INFO)
    {
        string formattedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        
        lock (lockObj)
        {
            File.AppendAllText(logFilePath, formattedMessage + Environment.NewLine);
            Console.WriteLine(formattedMessage);
        }
    }
}