namespace Jellyfin.Plugin.Pdf.Configuration;

/// <summary>
/// Controls the padding added around the rendered PDF page to produce a square thumbnail.
/// </summary>
public enum PaddingMode
{
    /// <summary>Fill empty space with white.</summary>
    White = 0,

    /// <summary>Fill empty space with transparency (PNG only).</summary>
    Transparent = 1,

    /// <summary>Do not add any padding; output the page at its original aspect ratio.</summary>
    None = 2
}
