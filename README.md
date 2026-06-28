# Jellyfin PDF Thumbnails Plugin

A plugin for [Jellyfin](https://jellyfin.org/) that automatically generates thumbnail images from PDF files by rendering the first page.

## Features

- Automatically generates cover thumbnails for PDF files in your Jellyfin library
- Renders the first page of each PDF as a cover image
- Integrates with Jellyfin's native image provider system (`IDynamicImageProvider`)
- Configurable render resolution (DPI) for quality vs. performance trade-off
- Configurable thumbnail padding mode (white, transparent, or no padding)
- Supports all major platforms: Windows, Linux (x64, ARM, musl), macOS

## Installation

### Manual Installation

1. Download the latest release zip from the [Releases](../../releases) page.
2. Extract all files into a new folder inside your Jellyfin plugins directory:
   ```
   <Jellyfin Data Path>/plugins/Jellyfin_PDF_plugin/
   ```
   | OS | Default path |
   |----|--------------|
   | Linux | `/var/lib/jellyfin/plugins/Jellyfin_PDF_plugin/` |
   | Windows (tray) | `%ProgramData%\Jellyfin\Server\plugins\Jellyfin_PDF_plugin\` |
   | Windows (portable) | `%LocalAppData%\jellyfin\plugins\Jellyfin_PDF_plugin\` |

3. The release zip contains:
   - `Jellyfin.Plugin.Pdf.dll`
   - `PDFtoImage.dll`
   - `SkiaSharp.dll`
   - `meta.json`
   - `runtimes/` — platform-specific native libraries (copy the full folder or only the subfolder matching your OS, e.g. `runtimes/linux-x64/`)

4. Restart Jellyfin. The plugin will appear under **Dashboard → Plugins**.

### Building from Source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
git clone https://github.com/YOUR_USERNAME/jellyfin-plugin-pdf.git
cd jellyfin-plugin-pdf
dotnet publish Jellyfin.Plugin.Pdf/Jellyfin.Plugin.Pdf.csproj -c Release
```

Copy the contents of `Jellyfin.Plugin.Pdf/bin/Release/net9.0/publish/` to your Jellyfin plugins folder.

## Configuration

Navigate to **Dashboard → Plugins → PDF Thumbnails** to configure:

| Setting | Description | Default | Range |
|---------|-------------|---------|-------|
| **Render Resolution (DPI)** | Controls thumbnail sharpness. Higher values produce better quality but take longer to render. | `150` | 36 – 600 |
| **Thumbnail Padding** | Background fill applied when squaring the thumbnail. *White padding* works best for JPEG covers; *Transparent* preserves the page background; *No padding* keeps the original aspect ratio. | `White padding` | — |

## Dependencies

All dependencies are bundled — no external tools or system libraries need to be installed separately.

| Package | Purpose |
|---------|---------|
| [PDFtoImage](https://github.com/sungaila/PDFtoImage) | Managed .NET PDF rendering via PDFium |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Image encoding and compositing |

## Compatibility

| Component | Version |
|-----------|---------|
| Jellyfin Server | 10.11.0 or later |
| .NET | 9.0 |
| Platforms | Windows (x64, x86, ARM64), Linux (x64, x86, ARM, ARM64, musl variants), macOS (x64, ARM64) |

## Contributing

Pull requests and issues are welcome. Please open an issue before starting significant work so we can discuss the approach.

## License

This project is licensed under the [MIT License](LICENSE).