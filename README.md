# Jellyfin PDF Plugin

Plugin for Jellyfin that generates thumbnail images from PDF files by rendering the first page.

## What it does

- Automatically generates thumbnail images for PDF files in your Jellyfin library.
- Renders the first page of each PDF as a JPEG thumbnail.
- Integrates with Jellyfin's image provider system (`IDynamicImageProvider`).
- Supports configurable render resolution (`RenderResolutionDpi`) via the plugin settings page.

## Installation

### Manual Installation

1. Download the latest release or build the plugin yourself (see below).
2. Copy the following DLLs to your Jellyfin plugins directory:
   ```
   <Jellyfin Data Path>/plugins/PdfThumbnails/
   ```
   Required files:
   - `Jellyfin.Plugin.Pdf.dll`
   - `PDFtoImage.dll`
   - `SkiaSharp.dll`
   - Any other native dependencies from the publish output

3. Restart Jellyfin.
4. The plugin will appear in **Dashboard → Plugins**.

### Building from Source

```bash
dotnet publish Jellyfin.Plugin.Pdf/Jellyfin.Plugin.Pdf.csproj -c Release -o ./publish
```

Then copy all files from `./publish/` to your Jellyfin plugins folder as described above.

## Configuration

After installation, go to **Dashboard → Plugins → PDF Thumbnails** to configure:

- **Render Resolution (DPI)**: Controls thumbnail quality/size at render time.
  - Default: `150`
  - Minimum: `36`
  - Maximum: `600`
  - Higher values increase detail and file size.

## Dependencies

All dependencies are bundled as NuGet packages — no external tools required. The plugin uses [PDFtoImage](https://github.com/sungaila/PDFtoImage) for managed PDF rendering.

## Compatibility

- **Jellyfin Server**: 10.9.0 or later
- **Framework**: .NET 8.0