using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharePointCrawler.Foundation.Models
{

    public class IngestRequest
    {

        [JsonPropertyName("sp_web_url")]
        public string? SpWebUrl { get; set; }
        [JsonPropertyName("doc_code")]
        public string? DocCode { get; set; }
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
        [JsonPropertyName("file_bytes")]
        public string? ContentBytes { get; set; }
    }

}
