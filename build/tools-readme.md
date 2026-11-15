# SaInbetween tools folder

Put this `SaInbetween` folder in your Unity project folder, next to `Assets` (or keep it anywhere and pick it in Edit > Project Settings > SA Inbetween). The Git-URL installer (`com.sa.inbetween.install`, `docs/install.md`) downloads and unpacks it for you and checks its SHA-256.

Already here: `cli/` (generator), `bake/` (baker), `humanoid-v1.safamily`. Both programs are self-contained; no .NET install is needed. On Windows they need the Microsoft Visual C++ Redistributable 2015-2022 (x64).

Two files come from NVIDIA MotionBricks and are not redistributed here:

1. `model/mmm_v6_soma_fp32.onnx` (the model; MotionBricks `assets/onnx/soma/`, 757,864,273 bytes, NVIDIA Open Model Agreement).
2. `soma_rest.pose` in this folder's top level (the SOMA rest pose: frame 0 of NVIDIA's sample `combat_quickstep_ff_idle_180_R_002__A365.bvh`).

The installer fetches both from NVIDIA's repository after asking you once (SA > Inbetween > Setup). By hand: put the model in `model/`, and make the rest pose with `tools/make_pose_library.py` in the sa-inbetween repository.

Project Settings > SA Inbetween shows every file as found or missing.
