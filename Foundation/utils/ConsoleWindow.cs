using System;
using System.Collections.Generic;
using SharePointCrawler.Foundation.Models;

namespace SharePointCrawler.Foundation.utils;

/// <summary>
/// Renders a simple dashboard using two bordered console windows.  The top
/// window displays information about the document currently being processed
/// while the bottom window shows details about the previously processed
/// document.  A running count of processed documents, the average
/// processing time, and an estimated time to completion are shown
/// beneath the windows.
/// </summary>
public static class ConsoleWindow
{
    private const int DesiredWidth = 150;
    private const int PaneHeight = 10;
    private const int HeaderHeight = 1;
    private static int Width => Math.Min(Console.BufferWidth, DesiredWidth);

    private static readonly List<(string Text, ConsoleColor Color)> _currentLines = new();
    private static readonly List<(string Text, ConsoleColor Color)> _previousLines = new();

    private static string _currentFolder = string.Empty;
    private static string _currentFile = string.Empty;

    private static int _processedCount;
    private static TimeSpan _totalTime = TimeSpan.Zero;
    private static int _totalDocuments;

    /// <summary>
    /// Clears the console and draws the bordered windows.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (Console.BufferWidth < DesiredWidth)
                    Console.BufferWidth = DesiredWidth;
                if (Console.WindowWidth < DesiredWidth)
                    Console.WindowWidth = DesiredWidth;
            }
        }
        catch
        {
            // Some environments don't allow resizing; ignore any errors.
        }

        Console.Clear();
        DrawStatus();
        DrawPaneBorder(HeaderHeight);
        DrawPaneBorder(HeaderHeight + PaneHeight);
        DrawMetrics();
    }

    /// <summary>
    /// Starts a new document in the current window.
    /// </summary>
    public static void StartDocument(DocumentInfo doc, DateTime start)
    {
        _currentLines.Clear();
        RedrawPane(_currentLines, HeaderHeight);
        Info($"Document: {doc.Name}");
        Info($"URL: {doc.Url}");
        Info($"Started: {start:T}");
    }

    /// <summary>
    /// Finalizes the current document, moves it to the previous window and
    /// updates overall metrics.
    /// </summary>
    public static void CompleteDocument(DocumentInfo doc, TimeSpan elapsed, bool success = true, string? errorMessage = null)
    {
        _previousLines.Clear();
        _previousLines.AddRange(_currentLines);
        var msg = success ? $"Completed in {elapsed.TotalSeconds:F1}s" : $"Error: {errorMessage} (elapsed {elapsed.TotalSeconds:F1}s)";
        var color = success ? ConsoleColor.Green : ConsoleColor.Red;
        AddLine(_previousLines, msg, color);

        _processedCount++;
        _totalTime += elapsed;
        DrawMetrics();
    }

    /// <summary>
    /// Sets the total number of documents to be processed.
    /// </summary>
    public static void SetTotalDocuments(int total)
    {
        _totalDocuments = total;
        DrawMetrics();
    }

    /// <summary>
    /// Writes an informational message to the current window.
    /// </summary>
    public static void Info(string message) => AddLine(_currentLines, message, ConsoleColor.White);

    /// <summary>
    /// Writes a success message to the current window.
    /// </summary>
    public static void Success(string message) => AddLine(_currentLines, message, ConsoleColor.Green);

    /// <summary>
    /// Writes an error message to the current window.
    /// </summary>
    public static void Error(string message) => AddLine(_currentLines, message, ConsoleColor.Red);

    private static void AddLine(List<(string Text, ConsoleColor Color)> lines, string message, ConsoleColor color)
    {
        lines.Add((message, color));
        if (lines.Count > PaneHeight - 2)
            lines.RemoveAt(0);
        var top = HeaderHeight + (ReferenceEquals(lines, _currentLines) ? 0 : PaneHeight);
        RedrawPane(lines, top);
    }

    private static void DrawPaneBorder(int top)
    {
        var horizontal = new string('-', Width - 2);
        Console.SetCursorPosition(0, top);
        Console.Write('+' + horizontal + '+');
        for (int i = 1; i < PaneHeight - 1; i++)
        {
            Console.SetCursorPosition(0, top + i);
            Console.Write('|' + new string(' ', Width - 2) + '|');
        }
        Console.SetCursorPosition(0, top + PaneHeight - 1);
        Console.Write('+' + horizontal + '+');
    }

    private static void RedrawPane(List<(string Text, ConsoleColor Color)> lines, int top)
    {
        for (int i = 0; i < PaneHeight - 2; i++)
        {
            Console.SetCursorPosition(1, top + i + 1);
            Console.Write(new string(' ', Width - 2));
        }

        int start = Math.Max(0, lines.Count - (PaneHeight - 2));
        for (int i = 0; i < Math.Min(lines.Count, PaneHeight - 2); i++)
        {
            var line = lines[start + i];
            Console.SetCursorPosition(1, top + i + 1);
            Console.ForegroundColor = line.Color;
            var text = line.Text.Length > Width - 2 ? line.Text[..(Width - 2)] : line.Text;
            Console.Write(text.PadRight(Width - 2));
            Console.ForegroundColor = ConsoleColor.White;
        }
    }

    private static void DrawMetrics()
    {
        var avgSeconds = _processedCount > 0 ? _totalTime.TotalSeconds / _processedCount : 0;
        var avgMinutes = avgSeconds / 60.0;
        var remainingDocs = Math.Max(0, _totalDocuments - _processedCount);
        string eta = _processedCount > 0
            ? TimeSpan.FromSeconds(avgSeconds * remainingDocs).ToString(@"hh\:mm\:ss")
            : "N/A";
        Console.SetCursorPosition(0, HeaderHeight + PaneHeight * 2);
        var msg = $"Processed: {_processedCount}/{_totalDocuments}  Avg Time: {avgSeconds:F1}s ({avgMinutes:F1}m)  ETA: {eta}";
        Console.Write(msg.PadRight(Width));
        double progress = _totalDocuments > 0 ? (double)_processedCount / _totalDocuments : 0;
        int barWidth = Math.Min(Width - 20, 40);
        int filled = (int)(barWidth * progress);
        var bar = "[" + new string('#', filled) + new string('-', barWidth - filled) + $"] {progress * 100:F0}%";
        Console.SetCursorPosition(0, HeaderHeight + PaneHeight * 2 + 1);
        Console.Write(bar.PadRight(Width));
    }

    /// <summary>
    /// Updates the status line with the current folder and file.
    /// </summary>
    public static void SetStatus(string folder, string file)
    {
        _currentFolder = folder;
        _currentFile = file;
        DrawStatus();
    }

    private static void DrawStatus()
    {
        Console.SetCursorPosition(0, 0);
        var line = $"Folder: {_currentFolder}  File: {_currentFile}";
        Console.Write(line.Length > Width ? line[..Width] : line.PadRight(Width));
    }
}
