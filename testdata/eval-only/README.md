# testdata/eval-only: EVALUATION ONLY, NVIDIA-DERIVED

Everything in this folder except this README is gitignored and must stay local.

- `goldens/` is produced by `tools/dump_goldens.py` from the official MotionBricks SOMA
  engine and model (NVIDIA Open Model Agreement) and the SOMA skeleton asset. The poses
  are synthetic (seeded perturbations of the skeleton's reference pose), but the
  skeleton, the network outputs, and every value derived from them are NVIDIA-derived.
- Use it only to evaluate our C# port locally. Never ship it, never commit it, never
  publish it, never copy it into a Unity package or build.
- Regenerate: `~/GitHub/motionbricks/.venv/bin/python tools/dump_goldens.py`
  (needs the RTX 3060, TensorRT 10.8, and the FP32 plan in `.trt-cache/`).
  Expected aggregate SHA256 in `goldens/MANIFEST.sha256.json`:
  `d3407ecd1ee2ec38a5df53cdd8748163d87a07220fd6a283a657bf4f2698bd41`.
