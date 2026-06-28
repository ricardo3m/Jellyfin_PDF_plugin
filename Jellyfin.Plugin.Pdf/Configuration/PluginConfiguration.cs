using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Pdf.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    private const int MinDpi = 36;
    private const int MaxDpi = 600;
    private const int DefaultDpi = 90;

    private int _renderResolutionDpi = DefaultDpi;

    /// <summary>
    /// Gets or sets the DPI used when rendering PDF pages as thumbnails.
    /// Valid range: 36–600. Values outside this range are clamped.
    /// </summary>
    public int RenderResolutionDpi
    {
        get => _renderResolutionDpi;
        set => _renderResolutionDpi = Math.Clamp(value, MinDpi, MaxDpi);
    }

    /// <summary>
    /// Gets or sets the padding mode applied when squaring the thumbnail.
    /// </summary>
    public PaddingMode ThumbnailPaddingMode { get; set; } = PaddingMode.Transparent;
}
