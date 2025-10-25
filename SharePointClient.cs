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

namespace SharePointCrawler;

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
    private readonly string _ingestUrl;
    private static readonly Regex PageNumberRegex = new(@"^(page\s*\d+(\s*of\s*\d+)?)|^\d+$", RegexOptions.IgnoreCase);
    private static readonly Regex SignatureRegex = new(@"^(signature|signed|approved by|prepared by).*", RegexOptions.IgnoreCase);
    private static readonly Regex ToCRegex = new(@"table of contents", RegexOptions.IgnoreCase);

    private HashSet<string>? _allowedTitles;
    private int _chunkSizeTokens = 0;
    private int _overlapTokens = 0;
    private string _collection = "";

    private static readonly Dictionary<Regex, string> CategoryKeywordMap = new()
    {
        [new Regex(@"\b(hr|human resources|employee)\b", RegexOptions.IgnoreCase)] = "HR",
        [new Regex(@"\b(it|information technology|software|system)\b", RegexOptions.IgnoreCase)] = "IT",
        [new Regex(@"\b(policy|procedure|guideline)\b", RegexOptions.IgnoreCase)] = "Policy",
        [new Regex(@"\b(form|template)\b", RegexOptions.IgnoreCase)] = "Form"
    };
    private static readonly HashSet<string> StopWords = new(new[]
    {
        "the","and","for","with","that","this","from","have","will","their","are","was","were","has","had","but","not","you","your","about","into","can","shall","may","might","should","could","been","being","over","under","after","before","between","within","upon","without","including","include","such","each","any","other","more","most","some","than","too","very","one","two","three"
    });
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
    public SharePointClient(string siteUrl, NetworkCredential? credential, HashSet<string> allowedTitles, int chunkSizeTokens, int overlapTokens, string collection, string ingestUrl)
    {
        if (string.IsNullOrWhiteSpace(siteUrl))
            throw new ArgumentException("Site URL must be provided", nameof(siteUrl));


        _allowedTitles = allowedTitles.Count == 0 ? null : allowedTitles;
        _chunkSizeTokens = chunkSizeTokens;
        _overlapTokens = overlapTokens;
        _collection = collection;
        _ingestUrl = ingestUrl;


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
    }

    /// <summary>
    /// Recursively enumerates all files within a document library.  The
    /// <paramref name="libraryRelativeUrl"/> parameter must be the server
    /// relative URL of the library or folder that you wish to crawl (for
    /// <summary>
    /// Recursively counts all documents under the specified library or folder.
    /// This is used to initialize the progress bar in the UI before processing
    /// begins.
    /// </summary>
    /// <param name="libraryRelativeUrl">Server relative URL of the document library or folder to crawl.</param>
    /// <returns>The total number of documents found.</returns>
    public async Task<int> CountDocumentsAsync(string libraryRelativeUrl)
    {
        if (string.IsNullOrWhiteSpace(libraryRelativeUrl))
            throw new ArgumentException("Library relative URL must be provided", nameof(libraryRelativeUrl));

        var normalizedRelativeUrl = libraryRelativeUrl.StartsWith("/") ? libraryRelativeUrl.Substring(1) : libraryRelativeUrl;
        normalizedRelativeUrl = normalizedRelativeUrl.EndsWith("?$expand=Folders,Files") ? normalizedRelativeUrl : $"{normalizedRelativeUrl}?$expand=Folders,Files";
        var endpoint = normalizedRelativeUrl;
        using var response = await _client.GetAsync(endpoint).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return 0;

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

        JsonElement root;
        if (document.RootElement.TryGetProperty("d", out var dProperty))
            root = dProperty;
        else
            root = document.RootElement;

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
                count += fileArray.GetArrayLength();
        }

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
                    var folderRelativeUrl = folderElement.GetProperty("odata.id").GetString();
                    if (!string.IsNullOrWhiteSpace(folderRelativeUrl))
                        count += await CountDocumentsAsync(folderRelativeUrl).ConfigureAwait(false);
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Enumerates all documents under the specified library or folder.  For each
    /// file discovered the crawler yields a <see cref="DocumentInfo"/> instance
    /// containing the file name, URL, metadata and binary data.
    /// </summary>
    /// <param name="libraryRelativeUrl">Server relative URL of the document library or folder to crawl.</param>
    /// <returns>An asynchronous stream of <see cref="DocumentInfo"/> objects.</returns>
    public async IAsyncEnumerable<DocumentInfo> GetDocumentsAsync(string libraryRelativeUrl)
    {
        if (string.IsNullOrWhiteSpace(libraryRelativeUrl))
            throw new ArgumentException("Library relative URL must be provided", nameof(libraryRelativeUrl));

        // Ensure the relative URL starts with a forward slash.
        var normalizedRelativeUrl = libraryRelativeUrl.StartsWith("/") ? libraryRelativeUrl.Substring(1) : libraryRelativeUrl;
        normalizedRelativeUrl = normalizedRelativeUrl.EndsWith("?$expand=Folders,Files") ? normalizedRelativeUrl : $"{normalizedRelativeUrl}?$expand=Folders,Files";
        //normalizedRelativeUrl = UrlEncoder.Default.Encode(normalizedRelativeUrl);
        // Build the REST endpoint.  We use $expand=Folders,Files so that
        // information about both folders and files is returned in one call
        //【697898085085864†L82-L86】.
        //var escapedRelativeUrl = normalizedRelativeUrl.Replace("'", "''");
        var endpoint = normalizedRelativeUrl;
        try
        {
            using var response = await _client.GetAsync(endpoint).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {

                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

                // Detect whether the JSON payload is wrapped in a top‑level "d" property
                // (verbose OData) or not (minimal metadata).  Some SharePoint
                // configurations return the entity directly without a wrapper, as
                // illustrated by the sample response provided in the user's report.
                JsonElement root;
                if (document.RootElement.TryGetProperty("d", out var dProperty))
                {
                    root = dProperty;
                }
                else
                {
                    root = document.RootElement;
                }

                // Enumerate files first.  In verbose responses the Files collection
                // contains a "results" property with the actual array.  In minimal
                // metadata responses the Files property itself is the array.  We
                // support both shapes.
                if (root.TryGetProperty("Files", out var filesElement))
                {
                    JsonElement fileArray;
                    // When Files is already an array (minimal metadata) avoid calling
                    // TryGetProperty on it since that will throw an exception.  Only
                    // attempt to read the "results" property if the Files element is
                    // an object.
                    if (filesElement.ValueKind == JsonValueKind.Array)
                    {
                        fileArray = filesElement;
                    }
                    else if (filesElement.TryGetProperty("results", out var fileResults))
                    {
                        fileArray = fileResults;
                    }
                    else
                    {
                        fileArray = default;
                    }

                    if (fileArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var fileElement in fileArray.EnumerateArray())
                        {
                            DocumentInfo? docInfo = null;
                            var start = DateTime.Now;
                            try
                            {
                                docInfo = await FetchFileInfoAsync(fileElement).ConfigureAwait(false);
                                if (_allowedTitles != null && !_allowedTitles.Contains(docInfo.Name) &&

                                    !_allowedTitles.Contains(docInfo.Metadata.GetValueOrDefault("Title")?.ToString()))
                                    continue;


                                ConsoleWindow.StartDocument(docInfo, start);
                                await SendToExternalApiAsync(docInfo).ConfigureAwait(false);
                                var elapsed = DateTime.Now - start;
                                ConsoleWindow.CompleteDocument(docInfo, elapsed, true);
                            }
                            catch (Exception ex)
                            {
                                var elapsed = DateTime.Now - start;
                                ConsoleWindow.Error($"Error: {ex.Message} (elapsed {elapsed.TotalSeconds:F1}s)");
                                if (docInfo != null)
                                {
                                    ErrorLogger.Log(docInfo.Name, docInfo.Url, ex.Message);
                                    ConsoleWindow.CompleteDocument(docInfo, elapsed, false, ex.Message);
                                    docInfo = null;
                                }
                            }
                            if (docInfo != null)
                            {
                                yield return docInfo;
                            }
                        }
                    }
                }

                // Enumerate folders and recurse into each one.  As with Files, the
                // Folders collection may either have a "results" property or be the
                // array itself depending on the metadata level.
                if (root.TryGetProperty("Folders", out var foldersElement))
                {
                    JsonElement folderArray;
                    // As with Files, check if Folders is an array before reading the
                    // "results" property to avoid invalid operations.
                    if (foldersElement.ValueKind == JsonValueKind.Array)
                    {
                        folderArray = foldersElement;
                    }
                    else if (foldersElement.TryGetProperty("results", out var folderResults))
                    {
                        folderArray = folderResults;
                    }
                    else
                    {
                        folderArray = default;
                    }

                    if (folderArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var folderElement in folderArray.EnumerateArray())
                        {
                            var folderRelativeUrl = folderElement.GetProperty("odata.id").GetString();
                            if (!string.IsNullOrWhiteSpace(folderRelativeUrl))
                            {
                                await foreach (var nestedDoc in GetDocumentsAsync(folderRelativeUrl).ConfigureAwait(false))
                                {
                                    yield return nestedDoc;
                                }
                            }
                        }
                    }
                }
            }

        }
        finally { }
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
        if (!string.IsNullOrWhiteSpace(doc.Url))
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

    protected async Task SendToExternalApiAsync(DocumentInfo doc)
    {
        // Only send PDFs (keep your switch if you want to skip other types)
        var extension = Path.GetExtension(doc.Name).ToLowerInvariant();
        if (extension != ".pdf")
            return;

        // Extract required fields from metadata
        string docId = doc.Metadata.TryGetValue("UniqueId", out var idObj) ? idObj?.ToString() ?? "" : "";
        string sourceUrl = $"{_rootUrl}{doc.Url}";
        string etag = doc.Metadata.TryGetValue("ETag", out var etagObj) ? etagObj?.ToString() ?? "" : "";
        string lastModified = doc.Metadata.TryGetValue("Modified", out var modObj) ? modObj?.ToString() ?? "" : "";
        string title = doc.Metadata.TryGetValue("Title", out var titleObj)
                       ? titleObj?.ToString() ?? Path.GetFileNameWithoutExtension(doc.Name)
                       : Path.GetFileNameWithoutExtension(doc.Name);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(120) };
        var url = _ingestUrl;

        try
        {
            using var form = new MultipartFormDataContent();

            // Add the file content
            var fileContent = new ByteArrayContent(doc.Data);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(fileContent, "file", doc.Name);

            // Add the other fields
            form.Add(new StringContent(docId), "doc_id");
            form.Add(new StringContent(sourceUrl), "source_url");
            form.Add(new StringContent(title), "title");
            form.Add(new StringContent(""), "category");
            form.Add(new StringContent(""), "keywords");


            var response = await httpClient.PostAsync(url, form);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                ConsoleWindow.Error(body);
                ErrorLogger.Log(doc.Name, doc.Url, body);
                return;
            }

            // If your API returns JSON like your IngestResponse, parse it:
            var resp = System.Text.Json.JsonSerializer.Deserialize<IngestUploadResponse>(body);
            if (resp != null)
            {
                ConsoleWindow.Success($"Status:{resp.Status} - Ingested document {resp.DocumentId} at {resp.Chunks} Chunks   {(string.IsNullOrEmpty(resp.Reason) == false ? $"Reason:{resp.Reason}" : "")}");
            }
            else
            {
                ConsoleWindow.Success("Uploaded successfully.");
            }
        }
        catch (Exception ex)
        {
            ConsoleWindow.Error(ex.ToString());
            ErrorLogger.Log(doc.Name, doc.Url, ex.ToString());
        }
    }

    /// <summary>
    /// Releases the underlying <see cref="HttpClient"/> and associated handler.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
    }
}