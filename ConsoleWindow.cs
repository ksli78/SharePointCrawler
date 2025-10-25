using System;
using System.Collections.Generic;

namespace SharePointCrawler;

/// <summary>
/// Provides a simple dashboard abstraction used by <see cref="SharePointClient"/> to
/// report progress to the <see cref="MainForm"/>.  This replaces the original
/// console-based dashboard with one that updates Windows Forms controls.
/// </summary>
public static class ConsoleWindow
{
    private static readonly List<(string Text, ConsoleColor Color)> _currentLines = new();
    private static readonly List<(string Text, ConsoleColor Color)> _previousLines = new();
    private static int _processedCount;
    private static TimeSpan _totalTime = TimeSpan.Zero;
    private static MainForm? _form;

    /// <summary>
    /// Initializes the dashboard and sets the total number of documents for the
    /// progress bar.
    /// </summary>
    public static void Initialize(MainForm form, int totalDocuments)
    {
        _form = form;
        _processedCount = 0;
        _totalTime = TimeSpan.Zero;
        _currentLines.Clear();
        _previousLines.Clear();
        _form.SetProgressMaximum(totalDocuments);
        _form.UpdateMetrics(0, TimeSpan.Zero);
    }

    /// <summary>
    /// Starts a new document in the current pane.
    /// </summary>
    public static void StartDocument(DocumentInfo doc, DateTime start)
    {
        _currentLines.Clear();
        Info($"Document: {doc.Name}");
        Info($"URL: {doc.Url}");
        Info($"Started: {start:T}");
    }

    /// <summary>
    /// Finalizes the current document, moves it to the previous pane and updates
    /// metrics and progress.
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
        _form?.UpdateMetrics(_processedCount, _totalTime);
        _form?.UpdateProgress(_processedCount);
    }

    /// <summary>
    /// Writes an informational message to the current pane.
    /// </summary>
    public static void Info(string message) => AddLine(_currentLines, message, ConsoleColor.Green);

    /// <summary>
    /// Writes a success message to the current pane.
    /// </summary>
    public static void Success(string message) => AddLine(_currentLines, message, ConsoleColor.Green);

    /// <summary>
    /// Writes an error message to the current pane.
    /// </summary>
    public static void Error(string message) => AddLine(_currentLines, message, ConsoleColor.Red);

    private static void AddLine(List<(string Text, ConsoleColor Color)> lines, string message, ConsoleColor color)
    {
        lines.Add((message, color));
        if (ReferenceEquals(lines, _currentLines))
            _form?.UpdateCurrentPane(lines);
        else
            _form?.UpdatePreviousPane(lines);
    }
}
