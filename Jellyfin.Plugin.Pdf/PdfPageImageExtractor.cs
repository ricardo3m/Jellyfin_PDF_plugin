using System.Linq;
using UglyToad.PdfPig;

namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// Detects when the first page of a PDF is essentially just a single embedded image
/// (e.g. a scanned cover) and extracts that image directly, avoiding an unnecessary
/// rasterization pass through PDFium.
/// </summary>
internal static class PdfPageImageExtractor
{
    /// <summary>Minimum fraction of the page area the image must cover to be treated as "the whole page".</summary>
    private const double FullPageCoverageThreshold = 0.90;

    /// <summary>
    /// Attempts to extract the first page's image directly, when the first page consists of
    /// nothing but a single image covering (almost) the entire page. If the page has any other
    /// content (text, vector graphics, multiple images, etc.) this returns false so the caller
    /// falls back to full rasterization.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file.</param>
    /// <param name="imageBytes">The extracted image bytes (PNG or JPEG), if successful.</param>
    /// <param name="rotationDegrees">
    /// The clockwise rotation (0, 90, 180 or 270) declared by the page that must be applied to
    /// the extracted image bytes to display them in the correct orientation.
    /// </param>
    /// <param name="displayedAspectRatio">
    /// The width-to-height ratio the page displays the image at (after any page rotation). The
    /// embedded image's raw pixels may be stored at a different aspect ratio and are stretched by
    /// a PDF viewer to fill the page rectangle; the caller must reproduce that stretch to avoid a
    /// squashed result.
    /// </param>
    /// <returns>True if a full-page image was found and extracted.</returns>
    public static bool TryExtractFirstPageImage(string pdfPath, out byte[]? imageBytes, out int rotationDegrees, out double displayedAspectRatio)
    {
        imageBytes = null;
        rotationDegrees = 0;
        displayedAspectRatio = 0;

        try
        {
            using var document = PdfDocument.Open(pdfPath);

            if (document.NumberOfPages < 1)
            {
                return false;
            }

            var page = document.GetPage(1);

            // If the page has any text (or other content besides the single image), it must be
            // rendered normally so that content isn't lost.
            if (page.Letters.Count > 0)
            {
                return false;
            }

            var images = page.GetImages().ToList();

            if (images.Count != 1)
            {
                return false;
            }

            var image = images[0];
            var pageArea = (double)page.Width * (double)page.Height;

            if (pageArea <= 0)
            {
                return false;
            }

            var imageArea = (double)image.Bounds.Width * (double)image.Bounds.Height;

            if (imageArea / pageArea < FullPageCoverageThreshold)
            {
                return false;
            }

            byte[]? extractedBytes = null;

            // Most common raster encodings (Flate, CCITT) can be converted to PNG directly.
            if (image.TryGetPng(out var png))
            {
                extractedBytes = png;
            }
            else
            {
                // JPEG-encoded (DCTDecode) images can't be decoded to raw samples without an
                // extra filter package, but the raw bytes are already a complete, usable JPEG file.
                var raw = image.RawBytes.ToArray();
                if (raw.Length > 2 && raw[0] == 0xFF && raw[1] == 0xD8)
                {
                    extractedBytes = raw;
                }
            }

            if (extractedBytes is null)
            {
                return false;
            }

            imageBytes = extractedBytes;

            // The raw image bytes don't carry the page's /Rotate entry, so the caller must
            // rotate them (clockwise) to match how a PDF viewer would display the page.
            rotationDegrees = ((page.Rotation.Value % 360) + 360) % 360;

            // The rectangle the image is painted into (in page space). A 90/270 page rotation
            // swaps width and height as seen by the viewer.
            var placedWidth = (double)image.Bounds.Width;
            var placedHeight = (double)image.Bounds.Height;
            displayedAspectRatio = placedHeight <= 0
                ? 0
                : (rotationDegrees == 90 || rotationDegrees == 270)
                    ? placedHeight / placedWidth
                    : placedWidth / placedHeight;
            return true;
        }
        catch
        {
            // Any parsing failure (encrypted, malformed, unsupported PDF, etc.) falls back
            // to the normal PDFium rasterization path.
            return false;
        }
    }
}

