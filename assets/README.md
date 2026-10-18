# GoggleCast icon

Original artwork generated for GoggleCast with the built-in image generation tool. No DJI artwork, wordmark or logo was used. The source PNG is preserved as generated, including its transparent background.

| File | Use |
| --- | --- |
| `gogglecast.png` | Original 1254 × 1254 RGBA artwork. |
| `gogglecast.ico` | Windows icon with 16, 24, 32, 48, 64, 128 and 256 pixel RGBA frames. |
| `gogglecast-preview.png` | 256 × 256 PNG preview. |
| `New-GoggleCastIcon.ps1` | Reproducible format conversion using Windows System.Drawing. |

The generator was asked for a 1024-square icon; its returned 1254-square source is retained without creative edits. ICO frames and the preview use proportional high-quality resizing only, with no redrawing.

## Generation brief

Original Windows app icon for GoggleCast: one centered, upright deep navy rounded-square tile, transparent outside the tile, with a bold goggles/display hybrid symbol. Ice white contours, electric blue casting arcs and a small cyan accent, matching the app's `#142840` header. Large simple silhouette, generous negative space, restrained Fluent depth and subtle highlights. No text, letters, watermark, camera lens, film-strip perforations, DJI marks, scene background, heavy shadows or neon RGB. Produce one finished icon, not a grid.

Regenerate the icon formats on Windows with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\assets\New-GoggleCastIcon.ps1
```
