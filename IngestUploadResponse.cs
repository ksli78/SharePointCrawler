using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharePointCrawler
{
    public class IngestUploadResponse
    {
        [JsonPropertyName("status")]    
        public string Status { get; set; } = string.Empty;
        [JsonPropertyName("doc_id")]
        public string DocumentId { get; set; } = string.Empty;
        [JsonPropertyName("chunks")]
        public int? Chunks { get; set; } = 0;
        [JsonPropertyName("reason")]
        public string? Reason { get; set; } = string.Empty;
    }
}
