using System.Buffers.Binary;

namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// Embeds physical pixel-density metadata (the <c>pHYs</c> chunk) into encoded PNG images.
/// </summary>
/// <remarks>
/// The configured render DPI controls how many pixels are rasterized per inch of the PDF
/// page, but SkiaSharp's PNG encoder does not write any pixel-density metadata into the
/// output file. Without it, image viewers and file managers fall back to an assumed
/// default (typically 96 DPI) when reporting the image's resolution, even though the pixel
/// dimensions were produced at the configured DPI. Adding the <c>pHYs</c> chunk makes the
/// file self-describe the correct density.
/// </remarks>
internal static class PngDpiMetadata
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Returns a copy of <paramref name="png"/> with a <c>pHYs</c> chunk inserted (immediately
    /// after the mandatory <c>IHDR</c> chunk) describing the given DPI. If <paramref name="png"/>
    /// does not look like a valid PNG, it is returned unchanged.
    /// </summary>
    /// <param name="png">The encoded PNG bytes.</param>
    /// <param name="dpi">The pixel density, in dots per inch, to embed.</param>
    /// <returns>The PNG bytes with the pixel-density metadata embedded.</returns>
    public static byte[] EmbedDpi(byte[] png, int dpi)
    {
        const int SignatureLength = 8;
        const int ChunkLengthFieldSize = 4;
        const int ChunkTypeFieldSize = 4;
        const int ChunkCrcFieldSize = 4;
        const int IhdrDataLength = 13;
        const int IhdrChunkSize = ChunkLengthFieldSize + ChunkTypeFieldSize + IhdrDataLength + ChunkCrcFieldSize;
        const int InsertOffset = SignatureLength + IhdrChunkSize;

        if (png.Length < InsertOffset || !png.AsSpan(0, SignatureLength).SequenceEqual(PngSignature))
        {
            return png;
        }

        // Convert DPI (dots per inch) to pixels per meter, as required by the pHYs chunk.
        var pixelsPerMeter = (uint)Math.Round(dpi / 0.0254);

        Span<byte> typeAndData = stackalloc byte[ChunkTypeFieldSize + 9];
        typeAndData[0] = (byte)'p';
        typeAndData[1] = (byte)'H';
        typeAndData[2] = (byte)'Y';
        typeAndData[3] = (byte)'s';
        BinaryPrimitives.WriteUInt32BigEndian(typeAndData[4..], pixelsPerMeter);
        BinaryPrimitives.WriteUInt32BigEndian(typeAndData[8..], pixelsPerMeter);
        typeAndData[12] = 1; // Unit specifier: 1 = meter.

        var crc = Crc32(typeAndData);

        var output = new byte[png.Length + ChunkLengthFieldSize + typeAndData.Length + ChunkCrcFieldSize];
        var offset = 0;

        png.AsSpan(0, InsertOffset).CopyTo(output.AsSpan(offset));
        offset += InsertOffset;

        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(offset), 9); // pHYs data length is always 9 bytes.
        offset += ChunkLengthFieldSize;

        typeAndData.CopyTo(output.AsSpan(offset));
        offset += typeAndData.Length;

        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(offset), crc);
        offset += ChunkCrcFieldSize;

        png.AsSpan(InsertOffset).CopyTo(output.AsSpan(offset));

        return output;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
