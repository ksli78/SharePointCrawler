using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharePointCrawler
{
    public class DocumentPayload
    {
        [JsonPropertyName("source_url")]
        public string Url { get; set; } = string.Empty;
        [JsonPropertyName("doc_id")]
        public string DocumentId { get; set; }= string.Empty;
        [JsonPropertyName("etag")]
        public string ETag { get; set; } = string.Empty;
        [JsonPropertyName("last_modified")]
        public string LastModified { get; set; } = string.Empty;
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("file")]
        public string FileBase64 { get; set; } = string.Empty;
    }
}
