namespace Jellyfin.Plugin.Pdf.Configuration;

/// <summary>
/// The image format used to encode generated thumbnails.
/// </summary>
public enum OutputImageFormat
{
    /// <summary>PNG (lossless).</summary>
    Png = 0,

    /// <summary>WebP (lossless or lossy).</summary>
    Webp = 1,

    /// <summary>JPEG (lossy, no transparency).</summary>
    Jpeg = 2
}
