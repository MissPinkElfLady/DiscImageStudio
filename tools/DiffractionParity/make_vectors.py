"""Parity vectors for the C# diffraction engine (src/DvdImageSolver/Diffraction).

Builds a fixed synthetic layout with DiscDiffractStudio's own Python engine and prints what the C#
self-test compares against: the bridge table, the last sector, and SHA-256 hashes of the
scrambled-domain bytes of selected ECC blocks. Optionally writes the whole ISO of a small layout.

    python make_vectors.py --dds PATH/TO/DiscDiffractStudio [--iso small.iso]

The views are analytic and already cell-sized, so no resampling is involved; the C# self-test builds
the same arrays (see SelfTests.TestDiffraction*). Keep the two in step.
"""
import argparse
import hashlib
import json
import sys

import numpy as np


def views(nx, ny, a, b, m):
    j, i = np.mgrid[0:ny, 0:nx]
    return ((i * a + j * b) % m).astype(np.float64) / (m - 1)


def build(mv, small=False):
    if small:
        pics = [mv.Picture("small", (0.0, 27.0), (6.0, 4.0), [views(120, 80, 7, 3, 101), views(120, 80, 5, 11, 97)],
                           carriers=(0x92, 0xB9), linear=True)]
    else:
        pics = [
            mv.Picture("stereo", (0.0, 43.0), (24.0, 24.0), [views(480, 480, 7, 3, 101), views(480, 480, 5, 11, 97)],
                       carriers=(0x92, 0xB9), linear=True),
            mv.Picture("three", (43.0, 0.0), (20.0, 16.0),
                       [views(320, 400, 3, 7, 89), views(320, 400, 13, 2, 83), views(320, 400, 1, 1, 61)],
                       carriers=(0x92, 0xB9, 0x66), gains=(1.0, 0.8, 0.9), turn_counts=(1, 2, 1), rotate=1, linear=True),
            mv.Picture("single", (-30.0, -30.0), (10.0, 10.0), [views(200, 200, 9, 4, 53)], carriers=(0x66,), linear=True),
        ]
    return mv.DiscLayout([p.build() for p in pics])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dds", required=True)
    ap.add_argument("--iso")
    args = ap.parse_args()
    sys.path.insert(0, args.dds)
    from dvdgrating import multiview as mv
    from dvdgrating.geometry import PRESETS, Spiral
    spiral = Spiral(PRESETS["dis-user-biaoding2"])
    lay = build(mv)
    out = {"bytes_used": lay.bytes_used(), "bridges": lay.bridges, "last_sector": mv.last_sector(lay, spiral)}
    mapper = mv.BlockMapper(lay, spiral)
    blocks = []
    for lba in range(0, out["last_sector"], 16 * 2003):
        q = mapper.block_bytes(lba)
        if q is not None:
            blocks.append([lba, hashlib.sha256(q.tobytes()).hexdigest(), int((q != lay.off_byte).sum())])
    out["blocks"] = blocks
    print(json.dumps(out, indent=1))
    if args.iso:
        small = build(mv, small=True)
        stats = mv.write_iso(args.iso, small, spiral, jobs=0)
        h = hashlib.sha256()
        with open(args.iso, "rb") as f:
            for chunk in iter(lambda: f.read(1 << 24), b""):
                h.update(chunk)
        print(json.dumps({"small_iso_sectors": stats["sectors"], "sha256": h.hexdigest()}))


if __name__ == "__main__":
    main()
