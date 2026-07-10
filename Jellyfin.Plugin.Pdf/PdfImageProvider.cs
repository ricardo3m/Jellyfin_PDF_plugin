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

                byte[] encodedBytes;

                if (PdfPageImageExtractor.TryExtractFirstPageImage(pdfPath, out var extractedImageBytes, out var rotationDegrees) &&
                    extractedImageBytes is not null)
                {
                    // The first page is essentially just a single full-page image (e.g. a
                    // scanned cover) with no other content: use the embedded image directly
                    // instead of rasterizing.
                    _logger.LogDebug("Using embedded full-page image as cover for {Path}", pdfPath);
                    encodedBytes = BuildCover(extractedImageBytes, paddingMode, rotationDegrees);
                }
                else
                {
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

                    encodedBytes = BuildCover(pageBytes, paddingMode);

                    // SkiaSharp's PNG encoder does not embed pixel-density metadata, so viewers
                    // otherwise default to reporting 96 DPI regardless of the configured render
                    // resolution. Embed a pHYs chunk so the file reports the actual DPI used.
                    encodedBytes = PngDpiMetadata.EmbedDpi(encodedBytes, dpi);
                }

                return new MemoryStream(encodedBytes);
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

    /// <summary>
    /// Decodes a source image, applies any required rotation and the configured padding mode,
    /// producing the final encoded PNG cover bytes.
    /// </summary>
    /// <param name="sourceImageBytes">The source image bytes (PNG or JPEG).</param>
    /// <param name="paddingMode">The padding mode to apply.</param>
    /// <param name="rotationDegrees">Clockwise rotation (0, 90, 180 or 270) to apply first.</param>
    /// <returns>The encoded PNG bytes.</returns>
    private static byte[] BuildCover(byte[] sourceImageBytes, Configuration.PaddingMode paddingMode, int rotationDegrees = 0)
    {
        using var decodedBitmap = SKBitmap.Decode(sourceImageBytes);
        using var rotatedBitmap = rotationDegrees == 0 ? null : Rotate(decodedBitmap, rotationDegrees);
        var pageBitmap = rotatedBitmap ?? decodedBitmap;

        if (paddingMode == Configuration.PaddingMode.None)
        {
            // No padding: output the page at its original aspect ratio.
            using var data = pageBitmap.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

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
        using var squareData = squareBitmap.Encode(SKEncodedImageFormat.Png, 100);
        return squareData.ToArray();
    }

    /// <summary>
    /// Returns a new bitmap with <paramref name="source"/> rotated clockwise by
    /// <paramref name="degrees"/> (must be 90, 180 or 270).
    /// </summary>
    private static SKBitmap Rotate(SKBitmap source, int degrees)
    {
        var swapDimensions = degrees == 90 || degrees == 270;
        var width = swapDimensions ? source.Height : source.Width;
        var height = swapDimensions ? source.Width : source.Height;

        var rotated = new SKBitmap(width, height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(rotated);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return rotated;
    }
}
