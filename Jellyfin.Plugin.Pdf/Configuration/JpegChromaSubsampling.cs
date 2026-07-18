namespace Jellyfin.Plugin.Pdf.Configuration;

/// <summary>
/// The chroma subsampling ratio used when encoding JPEG output. Lower chroma resolution
/// produces smaller files; 4:4:4 keeps full color detail.
/// </summary>
public enum JpegChromaSubsampling
{
    /// <summary>4:2:0 — smallest files (chroma sampled at half resolution both axes).</summary>
    Subsample420 = 0,

    /// <summary>4:2:2 — balanced (chroma halved horizontally only). SkiaSharp only.</summary>
    Subsample422 = 1,

    /// <summary>4:4:4 — full chroma resolution, largest files, best color fidelity.</summary>
    Subsample444 = 2
}
