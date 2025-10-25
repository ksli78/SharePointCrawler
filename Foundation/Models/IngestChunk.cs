using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharePointCrawler.Foundation.Models
{

    public class IngestChunk
    {
        // SharePoint identity / metadata
        public string? SpWebUrl { get; set; }
        public string? SpItemId { get; set; }
        public string? ETag { get; set; }

        public string? Title { get; set; }
        public string? Org { get; set; }
        public string? OrgCode { get; set; }
        public string? Category { get; set; }
        public string? DocCode { get; set; }
        public string? Owner { get; set; }
        public string? Version { get; set; }

        public string? RevisionDate { get; set; }
        public string? LatestReviewDate { get; set; }
        public string? DocumentReviewDate { get; set; }
        public string? ReviewApprovalDate { get; set; }

        public string? Keywords { get; set; }
        public List<string> EnterpriseKeywords { get; set; } = new();
        public List<string> AssociationIds { get; set; } = new();

        public string? Domain { get; set; }
        public List<string> AllowedGroups { get; set; } = new();

        // Content identity
        public string? FileName { get; set; }

        // Content (Markdown text preferred; content_bytes as fallback)
        public string? ContentBytes { get; set; }  // base64 if present
        public string? TextContent { get; set; }   // markdown/plain text
        public string? Summary { get; set; }

        // Chunking info coming from crawler
        public int? ChunkSize { get; set; }
        public int? ChunkOverlap { get; set; }
        public int? ChunkIndex { get; set; }
        public string? Breadcrumbs { get; set; }  // e.g., "Telecommuting Process"

        // Routing / storage hints
        [JsonPropertyName("persist")]
        [Display(Description = "If False, do not persist to disk")]
        public bool? Persist { get; set; } = true;

        [JsonPropertyName("collection")]
        [Display(Description = "Chroma collection name")]
        public string? Collection { get; set; } = "docs_v2";

        // Extra fields are allowed and will be carried into metadata
        [JsonExtensionData]
        public Dictionary<string, object>? ExtraFields { get; set; }
    }

}
