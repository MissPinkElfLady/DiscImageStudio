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
- Grey effects use single-byte carriers: `0x92` (16T), `0xB9` (10.67T), `0x66` (8T) on an `0xA5`
  background.
- One colour picture (`"effect": "color"`) uses DiscDiffractStudio's 91-byte string carriers with period
  compensation and white balance. Its colours are right near one tilt (`"copies": 1`, brightest) or two
  tilts (`"copies": 2`, the same picture written twice, each copy dimmer). A third copy would need
  gratings shorter than the 6.4T that can be written.
- `"coverage": "wide"` writes one colour picture over the whole disc. A lamp shows it in two opposite
  wedges; moving the lamp round the disc (or turning the disc) shows the rest (see
  [Whole-disc colour](#whole-disc-colour)).
- Multi-frame animation and viewing distances other than 50 cm are not ported yet. Carrier strings come
  from DiscDiffractStudio's table (`string_carriers.json`); periods it has not synthesised are refused.
- In the app, DVD projects have a **Grayscale | Color** switch on the Design page (above the tone panel) and
  on the Export page. Color writes the design composite as one whole-disc colour picture for Export and
  Burn, placed exactly where the grayscale picture would be. The tone preview and the design projection
  show the colours as encoded. Output read-back decodes a colour ISO from its bytes: every turn of the
  spiral carries one colour in a fixed cycle, so the share of grating bytes (not 0xA5) per colour in each
  pixel, over that colour's gain, gives its level. The read-back has its own Grayscale | Color switch, set
  from the ISO's sidecar (format `dvd-diffraction-colour-v1`) and adjustable for ISOs without one. Dithering,
  brightness and sRGB linearization are hidden, DVD compatibility does not apply, and a colour disc cannot
  hold files yet. The ISO holds raw sectors without a file system.
- Other effects (flips, stereo, spot colour) are command line only.
- `--preview disc.png` draws the design; with `--preview-light DEGREES` it simulates what the eye sees with
  the lamp at that many degrees clockwise from 12 o'clock.

## Usage

```
DvdImageSolver diffract --layout layout.json --iso-output image.iso [--preview disc.png]
disc-image-studio diffract --layout layout.json --iso-output image.iso [--preview disc.png]
```

`--preview` alone writes only the PNG, which takes seconds. Add `--preview-light 0` (lamp above),
`--preview-light 90` (lamp to the right) and so on to see the disc under a lamp.

A layout names the calibration and the pictures; see
[examples/diffraction/flip-layout.json](examples/diffraction/flip-layout.json) and the comments in
`src/DvdImageSolver/Diffraction/DiffractionProject.cs`.

- Canvas: millimetres from the hub, X right, Y up, as the data side is seen. `thetaRefDegrees` (default 0,
  as in DiscDiffractStudio) is the spiral angle that points to the top.
- `effect`: `single` (one grey image, `0x92`), `flip` or `stereo` (two grey images, `0x92` / `0xB9`), or
  `color` (one colour image, with `copies` 1 or 2). Or give `carriers` directly, with optional `gains` and
  `turnCounts`. `brightness` scales every view's lit share.
- A colour picture is seen by tilting, so its tracks must run up and down through it: put it at 3 or 9
  o'clock, or at 12 / 6 o'clock with a quarter turn. With `"lamp": "side"` it is seen by turning instead and
  goes at 12 or 6 o'clock.
- `"coverage": "wide"` (colour): one picture over the whole disc, e.g. `"centerMm": [0, 0]`,
  `"sizeMm": [118, 118]`; see
  [examples/diffraction/color-wide-layout.json](examples/diffraction/color-wide-layout.json).
- `quarterTurns`: counter-clockwise quarter turns of the images on the canvas.
- Images are cut down around their middle to the picture's aspect ratio, averaged onto 0.05 mm cells in
  linear light, and reduced to luminance (grey effects) or kept as red, green and blue (colour). Bright, high-contrast
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

## Colour

The picture's red, green and blue channels go on three string carriers of 16T, 13.93T and 12.13T (ratio
2^(1/5)). From the design pose (eyes 50 cm away, lamp 1 m away straight above the line of sight) an eye at
u = 0.288 sees them as 614, 535 and 466 nm. Each carrier's period changes across the picture in 0.5 % steps
so that every cell shows the colour the centre shows, and the turns are shared out 7 : 8 : 17 (two copies:
3 : 3 : 7 : 4 : 5 : 10 of every 32) so that equal channels give D65 white under a white LED.

A grating sends each colour to its own direction, so the colours drift as the disc is tilted away from the
design tilt. That is physics, not a limitation of the program: a second copy only adds a second tilt
where the colours are right.

## Whole-disc colour

With one lamp and one viewer, every point of the disc sends light to the eye by its own number
u = (l + e) · t. The in-plane part of l + e is the gradient of minus (distance to the lamp + distance to the
eye), so its circulation round any turn of the spiral is zero: on every turn, u is positive on one side,
negative on the other and zero at two or more points, where the tracks run straight towards the lamp. So no
pose can light a whole disc at once, which is also why Disc Color Studio's large images are cut off at some
angles.

`"coverage": "wide"` therefore writes the whole picture and tunes every cell for the lamp direction that
suits it best: the one that puts the cell's tracks across the line from the lamp, as for a picture at 3
o'clock with the lamp above. By symmetry about the hub that pose sees every cell like a cell at the same
radius on the 3 o'clock axis, so the periods depend only on the radius (with the eye 50 cm in front of the
hub they barely change at all). Under one lamp the picture shows in its own colours along the line through
the hub across the lamp direction and fades through shifted colours to dark within a few tens of degrees on
either side: two opposite wedges. Moving the lamp round the disc, or turning the disc, sweeps the wedges
round and shows the rest of the picture. `--preview-light` simulates this with the white-balance model.

## Parity with DiscDiffractStudio

`DvdImageSolver selftest` builds a fixed synthetic layout (three pictures, two to three views each) and
checks the SHA-256 of 42 ECC blocks against DiscDiffractStudio's own output. The expected hashes come
from `tools/DiffractionParity/make_vectors.py`, run against a DiscDiffractStudio checkout. A full
240,976-sector ISO of a smaller layout was also compared once and matched byte for byte. The colour
effect is checked the same way against DiscDiffractStudio's colour flip with the same channels as both
pictures (two copies) or only its first trio (one copy): turn counts, gains, every carrier string and
compensation map, and 56 ECC blocks (`tools/DiffractionParity/make_color_vectors.py`).

The image loading differs on purpose: DiscDiffractStudio resamples with Pillow, this port averages in
linear light itself, so pictures made from image files can differ by a grey step at cell edges.
