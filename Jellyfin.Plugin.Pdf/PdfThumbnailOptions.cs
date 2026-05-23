namespace Jellyfin.Plugin.Pdf;

public sealed class PdfThumbnailOptions
{
    public const int MinRenderResolutionDpi = 36;
    public const int MaxRenderResolutionDpi = 600;
    public const int DefaultRenderResolutionDpi = 150;

    private int _renderResolutionDpi = DefaultRenderResolutionDpi;

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
