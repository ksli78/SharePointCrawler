using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Drawing.Charts;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Fonts.TrueType.Tables;
using SharePointCrawler.Foundation.Models;
using DocumentFormat.OpenXml.Office2010.Excel;
using SharePointCrawler.Foundation.utils;

namespace SharePointCrawler.Foundation.services;

/// <summary>
/// Provides functionality to crawl a SharePoint document library using the
/// SharePoint REST API.  The crawler authenticates using a set of Windows
/// credentials and can recursively traverse the folders within a library to
/// retrieve both the metadata and binary contents of each file.  Calls to the
/// REST API leverage the <c>GetFolderByServerRelativeUrl</c> endpoint with
/// <c>$expand=Folders,Files</c> so that folder and file information can be
/// fetched together in one request【697898085085864†L82-L86】.  Individual file
/// contents are downloaded via the <c>GetFileByServerRelativeUrl</c> endpoint
/// using the <c>$value</c> segment【497258984103498†L142-L163】.  The
/// <see cref="SendToExternalApiAsync"/> method is intentionally left as a stub
/// so that callers can implement custom logic (such as posting the document
/// elsewhere) when a file is retrieved.
/// </summary>
public class SharePointClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _siteUrl;
    private string _rootUrl = string.Empty;
    private static readonly Regex PageNumberRegex = new(@"^(page\s*\d+(\s*of\s*\d+)?)|^\d+$", RegexOptions.IgnoreCase);
    private static readonly Regex SignatureRegex = new(@"^(signature|signed|approved by|prepared by).*", RegexOptions.IgnoreCase);
    private static readonly Regex ToCRegex = new(@"table of contents", RegexOptions.IgnoreCase);

    private HashSet<string> _allowedTitles = new HashSet<string>();
    private int _chunkSizeTokens = 0;
    private int _overlapTokens = 0;
    private string _collection = "";
    private string logFile = "log.txt";
    private StreamWriter _writer;
    public List<DocumentInfo> FailedDocuments { get; } = new();



    /// <summary>
    /// Constructs a new client for interacting with a SharePoint site.  The
    /// <paramref name="siteUrl"/> parameter should point at the root of the
    /// SharePoint site (for example, <c>https://server/sites/DevSite</c>).  A
    /// <see cref="NetworkCredential"/> is used to authenticate requests
    /// against an on‑premises farm.  If no credentials are supplied the
    /// underlying handler will use the default credentials of the current
    /// process.
    /// </summary>
    /// <param name="siteUrl">The base URL of the SharePoint site.</param>
    /// <param name="credential">Windows credentials for authentication.</param>
    public SharePointClient(string siteUrl, NetworkCredential? credential, HashSet<string>? allowedTitles, int chunkSizeTokens, int overlapTokens, string collection)
    {
        if (string.IsNullOrWhiteSpace(siteUrl))
            throw new ArgumentException("Site URL must be provided", nameof(siteUrl));


        _allowedTitles = allowedTitles;
        _chunkSizeTokens = chunkSizeTokens;
        _overlapTokens = overlapTokens;
        _collection = collection;


        // Trim trailing slashes from the site URL so we don't end up with
        // duplicate separators when constructing endpoint URIs.
        _siteUrl = siteUrl.TrimEnd('/');
        var rtUrl = new Uri(siteUrl);
        _rootUrl = $"{rtUrl.Scheme}://{rtUrl.Host}";
        var handler = new HttpClientHandler();
        if (credential != null)
        {
            handler.Credentials = credential;
            handler.PreAuthenticate = true;
        }
        else
        {
            handler.UseDefaultCredentials = true;
        }

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        // Request JSON output without additional metadata.  If you prefer a
        // verbose response (wrapped in a top‑level "d" property) you can
        // replace odata=minimalmetadata with odata=verbose.  The crawler
        // detects both shapes when parsing the response.
        _client.DefaultRequestHeaders.Accept.Clear();
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _client.DefaultRequestHeaders.Add("Prefer", "odata=minimalmetadata");

        _client.DefaultRequestHeaders.Add("X-FORMS_BASED_AUTH_ACCEPTED", "f");

        _writer = new StreamWriter(logFile, true);
    }

    private void Log(string message)
    {
        _writer.WriteLine($"{DateTime.Now:u} {message}");
        _writer.Flush();
    }

    /// <summary>
    /// Calculates the total number of documents that will be processed for the specified library.
    /// </summary>
    /// <param name="libraryRelativeUrl">Server relative URL of the document library to inspect.</param>
    /// <returns>The total count of documents that meet the filtering criteria.</returns>
    public async Task<int> GetTotalDocumentCountAsync(string libraryRelativeUrl)
    {
        if (string.IsNullOrWhiteSpace(libraryRelativeUrl))
            throw new ArgumentException("Library relative URL must be provided", nameof(libraryRelativeUrl));

        var folders = await GetAllFoldersAsync(libraryRelativeUrl).ConfigureAwait(false);
        int total = 0;
        foreach (var folder in folders)
        {
            total += await CountFilesInFolderAsync(folder).ConfigureAwait(false);
        }
        return total;
    }

    /// <summary>
    /// Enumerates all files in the specified library.  The crawler first
    /// retrieves a complete list of folders within the library and then
    /// iterates over each folder to download its files.  Sub‑folders are
    /// discovered during the initial enumeration so that the folder tree is
    /// traversed depth‑first before any files are processed.
    /// </summary>
    /// <param name="libraryRelativeUrl">Server relative URL of the document library to crawl.</param>
    /// <returns>An asynchronous stream of <see cref="DocumentInfo"/> objects.</returns>
    public async IAsyncEnumerable<DocumentInfo> GetDocumentsAsync(string libraryRelativeUrl)
    {
        if (string.IsNullOrWhiteSpace(libraryRelativeUrl))
            throw new ArgumentException("Library relative URL must be provided", nameof(libraryRelativeUrl));

        var folders = await GetAllFoldersAsync(libraryRelativeUrl).ConfigureAwait(false);
        foreach (var folder in folders)
        {
            ConsoleWindow.SetStatus(folder, string.Empty);
            await foreach (var doc in ProcessFilesInFolderAsync(folder).ConfigureAwait(false))
            {
                yield return doc;
            }
        }
    }

    /// <summary>
    /// Recursively builds a list of all folder URLs beneath the supplied
    /// library URL.
    /// </summary>
    private async Task<List<string>> GetAllFoldersAsync(string rootRelativeUrl)
    {
        var folders = new List<string>();
        var stack = new Stack<string>();
        stack.Push(rootRelativeUrl);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            folders.Add(current);

            var encoded = Uri.EscapeDataString(current.TrimStart('/'));
            var endpoint = $"{_siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encoded}')?$select=ServerRelativeUrl&$expand=Folders";
            using var response = await _client.GetAsync(endpoint).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) continue;

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("d", out var d)) root = d;

            if (root.TryGetProperty("Folders", out var foldersElement))
            {
                JsonElement folderArray;
                if (foldersElement.ValueKind == JsonValueKind.Array)
                    folderArray = foldersElement;
                else if (foldersElement.TryGetProperty("results", out var folderResults))
                    folderArray = folderResults;
                else
                    folderArray = default;

                if (folderArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var folderElement in folderArray.EnumerateArray())
                    {
                        if (folderElement.TryGetProperty("ServerRelativeUrl", out var serverUrl))
                        {
                            var url = serverUrl.GetString();
                            if (!string.IsNullOrWhiteSpace(url))
                                stack.Push(url);
                        }
                    }
                }
            }
        }

        return folders;
    }

    /// <summary>
    /// Retrieves all files within the specified folder.
    /// </summary>
    private async IAsyncEnumerable<DocumentInfo> ProcessFilesInFolderAsync(string folderRelativeUrl)
    {
        var encoded = Uri.EscapeDataString(folderRelativeUrl);
        var endpoint = $"{_siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encoded}')?$expand=Files,ListItemAllFields,Files/ListItemAllFields";
        using var response = await _client.GetAsync(endpoint).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"Request to {endpoint} failed with {response.StatusCode}");
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("d", out var dProp)) root = dProp;

        if (root.TryGetProperty("Files", out var filesElement))
        {
            JsonElement fileArray;
            if (filesElement.ValueKind == JsonValueKind.Array)
                fileArray = filesElement;
            else if (filesElement.TryGetProperty("results", out var fileResults))
                fileArray = fileResults;
            else
                fileArray = default;

            if (fileArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var fileElement in fileArray.EnumerateArray())
                {
                    if (!HasDocumentContentType(fileElement))
                        continue;

                    DocumentInfo? docInfo = null;
                    var start = DateTime.Now;
                    try
                    {
                        docInfo = await FetchFileInfoAsync(fileElement).ConfigureAwait(false);
                        if (_allowedTitles != null && !_allowedTitles.Contains(docInfo.Name) && !_allowedTitles.Contains(docInfo.Metadata.GetValueOrDefault("Title")?.ToString()))
                            continue;

                        ConsoleWindow.SetStatus(folderRelativeUrl, docInfo.Name);
                        ConsoleWindow.StartDocument(docInfo, start);
                        var success = await SendToExternalApiAsync(docInfo).ConfigureAwait(false);
                        var elapsed = DateTime.Now - start;
                        ConsoleWindow.CompleteDocument(docInfo, elapsed, success);
                    }
                    catch (Exception ex)
                    {
                        var elapsed = DateTime.Now - start;
                        ConsoleWindow.Error($"Error: {ex.Message} (elapsed {elapsed.TotalSeconds:F1}s)");
                        if (docInfo != null)
                        {
                            ErrorLogger.Log(docInfo.Name, docInfo.Url, ex.Message);
                            ConsoleWindow.CompleteDocument(docInfo, elapsed, false, ex.Message);
                        }
                        docInfo = null;
                    }
                    if (docInfo != null)
                    {
                        yield return docInfo;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Counts the number of files within the specified folder that meet the filtering criteria.
    /// </summary>
    private async Task<int> CountFilesInFolderAsync(string folderRelativeUrl)
    {
        var encoded = Uri.EscapeDataString(folderRelativeUrl);
        var endpoint = $"{_siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encoded}')?$expand=Files,ListItemAllFields,Files/ListItemAllFields";
        using var response = await _client.GetAsync(endpoint).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log($"Request to {endpoint} failed with {response.StatusCode}");
            return 0;
        }

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("d", out var dProp)) root = dProp;

        int count = 0;
        if (root.TryGetProperty("Files", out var filesElement))
        {
            JsonElement fileArray;
            if (filesElement.ValueKind == JsonValueKind.Array)
                fileArray = filesElement;
            else if (filesElement.TryGetProperty("results", out var fileResults))
                fileArray = fileResults;
            else
                fileArray = default;

            if (fileArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var fileElement in fileArray.EnumerateArray())
                {
                    if (!HasDocumentContentType(fileElement))
                        continue;

                    if (_allowedTitles != null)
                    {
                        string? name = null;
                        if (fileElement.TryGetProperty("Name", out var nameProp))
                            name = nameProp.GetString();
                        string? title = null;
                        if (fileElement.TryGetProperty("Title", out var titleProp))
                            title = titleProp.GetString();
                        if (!_allowedTitles.Contains(name ?? string.Empty) && !_allowedTitles.Contains(title ?? string.Empty))
                            continue;
                    }
                    count++;
                }
            }
        }
        return count;
    }

    private static bool HasDocumentContentType(JsonElement fileElement)
    {
        if (fileElement.ValueKind != JsonValueKind.Object)
            return false;

        if (fileElement.TryGetProperty("ListItemAllFields", out var listItem))
        {
            if (listItem.ValueKind == JsonValueKind.Object)
            {
                if (listItem.TryGetProperty("OData__x0068_wg8", out var ct))
                {
                    if (ct.ValueKind == JsonValueKind.String)
                        return string.Equals(ct.GetString(), "Document", StringComparison.OrdinalIgnoreCase);
                    if (ct.ValueKind == JsonValueKind.Object && ct.TryGetProperty("Name", out var name))
                        return string.Equals(name.GetString(), "Document", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        if (fileElement.TryGetProperty("ContentType", out var directCt) && directCt.ValueKind == JsonValueKind.String)
            return string.Equals(directCt.GetString(), "Document", StringComparison.OrdinalIgnoreCase);

        if (fileElement.TryGetProperty("ContentTypeId", out var directCtId))
        {
            var id = directCtId.GetString();
            if (!string.IsNullOrEmpty(id) && id.StartsWith("0x0101", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Retrieves the binary contents and metadata for a single file.  The
    /// <paramref name="fileElement"/> is the JSON element representing the
    /// file as returned by the SharePoint REST API.  This method extracts
    /// useful properties (such as Name and ServerRelativeUrl) and downloads
    /// the file data via the <c>$value</c> endpoint【497258984103498†L142-L163】.
    /// </summary>
    /// <param name="fileElement">A JSON element representing a file in the REST response.</param>
    /// <returns>A populated <see cref="DocumentInfo"/> instance.</returns>
    private async Task<DocumentInfo> FetchFileInfoAsync(JsonElement fileElement)
    {
        var doc = new DocumentInfo();

        // Copy all properties from the JSON into the metadata dictionary.
        foreach (var property in fileElement.EnumerateObject())
        {
            object? value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.ToString()
            };
            doc.Metadata[property.Name] = value;
        }

        // Extract specific fields for convenience.
        if (fileElement.TryGetProperty("Name", out var nameProp))
        {
            doc.Name = nameProp.GetString() ?? string.Empty;
        }
        if (fileElement.TryGetProperty("ServerRelativeUrl", out var urlProp))
        {
            doc.Url = urlProp.GetString() ?? string.Empty;
        }

        // Download the binary data for the file using the $value endpoint.  The
        // REST syntax for downloading a file is documented by Microsoft; you
        // call GetFileByServerRelativeUrl and append $value【497258984103498†L142-L163】.
        if (!string.IsNullOrWhiteSpace(doc.Url) && doc.Url.EndsWith("aspx") != true)
        {
            var escapedUrl = doc.Url.Replace("'", "''");
            var fileEndpoint = $"{_siteUrl}/_api/web/GetFileByServerRelativeUrl('{escapedUrl}')/$value";

            using var fileResponse = await _client.GetAsync(fileEndpoint).ConfigureAwait(false);
            if (fileResponse.IsSuccessStatusCode)
            {
                doc.Data = await fileResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            else
            {
                var msg = await fileResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                ConsoleWindow.Error(msg);
                ErrorLogger.Log(doc.Name, doc.Url, msg);
            }
        }

        return doc;
    }

    public async Task<bool> ResendDocumentAsync(DocumentInfo doc)
    {
        // Wrapper to allow external callers to retry a document
        return await SendToExternalApiAsync(doc).ConfigureAwait(false);
    }

    protected async Task<bool> SendToExternalApiAsync(DocumentInfo doc)
    {
        var extension = Path.GetExtension(doc.Name).ToLowerInvariant();
        
        if(extension != ".pdf")
        {
            ConsoleWindow.Error("Found unsuppported document, Only PDFs are handled at this time");
            return false;
        }
        

        IngestRequest request = new IngestRequest()
        {
            ContentBytes = Convert.ToBase64String(doc.Data),
            DocCode = doc.Metadata.TryGetValue("ETag", out var etag) ? etag?.ToString() : Guid.NewGuid().ToString(),
            FileName = doc.Name,
            SpWebUrl = $"{_rootUrl}{doc.Url}",
            Title = doc.Metadata.TryGetValue("Title", out var title) ? title?.ToString() : doc.Name,
        };


        // POST to your local AdamPY endpoint (update URL if needed)
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        try
        {
            var response = await httpClient.PostAsync($"http://adam.amentumspacemissions.com:8080/ingest", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorString = await response.Content.ReadAsStringAsync();
                ConsoleWindow.Error(errorString);
                ErrorLogger.Log(doc.Name, doc.Url, errorString);
                ErrorLogger.AppendToRetryList(doc.Name);
                if (!FailedDocuments.Contains(doc)) FailedDocuments.Add(doc);
                return false;
            }
            else
            {
                var resp = await response.Content.ReadFromJsonAsync<IngestResponse>();
                if (resp != null)
                {
                    ConsoleWindow.Success($"Status:{resp.Status} - {resp.Chunks} Chunks");
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            ConsoleWindow.Error(ex.ToString());
            ErrorLogger.Log(doc.Name, doc.Url, ex.ToString());
            ErrorLogger.AppendToRetryList(doc.Name);
            if (!FailedDocuments.Contains(doc)) FailedDocuments.Add(doc);
            return false;
        }
    }

    /// <summary>
    /// Releases the underlying <see cref="HttpClient"/> and associated handler.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
        _writer.Flush();
        _writer.Close();
        _writer.Dispose();
    }
}
