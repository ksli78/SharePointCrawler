using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

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
    public SharePointClient(string siteUrl, NetworkCredential? credential)
    {
        if (string.IsNullOrWhiteSpace(siteUrl))
            throw new ArgumentException("Site URL must be provided", nameof(siteUrl));

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

        _client = new HttpClient(handler);
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
    /// example, <c>/Shared Documents</c> or
    /// <c>/sites/DevSite/Documents/SubFolder</c>).  For each file discovered
    /// the crawler yields a <see cref="DocumentInfo"/> instance containing the
    /// file name, the server relative URL, a dictionary of metadata and the
    /// binary data for the file.
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
                            var docInfo = await FetchFileInfoAsync(fileElement).ConfigureAwait(false);
                            await SendToExternalApiAsync(docInfo).ConfigureAwait(false);
                            yield return docInfo;
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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(fileResponse.Content.ToString());
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        return doc;
    }

    /// <summary>
    /// A stub that can be overridden to send a document to an external API.  By
    /// default this method simply completes.  When building your own
    /// integration you can replace the body of this method with logic to
    /// transform <see cref="DocumentInfo"/> instances or post them to another
    /// service.  If network or database calls are required the method can be
    /// made asynchronous.
    /// </summary>
    /// <param name="doc">The document information to send.</param>
    protected virtual async Task SendToExternalApiAsync(DocumentInfo doc)
    {

        var payload = new RagIngestDocument
        {
            SpWebUrl = $"{_rootUrl}{doc.Url}",
            SpItemId = doc.Metadata.TryGetValue("UniqueId", out var id) ? id?.ToString() : null,
            ETag = doc.Metadata.TryGetValue("ETag", out var etag) ? etag?.ToString() : null,

            Title = doc.Metadata.TryGetValue("Title", out var title) ? title?.ToString() : null,
            Org = doc.Metadata.TryGetValue("Org", out var org) ? org?.ToString() : null,
            OrgCode = doc.Metadata.TryGetValue("Org_x0020_Code", out var orgCode) ? orgCode?.ToString() : null,
            Category = doc.Metadata.TryGetValue("Category", out var category) ? category?.ToString() : "All Documents",
            DocCode = doc.Metadata.TryGetValue("Document_x0020__x0023_", out var docCode) ? docCode?.ToString() : null,
            Owner = doc.Metadata.TryGetValue("Owner0", out var owner) ? owner?.ToString() : null,
            Version = doc.Metadata.TryGetValue("Version_", out var version) ? version?.ToString() : null,

            RevisionDate = doc.Metadata.TryGetValue("Revision_x0020_Date", out var rev) ? rev?.ToString() : null,
            LatestReviewDate = doc.Metadata.TryGetValue("Latest_x0020_Review_x0020_Date", out var latest) ? latest?.ToString() : null,
            DocumentReviewDate = doc.Metadata.TryGetValue("aaaa", out var docReview) ? docReview?.ToString() : null,
            ReviewApprovalDate = doc.Metadata.TryGetValue("Review_x0020_Approval_x0020_Date", out var approval) ? approval?.ToString() : null,

            Keywords = ExtractKeywords(doc, "Keywords"),
            EnterpriseKeywords = ExtractKeywords(doc, "TaxKeyword"),
            AssociationIds = ExtractKeywords(doc, "Association"),

            FileName = doc.Name,
            ContentBytes = Convert.ToBase64String(doc.Data),
            
        };
        var ctn = Convert.FromBase64String(payload.ContentBytes);

        using var httpClient = new HttpClient();
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync($"http://adam.amentumspacemissions.com:8000/ingest_document", content);

            if (!response.IsSuccessStatusCode)
                Console.WriteLine(response.Content.ToString());
        }catch(Exception ex)
        { 
            Console.WriteLine(ex.ToString()); 
        }
    }
    private List<string> ExtractKeywords(DocumentInfo doc, string field)
    {
        if (!doc.Metadata.TryGetValue(field, out var raw)) return new();
        if (raw is string s && s.Contains(";")) return s.Split(';').Select(x => x.Trim()).ToList();
        if (raw is IEnumerable<object> list) return list.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()!;
        return new() { raw?.ToString()! };
    }

    /// <summary>
    /// Releases the underlying <see cref="HttpClient"/> and associated handler.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
    }
}