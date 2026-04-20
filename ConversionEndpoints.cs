using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WordToPdfService.Services;

namespace WordToPdfService.Endpoints;

public static class ConversionEndpoints
{
    public static IEndpointRouteBuilder MapConversionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization();

        // POST /api/v1/convert
        // Multipart form with field "file" containing a .docx (or other supported type).
        // Returns application/pdf.
        group.MapPost("/convert",
            async Task<Results<FileContentHttpResult, BadRequest<ErrorDto>, ProblemHttpResult>> (
                HttpRequest request,
                IDocumentConverter converter,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Convert");

                if (!request.HasFormContentType)
                    return TypedResults.BadRequest(new ErrorDto(
                        "invalid_request", "Content-Type must be multipart/form-data."));

                var form = await request.ReadFormAsync(ct);
                var file = form.Files["file"] ?? form.Files.FirstOrDefault();

                if (file is null || file.Length == 0)
                    return TypedResults.BadRequest(new ErrorDto(
                        "missing_file", "No file uploaded. Send it as a multipart field named 'file'."));

                try
                {
                    await using var stream = file.OpenReadStream();
                    var pdf = await converter.ConvertToPdfAsync(stream, file.FileName, ct);

                    var pdfName = Path.GetFileNameWithoutExtension(file.FileName) + ".pdf";
                    logger.LogInformation("Converted {In} -> {Out} ({Bytes} bytes)",
                        file.FileName, pdfName, pdf.Length);

                    return TypedResults.File(pdf, "application/pdf", pdfName);
                }
                catch (ConversionException ex)
                {
                    logger.LogWarning(ex, "Conversion failed for {File}", file.FileName);
                    return TypedResults.Problem(
                        title: "Conversion failed",
                        detail: ex.Message,
                        statusCode: Microsoft.AspNetCore.Http.StatusCodes.Status422UnprocessableEntity);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // 499 = "Client Closed Request" (non-standard but widely used).
                    return TypedResults.Problem(
                        title: "Request cancelled",
                        statusCode: 499);
                }
            })
            .DisableAntiforgery()
            .WithName("ConvertDocumentToPdf");

        return app;
    }
}

public record ErrorDto(string Code, string Message);
