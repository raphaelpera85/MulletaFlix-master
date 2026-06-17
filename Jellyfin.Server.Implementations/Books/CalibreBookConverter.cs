using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Books;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Server.Implementations.Books
{
    /// <summary>
    /// Converts book formats to EPUB using Calibre's <c>ebook-convert</c> command-line tool.
    /// </summary>
    public class CalibreBookConverter : IBookToEpubConverter
    {
        private readonly ILogger<CalibreBookConverter> _logger;

        // Timeout for conversion: 5 minutes
        private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(5);

        // Search paths for ebook-convert
        private static readonly string[] PossibleCalibrePaths =
        {
            // Windows (default Calibre install)
            @"C:\Program Files\Calibre2\ebook-convert.exe",
            @"C:\Program Files (x86)\Calibre2\ebook-convert.exe",
            // Portable/stage bundled
            @"calibre\ebook-convert.exe",
            @"calibre-portable\ebook-convert.exe",
            // Linux/Mac (PATH)
            "/usr/bin/ebook-convert",
            "/usr/local/bin/ebook-convert",
        };

        private readonly string? _converterPath;

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mobi", ".azw", ".azw3", ".pdf", ".txt", ".html", ".htm",
            ".doc", ".docx", ".rtf", ".odt", ".pdb", ".prc", ".lit", ".lrf"
        };

        public CalibreBookConverter(ILogger<CalibreBookConverter> logger)
        {
            _logger = logger;

            // Try to find ebook-convert
            _converterPath = FindConverter();
            if (_converterPath != null)
            {
                _logger.LogInformation("BookReader: Found Calibre converter at {Path}", _converterPath);
            }
            else
            {
                _logger.LogWarning(
                    "BookReader: Calibre converter not found. Install Calibre (https://calibre-ebook.com) " +
                    "or place ebook-convert.exe in the server directory. MOBI/AZW conversion will be unavailable.");
            }
        }

        public bool CanConvert(string extension)
        {
            return _converterPath != null && SupportedExtensions.Contains(extension);
        }

        public async Task<BookConversionResult> ConvertToEpubAsync(
            BookConversionRequest request,
            CancellationToken cancellationToken)
        {
            if (_converterPath == null)
            {
                return new BookConversionResult
                {
                    Success = false,
                    ErrorCode = "ConverterNotInstalled",
                    ErrorMessage = "Calibre converter is not installed on the server. " +
                                   "Install Calibre (https://calibre-ebook.com) to enable book conversion."
                };
            }

            if (!File.Exists(request.SourcePath))
            {
                return new BookConversionResult
                {
                    Success = false,
                    ErrorCode = "SourceFileNotFound",
                    ErrorMessage = "The source book file was not found on the server."
                };
            }

            return await RunConversionAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private async Task<BookConversionResult> RunConversionAsync(
            BookConversionRequest request,
            CancellationToken cancellationToken)
        {
            var outputPath = request.OutputPath;
            var sourcePath = request.SourcePath;
            var ext = request.Extension;

            // Validate paths to prevent injection
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(outputPath))
            {
                return new BookConversionResult
                {
                    Success = false,
                    ErrorCode = "InvalidPath",
                    ErrorMessage = "Source or output path is invalid."
                };
            }

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            // Use Process.Start with individual arguments instead of string concatenation
            // to prevent command injection via malicious filenames
            var startInfo = new ProcessStartInfo
            {
                FileName = _converterPath!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // Pass arguments individually to avoid shell injection
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add(outputPath);

            _logger.LogInformation(
                "BookReader: Starting conversion: {Ext} -> EPUB (item {ItemId})",
                ext,
                request.ItemId);

            var stdoutBuilder = new System.Text.StringBuilder();
            var stderrBuilder = new System.Text.StringBuilder();

            try
            {
                using var process = new Process { StartInfo = startInfo };

                process.Start();

                // Read stdout/stderr asynchronously
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                var completed = process.WaitForExit((int)ConversionTimeout.TotalMilliseconds);

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);

                if (!completed)
                {
                    _logger.LogWarning(
                        "BookReader: Conversion timed out after {Timeout}s for item {ItemId}",
                        ConversionTimeout.TotalSeconds,
                        request.ItemId);

                    try { process.Kill(true); } catch { /* ignore */ }

                    return new BookConversionResult
                    {
                        Success = false,
                        ErrorCode = "ConversionTimeout",
                        ErrorMessage = $"The conversion timed out after {ConversionTimeout.TotalSeconds} seconds. " +
                                       "The file may be too large or corrupted."
                    };
                }

                if (process.ExitCode != 0)
                {
                    _logger.LogError(
                        "BookReader: Conversion failed for item {ItemId}. Exit code: {ExitCode}. Stderr: {Stderr}",
                        request.ItemId,
                        process.ExitCode,
                        TruncateStderr(stderr));

                    return new BookConversionResult
                    {
                        Success = false,
                        ErrorCode = "ConversionFailed",
                        ErrorMessage = $"The book could not be converted to EPUB. " +
                                       $"Converter exited with code {process.ExitCode}."
                    };
                }

                if (!File.Exists(outputPath))
                {
                    _logger.LogError(
                        "BookReader: Conversion reported success but output file not found: {Path}",
                        outputPath);

                    return new BookConversionResult
                    {
                        Success = false,
                        ErrorCode = "OutputFileNotFound",
                        ErrorMessage = "The conversion completed but the output file was not created."
                    };
                }

                var fileSize = new FileInfo(outputPath).Length;
                _logger.LogInformation(
                    "BookReader: Conversion successful for item {ItemId}. Output: {Path} ({Size} bytes)",
                    request.ItemId,
                    outputPath,
                    fileSize);

                return new BookConversionResult
                {
                    Success = true,
                    EpubPath = outputPath
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("BookReader: Conversion cancelled for item {ItemId}", request.ItemId);
                return new BookConversionResult
                {
                    Success = false,
                    ErrorCode = "Cancelled",
                    ErrorMessage = "The conversion was cancelled."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BookReader: Unexpected error converting item {ItemId}", request.ItemId);
                return new BookConversionResult
                {
                    Success = false,
                    ErrorCode = "UnexpectedError",
                    ErrorMessage = "An unexpected error occurred during conversion."
                };
            }
        }

        private static string? FindConverter()
        {
            // Check predefined paths
            foreach (var path in PossibleCalibrePaths)
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }

            // Try PATH environment variable
            try
            {
                var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
                foreach (var dir in paths)
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    var candidate = Path.Combine(dir, "ebook-convert.exe");
                    if (File.Exists(candidate))
                        return candidate;

                    candidate = Path.Combine(dir, "ebook-convert");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch
            {
                // Ignore PATH errors
            }

            return null;
        }

        private static string TruncateStderr(string stderr)
        {
            if (string.IsNullOrEmpty(stderr))
                return "(empty)";

            var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // Take first 5 lines and last 3 lines
            var relevant = lines.Take(5).Concat(lines.TakeLast(3));
            return string.Join("; ", relevant);
        }
    }
}
