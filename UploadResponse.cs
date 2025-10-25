using System.Text.Json.Serialization;

namespace SharePointCrawler
{
    /// <summary>
    /// Response model for the /upload-document API endpoint.
    /// </summary>
    public class UploadResponse
    {
        [JsonPropertyName("document_id")]
        public string DocumentId { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("source_url")]
        public string SourceUrl { get; set; } = string.Empty;
    }
}
