# Jellyfin PDF Plugin

Minimal plugin scaffold to support PDF thumbnails in Jellyfin.

## What it does

- Detects when a thumbnail file is missing for a PDF item.
- Generates a thumbnail from the first page of the PDF.
- Supports configurable render resolution (`RenderResolutionDpi`).

## Configuration

`RenderResolutionDpi` controls thumbnail quality/size at render time:

- Default: `150`
- Minimum: `36`
- Maximum: `600`

Higher values increase detail and file size.

## Dependencies

All dependencies are bundled as NuGet packages — no external tools required. The plugin uses [PDFtoImage](https://github.com/sungaila/PDFtoImage) for managed PDF rendering.