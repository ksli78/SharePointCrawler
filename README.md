# SharePoint Document Library Crawler

This sample project demonstrates how to crawl a SharePoint on‑premises site using the SharePoint REST API and .NET 8.  Starting from the root site URL the crawler discovers all document libraries, traverses each hierarchy, downloads every file and exposes both its binary contents and metadata.  A stubbed method (`SendToExternalApiAsync`) is provided so that you can add your own logic to process or forward the retrieved documents to another API.

## Key REST API concepts

* **Enumerating files and folders** – The crawler makes use of the `GetFolderByServerRelativeUrl` endpoint with the `$expand=Folders,Files` query option to retrieve both files and folders in a single call.  This pattern is recommended when you need to build a directory tree because it returns folder and file information together【697898085085864†L82-L86】.

* **Downloading file contents** – Individual file bytes are fetched using the `GetFileByServerRelativeUrl` endpoint and appending `$value` to the URL, which causes SharePoint to return the raw file data【497258984103498†L142-L163】.

* **Authentication** – Since the crawler targets an on‑premises farm, it authenticates using a `NetworkCredential`.  The example uses the `HttpClientHandler` to assign the credentials and sets the `X‑FORMS_BASED_AUTH_ACCEPTED` header to `f`, which instructs SharePoint to bypass the forms‑based authentication page when integrated Windows authentication is available.

## Project structure

```
sharepoint-crawler/
├── DocumentInfo.cs       # Simple data transfer object for document data and metadata
├── Program.cs            # Console entry point; parses args and runs the crawler
├── SharePointClient.cs   # Core class that interacts with SharePoint REST API
├── SharePointCrawler.csproj # .NET 8 project file
└── README.md             # This file
```

### DocumentInfo

`DocumentInfo` encapsulates everything you need about a file: its name, server relative URL, binary data and a dictionary containing all metadata returned by SharePoint.  Any custom columns configured in your library will automatically appear in the `Metadata` dictionary.

### SharePointClient

`SharePointClient` accepts a site URL and optional `NetworkCredential` in its constructor.  It can discover document libraries via `GetDocumentLibraryUrlsAsync` and crawl each one using `GetDocumentsAsync`.

Calling `GetDocumentLibraryUrlsAsync` returns the REST endpoints for each document library found under the site.  Passing one of these endpoints to `GetDocumentsAsync` yields an asynchronous stream of `DocumentInfo` objects representing every file found in that library and its subfolders.  Internally the client calls:

* `/_api/web/lists?$filter=BaseTemplate eq 101&$select=RootFolder/ServerRelativeUrl&$expand=RootFolder` to locate document libraries.
* `/_api/web/GetFolderByServerRelativeUrl('<relativeUrl>')?$expand=Folders,Files` to enumerate both child folders and files【697898085085864†L82-L86】.
* `/_api/web/GetFileByServerRelativeUrl('<fileRelativeUrl>')/$value` to download the bytes of each file【497258984103498†L142-L163】.

You can override the protected `SendToExternalApiAsync` method in a derived class or modify its body to post each document elsewhere.

### Program

The console host expects the following command‑line arguments:

```text
dotnet run <siteUrl> <username> <password> [domain]
```

* `siteUrl` – The base URL of your SharePoint site (e.g. `https://server/sites/DevSite`).
* `username`/`password` – Credentials for a user with read access to the site.
* `domain` – Optional Active Directory domain for on‑premises environments.

The program constructs a `NetworkCredential` from the supplied account, discovers all document libraries under the site and iterates through every file returned by the crawler.  Processing feedback is written to the console and a log file.

## Building and running

This repository does not include compiled binaries.  To run the crawler:

1. Install .NET 8 SDK on your machine.
2. Navigate to the `sharepoint-crawler` directory.
3. Restore dependencies (there are no external NuGet packages used):

   ```bash
   dotnet restore
   ```

4. Build and run the project, passing the required arguments:

   ```bash
   dotnet run -- https://server/sites/DevSite user password DOMAIN
   ```

Replace `https://server/sites/DevSite` with your site URL.  If you do not specify a domain the credential will assume a local account.

## Extending the crawler

The stubbed `SendToExternalApiAsync` method provides a convenient hook for integrating with other systems.  For example, you could override the method in a subclass and use `HttpClient` to post the document to an external service or write it to cloud storage.  The `DocumentInfo` instance passed into the method exposes both the raw bytes (`Data`) and metadata dictionary (`Metadata`) for your convenience.

Because SharePoint’s REST service includes all custom columns for a file in the metadata, you can use the contents of the `Metadata` dictionary to make routing or transformation decisions before sending the file to another system.