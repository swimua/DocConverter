namespace WordToPdfService.Services;

public interface IDocumentConverter
{
    /// <summary>
    /// Converts an input document stream (.docx, .doc, .rtf, .odt) into a PDF byte array.
    /// Throws <see cref="ConversionException"/> when the conversion fails.
    /// </summary>
    Task<byte[]> ConvertToPdfAsync(
        Stream input,
        string originalFileName,
        CancellationToken cancellationToken = default);
}

public sealed class ConversionException : Exception
{
    public ConversionException(string message) : base(message) { }
    public ConversionException(string message, Exception inner) : base(message, inner) { }
}
