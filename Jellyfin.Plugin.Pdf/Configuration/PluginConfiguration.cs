using System.Reflection;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Pdf.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    private const int MinDpi = 36;
    private const int MaxDpi = 1000;
    private const int DefaultDpi = 90;

    private const int MinImageDimension = 200;
    private const int MaxImageDimensionLimit = 20000;

    private const int MinQuality = 1;
    private const int MaxQuality = 100;
    private const int DefaultQuality = 90;

    private const int MinLosslessEffort = 0;
    private const int MaxLosslessEffort = 100;
    private const int DefaultLosslessEffort = 100;

    private const int MinWebpMethod = 0;
    private const int MaxWebpMethod = 6;
    private const int DefaultWebpMethod = 4;

    private int _renderResolutionDpi = DefaultDpi;
    private int _maxImageDimension;
    private int _imageQuality = DefaultQuality;
    private int _losslessEffort = DefaultLosslessEffort;
    private int _webpMethod = DefaultWebpMethod;

    /// <summary>
    /// Gets or sets the DPI used when rendering PDF pages as thumbnails.
    /// Valid range: 36–1000. Values outside this range are clamped.
    /// </summary>
    public int RenderResolutionDpi
    {
        get => _renderResolutionDpi;
        set => _renderResolutionDpi = Math.Clamp(value, MinDpi, MaxDpi);
    }

    /// <summary>
    /// Gets or sets the maximum size, in pixels, of the longest side of the generated image.
    /// A value of 0 means no limit (the resolution is determined solely by the render DPI).
    /// Any positive value is clamped to the 200–20000 range. The limit only ever reduces the
    /// image; it never enlarges it.
    /// </summary>
    public int MaxImageDimension
    {
        get => _maxImageDimension;
        set => _maxImageDimension = value <= 0 ? 0 : Math.Clamp(value, MinImageDimension, MaxImageDimensionLimit);
    }

    /// <summary>
    /// Gets or sets the output image format used to encode the generated thumbnail.
    /// </summary>
    public OutputImageFormat OutputFormat { get; set; } = OutputImageFormat.Png;

    /// <summary>
    /// Gets or sets a value indicating whether WebP output uses lossless compression.
    /// Only relevant when <see cref="OutputFormat"/> is <see cref="OutputImageFormat.Webp"/>.
    /// </summary>
    public bool WebpLossless { get; set; } = true;

    /// <summary>
    /// Gets or sets the quality (1–100) used for lossy encoding (WebP lossy and JPEG).
    /// Values outside this range are clamped.
    /// </summary>
    public int ImageQuality
    {
        get => _imageQuality;
        set => _imageQuality = Math.Clamp(value, MinQuality, MaxQuality);
    }

    /// <summary>
    /// Gets or sets the lossless compression effort (0–100) used for PNG and WebP lossless.
    /// Higher values produce smaller files at the cost of more processing time, with no quality
    /// loss. Values outside this range are clamped.
    /// </summary>
    public int LosslessEffort
    {
        get => _losslessEffort;
        set => _losslessEffort = Math.Clamp(value, MinLosslessEffort, MaxLosslessEffort);
    }

    /// <summary>
    /// Gets or sets the padding mode applied when squaring the thumbnail.
    /// </summary>
    public PaddingMode ThumbnailPaddingMode { get; set; } = PaddingMode.Transparent;

    /// <summary>
    /// Gets or sets the engine used for the final image-encode step. Rendering and compositing
    /// always use SkiaSharp; only the encode stage differs. <see cref="EncoderEngine.SkiaOnly"/>
    /// is the default and keeps the existing behaviour with no extra dependency.
    /// </summary>
    public EncoderEngine Engine { get; set; } = EncoderEngine.SkiaOnly;

    /// <summary>
    /// Gets or sets the WebP encoding method (0–6). Higher values give better compression at the
    /// cost of speed, with no additional quality loss. Only used by the
    /// <see cref="EncoderEngine.SkiaImageSharp"/> engine for WebP output. Values outside the
    /// range are clamped.
    /// </summary>
    public int WebpMethod
    {
        get => _webpMethod;
        set => _webpMethod = Math.Clamp(value, MinWebpMethod, MaxWebpMethod);
    }

    /// <summary>
    /// Gets or sets a value indicating whether WebP lossless output uses near-lossless
    /// pre-processing, which compresses further with minimal, usually invisible, quality loss.
    /// Only used by the <see cref="EncoderEngine.SkiaImageSharp"/> engine for WebP lossless
    /// output.
    /// </summary>
    public bool WebpNearLossless { get; set; }

    /// <summary>
    /// Gets or sets the chroma subsampling ratio used for JPEG output. Only relevant when
    /// <see cref="OutputFormat"/> is <see cref="OutputImageFormat.Jpeg"/>. Note that 4:2:2 is
    /// only supported by the <see cref="EncoderEngine.SkiaOnly"/> engine; the ImageSharp engine
    /// falls back to 4:2:0 for that setting.
    /// </summary>
    public JpegChromaSubsampling JpegSubsampling { get; set; } = JpegChromaSubsampling.Subsample420;

    /// <summary>
    /// Gets the version of the bundled SixLabors.ImageSharp encode library that is actually
    /// loaded at runtime. This is read-only and informational only: it is surfaced on the
    /// configuration page so the active ImageSharp version is visible. Because it has no setter
    /// it is neither persisted to disk nor overwritten when the configuration is saved.
    /// </summary>
    public string ImageSharpVersion
    {
        get
        {
            try
            {
                var assembly = typeof(SixLabors.ImageSharp.Image).Assembly;
                var informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;
                if (!string.IsNullOrEmpty(informational))
                {
                    var plus = informational.IndexOf('+', System.StringComparison.Ordinal);
                    return plus >= 0 ? informational[..plus] : informational;
                }

                return assembly.GetName().Version?.ToString() ?? "unknown";
            }
            catch (System.Exception)
            {
                // The ImageSharp assembly may not be present/loadable (e.g. "Skia only" installs
                // without it). The version is purely informational, so never let this break the
                // configuration page.
                return "unavailable";
            }
        }
    }
}
