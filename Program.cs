using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SharePointCrawler;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Usage: dotnet run <siteUrl> <libraryRelativeUrl> <username> <password> [domain] [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --mode <all|titles>           Crawl mode (default all)");
            Console.WriteLine("  --titles-file <path>          File containing titles to ingest");
            Console.WriteLine("  --titles \"TitleA;TitleB\"   Semicolon separated titles");
            Console.WriteLine("  --collection <name>          Target embedding collection (default docs_v2)");
            Console.WriteLine("  --chunk-size-tokens <num>    Tokens per chunk (default 350)");
            Console.WriteLine("  --chunk-overlap-tokens <num> Token overlap (default 80)");
            Console.WriteLine("  --max-docs <num>             Limit number of documents (optional)");
            return;
        }

        var siteUrl = args[0];
        var libraryRelativeUrl = $"{siteUrl}/_api/web/GetFolderByServerRelativeUrl('{args[1]}')?$expand=Folders,Files";
        var username = args[2];
        var password = args[3];
        string domain = string.Empty;
        var index = 4;
        if (args.Length > 4 && !args[4].StartsWith("--"))
        {
            domain = args[4];
            index = 5;
        }

        var options = new CrawlerOptions();
        string? titlesFile = null;
        string? titlesInline = null;

        for (; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--mode":
                    options.Mode = index + 1 < args.Length ? args[++index] : options.Mode;
                    break;
                case "--titles-file":
                    titlesFile = index + 1 < args.Length ? args[++index] : null;
                    break;
                case "--titles":
                    titlesInline = index + 1 < args.Length ? args[++index] : null;
                    break;
                case "--collection":
                    options.Collection = index + 1 < args.Length ? args[++index] : options.Collection;
                    break;
                case "--chunk-size-tokens":
                    if (index + 1 < args.Length && int.TryParse(args[++index], out var cst))
                        options.ChunkSizeTokens = cst;
                    break;
                case "--chunk-overlap-tokens":
                    if (index + 1 < args.Length && int.TryParse(args[++index], out var cot))
                        options.ChunkOverlapTokens = cot;
                    break;
                case "--max-docs":
                    if (index + 1 < args.Length && int.TryParse(args[++index], out var md))
                        options.MaxDocs = md;
                    break;
                default:
                    Console.WriteLine($"Unknown argument {args[index]}");
                    break;
            }
        }

        HashSet<string>? titleSet = null;
        HashSet<string> matchedTitles = new(StringComparer.OrdinalIgnoreCase);
        if (options.Mode.Equals("titles", StringComparison.OrdinalIgnoreCase))
        {
            titleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(titlesFile) && File.Exists(titlesFile))
            {
                foreach (var line in File.ReadAllLines(titlesFile))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        titleSet.Add(line.Trim());
                }
            }
            if (!string.IsNullOrWhiteSpace(titlesInline))
            {
                foreach (var part in titlesInline.Split(';'))
                {
                    if (!string.IsNullOrWhiteSpace(part))
                        titleSet.Add(part.Trim());
                }
            }
            options.Titles = titleSet;
        }

        NetworkCredential credential = new(username, password, domain);
        ConsoleWindow.Initialize();

        Func<DocumentInfo, bool>? filter = null;
        if (titleSet != null)
        {
            filter = doc =>
            {
                var docTitle = doc.Metadata.TryGetValue("Title", out var t)
                    ? t?.ToString()?.Trim()
                    : Path.GetFileNameWithoutExtension(doc.Name);
                if (docTitle != null && titleSet.Contains(docTitle))
                {
                    matchedTitles.Add(docTitle);
                    return true;
                }
                return false;
            };
        }

        using var client = new SharePointClient(siteUrl, credential, options, filter);
        await foreach (var _ in client.GetDocumentsAsync(libraryRelativeUrl))
        {
            // Processing feedback is handled by SharePointClient via ConsoleWindow.
        }

        Console.WriteLine($"Docs scanned: {client.DocsScanned}, selected: {client.DocsSelected}, chunks: {client.ChunksProduced}, collection: {client.Collection}");
        if (titleSet != null)
        {
            foreach (var t in titleSet.Except(matchedTitles))
            {
                Console.WriteLine($"Title not matched: {t}");
            }
        }
    }
}
