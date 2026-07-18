using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using ImageSharpImage = SixLabors.ImageSharp.Image;

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
            var maxDimension = config?.MaxImageDimension ?? 0;
            var encodeSettings = new EncodeSettings(
                config?.OutputFormat ?? Configuration.OutputImageFormat.Png,
                config?.WebpLossless ?? true,
                config?.ImageQuality ?? 90,
                config?.LosslessEffort ?? 100,
                config?.Engine ?? Configuration.EncoderEngine.SkiaOnly,
                config?.WebpMethod ?? 4,
                config?.WebpNearLossless ?? false,
                config?.JpegSubsampling ?? Configuration.JpegChromaSubsampling.Subsample420);

            _logger.LogDebug(
                "Generating cover for {Path}: dpi={Dpi} format={Format} engine={Engine} quality={Quality} effort={Effort} maxDim={MaxDim} padding={Padding}",
                pdfPath, dpi, encodeSettings.Format, encodeSettings.Engine, encodeSettings.Quality, encodeSettings.LosslessEffort, maxDimension, paddingMode);

            var memoryStream = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] encodedBytes;

                if (PdfPageImageExtractor.TryExtractFirstPageImage(pdfPath, out var extractedImageBytes, out var rotationDegrees, out var displayedAspectRatio) &&
                    extractedImageBytes is not null)
                {
                    // The first page is essentially just a single full-page image (e.g. a
                    // scanned cover) with no other content: use the embedded image directly
                    // instead of rasterizing.
                    _logger.LogDebug("Using embedded full-page image as cover for {Path}", pdfPath);
                    encodedBytes = BuildCover(extractedImageBytes, paddingMode, rotationDegrees, displayedAspectRatio, maxDimension, encodeSettings, _logger);
                }
                else
                {
                    // Render with an explicit white background so PDFs that have no
                    // background fill of their own don't produce a transparent page (which
                    // shows as grey in dark-themed clients such as Jellyfin's default UI).
                    var renderOptions = new RenderOptions(Dpi: dpi, BackgroundColor: SKColors.White);

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

                    encodedBytes = BuildCover(pageBytes, paddingMode, maxImageDimension: maxDimension, settings: encodeSettings, logger: _logger);

                    // SkiaSharp's PNG encoder does not embed pixel-density metadata, so viewers
                    // otherwise default to reporting 96 DPI regardless of the configured render
                    // resolution. Embed a pHYs chunk so the file reports the actual DPI used.
                    // When a resolution limit downscales the page, the effective DPI drops
                    // proportionally, so report that reduced value instead of the configured one.
                    // Only PNG carries this chunk; other formats store density differently or not
                    // at all, which is irrelevant for cover display.
                    if (encodeSettings.Format == Configuration.OutputImageFormat.Png)
                    {
                        var effectiveDpi = dpi;
                        if (maxDimension > 0)
                        {
                            var longestSide = GetLongestSide(pageBytes);
                            if (longestSide > maxDimension)
                            {
                                effectiveDpi = Math.Max(1, (int)Math.Round(dpi * (double)maxDimension / longestSide));
                            }
                        }

                        encodedBytes = PngDpiMetadata.EmbedDpi(encodedBytes, effectiveDpi);
                    }
                }

                return new MemoryStream(encodedBytes);
            }, cancellationToken).ConfigureAwait(false);

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StreamContent(memoryStream);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(encodeSettings.Format));
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
    /// <param name="displayedAspectRatio">
    /// The width-to-height ratio the page displays the image at. When greater than zero and it
    /// differs from the decoded image's native pixel aspect ratio, the image is stretched to
    /// match, reproducing how a PDF viewer paints the image into the page rectangle. This avoids
    /// a vertically (or horizontally) squashed cover when the embedded image's stored pixels have
    /// a different aspect ratio than the page.
    /// </param>
    /// <param name="maxImageDimension">
    /// The maximum allowed size (in pixels) of the page's longest side. Zero means no limit. The
    /// limit only ever reduces the working size; it never enlarges the image.
    /// </param>
    /// <param name="settings">The output format and quality/effort settings used to encode.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>The encoded image bytes.</returns>
    private static byte[] BuildCover(byte[] sourceImageBytes, Configuration.PaddingMode paddingMode, int rotationDegrees = 0, double displayedAspectRatio = 0, int maxImageDimension = 0, EncodeSettings settings = default, ILogger? logger = null)
    {
        // Always try to build the cover at the page's full resolution first so no quality is
        // lost. Very high render DPI or large pages can, however, produce bitmaps (especially the
        // square padding canvas) that exceed the per-bitmap size limit, which makes SkiaSharp
        // throw "Unable to allocate pixels for the bitmap". Rather than fail and leave the item
        // with no cover at all, shrink the working size in small 500px steps until the allocation
        // succeeds; the reduction only ever happens after the full-resolution attempt has failed.
        const int StepPx = 500;
        const int MinWorkingSide = 500;

        // A configured resolution limit (> 0) caps the longest side of the page. Zero keeps the
        // full render resolution.
        var configuredCap = maxImageDimension > 0 ? maxImageDimension : 0;

        Exception lastAllocationFailure;
        try
        {
            return BuildCoverCore(sourceImageBytes, paddingMode, rotationDegrees, displayedAspectRatio, configuredCap, settings, logger);
        }
        catch (Exception ex) when (IsPixelAllocationFailure(ex))
        {
            lastAllocationFailure = ex;
        }

        // The full-resolution (or capped) attempt failed; start just below the page's actual
        // longest side and step down 500px at a time. Never start above the configured cap so the
        // resolution limit is always respected.
        var longestSide = GetLongestSide(sourceImageBytes);
        var startCap = longestSide > StepPx ? longestSide - StepPx : 6000;
        if (configuredCap > 0)
        {
            startCap = Math.Min(startCap, configuredCap);
        }

        for (var cap = startCap; cap >= MinWorkingSide; cap -= StepPx)
        {
            try
            {
                return BuildCoverCore(sourceImageBytes, paddingMode, rotationDegrees, displayedAspectRatio, cap, settings, logger);
            }
            catch (Exception ex) when (IsPixelAllocationFailure(ex))
            {
                lastAllocationFailure = ex;
            }
        }

        throw lastAllocationFailure;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="ex"/> represents a failure to allocate the pixel
    /// buffer for a bitmap (out of memory), which is recoverable by using a smaller working size.
    /// </summary>
    private static bool IsPixelAllocationFailure(Exception ex) =>
        ex is OutOfMemoryException ||
        ex.Message.Contains("allocate", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads only the header of <paramref name="imageBytes"/> to return the longest pixel
    /// dimension of the encoded image, without allocating its full pixel buffer. Returns 0 when
    /// the dimensions cannot be determined.
    /// </summary>
    private static int GetLongestSide(byte[] imageBytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(imageBytes));
        if (codec is null)
        {
            return 0;
        }

        var info = codec.Info;
        return Math.Max(info.Width, info.Height);
    }

    /// <summary>
    /// Builds the cover image. When <paramref name="maxWorkingSide"/> is greater than zero, the
    /// page is scaled so neither dimension exceeds it before compositing; zero means full
    /// resolution (no reduction).
    /// </summary>
    private static byte[] BuildCoverCore(
        byte[] sourceImageBytes,
        Configuration.PaddingMode paddingMode,
        int rotationDegrees,
        double displayedAspectRatio,
        int maxWorkingSide,
        EncodeSettings settings,
        ILogger? logger)
    {
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

        using var decodedBitmap = SKBitmap.Decode(sourceImageBytes)
            ?? throw new InvalidOperationException("Failed to decode the source image.");
        using var rotatedBitmap = rotationDegrees == 0 ? null : Rotate(decodedBitmap, rotationDegrees);
        var orientedBitmap = rotatedBitmap ?? decodedBitmap;

        // Bound the input before the aspect correction so the correction itself cannot allocate an
        // oversized bitmap when a cap is in effect.
        using var preCapBitmap = maxWorkingSide > 0 ? Downscale(orientedBitmap, maxWorkingSide, sampling) : null;
        var basisBitmap = preCapBitmap ?? orientedBitmap;

        using var reshapedBitmap = ReshapeToAspect(basisBitmap, displayedAspectRatio);
        var aspectBitmap = reshapedBitmap ?? basisBitmap;

        using var postCapBitmap = maxWorkingSide > 0 ? Downscale(aspectBitmap, maxWorkingSide, sampling) : null;
        var pageBitmap = postCapBitmap ?? aspectBitmap;

        if (paddingMode == Configuration.PaddingMode.None)
        {
            // No padding: output the page at its original aspect ratio.
            return EncodeBitmap(pageBitmap, settings, logger);
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
        return EncodeBitmap(squareBitmap, settings, logger);
    }

    /// <summary>
    /// Encodes <paramref name="bitmap"/> using the format and quality/effort in
    /// <paramref name="settings"/>. JPEG has no alpha channel, so an image with transparency is
    /// first flattened onto a white background to avoid a black fill. The final encode step is
    /// dispatched to either SkiaSharp or ImageSharp according to <see cref="EncodeSettings.Engine"/>.
    /// </summary>
    private static byte[] EncodeBitmap(SKBitmap bitmap, EncodeSettings settings, ILogger? logger = null)
    {
        SKBitmap? flattened = null;
        try
        {
            var target = bitmap;
            if (settings.Format == Configuration.OutputImageFormat.Jpeg && !bitmap.Info.IsOpaque)
            {
                flattened = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                using (var flattenCanvas = new SKCanvas(flattened))
                {
                    flattenCanvas.Clear(SKColors.White);
                    flattenCanvas.DrawBitmap(bitmap, 0, 0);
                    flattenCanvas.Flush();
                }

                target = flattened;
            }

            if (settings.Engine == Configuration.EncoderEngine.SkiaImageSharp)
            {
                if (TryEncodeWithImageSharp(target, settings, out var imageSharpBytes, logger))
                {
                    return imageSharpBytes;
                }

                // ImageSharp failed or produced undecodable output; fall back to Skia.
                logger?.LogDebug(
                    "ImageSharp encode failed for {Format} {Width}x{Height}; using SkiaSharp encoder.",
                    settings.Format, target.Width, target.Height);
            }

            return EncodeWithSkia(target, settings);
        }
        finally
        {
            flattened?.Dispose();
        }
    }

    /// <summary>
    /// Encodes <paramref name="target"/> with SkiaSharp using the format-specific encoder options.
    /// </summary>
    private static byte[] EncodeWithSkia(SKBitmap target, EncodeSettings settings)
    {
        using var stream = new SKDynamicMemoryWStream();
        using var pixmap = target.PeekPixels()
            ?? throw new InvalidOperationException("Failed to access bitmap pixels for encoding.");

        bool encoded;
        switch (settings.Format)
        {
            case Configuration.OutputImageFormat.Webp:
                var webpCompression = settings.WebpLossless
                    ? SKWebpEncoderCompression.Lossless
                    : SKWebpEncoderCompression.Lossy;
                var webpQuality = settings.WebpLossless ? settings.LosslessEffort : settings.Quality;
                encoded = pixmap.Encode(stream, new SKWebpEncoderOptions(webpCompression, webpQuality));
                break;

            case Configuration.OutputImageFormat.Jpeg:
                encoded = pixmap.Encode(
                    stream,
                    new SKJpegEncoderOptions(settings.Quality, MapSkiaDownsample(settings.JpegSubsampling), SKJpegEncoderAlphaOption.Ignore));
                break;

            default:
                // PNG is always lossless; map the 0–100 effort onto the zlib level (0–9).
                var zlibLevel = Math.Clamp((int)Math.Round(settings.LosslessEffort / 100.0 * 9), 0, 9);
                encoded = pixmap.Encode(stream, new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zlibLevel));
                break;
        }

        if (!encoded)
        {
            throw new InvalidOperationException($"Failed to encode image as {settings.Format}.");
        }

        using var data = stream.DetachAsData();
        return data.ToArray();
    }

    /// <summary>
    /// Attempts to encode <paramref name="target"/> with ImageSharp, which exposes WebP controls
    /// (encoding method and near-lossless) that SkiaSharp lacks. Returns <c>false</c> (without
    /// throwing) if the pixel bridge or encode fails, so the caller can fall back to SkiaSharp.
    /// </summary>
    private static bool TryEncodeWithImageSharp(SKBitmap target, EncodeSettings settings, out byte[] result, ILogger? logger = null)
    {
        try
        {
            using var image = ToImageSharpImage(target);
            IImageEncoder encoder;
            switch (settings.Format)
            {
                case Configuration.OutputImageFormat.Webp:
                    encoder = new WebpEncoder
                    {
                        FileFormat = settings.WebpLossless
                            ? WebpFileFormatType.Lossless
                            : WebpFileFormatType.Lossy,
                        Quality = settings.WebpLossless ? settings.LosslessEffort : settings.Quality,
                        Method = (WebpEncodingMethod)Math.Clamp(settings.WebpMethod, 0, 6),
                        NearLossless = settings.WebpLossless && settings.WebpNearLossless,
                    };
                    break;

                case Configuration.OutputImageFormat.Jpeg:
                    encoder = new JpegEncoder
                    {
                        Quality = settings.Quality,
                        ColorType = MapImageSharpJpegColor(settings.JpegSubsampling),
                    };
                    break;

                default:
                    // Suppress ImageSharp's own pHYs chunk so the shared PngDpiMetadata step (which
                    // reflects any resolution-limit downscale) remains the single source of density.
                    image.Metadata.ResolutionUnits = PixelResolutionUnit.AspectRatio;
                    encoder = new PngEncoder
                    {
                        CompressionLevel = (PngCompressionLevel)Math.Clamp(
                            (int)Math.Round(settings.LosslessEffort / 100.0 * 9), 0, 9),
                    };
                    break;
            }

            using var stream = new MemoryStream();
            image.Save(stream, encoder);
            result = stream.ToArray();

            // Validate that SkiaSharp can fully decode the result. ImageSharp 2.x can silently
            // produce a malformed bitstream for certain inputs (e.g. WebP lossy on large bitmaps),
            // where the container header is valid (SKCodec.Create succeeds) but the pixel data
            // is corrupt (actual decode fails). SKBitmap.Decode performs a full decode and returns
            // null on any bitstream error, which is exactly what Jellyfin uses when serving and
            // processing images. Return false so the caller falls back to EncodeWithSkia.
            using (var decodedValidation = SKBitmap.Decode(result))
            {
                if (decodedValidation is null)
                {
                    logger?.LogWarning(
                        "ImageSharp produced an undecodable {Format} output ({Width}x{Height}); falling back to the SkiaSharp encoder.",
                        settings.Format, target.Width, target.Height);
                    result = Array.Empty<byte>();
                    return false;
                }
            }

            return true;
        }
        catch (Exception)
        {
            result = Array.Empty<byte>();
            return false;
        }
    }

    /// <summary>
    /// Bridges an <see cref="SKBitmap"/> to an ImageSharp <see cref="Image{Rgba32}"/>, reading the
    /// pixels as straight (unpremultiplied) RGBA so channel order and alpha match ImageSharp's
    /// <see cref="Rgba32"/> layout and avoid color fringing on transparent edges.
    /// </summary>
    private static Image<Rgba32> ToImageSharpImage(SKBitmap bitmap)
    {
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var buffer = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            using var pixmap = bitmap.PeekPixels()
                ?? throw new InvalidOperationException("Failed to access bitmap pixels for the ImageSharp bridge.");
            if (!pixmap.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0))
            {
                throw new InvalidOperationException("Failed to read bitmap pixels for the ImageSharp bridge.");
            }
        }
        finally
        {
            handle.Free();
        }

        return ImageSharpImage.LoadPixelData<Rgba32>(buffer, bitmap.Width, bitmap.Height);
    }

    /// <summary>
    /// Maps the configured JPEG subsampling to the SkiaSharp downsample option.
    /// </summary>
    private static SKJpegEncoderDownsample MapSkiaDownsample(Configuration.JpegChromaSubsampling subsampling) => subsampling switch
    {
        Configuration.JpegChromaSubsampling.Subsample444 => SKJpegEncoderDownsample.Downsample444,
        Configuration.JpegChromaSubsampling.Subsample422 => SKJpegEncoderDownsample.Downsample422,
        _ => SKJpegEncoderDownsample.Downsample420,
    };

    /// <summary>
    /// Maps the configured JPEG subsampling to the ImageSharp color type. ImageSharp's JPEG
    /// encoder does not support 4:2:2, so that setting falls back to 4:2:0.
    /// </summary>
    private static JpegColorType MapImageSharpJpegColor(Configuration.JpegChromaSubsampling subsampling) => subsampling switch
    {
        Configuration.JpegChromaSubsampling.Subsample444 => JpegColorType.YCbCrRatio444,
        _ => JpegColorType.YCbCrRatio420,
    };

    /// <summary>
    /// Returns the MIME content type for the given output <paramref name="format"/>.
    /// </summary>
    private static string GetContentType(Configuration.OutputImageFormat format) => format switch
    {
        Configuration.OutputImageFormat.Webp => "image/webp",
        Configuration.OutputImageFormat.Jpeg => "image/jpeg",
        _ => "image/png",
    };

    /// <summary>
    /// The output format and quality/effort settings used to encode the final cover image.
    /// </summary>
    private readonly record struct EncodeSettings(
        Configuration.OutputImageFormat Format,
        bool WebpLossless,
        int Quality,
        int LosslessEffort,
        Configuration.EncoderEngine Engine,
        int WebpMethod,
        bool WebpNearLossless,
        Configuration.JpegChromaSubsampling JpegSubsampling);

    /// <summary>
    /// Returns a copy of <paramref name="source"/> scaled down so that neither dimension exceeds
    /// <paramref name="maxSide"/> (preserving aspect ratio), or <c>null</c> when it already fits.
    /// </summary>
    private static SKBitmap? Downscale(SKBitmap source, int maxSide, SKSamplingOptions sampling)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxSide || longest <= 0)
        {
            return null;
        }

        var scale = (double)maxSide / longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var scaled = new SKBitmap(width, height, source.ColorType, source.AlphaType);
        if (!source.ScalePixels(scaled, sampling))
        {
            scaled.Dispose();
            return null;
        }

        return scaled;
    }

    /// <summary>
    /// Returns a new bitmap resized so its pixel aspect ratio matches
    /// <paramref name="targetAspectRatio"/> (width / height), or <c>null</c> when no reshaping is
    /// needed. To preserve all of the source's detail, the proportionally-too-small dimension is
    /// enlarged (matching how a PDF viewer stretches the image into the page rectangle) rather
    /// than discarding pixels from the other dimension.
    /// </summary>
    private static SKBitmap? ReshapeToAspect(SKBitmap source, double targetAspectRatio)
    {
        if (targetAspectRatio <= 0 || source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        var currentAspect = (double)source.Width / source.Height;

        // Ignore negligible differences (rounding / sub-pixel) to avoid needless re-encoding.
        if (Math.Abs(currentAspect - targetAspectRatio) / targetAspectRatio <= 0.01)
        {
            return null;
        }

        int width;
        int height;
        if (currentAspect > targetAspectRatio)
        {
            // Too wide for the target: make it taller.
            width = source.Width;
            height = (int)Math.Round(source.Width / targetAspectRatio);
        }
        else
        {
            // Too tall for the target: make it wider.
            height = source.Height;
            width = (int)Math.Round(source.Height * targetAspectRatio);
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var resized = new SKBitmap(width, height, source.ColorType, source.AlphaType);
        if (!source.ScalePixels(resized, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)))
        {
            resized.Dispose();
            return null;
        }

        return resized;
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
