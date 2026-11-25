# DVD diffraction pictures (experimental)

Disc Image Studio's normal DVD picture is already a diffraction effect in disguise: light areas carry the
scrambled byte `0x92`, whose EFMPlus marks repeat every 16 channel bits (2.13 µm), and dark areas carry
`0xA5` (6.4T). The `diffract` command goes further. It writes several such gratings, each with its own
period, so that different pictures are seen from different directions: a two-frame flip that switches as
the disc tilts, or a stereo pair where each eye sees its own picture.

The engine is ported from [DiscDiffractStudio](https://github.com/Neo-626/DiscDiffractStudio) by Neo-626
(GPL-3.0-or-later), which itself builds on this project's EFMPlus tables, scrambler and calibration.

## Status

- DVD-R only. Other media and burners have not been tried.
- Single-byte carriers only: `0x92` (16T), `0xB9` (10.67T), `0x66` (8T) on an `0xA5` background.
  Animations with more frames, viewing distances other than 50 cm and colour need DiscDiffractStudio's
  91-byte string carriers, which are not ported yet.
- Command line only; no Design-workspace integration yet. The ISO holds raw sectors without a file system.
- No diffraction preview yet.

## Usage

```
DvdImageSolver diffract --layout layout.json --iso-output image.iso
disc-image-studio diffract --layout layout.json --iso-output image.iso
```

A layout names the calibration and the pictures; see
[examples/diffraction/flip-layout.json](examples/diffraction/flip-layout.json) and the comments in
`src/DvdImageSolver/Diffraction/DiffractionProject.cs`.

- Canvas: millimetres from the hub, X right, Y up, as the data side is seen. `thetaRefDegrees` (default 0,
  as in DiscDiffractStudio) is the spiral angle that points to the top.
- `effect`: `single` (one image, `0x92`), `flip` or `stereo` (two images, `0x92` / `0xB9`). Or give
  `carriers` directly, with optional `gains` and `turnCounts`.
- `quarterTurns`: counter-clockwise quarter turns of the images on the canvas.
- Images are averaged onto 0.05 mm cells in linear light and reduced to luminance. Bright, high-contrast
  material works best; dark material burns dark.

How to look at the disc (from DiscDiffractStudio): use a small bright lamp about a metre away. Pictures at
12 or 6 o'clock are seen by turning the disc left and right, with the lamp to the right of your line of
sight; pictures at 3 or 9 o'clock are seen by tilting it, with the lamp above your line of sight.

## How it works

Turn *n* of the spiral inside a picture belongs to view *n* mod *N* (or to the cycle given by `turnCounts`).
A turn carries its view's carrier where that view is light and `0xA5` where it is dark. Grey comes from how
many of a view's turns are lit in a cell: 32 bit-reversed thresholds plus a 4 × 4 ordered dither.

Only bytes 88–255 are used, and never a byte for which the recorder could choose between two code words
for DSV control, so the recorder writes exactly the planned marks. Where a switch between two carriers
would still leave it a choice (`0x66` → `0x92`), the first byte of the new run becomes a bridge byte
(`0xA5`).

The generator fills whole ECC blocks of scrambled-domain bytes through
`FastDispersionImageWriter.WriteScrambledBlockStream`, which descrambles and streams them like the
normal DVD writer.

## Parity with DiscDiffractStudio

`DvdImageSolver selftest` builds a fixed synthetic layout (three pictures, two to three views each) and
checks the SHA-256 of 42 ECC blocks against DiscDiffractStudio's own output. The expected hashes come
from `tools/DiffractionParity/make_vectors.py`, run against a DiscDiffractStudio checkout. A full
240,976-sector ISO of a smaller layout was also compared once and matched byte for byte.

The image loading differs on purpose: DiscDiffractStudio resamples with Pillow, this port averages in
linear light itself, so pictures made from image files can differ by a grey step at cell edges.
