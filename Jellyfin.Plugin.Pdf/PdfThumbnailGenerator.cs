using PDFtoImage;

namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// Generates thumbnail images from PDF files.
/// </summary>
public sealed class PdfThumbnailGenerator
{
    private readonly PdfThumbnailOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfThumbnailGenerator"/> class.
    /// </summary>
    /// <param name="options">The thumbnail generation options.</param>
    public PdfThumbnailGenerator(PdfThumbnailOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Ensures a thumbnail exists for the given PDF file, generating one if needed.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file.</param>
    /// <param name="thumbnailPath">Path where the thumbnail should be saved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a thumbnail was generated; false if one already existed.</returns>
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
