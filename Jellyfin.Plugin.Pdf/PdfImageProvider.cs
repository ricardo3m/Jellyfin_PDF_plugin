using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
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
    /// <remarks>
    /// Returns true for all <see cref="Book"/> items so that the provider
    /// appears in the library settings UI. PDF-only filtering is done in
    /// <see cref="GetImages"/> to avoid blocking other book types.
    /// </remarks>
    public bool Supports(BaseItem item) => item is Book;

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
        if (string.IsNullOrEmpty(item.Path) ||
            !Path.GetExtension(item.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Enumerable.Empty<RemoteImageInfo>());
        }

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
    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        if (!url.StartsWith(UrlPrefix, StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        var pdfPath = Uri.UnescapeDataString(url[UrlPrefix.Length..]);

        if (!File.Exists(pdfPath))
        {
            _logger.LogWarning("PDF file not found: {Path}", pdfPath);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        try
        {
            var config = Plugin.Instance?.Configuration;
            var dpi = config?.RenderResolutionDpi ?? 150;
            var paddingMode = config?.ThumbnailPaddingMode ?? Configuration.PaddingMode.White;

            var memoryStream = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var renderOptions = new RenderOptions(Dpi: dpi);

                // Render the first PDF page to an in-memory bitmap.
                byte[] pageBytes;
                using (var pageStream = new MemoryStream())
                using (var pdfStream = File.OpenRead(pdfPath))
                {
                    Conversion.SavePng(
                        pageStream,
                        pdfStream,
                        page: (Index)0,
                        leaveOpen: false,
                        password: null,
                        options: renderOptions);

                    // Decode via byte array to avoid SKManagedStream (which wraps a .NET Stream
                    // with native callbacks). SKManagedStream finalizers can cause an
                    // InvalidCastException when the GC finalizer thread crosses assembly contexts.
                    pageBytes = pageStream.ToArray();
                }

                cancellationToken.ThrowIfCancellationRequested();

                using var pageBitmap = SKBitmap.Decode(pageBytes);

                var result = new MemoryStream();

                if (paddingMode == Configuration.PaddingMode.None)
                {
                    // No padding: output the page at its original aspect ratio.
                    pageBitmap.Encode(result, SKEncodedImageFormat.Png, 100);
                }
                else
                {
                    var side = Math.Max(pageBitmap.Width, pageBitmap.Height);
                    var fillColor = paddingMode == Configuration.PaddingMode.White
                        ? SKColors.White
                        : SKColors.Transparent;

                    using var squareBitmap = new SKBitmap(side, side, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using var canvas = new SKCanvas(squareBitmap);
                    canvas.Clear(fillColor);
                    canvas.DrawBitmap(
                        pageBitmap,
                        (side - pageBitmap.Width) / 2f,
                        (side - pageBitmap.Height) / 2f);
                    canvas.Flush();
                    squareBitmap.Encode(result, SKEncodedImageFormat.Png, 100);
                }

                result.Position = 0;
                return result;
            }, cancellationToken).ConfigureAwait(false);

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StreamContent(memoryStream);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        }
        catch (OperationCanceledException)
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating cover for PDF: {Path}", pdfPath);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }
}
