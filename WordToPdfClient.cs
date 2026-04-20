// ----------------------------------------------------------------------------
// WordToPdfClient — drop-in helper for Creatio server-side C# (business process
// Script Task, User Task, or custom service).
//
// Requires .NET dependencies that ship with Creatio out of the box:
//   - System.Net.Http
//   - Terrasoft.Core (for EntitySchemaQuery and attaching the PDF to a record)
//
// Configuration:
//   Store the base URL and API key in Creatio "System settings" (SysSettings):
//     - WordToPdfServiceUrl  (text)   e.g. https://word-to-pdf.yourcompany.com
//     - WordToPdfApiKey      (secure text)
// ----------------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Terrasoft.Core;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Factories;

namespace Custom.Integrations
{
    public sealed class WordToPdfClient
    {
        private static readonly HttpClient SharedHttp = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        private readonly string _baseUrl;
        private readonly string _apiKey;

        public WordToPdfClient(UserConnection userConnection)
        {
            _baseUrl = Terrasoft.Core.Configuration.SysSettings
                .GetValue(userConnection, "WordToPdfServiceUrl", "").TrimEnd('/');
            _apiKey = Terrasoft.Core.Configuration.SysSettings
                .GetValue(userConnection, "WordToPdfApiKey", "");
            if (string.IsNullOrWhiteSpace(_baseUrl))
                throw new InvalidOperationException("SysSetting 'WordToPdfServiceUrl' is not configured.");
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("SysSetting 'WordToPdfApiKey' is not configured.");
        }

        /// <summary>Converts raw .docx bytes to a PDF byte array.</summary>
        public async Task<byte[]> ConvertAsync(
            byte[] docx,
            string fileName,
            CancellationToken ct = default)
        {
            if (docx == null || docx.Length == 0)
                throw new ArgumentException("Empty docx content.", nameof(docx));
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "document.docx";

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(docx);
            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue(
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            content.Add(fileContent, "file", fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/v1/convert")
            {
                Content = content
            };
            request.Headers.Add("X-API-Key", _apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));

            using var response = await SharedHttp.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Word-to-PDF conversion failed ({(int)response.StatusCode}): {body}");
            }

            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reads a Creatio file (e.g. row of ContactFile / AccountFile / any "*File" schema),
        /// converts it to PDF, and saves a NEW sibling row with the PDF attached.
        /// </summary>
        /// <returns>Id of the newly created PDF file row.</returns>
        public async Task<Guid> ConvertCreatioFileAsync(
            UserConnection userConnection,
            string fileSchemaName,      // e.g. "ContactFile", "AccountFile", "OpportunityFile"
            Guid sourceFileId,
            CancellationToken ct = default)
        {
            // 1) Load the source file record
            var schema = userConnection.EntitySchemaManager.GetInstanceByName(fileSchemaName);
            var source = schema.CreateEntity(userConnection);
            if (!source.FetchFromDB(sourceFileId))
                throw new InvalidOperationException(
                    $"{fileSchemaName} row {sourceFileId} not found.");

            var sourceName = source.GetTypedColumnValue<string>("Name");
            var sourceData = source.GetTypedColumnValue<byte[]>("Data");

            // 2) Convert
            var pdfBytes = await ConvertAsync(sourceData, sourceName, ct).ConfigureAwait(false);
            var pdfName = Path.GetFileNameWithoutExtension(sourceName) + ".pdf";

            // 3) Create a new file row next to the original (same parent record, same folder)
            var pdf = schema.CreateEntity(userConnection);
            pdf.SetDefColumnValues();
            pdf.SetColumnValue("Id", Guid.NewGuid());
            pdf.SetColumnValue("Name", pdfName);
            pdf.SetColumnValue("TypeId", Terrasoft.Configuration.FileConsts.FileTypeUId); // "File" type
            pdf.SetColumnValue("Size", pdfBytes.Length);
            pdf.SetColumnValue("Data", pdfBytes);

            // Copy the parent reference (different file schemas use different column names,
            // e.g. ContactFile.Contact, AccountFile.Account, OpportunityFile.Opportunity).
            foreach (var col in source.Schema.Columns)
            {
                if (col.ReferenceSchema != null
                    && col.Name != "CreatedBy" && col.Name != "ModifiedBy"
                    && col.Name != "Type" && col.Name != "Owner")
                {
                    pdf.SetColumnValue(col.ColumnValueName,
                        source.GetColumnValue(col.ColumnValueName));
                }
            }

            pdf.Save();
            return pdf.PrimaryColumnValue;
        }
    }
}
