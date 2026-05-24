using PDFtoImage;

namespace Jellyfin.Plugin.Pdf;

public sealed class PdfThumbnailGenerator
{
    private readonly PdfThumbnailOptions _options;

    public PdfThumbnailGenerator(PdfThumbnailOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<bool> EnsureThumbnailExistsAsync(
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
            return Task.FromResult(false);
        }

        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF file was not found.", pdfPath);
        }

        var outputDirectory = Path.GetDirectoryName(thumbnailPath) ?? string.Empty;
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var extension = Path.GetExtension(thumbnailPath);
        var usePng = extension.Equals(".png", StringComparison.OrdinalIgnoreCase);

        var renderOptions = new RenderOptions(Dpi: _options.RenderResolutionDpi);

        using var pdfStream = File.OpenRead(pdfPath);

        if (usePng)
        {
            Conversion.SavePng(thumbnailPath, pdfStream, page: 0, leaveOpen: false, password: null, options: renderOptions);
        }
        else
        {
            Conversion.SaveJpeg(thumbnailPath, pdfStream, page: 0, leaveOpen: false, password: null, options: renderOptions);
        }

        return Task.FromResult(true);
    }
}
