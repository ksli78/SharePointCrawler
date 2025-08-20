using System.Collections.Generic;

namespace SharePointCrawler;

/// <summary>
/// Represents a document fetched from SharePoint.  Each document consists of a
/// name, its server relative URL, the binary data for the file and a
/// collection of metadata values.  The metadata dictionary contains all
/// properties returned by the SharePoint REST API for the file.  Depending on
/// how the underlying list is configured, this may include fields such as
/// <c>UniqueId</c>, <c>Length</c>, <c>TimeCreated</c>, <c>TimeLastModified</c>,
/// <c>ServerRelativeUrl</c> and any custom columns attached to the library.
/// </summary>
public class DocumentInfo
{
    /// <summary>
    /// The display name of the file.  This corresponds to the file's
    /// <c>Name</c> property in SharePoint.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The server relative URL of the file.  This value can be used to
    /// construct additional REST endpoints or build direct links to the file in
    /// SharePoint.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The raw binary contents of the file.  When the crawler fetches a file
    /// using the <c>$value</c> endpoint, it populates this property with the
    /// returned bytes.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// A dictionary of metadata values associated with the file.  Keys are the
    /// property names returned by the SharePoint REST API and values are the
    /// corresponding property values.  Consumers of the crawler can inspect
    /// this dictionary to read additional information about each document.
    /// </summary>
    public IDictionary<string, object?> Metadata { get; set; } = new Dictionary<string, object?>();
}