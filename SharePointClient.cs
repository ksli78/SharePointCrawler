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
    private static readonly Regex PageNumberRegex = new(@"^(page\s*\d+(\s*of\s*\d+)?)|^\d+$", RegexOptions.IgnoreCase);
    private static readonly Regex SignatureRegex = new(@"^(signature|signed|approved by|prepared by).*", RegexOptions.IgnoreCase);
    private static readonly Regex ToCRegex = new(@"table of contents", RegexOptions.IgnoreCase);

    private HashSet<string> _allowedTitles = new HashSet<string>();
    private int _chunkSizeTokens = 0;
    private int _overlapTokens = 0;
    private string _collection = "";
    private string logFile = "log.txt";
    private StreamWriter _writer;
   
    
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
        var endpoint = $"{_siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encoded}')?$expand=Files";
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
                    DocumentInfo? docInfo = null;
                    var start = DateTime.Now;
                    try
                    {
                        docInfo = await FetchFileInfoAsync(fileElement).ConfigureAwait(false);
                        if (_allowedTitles != null && !_allowedTitles.Contains(docInfo.Name) && !_allowedTitles.Contains(docInfo.Metadata.GetValueOrDefault("Title")?.ToString()))
                            continue;

                        ConsoleWindow.SetStatus(folderRelativeUrl, docInfo.Name);
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
        if (!string.IsNullOrWhiteSpace(doc.Url) && doc.Url.EndsWith("aspx") != true )
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
    private static IList<string> Tokenize(string text)
    {
        // naïve tokenizer: split on whitespace and punctuation
        return text?
            .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '-', '(', ')', '[', ']', '{', '}', '!', '?', '"' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList() ?? new List<string>();
    }

    private static List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
    {
        var tokens = Tokenize(text);
        var chunks = new List<string>();
        for (int start = 0; start < tokens.Count; start += (chunkSize - overlap))
        {
            var window = tokens.Skip(start).Take(chunkSize).ToList();
            if (window.Count == 0) break;
            chunks.Add(string.Join(" ", window));
            if (start + chunkSize >= tokens.Count) break;
        }
        return chunks;
    }
    private string BuildBreadcrumbs(DocumentInfo doc)
    {
        string? title = doc.Metadata.TryGetValue("Title", out var t) ? t?.ToString() : doc.Name;
        return title ?? "";
    }

    protected async Task SendToExternalApiAsync(DocumentInfo doc)
    {
        string? textContent = null;
        var extension = Path.GetExtension(doc.Name).ToLowerInvariant();
        try
        {
            switch (extension)
            {
                case ".txt":
                case ".md":
                    textContent = Encoding.UTF8.GetString(doc.Data);
                    break;
                case ".pdf":
                    PdfToMarkdownConverter converter = new PdfToMarkdownConverter();
                    textContent = converter.ConvertToMarkdown(doc.Data);
                    break;
                case ".docx":
                    textContent = ExtractWordText(doc.Data);
                    break;
                case ".xlsx":
                    textContent = ExtractExcelText(doc.Data);
                    break;
            }
        }
        catch (Exception ex)
        {
            var msg = $"Failed to extract text for {doc.Name}: {ex.Message}";
            ConsoleWindow.Error(msg);
            ErrorLogger.Log(doc.Name, doc.Url, msg);
        }

        if (textContent != null)
        {
            textContent = CleanText(textContent);
            // Only proceed if we have enough cleaned text.  The infer_metadata
            // endpoint requires a reasonable length to produce a summary and
            // keywords.  If the document is too short, skip ingestion.
            if (string.IsNullOrWhiteSpace(textContent) || textContent.Length < 500)
            {
                ConsoleWindow.Info($"Skipping {doc.Name} due to insufficient content ({textContent?.Length ?? 0} chars).");
                return;
            }
        }
        // Call the infer_metadata API to obtain summary, category and keywords.
        string? inferredSummary = null;
        string? inferredCategory = null;
        List<string>? inferredKeywords = null;

        if (!string.IsNullOrWhiteSpace(textContent))
        {
            try
            {
                var meta = await InferMetadataAsync(textContent, doc).ConfigureAwait(false);
                if (meta != null)
                {
                    inferredSummary = meta.Summary;
                    inferredCategory = meta.Category;
                    inferredKeywords = meta.Keywords;
                }
            }
            catch (Exception ex)
            {
                ConsoleWindow.Error($"infer_metadata failed: {ex.Message}");
            }
        }

        var breadcrumbs = BuildBreadcrumbs(doc);
        var chunks = textContent != null
            ? SplitIntoChunks(textContent, _chunkSizeTokens, _overlapTokens)
            : new List<string> { null }; // fallback to whole file if no text

        List<IngestChunk> ingestChunks = new List<IngestChunk>();

        foreach (var (chunkText, idx) in chunks.Select((c, i) => (c, i)))
        {
            var inChunk = new IngestChunk()
            {
                AllowedGroups = ["everyone"],
                SpWebUrl = $"{_rootUrl}{doc.Url}",
                SpItemId = doc.Metadata.TryGetValue("UniqueId", out var id) ? id?.ToString() : new Guid().ToString(),
                ETag = doc.Metadata.TryGetValue("ETag", out var etag) ? etag?.ToString() : null,
                Title = doc.Metadata.TryGetValue("Title", out var title) ? title?.ToString() : doc.Name,
                FileName = doc.Name,
                TextContent = chunkText,
                ContentBytes = textContent is null ? Convert.ToBase64String(doc.Data) : null,
                Collection = _collection,
                ChunkIndex = idx,
                Breadcrumbs = breadcrumbs,
                ChunkSize = _chunkSizeTokens,
                ChunkOverlap = _overlapTokens,
                Org = doc.Metadata.TryGetValue("Org", out var org) ? org?.ToString() : null,
                OrgCode = doc.Metadata.TryGetValue("Org_x0020_Code", out var orgCode) ? orgCode?.ToString() : null,
                DocCode = doc.Metadata.TryGetValue("Document_x0020__x0023_", out var docCode) ? docCode?.ToString() : null,
                Owner = doc.Metadata.TryGetValue("Owner0", out var owner) ? owner?.ToString() : null,
                Version = doc.Metadata.TryGetValue("Version_", out var version) ? version?.ToString() : null,
                RevisionDate = doc.Metadata.TryGetValue("Revision_x0020_Date", out var rev) ? rev?.ToString() : null,
                LatestReviewDate = doc.Metadata.TryGetValue("Latest_x0020_Review_x0020_Date", out var latest) ? latest?.ToString() : null,
                DocumentReviewDate = doc.Metadata.TryGetValue("aaaa", out var docReview) ? docReview?.ToString() : null,
                ReviewApprovalDate = doc.Metadata.TryGetValue("Review_x0020_Approval_x0020_Date", out var approval) ? approval?.ToString() : null,
                EnterpriseKeywords = ExtractKeywords(doc, "TaxKeyword"),
                AssociationIds = ExtractKeywords(doc, "Association"),
                // Assign summary, category and keywords from infer_metadata if available,
                // otherwise fallback to simple heuristics.
                Summary = inferredSummary ?? (textContent != null ? GenerateSummary(textContent) : null),
                Category = inferredCategory ?? DetectCategory(textContent ?? string.Empty),
                Keywords = inferredKeywords != null && inferredKeywords.Count > 0 ? string.Join(",", inferredKeywords) : null,
            };

            ingestChunks.Add(inChunk);
        }


        var ingestRequest = new IngestRequest()
        {
            Chunks = ingestChunks,
        };


        // POST to your local AdamPY endpoint (update URL if needed)
        var json = JsonSerializer.Serialize(ingestRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var content = new StringContent(json, Encoding.UTF8, "application/json");


        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        try
        {
            var response = await httpClient.PostAsync($"http://adam.amentumspacemissions.com:8000/ingest_document", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorString = await response.Content.ReadAsStringAsync();
                ConsoleWindow.Error(errorString);
                ErrorLogger.Log(doc.Name, doc.Url, errorString);
            }
            else
            {
                var resp = await response.Content.ReadFromJsonAsync<IngestResponse>();
                if (resp != null)
                {
                    ConsoleWindow.Success($"Status:{resp.Success} - Ingested document {resp.DocID} at {resp.Chunks} Chunks via {resp.IngestType}");
                    if (!string.IsNullOrWhiteSpace(resp.Summary))
                        ConsoleWindow.Info($"Summary:{resp.Summary}");
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleWindow.Error(ex.ToString());
            ErrorLogger.Log(doc.Name, doc.Url, ex.ToString());
        }
    }



    private static string ExtractPdfText(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var document = PdfDocument.Open(ms);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    private static string ExtractWordText(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var doc = WordprocessingDocument.Open(ms, false);
        var sb = new StringBuilder();
        var body = doc.MainDocumentPart?.Document.Body;
        if (body != null)
        {
            foreach (var text in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
            {
                sb.Append(text.Text);
            }
        }
        return sb.ToString();
    }

    private static string ExtractExcelText(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var document = SpreadsheetDocument.Open(ms, false);
        var sb = new StringBuilder();
        var wbPart = document.WorkbookPart;
        if (wbPart?.Workbook.Sheets != null)
        {
            foreach (Sheet sheet in wbPart.Workbook.Sheets.OfType<Sheet>())
            {
                var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
                foreach (var row in wsPart.Worksheet.Descendants<Row>())
                {
                    foreach (var cell in row.Descendants<Cell>())
                    {
                        var text = GetCellValue(cell, wbPart);
                        if (!string.IsNullOrWhiteSpace(text))
                            sb.Append(text).Append(' ');
                    }
                    sb.AppendLine();
                }
            }
        }
        return sb.ToString();
    }

    private static string GetCellValue(Cell cell, WorkbookPart wbPart)
    {
        var value = cell.CellValue?.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            var sstPart = wbPart.SharedStringTablePart;
            if (sstPart != null)
            {
                return sstPart.SharedStringTable.ChildElements[int.Parse(value)].InnerText;
            }
        }
        return value;
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var lines = text.Split('\n');
        var lineCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            lineCounts[trimmed] = lineCounts.TryGetValue(trimmed, out var c) ? c + 1 : 1;
        }
        var totalLines = lines.Length;
        var sb = new StringBuilder();
        var inToc = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (PageNumberRegex.IsMatch(trimmed) || SignatureRegex.IsMatch(trimmed)) continue;
            if (lineCounts.TryGetValue(trimmed, out var count) && count > totalLines * 0.5) continue; // header/footer
            if (!inToc && ToCRegex.IsMatch(trimmed)) { inToc = true; continue; }
            if (inToc)
            {
                if (string.IsNullOrWhiteSpace(trimmed)) inToc = false;
                continue;
            }
            sb.AppendLine(trimmed);
        }

        var cleaned = sb.ToString();
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim();
    }

    private static string DeriveTitle(string? original, string text, string fileName)
    {
        var firstLine = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (string.IsNullOrWhiteSpace(original) || original.Equals(Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase))
        {
            return firstLine ?? original ?? fileName;
        }
        return original;
    }

    private static string? DetectCategory(string text)
    {
        foreach (var kvp in CategoryKeywordMap)
        {
            if (kvp.Key.IsMatch(text)) return kvp.Value;
        }
        return null;
    }

    private static List<string> GenerateKeywords(string text, int max = 10)
    {
        var tokens = Regex.Matches(text.ToLowerInvariant(), @"\b[a-z]{3,}\b").Select(m => m.Value)
            .Where(t => !StopWords.Contains(t));
        var freq = tokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        return freq.OrderByDescending(kv => kv.Value).Take(max).Select(kv => kv.Key).ToList();
    }

    private static string GenerateSummary(string text, int maxSentences = 3)
    {
        var sentences = Regex.Split(text, @"(?<=[\.\!\?])\s+").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return string.Join(" ", sentences.Take(maxSentences));
    }
    private List<string> ExtractKeywords(DocumentInfo doc, string field)
    {
        if (!doc.Metadata.TryGetValue(field, out var raw)) return new();
        if (raw is string s && s.Contains(";")) return s.Split(';').Select(x => x.Trim()).ToList();
        if (raw is IEnumerable<object> list) return list.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()!;
        return new() { raw?.ToString()! };
    }

    private class IngestResponse
    {
        [JsonPropertyName("ok")]
        public bool Success { get; set; }
        [JsonPropertyName("doc_id")]
        public string? DocID { get; set; }
        [JsonPropertyName("chunks")]
        public int Chunks { get; set; }
        [JsonPropertyName("used")]
        public string? IngestType { get; set; }
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
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

    /// <summary>
    /// Calls the infer_metadata endpoint to obtain a summary, category and keyword list
    /// for a given document.  If the call fails for any reason, null is returned
    /// and callers should fall back to local heuristics.
    /// </summary>
    /// <param name="text">The cleaned text of the document.</param>
    /// <param name="doc">The document info for metadata (title, doc code).</param>
    /// <returns>An InferMetadataResult on success, otherwise null.</returns>
    private async Task<InferMetadataResult?> InferMetadataAsync(string text, DocumentInfo doc)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Build the request payload.  Include title and doc_code if available.
        string? title = doc.Metadata.TryGetValue("Title", out var tVal) ? tVal?.ToString() : doc.Name;
        string? docCode = doc.Metadata.TryGetValue("Document_x0020__x0023_", out var dcVal) ? dcVal?.ToString() : null;
        var payload = new
        {
            text = text,
            title = title,
            doc_code = docCode
        };

        var json = JsonSerializer.Serialize(payload);
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var response = await httpClient.PostAsync("http://adam.amentumspacemissions.com:8000/infer_metadata", content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                ConsoleWindow.Error($"infer_metadata HTTP {response.StatusCode}: {err}");
                return null;
            }
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var result = await JsonSerializer.DeserializeAsync<InferMetadataResult>(stream, options).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            ConsoleWindow.Error($"infer_metadata exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Helper DTO for infer_metadata responses.
    /// </summary>
    private class InferMetadataResult
    {
        public string? Summary { get; set; }
        public string? Category { get; set; }
        public List<string> Keywords { get; set; } = new();
        public Dictionary<string, JsonElement>? Debug { get; set; }
    }

    /// <summary>
    /// Calls the infer_metadata endpoint to obtain a summary, category and keyword list
    /// for a given document.  If the call fails for any reason, null is returned
    /// and callers should fall back to local heuristics.
    /// </summary>
    /// <param name="text">The cleaned text of the document.</param>
    /// <param name="doc">The document info for metadata (title, doc code).</param>
    /// <returns>An InferMetadataResult on success, otherwise null.</returns>
    private async Task<InferMetadataResult?> InferMetadataAsync(string text, DocumentInfo doc)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Build the request payload.  Include title and doc_code if available.
        string? title = doc.Metadata.TryGetValue("Title", out var tVal) ? tVal?.ToString() : doc.Name;
        string? docCode = doc.Metadata.TryGetValue("Document_x0020__x0023_", out var dcVal) ? dcVal?.ToString() : null;
        var payload = new
        {
            text = text,
            title = title,
            doc_code = docCode
        };

        var json = JsonSerializer.Serialize(payload);
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var response = await httpClient.PostAsync("http://adam.amentumspacemissions.com:8000/infer_metadata", content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                ConsoleWindow.Error($"infer_metadata HTTP {response.StatusCode}: {err}");
                return null;
            }
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var result = await JsonSerializer.DeserializeAsync<InferMetadataResult>(stream, options).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            ConsoleWindow.Error($"infer_metadata exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Helper DTO for infer_metadata responses.
    /// </summary>
    private class InferMetadataResult
    {
        public string? Summary { get; set; }
        public string? Category { get; set; }
        public List<string> Keywords { get; set; } = new();
        public Dictionary<string, JsonElement>? Debug { get; set; }
    }
}
