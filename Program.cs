using System;
using System.Net;
using System.Threading.Tasks;

namespace SharePointCrawler;

/// <summary>
/// Entry point for the SharePoint crawler. This console application
/// accepts the URL of a SharePoint site along with Windows credentials
/// and automatically discovers all document libraries beneath the root
/// site. Each library is then crawled and every document is processed.
/// The credentials should correspond to a user account with permission
/// to read from the target site.
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

        if (args.Length < 3)
        {
            Console.WriteLine("Usage: dotnet run <siteUrl> <username> <password> [domain]");
            Console.WriteLine();
            Console.WriteLine("siteUrl:  The base URL of your SharePoint site (e.g. https://server/sites/DevSite)");
            Console.WriteLine("username: The user name to authenticate with");
            Console.WriteLine("password: The password for the user");
            Console.WriteLine("domain (optional): The Active Directory domain (on‑prem only)");
            return;
        }

        var siteUrl = args[0];
        var username = args[1];
        var password = args[2];

        var domain = string.Empty;
        int optionalStart = 3;
        if (args.Length > 3 && !args[3].StartsWith("--"))
        {
            domain = args[3];
            optionalStart = 4;
        }

        // Parse optional named arguments
        foreach (var arg in args.Skip(optionalStart))
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

        using var client = new SharePointClient(siteUrl, credential, allowedTitles, chunkSizeTokens, overlapTokens, collection);
        var libraries = await client.GetDocumentLibraryUrlsAsync();
        foreach (var library in libraries)
        {
            await foreach (var doc in client.GetDocumentsAsync(library))
            {
                // Processing feedback is handled by SharePointClient via ConsoleWindow.
            }
        }
    }
}