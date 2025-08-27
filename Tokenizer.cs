using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SharePointCrawler;

/// <summary>
/// Provides deterministic token counting and splitting utilities used by the
/// crawler to chunk documents for embedding.  The implementation avoids any
/// external model dependencies by approximating tokens using a simple
/// whitespace and punctuation based splitter.
/// </summary>
public static class Tokenizer
{
    private static readonly Regex TokenRegex = new(@"\w+|[^\w\s]", RegexOptions.Compiled);

    private record TokenSpan(int Start, int Length);

    private static List<TokenSpan> TokenizeInternal(string text)
    {
        var matches = TokenRegex.Matches(text);
        var spans = new List<TokenSpan>(matches.Count);
        foreach (Match m in matches)
        {
            spans.Add(new TokenSpan(m.Index, m.Length));
        }
        return spans;
    }

    /// <summary>
    /// Counts the approximate number of tokens in the supplied text.
    /// </summary>
    public static int CountTokens(string text) => TokenizeInternal(text).Count;

    /// <summary>
    /// Splits the text into windows based on a target token count and overlap.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <param name="target">Approximate tokens per chunk.</param>
    /// <param name="overlap">Number of overlapping tokens between chunks.</param>
    /// <returns>A sequence of text slices preserving character boundaries.</returns>
    public static IEnumerable<(int Start, int Length, string Text)> SmartSplitByTokens(
        string text, int target = 350, int overlap = 80)
    {
        var spans = TokenizeInternal(text);
        if (spans.Count == 0)
            yield break;

        var index = 0;
        while (index < spans.Count)
        {
            var end = Math.Min(index + target, spans.Count);
            var startChar = spans[index].Start;
            var endSpan = spans[end - 1];
            var endChar = endSpan.Start + endSpan.Length;
            yield return (startChar, endChar - startChar, text.Substring(startChar, endChar - startChar));

            if (end == spans.Count)
                break;
            index = Math.Max(end - overlap, index + 1);
        }
    }
}

