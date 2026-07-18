namespace Jellyfin.Plugin.Pdf.Configuration;

/// <summary>
/// The engine used for the final image-encode step. Rendering and compositing always use
/// SkiaSharp; only the last encode stage differs.
/// </summary>
public enum EncoderEngine
{
    /// <summary>Encode with SkiaSharp only (no extra dependency).</summary>
    SkiaOnly = 0,

    /// <summary>
    /// Encode with SixLabors.ImageSharp, which exposes extra WebP controls (encoding method and
    /// near-lossless) that SkiaSharp does not.
    /// </summary>
    SkiaImageSharp = 1
}
