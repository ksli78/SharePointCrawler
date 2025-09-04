using System;
using System.IO;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SharePointCrawler.Foundation.services;

/// <summary>
/// Writes document processing errors to a log file located next to the
/// running executable.
/// </summary>
public static class ErrorLogger
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "error.log");
    private static readonly string ErrorListFile= Path.Combine(AppContext.BaseDirectory, "retryList.txt");
    /// <summary>
    /// Appends an error entry for the specified document.
    /// </summary>
    public static void Log(string name, string url, string message)
    {
        try
        {
            var line = $"{DateTime.Now:u}\t{name}\t{url}\t{message}";
            File.AppendAllLines(LogPath, new[] { line });
        }
        catch
        {
            // ignore logging failures
        }
    }
    public static void AppedToRetryList(string docTitle)
    {
        try
        {
            File.AppendAllLines(LogPath, new[] { docTitle });
        }
        catch
        {
            // ignore logging failures
        }
    }
}

