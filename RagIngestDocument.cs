using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharePointCrawler
{
    public class RagIngestDocument
    {
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

        public List<string>? Keywords { get; set; }
        public List<string>? EnterpriseKeywords { get; set; }
        public List<string>? AssociationIds { get; set; }

        public string? Domain { get; set; } = "MS Documents";
        public List<string>? AllowedGroups { get; set; } = new() { "AllEmployees" };

        public string? FileName { get; set; }
        public string? ContentBytes { get; set; }
        public string? TextContent { get; set; }
        public string? Summary { get; set; }

        public int ChunkSize { get; set; } = 1400;
        public int ChunkOverlap { get; set; } = 300;
        public bool Persist { get; set; } = false;

        public string Collection { get; set; } = "docs";
        public string Breadcrumbs { get; set; } = ""; 
        public int ChunkIndex { get; set; } = 0;


    }
}
