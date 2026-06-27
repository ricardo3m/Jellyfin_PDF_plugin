using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using PDFtoImage;

namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// Provides cover images for PDF files by rendering the first page.
/// Implements <see cref="IRemoteImageProvider"/> so it appears in Jellyfin's
/// library "Image fetchers" settings and is invoked during metadata refresh scans.
/// </summary>
public class PdfImageProvider : IRemoteImageProvider
{
    // Custom URI scheme used to pass the local PDF path through Jellyfin's
    // image-download pipeline (GetImages → GetImageResponse).
    private const string UrlPrefix = "pdf-cover://";

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
    /// <remarks>This name appears in the library's "Image fetchers" settings page.</remarks>
    public string Name => "PDF Thumbnail Provider";

    /// <inheritdoc />
    public bool Supports(BaseItem item)
        => !string.IsNullOrEmpty(item?.Path) &&
           Path.GetExtension(item.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Called during metadata refresh. Returns a pseudo-URL that encodes the
    /// local PDF path; <see cref="GetImageResponse"/> intercepts this URL and
    /// renders the PDF instead of making an HTTP request.
    /// </remarks>
    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var results = new List<RemoteImageInfo>
        {
            new RemoteImageInfo
            {
                ProviderName = Name,
                Url = UrlPrefix + Uri.EscapeDataString(item.Path),
                Type = ImageType.Primary,
            }
        };

        return Task.FromResult<IEnumerable<RemoteImageInfo>>(results);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Intercepts the pseudo-URL produced by <see cref="GetImages"/>, renders the
    /// first page of the PDF with PDFtoImage/SkiaSharp, and returns the JPEG bytes
    /// wrapped in a fake <see cref="HttpResponseMessage"/> — no network call is made.
    /// </remarks>
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        if (!url.StartsWith(UrlPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }

        var pdfPath = Uri.UnescapeDataString(url[UrlPrefix.Length..]);

        if (!File.Exists(pdfPath))
        {
            _logger.LogWarning("PDF file not found: {Path}", pdfPath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        try
        {
            var dpi = Plugin.Instance?.Configuration.RenderResolutionDpi ?? 150;
            var renderOptions = new RenderOptions(Dpi: dpi);

            var memoryStream = new MemoryStream();
            using (var pdfStream = File.OpenRead(pdfPath))
            {
                Conversion.SaveJpeg(
                    memoryStream,
                    pdfStream,
                    page: 0,
                    leaveOpen: false,
                    password: null,
                    options: renderOptions);
            }

            memoryStream.Position = 0;

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StreamContent(memoryStream);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating cover for PDF: {Path}", pdfPath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
