# Diffraction parity vectors

`make_vectors.py` builds the synthetic layout used by `DiffractionSelfTests` with DiscDiffractStudio's
Python engine and prints the values the C# self-test compares against.

```
pip install numpy scipy pillow
python make_vectors.py --dds PATH/TO/DiscDiffractStudio [--iso small.iso]
```

If you change the layout in either place, change it in both and paste the new block hashes into
`src/DvdImageSolver/Diffraction/DiffractionSelfTests.cs`.
