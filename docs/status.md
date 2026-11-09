# Status

What is built, what is verified, and what is not. Updated 2026-11-09.

## Production-ready
- Contract, wire codec, motion file (`.saib`), recipe hashing: tested, property-tested, zero-allocation kernels.
- Engine (encode, decode, endpoint correction, IK pin): matches NVIDIA's TensorRT goldens to 0.06 mm and 0.002 rotation components (see `docs/engine/status.md`). Parity tests need the NVIDIA evaluation data and are skipped in CI.
- ONNX CPU backend (`PortedGenerator`): bit-deterministic, about 4.6 s cold for 50 frames.
- CLI `sa-inbetween` and bake tool `sa-soma-bake` (sa-motion repo): error handling, exit codes, tests.
- Unity packages `com.sa.inbetween` (core) and `com.sa.inbetween.sa` (importer, `SaClipPlayer`, Inbetween Studio window).
- CI on Linux and Windows. Tagging `vX.Y.Z` builds a release: CLI for linux-x64 and win-x64, both Unity packages, SHA256SUMS (`build/package-release.sh`).

## Not built
- GPU/TensorRT sidecar (P05, P06, P14, P15). The CPU path is the supported one; the sidecar is a speed optimisation only.
- BenchmarkDotNet suite (P19) and mutation testing (P20). Allocation is covered by `EngineAllocationSpec`.
- Contacts into leg IK and root travel in the family bake (P16). Clips are attached at runtime, not through a subscene baker.
- Video poses (P27) and move tuning (P28).

## Needs a person
- Foot gate in the SOMA retarget: sole is the lower of mid-foot and toe-ball, hip reference lowered to 0.9864 m.
- Arm, forearm and hand twist on a skinned character.
- 77-entry skeleton parent table: confirm the NVIDIA licence allows it in source.
- Finger and hand joints are filled from the last context pose; up to 0.36 degrees from the official engine mid-clip.

## Licences
Code is Apache-2.0. The model weights are under the NVIDIA Open Model Agreement and are not shipped; users download them. Sample motions and goldens are evaluation-only and never committed.
