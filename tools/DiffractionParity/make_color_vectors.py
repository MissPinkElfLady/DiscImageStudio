"""Parity vectors for the single-image colour effect of the C# diffraction engine.

Builds DiscDiffractStudio's colour-flip object from analytic red, green and blue channels (the same image
for both pictures when --copies 2; only the first colour trio when --copies 1) and prints what
DiffractionSelfTests compares against: white-balance turn counts and gains, every string carrier's
variant periods and bytes (SHA-256), and SHA-256 hashes of selected ECC blocks.

    python make_color_vectors.py --dds PATH/TO/DiscDiffractStudio --copies 2

DiscDiffractStudio synthesises string carriers it has not cached (about 0.6 s each); nothing is written
to its cache file.
"""
import argparse
import hashlib
import json
import sys

import numpy as np


def channel(nx, ny, a, b, m):
    j, i = np.mgrid[0:ny, 0:nx]
    return ((i * a + j * b) % m).astype(np.float64) / (m - 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dds", required=True)
    ap.add_argument("--copies", type=int, default=2)
    args = ap.parse_args()
    sys.path.insert(0, args.dds)
    from dvdgrating import strings
    strings.save_cache = lambda: None
    from dvdgrating import multiview as mv
    from dvdgrating import presets as ps
    from dvdgrating.geometry import PRESETS, Spiral

    place = ps.Place({"center_mm": [43.0, 0.0], "size_mm": [24.0, 24.0]}).as_drawn()
    periods, u_trio = ps.colorflip_plan()
    rgb = [channel(480, 480, 7, 3, 101), channel(480, 480, 5, 11, 97), channel(480, 480, 3, 13, 89)]
    if args.copies == 1:
        periods, eyes = periods[:3], [((0, 1, 2), u_trio[0])]
    else:
        eyes = [((0, 1, 2), u_trio[0]), ((3, 4, 5), u_trio[1])]
    n = len(periods)
    views = [rgb[k % 3] for k in range(n)]
    need = ps.white_balance(periods, eyes)
    turns = ps.whole_turns(need, ps.COLOR_CYCLE)
    gains = need / (np.array(turns) / sum(turns))
    gains = tuple(float(g) for g in gains / gains.max())
    carriers = ps._strings_for(place, periods, lambda k: ps._cyclopean(place, u_trio[k // 3]))
    pic = mv.Picture("color", place.centre, place.size, views, carriers, gains, place.cell, True, mv.OFF_BYTE,
                     "tracks", turns).build()
    lay = mv.DiscLayout([pic])
    spiral = Spiral(PRESETS["dis-user-biaoding2"])
    out = {"periods": periods, "need": need.tolist(), "turns": list(turns), "gains": list(gains),
           "carriers": [{"nominal": c.nominal, "periods": [s.period_bits for s in c.strings],
                         "bytes_sha256": hashlib.sha256(b"".join(s.data.tobytes() for s in c.strings)).hexdigest(),
                         "variant_sha256": hashlib.sha256(c.variant.astype(np.uint8).tobytes()).hexdigest()}
                        for c in carriers],
           "last_sector": mv.last_sector(lay, spiral)}
    mapper = mv.BlockMapper(lay, spiral)
    blocks = []
    for lba in range(0, out["last_sector"], 16 * 1001):
        q = mapper.block_bytes(lba)
        if q is not None:
            blocks.append([lba, hashlib.sha256(q.tobytes()).hexdigest(), int((q != lay.off_byte).sum())])
    out["blocks"] = blocks
    print(json.dumps(out, indent=1))


if __name__ == "__main__":
    main()
