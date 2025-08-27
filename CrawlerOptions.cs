using System;
using System.Collections.Generic;

namespace SharePointCrawler;

/// <summary>
/// Options controlling how the crawler processes and posts documents.
/// </summary>
public class CrawlerOptions
{
    public string Mode { get; set; } = "all";
    public HashSet<string>? Titles { get; set; }
        = null; // Populated when mode == titles
    public string Collection { get; set; }
        = Environment.GetEnvironmentVariable("CRAWLER_COLLECTION") ?? "docs_v2";
    public int ChunkSizeTokens { get; set; } = 350;
    public int ChunkOverlapTokens { get; set; } = 80;
    public int? MaxDocs { get; set; }
        = null; // Optional limiter for testing
}
