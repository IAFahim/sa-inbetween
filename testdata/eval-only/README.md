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
  `5ce984291a24299e56abf3944af8b77f1dc0df4ef326f58c4e8f6bd4f9e18179`.
