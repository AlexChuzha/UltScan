using System;
using System.IO;

namespace UltScan;

public static class TranslationLogger
{
    private static readonly object Sync = new();
    private static string? _currentLogPath;

    public static string LogsFolder => Path.Combine(AppContext.BaseDirectory, "Logs");

    public static void LogPair(string original, string translated)
    {
        if (System.Windows.Application.Current is not App app ||
            !app.Settings.ExperimentalTranslationLogging)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(original) && string.IsNullOrWhiteSpace(translated))
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var entry =
            $"[{timestamp}]{Environment.NewLine}" +
            "OCR:" + Environment.NewLine +
            original + Environment.NewLine +
            "Translation:" + Environment.NewLine +
            translated + Environment.NewLine +
            "---" + Environment.NewLine;

        lock (Sync)
        {
            if (_currentLogPath == null)
            {
                Directory.CreateDirectory(LogsFolder);
                var fileName = $"translation_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                _currentLogPath = Path.Combine(LogsFolder, fileName);
            }

            File.AppendAllText(_currentLogPath, entry);
        }
    }
}
