using System;
using System.Collections.Generic;

namespace SharePointCrawler;

/// <summary>
/// Provides a simple faux window in the console for displaying status
/// messages with basic color formatting. The window maintains a bordered
/// region and prints informational (white), success (green) and error
/// (red) messages. Earlier lines scroll off as new messages are added.
/// </summary>
public static class ConsoleWindow
{
    private static readonly List<(string Text, ConsoleColor Color)> _lines = new();
    private const int Width = 100;
    private const int Height = 20;

    /// <summary>
    /// Initializes the window by clearing the console and drawing the border.
    /// </summary>
    public static void Initialize()
    {
        Console.Clear();
        DrawBorder();
    }

    /// <summary>
    /// Clears existing lines and starts a new document section.
    /// </summary>
    public static void NewDocument(DocumentInfo doc, DateTime start)
    {
        _lines.Clear();
        Info($"Document: {doc.Name}");
        Info($"URL: {doc.Url}");
        Info($"Started: {start:T}");
    }

    /// <summary>
    /// Writes an informational message in white.
    /// </summary>
    public static void Info(string message) => AddLine(message, ConsoleColor.White);

    /// <summary>
    /// Writes a success message in green.
    /// </summary>
    public static void Success(string message) => AddLine(message, ConsoleColor.Green);

    /// <summary>
    /// Writes an error message in red.
    /// </summary>
    public static void Error(string message) => AddLine(message, ConsoleColor.Red);

    private static void AddLine(string message, ConsoleColor color)
    {
        _lines.Add((message, color));
        if (_lines.Count > Height - 2)
            _lines.RemoveAt(0);
        Redraw();
    }

    private static void DrawBorder()
    {
        var horizontal = new string('-', Width - 2);
        Console.SetCursorPosition(0, 0);
        Console.Write('+' + horizontal + '+');
        for (int i = 1; i < Height - 1; i++)
        {
            Console.SetCursorPosition(0, i);
            Console.Write('|' + new string(' ', Width - 2) + '|');
        }
        Console.SetCursorPosition(0, Height - 1);
        Console.Write('+' + horizontal + '+');
    }

    private static void Redraw()
    {
        for (int i = 0; i < Height - 2; i++)
        {
            Console.SetCursorPosition(1, i + 1);
            Console.Write(new string(' ', Width - 2));
        }

        int start = Math.Max(0, _lines.Count - (Height - 2));
        for (int i = 0; i < Math.Min(_lines.Count, Height - 2); i++)
        {
            var line = _lines[start + i];
            Console.SetCursorPosition(1, i + 1);
            Console.ForegroundColor = line.Color;
            var text = line.Text.Length > Width - 2 ? line.Text[..(Width - 2)] : line.Text;
            Console.Write(text.PadRight(Width - 2));
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}

