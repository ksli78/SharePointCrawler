using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

public sealed class PdfToMarkdownOptions
{
    public double LineYTolerance { get; set; } = 2.0;
    public double RepeatLineRemovalThreshold { get; set; } = 0.6;

    // Hard noise (always drop) 
    public List<string> NoiseStartsWith { get; } = new()
{
    "This document contains proprietary information",
    "Unauthorized use",                    // catches the start of the banner, even if broken across lines
    "Uncontrolled if printed",
    "Before using this document, the reader is responsible",
    "Copyright",
    "All rights reserved",
    "use, reproduction, or distribution",  // catches the second half of the banner if it starts a line
    // Additional corporate banners and CUI/Privacy warnings commonly present in SOP headers
    "CUI",
    "Controlled Unclassified",
    "Privacy Act",
    "Sensitive but unclassified"
};

    public List<Regex> NoisePatterns { get; } = new()
{
    // Existing patterns for pages, CLG codes, revisions, and banners
    new Regex(@"(?i)^\s*Page\s*:\s*\d+\s*of\s*\d+\s*$"),
    new Regex(@"(?i)^CLG\-[A-Z\-]+\d+(\s*Page\s*\d+)?$"),
    new Regex(@"(?i)^\s*Revision\s*:\s*[A-Za-z0-9]+\s*$"),
    new Regex(@"(?i)\b(CUI|Controlled\s+Unclassified|Privacy\s+Act|Sensitive\s+but\s+unclassified)\b"),
    new Regex(@"(?i)\bproprietary information\b"),
    new Regex(@"(?i)\bUnauthorized\s+use\b"),
    new Regex(@"(?i)\buse\s*,\s*reproduction\s*,\s*or\s*distribution\b"),
    new Regex(@"(?i)\breproduction\s*,\s*or\s*distribution\b"),
    new Regex(@"(?i)\buncontrolled if printed\b"),
    new Regex(@"(?i)\bAll rights reserved\b"),

    // NEW: drop any isolated “use, or” fragment
    new Regex(@"(?i)^\s*use\s*,?\s*or\s*$")
};


    // Numbered headings like "1.0 Purpose"
    public Regex NumberedHeading { get; set; } =
        new Regex(@"^(?<num>\d+(?:\.\d+)*)(?:\s+|\s*-\s*)(?<title>.+)$");

    // Keys in the banner grid
    public Regex HeaderKey { get; set; } =
        new Regex(@"(?i)^(Document\s*No\.?|Effective\s*Date|Revision|Accountable\s*Organization|Management\s*Approval|Source|Page)\s*:\s*$");

    public Regex HeaderKeyValue { get; set; } =
        new Regex(@"^(?<key>Document\s*No\.?|Effective\s*Date|Revision|Accountable\s*Organization|Management\s*Approval|Source|Page)\s*:\s*(?<val>.+)$", RegexOptions.IgnoreCase);

    // “SOP” banner tokens used to locate the title
    public string BannerTop = "Management System";
    public string BannerMid = "Standard Operating Procedure";

    // “Process table” trigger tokens — we only table-ize when we see these together
    public string[] ProcessTableHeaderTokens = { "Step", "Responsibility", "Action" };

    // Table columnization tuning
    public int TableLookaheadLines { get; set; } = 8;
    public double LargeGapThreshold { get; set; } = 22.0;          // pts gap between word boxes to consider a column break
    public double ColumnAnchorMergeTolerance { get; set; } = 16.0;  // merge near anchors
}

public sealed class PdfToMarkdownConverter
{
    private readonly PdfToMarkdownOptions _opt;
    public PdfToMarkdownConverter(PdfToMarkdownOptions? options = null) => _opt = options ?? new PdfToMarkdownOptions();

    // ---------- Public APIs ----------
    public string ConvertToMarkdown(string pdfPath)
    {
        using var pdf = PdfDocument.Open(pdfPath);
        var name = Path.GetFileNameWithoutExtension(pdfPath);
        return ConvertToMarkdownCore(pdf, name);
    }

    public string ConvertToMarkdown(byte[] pdfBytes, string? logicalName = null)
    {
        using var ms = new MemoryStream(pdfBytes, writable: false);
        using var pdf = PdfDocument.Open(ms);
        return ConvertToMarkdownCore(pdf, logicalName ?? "document");
    }

    public string ConvertToMarkdown(Stream pdfStream, string? logicalName = null)
    {
        if (!pdfStream.CanSeek)
        {
            using var copy = new MemoryStream();
            pdfStream.CopyTo(copy);
            copy.Position = 0;
            using var pdf = PdfDocument.Open(copy);
            return ConvertToMarkdownCore(pdf, logicalName ?? "document");
        }
        else
        {
            var pos = pdfStream.Position;
            using var pdf = PdfDocument.Open(pdfStream);
            var md = ConvertToMarkdownCore(pdf, logicalName ?? "document");
            pdfStream.Position = pos;
            return md;
        }
    }

    // ---------- Core ----------
    private string ConvertToMarkdownCore(PdfDocument pdf, string logicalName)
    {
        // Build per‑page line structures
        var allPages = new List<PageLines>();
        foreach (var pg in pdf.GetPages())
            allPages.Add(BuildLines(pg, _opt.LineYTolerance));

        // Identify repeating header/footer lines to drop
        var repeatSet = DetectRepeatingLines(allPages, _opt.RepeatLineRemovalThreshold);

        // Parse the header and title from the first page
        var (header, headerLineTexts, title) = ParseHeaderAndTitle(allPages.FirstOrDefault());

        // Start the Markdown with the document title (no metadata table)
        var md = new StringBuilder();
        var h1 = title ?? header.GetValueOrDefault("doc") ?? logicalName;
        md.AppendLine("# " + EscapeMd(h1));
        md.AppendLine();

        bool inProcessSection = false;
        var paraBuf = new StringBuilder();

        // Helper to emit any accumulated paragraph text
        void FlushParagraph()
        {
            if (paraBuf.Length > 0)
            {
                md.AppendLine(paraBuf.ToString());
                md.AppendLine();
                paraBuf.Clear();
            }
        }

        // Decide whether to merge the current line with the next one
        bool ShouldMerge(string curr, string? next)
        {
            if (string.IsNullOrWhiteSpace(curr) || string.IsNullOrWhiteSpace(next)) return false;
            string c = curr.TrimEnd();
            string n = next.TrimStart();
            // Break paragraphs on headings or list/outline indicators
            // If the next line begins with a number (e.g. "6.1"), a letter + '.' or ')',
            // a bullet character or a capitalized word, treat it as a new list item or section.
            if (Regex.IsMatch(n, @"^(\d+(?:\.\d+)*\b|[A-Za-z]\.|[A-Za-z]\)|[-\u2022])")) return false;
            // If current line ends with sentence punctuation, do not merge
            if (Regex.IsMatch(c, @"[\.!\?:;]$")) return false;
            // Merge only if the next line starts with a lower case letter
            char first = n[0];
            if (char.IsLower(first)) return true;
            return false;
        }

        for (int pi = 0; pi < allPages.Count; pi++)
        {
            var body = allPages[pi].Lines
                .Where(l => !repeatSet.Contains(l.Text.Trim()))
                .Where(l => !IsNoise(l.Text))
                .Where(l => !(pi == 0 && headerLineTexts.Contains(l.Text.Trim())))
                .ToList();

            // Remove duplicate title on the first page
            if (pi == 0 && title is not null)
                body.RemoveAll(l => string.Equals(l.Text.Trim(), title, StringComparison.OrdinalIgnoreCase));

            int i = 0;
            while (i < body.Count)
            {
                var raw = body[i];
                var text = raw.Text.Trim();

                // 1) Numbered heading?
                var mh = _opt.NumberedHeading.Match(text);
                if (mh.Success)
                {
                    FlushParagraph();
                    var num = mh.Groups["num"].Value;
                    var ttl = mh.Groups["title"].Value.Trim();
                    var level = Math.Min(6, 2 + num.Count(c => c == '.'));
                    md.AppendLine($"{new string('#', level)} {EscapeMd($"{num} {ttl}")}");
                    md.AppendLine();
                    // Enable Process-table parsing only within section 6.x
                    inProcessSection = num.StartsWith("6");
                    i++;
                    continue;
                }

                // 2) Process table header?  Try to detect and extract as Markdown table within the Process section.
                if (inProcessSection && LooksLikeProcessTableHeader(raw))
                {
                    var anchors = ComputeColumnAnchors(body, i, _opt.TableLookaheadLines);
                    // Peek at the next 2 lines; require at least 2 filled columns in each
                    bool looksTabular = false;
                    if (anchors.Count >= 3)
                    {
                        int ok = 0;
                        for (int peek = i + 1; peek < Math.Min(body.Count, i + 3); peek++)
                        {
                            var filled = CountFilledColumns(body[peek], anchors);
                            if (filled >= 2) ok++;
                        }
                        looksTabular = ok >= 2; // header + at least two lines that look like rows
                    }
                    if (looksTabular)
                    {
                        FlushParagraph();
                        var (rows, consumed) = ExtractTableWithAnchors(body, i, anchors);
                        if (rows.Count >= 2)
                        {
                            md.AppendLine(RenderTable(rows));
                            md.AppendLine();
                            i += consumed;
                            continue;
                        }
                    }
                    // Otherwise: fall through and treat as normal text
                }

                // 3) Plain text lines: accumulate into paragraphs
                string escaped = EscapeMd(text);
                if (paraBuf.Length == 0)
                {
                    paraBuf.Append(escaped);
                }
                else
                {
                    // Add a space before concatenating to avoid run‑on words
                    paraBuf.Append(" " + escaped);
                }
                // Decide if we should merge with the next line or flush now
                string? nextText = null;
                if (i + 1 < body.Count) nextText = body[i + 1].Text;
                if (!ShouldMerge(text, nextText))
                {
                    FlushParagraph();
                }
                i++;
            }
            // NOTE: Do not flush at page boundaries here; paragraphs can continue onto the next page.
        }

        // Flush any residual paragraph after the last page
        FlushParagraph();

        return md.ToString().Trim() + Environment.NewLine;
    }


    // ---------- Layout ----------
    private sealed class WordBox
    {
        public string Text { get; init; } = "";
        public double Left { get; init; }
        public double Right { get; init; }
    }
    private sealed class LineEx
    {
        public double Y { get; init; }
        public string Text { get; set; } = "";
        public List<WordBox> Words { get; } = new();
    }
    private sealed class PageLines
    {
        public int PageNumber { get; }
        public double Height { get; }
        public List<LineEx> Lines { get; } = new();
        public PageLines(int number, double height) { PageNumber = number; Height = height; }
    }

    private static PageLines BuildLines(Page page, double yTol)
    {
        var words = page.GetWords();
        var grouped = words
            .GroupBy(w => RoundTo((w.BoundingBox.Top + w.BoundingBox.Bottom) / 2.0, yTol))
            .OrderByDescending(g => g.Key);

        var pl = new PageLines(page.Number, page.Height);
        foreach (var g in grouped)
        {
            var ln = new LineEx { Y = g.Key };
            foreach (var w in g.OrderBy(w => w.BoundingBox.Left))
            {
                var t = (w.Text ?? "").Trim();
                if (t.Length == 0) continue;
                ln.Words.Add(new WordBox { Text = t, Left = w.BoundingBox.Left, Right = w.BoundingBox.Right });
            }
            ln.Text = string.Join(" ", ln.Words.Select(x => x.Text));
            if (!string.IsNullOrWhiteSpace(ln.Text)) pl.Lines.Add(ln);
        }
        return pl;
    }

    private static double RoundTo(double value, double tol) => Math.Round(value / tol) * tol;

    // ---------- Repeating banners ----------
    private HashSet<string> DetectRepeatingLines(List<PageLines> pages, double threshold)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pages)
        {
            foreach (var t in p.Lines.Take(4).Select(l => l.Text.Trim()).Concat(p.Lines.TakeLast(4).Select(l => l.Text.Trim())))
            {
                if (t.Length == 0) continue;
                counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
            }
        }
        var minHits = (int)Math.Ceiling(Math.Max(1, pages.Count * threshold));
        return new HashSet<string>(counts.Where(kv => kv.Value >= minHits).Select(kv => kv.Key),
                                   StringComparer.OrdinalIgnoreCase);
    }

    // ---------- Noise ----------
    private bool IsNoise(string line)
    {
        var l = line.Trim();
        if (l.Length == 0) return false;

        foreach (var s in _opt.NoiseStartsWith)
            if (l.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var rx in _opt.NoisePatterns)
            if (rx.IsMatch(l)) return true;

        // page strings anywhere (“Page: 1 of 4”, “CLG-EN-PR-0175 Page: 2 of 4”)
        if (Regex.IsMatch(l, @"(?i)\bPage\s*:\s*\d+\s*of\s*\d+\b")) return true;

        return false;
    }


    private static string EscapeMd(string s)
        => s.Replace("|", "\\|").Replace("*", "\\*").Replace("_", "\\_").Trim();

    private static void WriteMetaRow(StringBuilder sb, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"| {EscapeMd(key)} | {EscapeMd(value!)} |");
    }





    private static (string left, string right) SplitByLargestGap(LineEx line)
    {
        if (line.Words.Count < 2) return (line.Text, "");
        int cut = -1; double best = double.MinValue;
        for (int i = 0; i < line.Words.Count - 1; i++)
        {
            var gap = line.Words[i + 1].Left - line.Words[i].Right;
            if (gap > best) { best = gap; cut = i; }
        }
        var left = string.Join(" ", line.Words.Take(cut + 1).Select(w => w.Text));
        var right = string.Join(" ", line.Words.Skip(cut + 1).Select(w => w.Text));
        return (left.Trim(), right.Trim());
    }

    private void AssignHeader(Dictionary<string, string> dict, string keyRaw, string valRaw)
    {
        var key = keyRaw.Trim().TrimEnd(':').ToLowerInvariant();
        var val = valRaw.Trim();

        switch (key)
        {
            case "document no.":
            case "document no":
                if (Regex.IsMatch(val, @"^[A-Z0-9\-]+$")) dict["doc"] = val;
                break;
            case "effective date":
                if (Regex.IsMatch(val, @"^\d{2}/\d{2}/\d{4}$")) dict["eff"] = val;
                break;
            case "revision":
                if (Regex.IsMatch(val, @"^[A-Za-z0-9]+$")) dict["rev"] = val;
                break;
            case "accountable organization":
                dict["org"] = val;
                break;
            case "management approval":
                dict["appr"] = val;
                break;
            case "source":
                dict["src"] = val;
                break;
            case "page": /*ignore*/ break;
        }
    }


    // Robust header + title parsing for your SOP layout.
    // - Title = pure-word lines BETWEEN the "Document No./Page" value row and the "Effective Date" key row.
    // - Keys like "Document No.: Page:" are treated as a multi-key row, not "Key: value".
    // - Values for "Accountable Organization" and "Management Approval" can share one line; we split a trailing name.
    private (Dictionary<string, string> header, HashSet<string> headerLines, string? title)
        ParseHeaderAndTitle(PageLines? first)
    {
        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var headerLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? title = null;
        if (first == null) return (header, headerLines, title);

        // Work on a generous top window (banner + grid)
        var topObjs = first.Lines.Take(Math.Min(50, Math.Max(25, first.Lines.Count))).ToList();
        var lines = topObjs.Select(l => l.Text.Trim()).ToList();

        // --- helpers
        // Titles often contain mixed case words, numbers and punctuation such as hyphens, commas or parentheses.
        // Allow a broader character set so that titles like "Phase 2 – Facility Start‑Up" or "Part 6.0/6.1" are captured.
        static bool PureWords(string s) => Regex.IsMatch(s, @"^[A-Za-z0-9][A-Za-z0-9 \-,()/:]{0,120}$");
        static bool IsKeyLine(string s) => Regex.IsMatch(s, @"(?i)^(Document\s*No\.?|Effective\s*Date|Revision|Accountable\s*Organization|Management\s*Approval|Source|Page)\s*:");
        static string StripColon(string s) => Regex.Replace(s, @"\s*:\s*$", "");

        int IndexOfKey(string key) => lines.FindIndex(s => Regex.IsMatch(s, $@"(?i)^{Regex.Escape(key)}\s*:"));

        // Find the key rows (by label)
        int docKeyIdx = IndexOfKey("Document No.");
        int pageKeyIdx = IndexOfKey("Page");
        int effKeyIdx = IndexOfKey("Effective Date");
        int revKeyIdx = IndexOfKey("Revision");
        int orgKeyIdx = IndexOfKey("Accountable Organization");
        int apprKeyIdx = IndexOfKey("Management Approval");

        // Find "Standard Operating Procedure" fragments and drop them from body later
        for (int i = 0; i < Math.Min(lines.Count, 15); i++)
        {
            var win = string.Join(" ", lines.Skip(i).Take(3));
            if (Regex.IsMatch(win, @"(?i)\bstandard\s+operating\s+procedure\b"))
            {
                for (int j = i; j < Math.Min(lines.Count, i + 3); j++) headerLines.Add(lines[j]);
                break;
            }
        }

        // Normalize "Key:\nValue" → "Key: Value" in memory; remember value-only lines to drop
        var keyOnly = new Regex(@"(?i)^(Document\s*No\.?|Effective\s*Date|Revision|Accountable\s*Organization|Management\s*Approval|Source|Page)\s*:\s*$");
        var keyVal = new Regex(@"^(?<k>Document\s*No\.?|Effective\s*Date|Revision|Accountable\s*Organization|Management\s*Approval|Source|Page)\s*:\s*(?<v>.+)$", RegexOptions.IgnoreCase);
        var rawValuesToDrop = new List<string>();

        for (int i = 0; i < lines.Count - 1; i++)
        {
            if (keyOnly.IsMatch(lines[i]))
            {
                var label = StripColon(lines[i]);
                lines[i] = $"{label}: {lines[i + 1]}";
                rawValuesToDrop.Add(lines[i + 1]);
                lines.RemoveAt(i + 1);
                i--;
            }
        }

        // IMPORTANT: If a "Key: value" line's value itself LOOKS like a key (e.g., "Document No.: Page:"),
        // we must NOT treat it as "Key: value". We'll skip those here.
        bool ValueLooksLikeKey(string v) => Regex.IsMatch(v, @"(?i)^(Document|Effective|Revision|Accountable|Management|Source|Page)\s*:\s*$");

        // Locate the *value row* for Document No./Page (needed to bound the title block).
        int docValueIdx = -1;
        if (docKeyIdx >= 0)
        {
            for (int j = docKeyIdx + 1; j < Math.Min(lines.Count, docKeyIdx + 8); j++)
            {
                var v = lines[j];
                if (string.IsNullOrWhiteSpace(v)) continue;
                if (IsKeyLine(v)) break;
                docValueIdx = j;
                break;
            }
        }

        // -------- TITLE: pure-word lines BETWEEN docValueIdx and the Effective Date key row --------
        if (docValueIdx >= 0 && effKeyIdx > docValueIdx)
        {
            var parts = new List<string>();
            for (int j = docValueIdx + 1; j < effKeyIdx; j++)
            {
                var cand = lines[j];
                if (string.IsNullOrWhiteSpace(cand)) continue;
                if (IsKeyLine(cand)) break;
                if (!PureWords(cand)) continue;
                parts.Add(cand);
                headerLines.Add(cand); // remove from body
            }
            if (parts.Count > 0)
                title = string.Join(" ", parts); // e.g., "Fitness Center Access"
        }

        // Utility: find the next non-key, non-empty value line after a key (skip title lines)
        string? FindValueRowAfter(int keyIndex, int maxLookahead = 10)
        {
            for (int j = keyIndex + 1; j < Math.Min(lines.Count, keyIndex + 1 + maxLookahead); j++)
            {
                var v = lines[j];
                if (string.IsNullOrWhiteSpace(v)) continue;
                if (IsKeyLine(v)) break;               // next grid row begins
                if (headerLines.Contains(v)) continue; // skip title lines
                return v;
            }
            return null;
        }

        // Split combined value rows ("06/08/2023 G", "CLG-EN-PR-0175 1 of 4") into (left,right)
        static (string left, string right) SplitComboValue(string raw)
        {
            var bigGap = Regex.Split(raw, @"\s{2,}").Where(s => s.Trim().Length > 0).ToArray();
            if (bigGap.Length >= 2) return (bigGap[0].Trim(), bigGap[1].Trim());

            if (Regex.IsMatch(raw, @"^\d{2}/\d{2}/\d{4}\s+[A-Za-z0-9]+$"))
            {
                var sp = Regex.Split(raw.Trim(), @"\s+"); return (sp[0], sp[1]);
            }
            var m = Regex.Match(raw, @"^(?<doc>[A-Z0-9\-]+)\s+(?<page>\d+\s+of\s+\d+)$", RegexOptions.IgnoreCase);
            if (m.Success) return (m.Groups["doc"].Value, m.Groups["page"].Value);

            int mid = raw.Length / 2;
            int idx = raw.LastIndexOf(' ', mid);
            if (idx <= 0) idx = raw.IndexOf(' ', mid);
            if (idx > 0) return (raw[..idx].Trim(), raw[(idx + 1)..].Trim());
            return (raw.Trim(), "");
        }

        void Mark(string s) { if (!string.IsNullOrWhiteSpace(s)) headerLines.Add(s.Trim()); }

        // ---- Document No. / Page (ignore Page in metadata)
        if (docKeyIdx >= 0)
        {
            var raw = FindValueRowAfter(docKeyIdx, 8);
            if (raw != null)
            {
                var (dv, pv) = SplitComboValue(raw);
                if (Regex.IsMatch(dv, @"^[A-Z0-9][A-Z0-9\-]{4,}$")) header["doc"] = dv;
                Mark(lines[docKeyIdx]); Mark(raw);
            }
        }

        // ---- Effective Date / Revision (handle combined "06/08/2023 G" or separate rows)
        if (effKeyIdx >= 0)
        {
            var raw = FindValueRowAfter(effKeyIdx, 8);
            if (raw != null)
            {
                var (dv, rv) = SplitComboValue(raw);
                if (Regex.IsMatch(dv, @"^\d{2}/\d{2}/\d{4}$")) header["eff"] = dv;
                if (Regex.IsMatch(rv, @"^[A-Za-z0-9]{1,3}$")) header["rev"] = rv;
                Mark(lines[effKeyIdx]); Mark(raw);
            }
        }
        // If Revision still missing, try its own row
        if (!header.ContainsKey("rev") && revKeyIdx >= 0)
        {
            var raw = FindValueRowAfter(revKeyIdx, 8);
            if (raw != null && Regex.IsMatch(raw.Trim(), @"^[A-Za-z0-9]{1,3}$"))
            {
                header["rev"] = raw.Trim(); Mark(lines[revKeyIdx]); Mark(raw);
            }
        }

        // ---- Accountable Organization / Management Approval
        if (orgKeyIdx >= 0)
        {
            var raw = FindValueRowAfter(orgKeyIdx, 8);
            if (raw != null)
            {
                string orgVal = raw.Trim();
                // If this line ALSO contains a trailing person name, split it off to Management Approval.
                var nameMatch = Regex.Match(orgVal, @"\s([A-Z][a-z]+(?:\s+[A-Z][a-z'.-]+)+)$");
                if (nameMatch.Success && !header.ContainsKey("appr") && apprKeyIdx >= 0)
                {
                    var name = nameMatch.Groups[1].Value.Trim();
                    header["appr"] = name;
                    orgVal = orgVal[..^name.Length].Trim();
                }
                header["org"] = orgVal;
                Mark(lines[orgKeyIdx]); Mark(raw);
            }
        }
        if (!header.ContainsKey("appr") && apprKeyIdx >= 0)
        {
            var raw = FindValueRowAfter(apprKeyIdx, 8);
            if (raw != null)
            {
                // name validator
                if (Regex.IsMatch(raw.Trim(), @"^[A-Za-z]+(?:\s+[A-Za-z'.-]+)+$"))
                    header["appr"] = raw.Trim();
                Mark(lines[apprKeyIdx]); Mark(raw);
            }
        }

        // Finally, parse any plain "Key: value" rows (but skip when value looks like a key)
        foreach (var l in lines)
        {
            var m = keyVal.Match(l);
            if (!m.Success) continue;
            var val = m.Groups["v"].Value.Trim();
            if (ValueLooksLikeKey(val)) continue; // e.g., "Document No.: Page:"
            var k = m.Groups["k"].Value.Trim().ToLowerInvariant();

            switch (k)
            {
                case "document no.":
                case "document no":
                    if (!header.ContainsKey("doc") && Regex.IsMatch(val, @"^[A-Z0-9][A-Z0-9\-]{4,}$")) header["doc"] = val; break;
                case "effective date":
                    if (!header.ContainsKey("eff") && Regex.IsMatch(val, @"^\d{2}/\d{2}/\d{4}$")) header["eff"] = val; break;
                case "revision":
                    if (!header.ContainsKey("rev") && Regex.IsMatch(val, @"^[A-Za-z0-9]{1,3}$")) header["rev"] = val; break;
                case "accountable organization":
                    if (!header.ContainsKey("org")) header["org"] = val; break;
                case "management approval":
                    if (!header.ContainsKey("appr") && Regex.IsMatch(val, @"^[A-Za-z]+(?:\s+[A-Za-z'.-]+)+$")) header["appr"] = val; break;
                case "source":
                    if (!header.ContainsKey("src")) header["src"] = val; break;
            }
            headerLines.Add(l);
        }

        // Always drop banner tokens + any raw value-only lines we folded earlier
        headerLines.Add("Management System");
        headerLines.Add("Standard Operating Procedure");
        foreach (var v in rawValuesToDrop) headerLines.Add(v);

        return (header, headerLines, title);
    }


    // ---------- Process table detection ----------
    private bool LooksLikeProcessTableHeader(LineEx line)
    {
        var t = line.Text;
        return _opt.ProcessTableHeaderTokens.All(tok =>
            t.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    private bool IsOrphanWord(List<LineEx> body, int idx)
    {
        var txt = body[idx].Text.Trim();
        if (!Regex.IsMatch(txt, @"^[A-Za-z]{1,12}$")) return false;
        bool prevIsHeading = idx > 0 && _opt.NumberedHeading.IsMatch(body[idx - 1].Text);
        bool nextIsHeading = idx + 1 < body.Count && _opt.NumberedHeading.IsMatch(body[idx + 1].Text);
        return prevIsHeading || nextIsHeading;
    }

    // Prefer anchors directly from the header tokens; fall back to gaps.
    // Keep exactly 3 columns for Step | Responsibility | Action.
    private List<double> ComputeColumnAnchors(List<LineEx> lines, int start, int lookahead)
    {
        var header = lines[start];
        var anchors = new List<double>();
        foreach (var w in header.Words)
        {
            if (string.Equals(w.Text, "Step", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(w.Text, "Responsibility", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(w.Text, "Action", StringComparison.OrdinalIgnoreCase))
            {
                anchors.Add(w.Left);
            }
        }
        anchors = anchors.Distinct().OrderBy(x => x).ToList();
        if (anchors.Count == 3) return anchors;

        // Fallback: gaps across several lines
        var pts = new List<double>();
        int end = Math.Min(lines.Count, start + lookahead);
        for (int i = start; i < end; i++)
        {
            var ws = lines[i].Words;
            if (ws.Count < 2) continue;
            pts.Add(ws[0].Left);
            for (int j = 0; j < ws.Count - 1; j++)
            {
                var gap = ws[j + 1].Left - ws[j].Right;
                if (gap >= _opt.LargeGapThreshold) pts.Add(ws[j + 1].Left);
            }
        }
        if (pts.Count == 0) return anchors;

        pts.Sort();
        var merged = new List<double> { pts[0] };
        foreach (var x in pts.Skip(1))
        {
            if (Math.Abs(x - merged[^1]) <= _opt.ColumnAnchorMergeTolerance)
                merged[^1] = (merged[^1] + x) / 2.0;
            else
                merged.Add(x);
        }
        return merged.Take(3).ToList(); // keep 3 columns max
    }


    // Merge wrapped lines: if first column is empty, append the text to the previous row's last non-empty cell.
    private (List<string[]>, int) ExtractTableWithAnchors(List<LineEx> lines, int start, List<double> anchors)
    {
        var block = new List<LineEx> { lines[start] };
        int i = start + 1;

        while (i < lines.Count)
        {
            if (_opt.NumberedHeading.IsMatch(lines[i].Text)) break;
            var filled = CountFilledColumns(lines[i], anchors);
            if (filled >= 1) block.Add(lines[i++]); else break;
        }

        var rows = new List<string[]>();
        foreach (var ln in block)
        {
            var cols = SliceIntoColumns(ln, anchors).Select(c => c.Trim()).ToArray();

            if (rows.Count > 0)
            {
                bool isHeader = rows.Count == 1 && rows[0].Any(c => c.Contains("Step", StringComparison.OrdinalIgnoreCase));
                // If this line doesn't start a new row (no Step; first col empty) → merge into previous row.
                if (!isHeader && string.IsNullOrWhiteSpace(cols[0]))
                {
                    var last = rows[^1];
                    int target = 2; // favor Action column
                    if (string.IsNullOrWhiteSpace(last[target])) target = 1; // else Responsibility
                    last[target] = (last[target] + " " + string.Join(" ", cols.Skip(1))).Trim();
                    continue;
                }
            }

            rows.Add(cols);
        }

        // Ensure we don't output header-only table
        if (rows.Count < 2) return (new List<string[]>(), block.Count);
        return (rows, block.Count);
    }


    private int CountFilledColumns(LineEx line, List<double> anchors)
    {
        var cols = SliceIntoColumns(line, anchors);
        return cols.Count(c => !string.IsNullOrWhiteSpace(c));
    }

    private string[] SliceIntoColumns(LineEx line, List<double> anchors)
    {
        var buckets = new List<List<string>>(Enumerable.Range(0, anchors.Count).Select(_ => new List<string>()));
        foreach (var w in line.Words)
        {
            int best = 0;
            double bestDist = Math.Abs(w.Left - anchors[0]);
            for (int k = 1; k < anchors.Count; k++)
            {
                var d = Math.Abs(w.Left - anchors[k]);
                if (d < bestDist) { best = k; bestDist = d; }
            }
            buckets[best].Add(w.Text);
        }
        return buckets.Select(b => string.Join(" ", b)).ToArray();
    }

    private static string RenderTable(List<string[]> rows)
    {
        if (rows.Count == 0) return string.Empty;
        int cols = rows.Max(r => r.Length);
        var norm = rows.Select(r => r.Length == cols ? r :
                          r.Concat(Enumerable.Repeat("", cols - r.Length)).ToArray()).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("| " + string.Join(" | ", norm[0].Select(EscapeMd)) + " |");
        sb.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", cols)) + " |");
        foreach (var r in norm.Skip(1))
            sb.AppendLine("| " + string.Join(" | ", r.Select(EscapeMd)) + " |");
        return sb.ToString();
    }
}