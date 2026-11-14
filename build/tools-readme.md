# SaInbetween tools folder

Put this `SaInbetween` folder in your Unity project folder, next to `Assets` (or keep it anywhere and pick it in Edit > Project Settings > SA Inbetween).

Already here: `cli/` (generator), `bake/` (baker), `humanoid-v1.safamily`. Both programs are self-contained; no .NET install is needed. On Windows they need the Microsoft Visual C++ Redistributable 2015-2022 (x64).

Two files come from NVIDIA MotionBricks and are not redistributed here:

1. `model/mmm_v6_soma_fp32.onnx` (the model; MotionBricks `assets/onnx/soma/`, 757,864,273 bytes).
2. `soma_rest.pose` in this folder's top level (a SOMA rest pose). Make it once with `tools/make_pose_library.py` in the sa-inbetween repository, or copy it from a colleague.

Project Settings > SA Inbetween shows every file as found or missing.
