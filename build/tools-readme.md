# SaInbetween tools folder

Unzip this folder into your Unity project root as `SaInbetween/` (or pick it in Edit > Project Settings > SA Inbetween).

Already here: `cli/` (generator), `bake/` (baker), `humanoid-v1.safamily`.

Two files come from NVIDIA MotionBricks and are not redistributed here:

1. `model/mmm_v6_soma_fp32.onnx` (the model; MotionBricks `assets/onnx/soma/`).
2. `soma_rest.pose` in this folder's top level (a SOMA rest pose). Make it once with `tools/make_pose_library.py` in the sa-inbetween repository, or copy it from a colleague.

Project Settings > SA Inbetween shows every file as found or missing.
