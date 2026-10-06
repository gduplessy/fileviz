# FileViz 0.3.2 preview

The Windows taskbar now uses the same FileViz mark as the app.

## Changes

- Embed the FileViz icon in the Windows executable and main window, including nine transparent sizes from 16 to 256 pixels.
- Bind the title-bar image to the window icon so they stay consistent. Explorer and shortcuts use the embedded executable icon.
- Keep SVG artwork and a reproducible Windows icon generator in the repository.

## Packages and validation

Use `FileViz-0.3.2-win-x64-setup.exe` or extract the complete `FileViz-0.3.2-win-x64-portable.zip`. Verify `SHA256SUMS.txt`.

The desktop build passes with no warnings; a visible disposable fixture verifies valid small and large native window icons. Saved inventories, duplicate results, and cleanup journals are preserved during upgrades. Close the previous app before installing; restarting interrupts an unfinished analysis.
