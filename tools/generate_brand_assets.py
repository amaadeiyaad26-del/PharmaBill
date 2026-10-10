"""Deprecated: brand UI now uses the WPF vector control PharmaBill3DLogo.

To regenerate desktop icons / PDF watermark PNGs from that vector control:

    dotnet run --project tools/RenderBrandIcon -c Release

Do not revive image-threshold / flood-fill cutout scripts against logo_3d.png.
"""

raise SystemExit(
	"Raster cutout scripts are retired. Use: dotnet run --project tools/RenderBrandIcon -c Release"
)
