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

## Runtime dependency

Thumbnail generation uses `pdftoppm` (Poppler). Ensure it is available on the host path.