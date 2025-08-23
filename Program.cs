using System;
using System.Net;
using System.Threading.Tasks;

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
        var libraryRelativeUrl = $"{siteUrl}/_api/web/GetFolderByServerRelativeUrl('{args[1]}')?$expand=Folders,Files";
        var username = args[2];
        var password = args[3];
        var domain = args.Length > 4 ? args[4] : string.Empty;

        // Create the credential.  If a domain is supplied we include it;
        // otherwise we assume a local machine account.
        NetworkCredential credential = new(username, password, domain);

        ConsoleWindow.Initialize();

        using var client = new SharePointClient(siteUrl, credential);
        await foreach (var doc in client.GetDocumentsAsync(libraryRelativeUrl))
        {
            // Processing feedback is handled by SharePointClient via ConsoleWindow.
        }
    }
}