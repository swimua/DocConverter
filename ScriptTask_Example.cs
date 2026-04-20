// ----------------------------------------------------------------------------
// Example Script Task body for a Creatio business process.
//
// Assumes the process has:
//   - A process parameter "SourceFileId" (Guid) — id of the ContactFile row to convert
//   - A process parameter "ResultPdfFileId" (Guid, output)
//
// Requires the WordToPdfClient class (WordToPdfClient.cs) deployed in a
// Configuration package.
// ----------------------------------------------------------------------------

try
{
    var client = new Custom.Integrations.WordToPdfClient(UserConnection);

    // Synchronously wait inside the script task (Creatio script tasks are sync).
    var pdfId = client.ConvertCreatioFileAsync(
            UserConnection,
            fileSchemaName: "ContactFile",
            sourceFileId: Get<Guid>("SourceFileId"))
        .GetAwaiter()
        .GetResult();

    Set<Guid>("ResultPdfFileId", pdfId);
    return true;
}
catch (Exception ex)
{
    Terrasoft.Common.Logging.LogManager
        .GetLogger("WordToPdf")
        .Error("Conversion failed", ex);
    throw;
}
