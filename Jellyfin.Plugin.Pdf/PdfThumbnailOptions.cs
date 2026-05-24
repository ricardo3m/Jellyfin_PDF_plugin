namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// Options for PDF thumbnail generation.
/// </summary>
public sealed class PdfThumbnailOptions
{
    /// <summary>Minimum allowed render resolution in DPI.</summary>
    public const int MinRenderResolutionDpi = 36;

    /// <summary>Maximum allowed render resolution in DPI.</summary>
    public const int MaxRenderResolutionDpi = 600;

    /// <summary>Default render resolution in DPI.</summary>
    public const int DefaultRenderResolutionDpi = 150;

    private int _renderResolutionDpi = DefaultRenderResolutionDpi;

    /// <summary>
    /// Gets or sets the render resolution in DPI. Must be between 36 and 600.
    /// </summary>
    public int RenderResolutionDpi
    {
        get => _renderResolutionDpi;
        set
        {
            if (value < MinRenderResolutionDpi || value > MaxRenderResolutionDpi)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Render resolution must be between {MinRenderResolutionDpi} and {MaxRenderResolutionDpi} DPI.");
            }

            _renderResolutionDpi = value;
        }
    }
}
