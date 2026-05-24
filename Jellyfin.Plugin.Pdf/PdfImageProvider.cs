using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using PDFtoImage;

namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// Provides thumbnail images for PDF files by rendering the first page.
/// </summary>
public class PdfImageProvider : IDynamicImageProvider
{
    private readonly ILogger<PdfImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfImageProvider"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{PdfImageProvider}"/> interface.</param>
    public PdfImageProvider(ILogger<PdfImageProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "PDF Thumbnail Provider";

    /// <inheritdoc />
    public bool Supports(BaseItem item)
    {
        if (item == null || string.IsNullOrEmpty(item.Path))
        {
            return false;
        }

        return Path.GetExtension(item.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetImage(BaseItem item, ImageType type, CancellationToken cancellationToken)
    {
        if (item == null || string.IsNullOrEmpty(item.Path))
        {
            return Task.FromResult(new DynamicImageResponse { HasImage = false });
        }

        var pdfPath = item.Path;

        if (!File.Exists(pdfPath))
        {
            _logger.LogWarning("PDF file not found: {Path}", pdfPath);
            return Task.FromResult(new DynamicImageResponse { HasImage = false });
        }

        MemoryStream? memoryStream = null;
        try
        {
            var dpi = Plugin.Instance?.Configuration.RenderResolutionDpi ?? 150;
            var renderOptions = new RenderOptions(Dpi: dpi);

            using var pdfStream = File.OpenRead(pdfPath);
            memoryStream = new MemoryStream();

            Conversion.SaveJpeg(memoryStream, pdfStream, page: 0, leaveOpen: true, password: null, options: renderOptions);
            memoryStream.Position = 0;

            return Task.FromResult(new DynamicImageResponse
            {
                HasImage = true,
                Stream = memoryStream,
                Format = ImageFormat.Jpg
            });
        }
        catch (Exception ex)
        {
            memoryStream?.Dispose();
            _logger.LogError(ex, "Error generating thumbnail for PDF: {Path}", pdfPath);
            return Task.FromResult(new DynamicImageResponse { HasImage = false });
        }
    }
}
