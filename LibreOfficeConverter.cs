using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace WordToPdfService.Services;

public sealed class ConverterOptions
{
    /// <summary>Path to the soffice binary. Default = "soffice" (must be on PATH).</summary>
    public string SofficeBinary { get; set; } = "soffice";

    /// <summary>Hard timeout for a single conversion (seconds).</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Max upload size in MB (read in Program.cs).</summary>
    public int MaxUploadMb { get; set; } = 50;

    /// <summary>Working directory for temp files. Default = system temp.</summary>
    public string? WorkingDirectory { get; set; }
}

/// <summary>
/// Converts office documents to PDF by spawning a headless LibreOffice process.
/// Each conversion uses an isolated user-profile directory so calls are safe to run in parallel.
/// </summary>
public sealed class LibreOfficeConverter : IDocumentConverter
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".rtf", ".odt", ".txt"
    };

    private readonly ConverterOptions _options;
    private readonly ILogger<LibreOfficeConverter> _logger;

    public LibreOfficeConverter(
        IOptions<ConverterOptions> options,
        ILogger<LibreOfficeConverter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<byte[]> ConvertToPdfAsync(
        Stream input,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new ConversionException(
                $"Unsupported file extension '{ext}'. Allowed: {string.Join(", ", AllowedExtensions)}.");

        var workRoot = _options.WorkingDirectory ?? Path.GetTempPath();
        var jobId = Guid.NewGuid().ToString("N");
        var jobDir = Path.Combine(workRoot, $"w2p-{jobId}");
        var profileDir = Path.Combine(jobDir, "profile");
        var inputPath = Path.Combine(jobDir, "input" + ext);
        var outputPath = Path.Combine(jobDir, "input.pdf");

        Directory.CreateDirectory(jobDir);
        Directory.CreateDirectory(profileDir);

        try
        {
            await using (var fs = File.Create(inputPath))
            {
                await input.CopyToAsync(fs, cancellationToken);
            }

            var profileUri = new Uri(profileDir + Path.DirectorySeparatorChar).AbsoluteUri;
            var args =
                $"-env:UserInstallation={profileUri} " +
                "--headless --norestore --nologo --nofirststartwizard " +
                "--convert-to pdf --outdir \"" + jobDir + "\" \"" + inputPath + "\"";

            var psi = new ProcessStartInfo
            {
                FileName = _options.SofficeBinary,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = jobDir
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var stdout = new System.Text.StringBuilder();
            var stderr = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            if (!process.Start())
                throw new ConversionException("Failed to start LibreOffice process.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                if (timeoutCts.IsCancellationRequested)
                    throw new ConversionException(
                        $"LibreOffice timed out after {_options.TimeoutSeconds}s converting '{originalFileName}'.");
                throw;
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("LibreOffice exit code {Code}. stderr: {Err}",
                    process.ExitCode, stderr.ToString());
                throw new ConversionException(
                    $"LibreOffice failed (exit {process.ExitCode}): {stderr.ToString().Trim()}");
            }

            if (!File.Exists(outputPath))
                throw new ConversionException(
                    $"Conversion finished but no PDF was produced. stderr: {stderr.ToString().Trim()}");

            return await File.ReadAllBytesAsync(outputPath, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(jobDir, recursive: true); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not clean up {Dir}", jobDir); }
        }
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}
