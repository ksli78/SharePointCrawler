using System;
using System.Net;
using System.Threading.Tasks;
using SharePointCrawler.Foundation.services;
using SharePointCrawler.Foundation.utils;

namespace SharePointCrawler;

/// <summary>
/// The entry point for the SharePoint crawler.  This console application
/// accepts a SharePoint site URL, the server relative URL of the document
/// library or folder to crawl and a set of Windows credentials.  It then
/// instantiates a <see cref="SharePointClient"/> and iterates through all
/// documents returned by the SharePoint REST API, printing basic
/// information about each file to the console.  The credentials should
/// correspond to a user account with permission to read from the target
/// library.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        // Default settings
        string mode = "all";
        string? titlesFile = null;
        string? titlesList = null;
        string collection = "docs_v2";
        int chunkSizeTokens = 350;
        int overlapTokens = 80;

        if (args.Length < 4)
        {
            Console.WriteLine("Usage: dotnet run <siteUrl> <libraryRelativeUrl> <username> <password> [domain]");
            Console.WriteLine();
            Console.WriteLine("siteUrl:           The base URL of your SharePoint site (e.g. https://server/sites/DevSite)");
            Console.WriteLine("libraryRelativeUrl: Server relative URL of the document library or folder to crawl (e.g. /Shared Documents)");
            Console.WriteLine("username:          The user name to authenticate with");
            Console.WriteLine("password:          The password for the user");
            Console.WriteLine("domain (optional): The Active Directory domain (on‑prem only)");
            return;
        }

        var siteUrl = args[0];
        var libraryRelativeUrl = args[1];
        var username = args[2];
        var password = args[3];
        var domain = args.Length > 4 ? args[4] : string.Empty;


        // Parse optional named arguments
        foreach (var arg in args.Skip(5))
        {
            if (arg.StartsWith("--mode=")) mode = arg.Split('=')[1];
            else if (arg.StartsWith("--titles-file=")) titlesFile = arg.Split('=')[1];
            else if (arg.StartsWith("--titles=")) titlesList = arg.Split('=')[1];
            else if (arg.StartsWith("--collection=")) collection = arg.Split('=')[1];
            else if (arg.StartsWith("--chunk-size-tokens=")) chunkSizeTokens = int.Parse(arg.Split('=')[1]);
            else if (arg.StartsWith("--chunk-overlap-tokens=")) overlapTokens = int.Parse(arg.Split('=')[1]);
        }

        HashSet<string>? allowedTitles = null;
        if (mode == "titles")
        {
            allowedTitles = new HashSet<string>();
            allowedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(titlesFile))
                allowedTitles.UnionWith(File.ReadAllLines(titlesFile).Where(l => !string.IsNullOrWhiteSpace(l)));
            if (!string.IsNullOrWhiteSpace(titlesList))
                allowedTitles.UnionWith(titlesList.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0));
        }


        // Create the credential.  If a domain is supplied we include it;
        // otherwise we assume a local machine account.
        NetworkCredential credential = new(username, password, domain);

        ConsoleWindow.Initialize();
        // Start fresh retry list for this run
        ErrorLogger.ClearRetryList();

        using var client = new SharePointClient(siteUrl, credential, allowedTitles,chunkSizeTokens,overlapTokens,collection);
        var totalDocs = await client.GetTotalDocumentCountAsync(libraryRelativeUrl);
        ConsoleWindow.SetTotalDocuments(totalDocs);
        await foreach (var doc in client.GetDocumentsAsync(libraryRelativeUrl))
        {
            // Processing feedback is handled by SharePointClient via ConsoleWindow.
        }

        // After crawl completes, offer to retry failed documents sent to Adam API
        if (client.FailedDocuments.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("The following documents failed during Adam API ingestion:");
            for (int i = 0; i < client.FailedDocuments.Count; i++)
            {
                var d = client.FailedDocuments[i];
                Console.WriteLine($"  [{i + 1}] {d.Name}  ({d.Url})");
            }
            Console.Write("Would you like to retry any of these? [y/N]: ");
            var retryAnswer = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(retryAnswer) && (retryAnswer.Equals("y", StringComparison.OrdinalIgnoreCase) || retryAnswer.Equals("yes", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("Enter item numbers to retry (comma-separated), or 'all' to retry all:");
                Console.Write("> ");
                var selection = Console.ReadLine()?.Trim();
                List<int> indices = new();
                if (string.IsNullOrWhiteSpace(selection) || selection.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    indices = Enumerable.Range(1, client.FailedDocuments.Count).ToList();
                }
                else
                {
                    foreach (var part in selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (int.TryParse(part, out var idx) && idx >= 1 && idx <= client.FailedDocuments.Count)
                            indices.Add(idx);
                    }
                }

                int successCount = 0;
                int failCount = 0;
                foreach (var idx in indices.Distinct())
                {
                    var doc = client.FailedDocuments[idx - 1];
                    Console.WriteLine($"Retrying: {doc.Name}");
                    var ok = await client.ResendDocumentAsync(doc);
                    if (ok)
                    {
                        successCount++;
                        client.FailedDocuments.Remove(doc);
                    }
                    else
                    {
                        failCount++;
                    }
                }
                Console.WriteLine($"Retry complete. Succeeded: {successCount}, Failed: {failCount}");
            }
        }
    }
}
