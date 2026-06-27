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
2. Create the plugin folder in your Jellyfin data directory:
   ```
   <Jellyfin Data Path>/plugins/PdfThumbnails/
   ```
   - **Linux**: typically `/var/lib/jellyfin/plugins/PdfThumbnails/`
   - **Windows**: typically `C:\ProgramData\Jellyfin\Server\plugins\PdfThumbnails\`

3. Copy all files from the `publish/` folder to the plugin directory, including the `runtimes/` subfolder (or just the subfolder matching your OS, e.g. `runtimes/linux-x64/`):
   - `Jellyfin.Plugin.Pdf.dll`
   - `PDFtoImage.dll`
   - `SkiaSharp.dll`
   - `meta.json`
   - `runtimes/` (native libraries for PDF and image rendering)

4. Restart Jellyfin.
5. The plugin will appear in **Dashboard → Plugins**.

### Building from Source

Requires the **.NET 9 SDK** installed locally.

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

- **Jellyfin Server**: 10.11.0 or later
- **Framework**: .NET 9.0