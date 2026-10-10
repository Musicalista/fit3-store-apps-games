# Light app format and API

## The `.f3a` file (little endian)

```
  0  "F3A1"                 magic
  4  u16 api version (1)    u16 flags (0)
  8  u32 image size
 12  u32 bss size
 16  u32 entry offset       (f3app_main, Thumb bit cleared)
 20  u32 CRC32 of the image
 24  u32 relocation count
 28  u32 reserved
 32  16 bytes name (Portuguese, ASCII, NUL padded)
 48  16 bytes name (English)
 64  image                  (text + rodata + data, linked at address 0)
 ... relocation table       (u32 offsets of absolute words to add the load address to)
```

`appcc.py` builds it: it compiles for Cortex-M4 (Thumb, hard float, freestanding), links with `app.ld`, lists the `R_ARM_ABS32` words as relocations and refuses anything that would need a C library. The loader copies the image into RAM, checks the magic and CRC, applies the relocations, and calls `f3app_main(api)`.

An app list lives in `/user/apps/index.txt`, one line per app: `id;BGRRGGBB;FGRRGGBB;Name PT;Name EN` (the colours are RRGGBB, for the launcher tile). The file is `/user/apps/<id>.f3a`. Ids are 1 to 11 characters from `a-z 0-9 _ -`.

## The API (`apps/f3app.h`)

The watch calls `f3app_main(api)` once, then `tick(app, pressed, x, y, tap)` about every 33 ms; the app draws the whole frame into `api->fb` (256x402, RGB565) and returns 1 to keep running or 0 to exit. The watch draws its own close button at the top right.

| Member | Meaning |
|---|---|
| `fb, w, h` | frame buffer, 256 x 402 |
| `malloc, free` | memory |
| `ticks()` | milliseconds |
| `vibrate(id)` | 1 short, 4 long, 5 error |
| `rect, rrect, disc, text, ctext, num` | drawing (clipped); text is an 8x12 font times a scale |
| `fopen, fread, fwrite, fclose` | files, keep to `/user/` |
| `lang_pt` | 1 when the watch language is Portuguese |
| `time(F3Time*)`, `battery()` | real time and battery percent. **NULL on loaders older than v1.7: check before use** |

`apps/f3game.h` has small helpers on top (touch gestures, long press, best-score files, number formatting, a title bar).
