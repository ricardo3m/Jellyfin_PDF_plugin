using System.Diagnostics;

namespace Jellyfin.Plugin.Pdf;

public sealed class PdfThumbnailGenerator
{
    private readonly PdfThumbnailOptions _options;

    public PdfThumbnailGenerator(PdfThumbnailOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<bool> EnsureThumbnailExistsAsync(
        string pdfPath,
        string thumbnailPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            throw new ArgumentException("PDF path is required.", nameof(pdfPath));
        }

        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            throw new ArgumentException("Thumbnail path is required.", nameof(thumbnailPath));
        }

        if (File.Exists(thumbnailPath))
        {
            return false;
        }

        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF file was not found.", pdfPath);
        }

        var extension = Path.GetExtension(thumbnailPath);
        var usePng = extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
        var formatFlag = usePng ? "-png" : "-jpeg";

        var outputDirectory = Path.GetDirectoryName(thumbnailPath) ?? string.Empty;
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var outputBasePath = Path.Combine(
            outputDirectory,
            Path.GetFileNameWithoutExtension(thumbnailPath));

        var expectedOutputPath = Path.ChangeExtension(outputBasePath, usePng ? ".png" : ".jpg");
        var suffixedOutputPath = outputBasePath + "-1" + (usePng ? ".png" : ".jpg");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "pdftoppm",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        processStartInfo.ArgumentList.Add("-f");
        processStartInfo.ArgumentList.Add("1");
        processStartInfo.ArgumentList.Add("-singlefile");
        processStartInfo.ArgumentList.Add(formatFlag);
        processStartInfo.ArgumentList.Add("-r");
        processStartInfo.ArgumentList.Add(_options.RenderResolutionDpi.ToString());
        processStartInfo.ArgumentList.Add(pdfPath);
        processStartInfo.ArgumentList.Add(outputBasePath);

        Process process;
        try
        {
            process = Process.Start(processStartInfo)
                ?? throw new InvalidOperationException("Unable to start 'pdftoppm' process.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                "The 'pdftoppm' executable was not found. Install Poppler and ensure 'pdftoppm' is available on PATH.",
                ex);
        }

        using (process)
        {
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to generate thumbnail from first page. ExitCode={process.ExitCode}. {error}");
            }
        }

        var producedOutputPath = File.Exists(expectedOutputPath)
            ? expectedOutputPath
            : (File.Exists(suffixedOutputPath) ? suffixedOutputPath : null);

        if (producedOutputPath is null)
        {
            throw new InvalidOperationException("Thumbnail generation completed but no output file was produced.");
        }

        if (!producedOutputPath.Equals(thumbnailPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(producedOutputPath, thumbnailPath);
        }

        return true;
    }
}
