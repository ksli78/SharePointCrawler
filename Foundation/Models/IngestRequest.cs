using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharePointCrawler.Foundation.Models
{

    public class IngestRequest
    {
        // Accept either one chunk or many chunks in a single POST
        public List<IngestChunk>? Chunks { get; set; }

        // Back-compat: allow a single object body (the old handler used this)
        // If provided, we will normalize it into a single chunk ingestion.
        public string? SpWebUrl { get; set; }
        public string? SpItemId { get; set; }
        public string? ETag { get; set; }
        public string? Title { get; set; }
        public string? FileName { get; set; }
        public string? TextContent { get; set; }
        public string? ContentBytes { get; set; }
        public string? Collection { get; set; }
        public int? ChunkIndex { get; set; }
        public string? Breadcrumbs { get; set; }
    }

}
