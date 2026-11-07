# Engine status (P07, P09, P10)

Branch `packet/engine`. Measured on 2026-11-08 against `testdata/eval-only/goldens/`
(manifest aggregate `d3407ecd...bd41`, 12 requests). Tags: VERIFIED = measured by a test in this
branch; INFERRED = reasoning, not measured.

## Tests (VERIFIED)

| Trait | Total | Pass | Skip | Notes |
|---|---|---|---|---|
| P07 | 22 | 22 | 0 | units, rotations, 6D, spaces, mirror, pose mapping |
| P09 | 13 | 12 | 1 | skip = `EncodeSpec.Golden_corpus_*` placeholder (needs P03 `manifest.sha256`) |
| P10 | 19 | 18 | 1 | skip = `DecodeSpec.Golden_corpus_*` placeholder (same) |
| P11 | 4 | 4 | 0 | `NetworkTensors` implemented here (needed by encode/decode) |
| Architecture | 6 | 6 | 0 | |

Without goldens the 15 eval-only tests (`Category=EvalOnly`) report Skipped "eval-only goldens not
present" (VERIFIED with `SA_GOLDENS_DIR=/nonexistent`). Set `SA_ENGINE_METRICS=<file>` to have the
golden tests append the worst error per metric to that file.

## Golden parity, worst over all requests (VERIFIED)

| Test | Metric | Worst | Request | Threshold |
|---|---|---|---|---|
| T1 pad token | bitwise | exact | all | exact |
| T2 FK | mm / deg | 0.00155 / 4.9e-5 | req05 / req04 | 0.01 / 0.01 |
| T3 alignment | abs | 1.2e-7 | req04 | 1e-5 |
| T4 grv / local_poses | abs | 1.07e-6 / 4.8e-7 | req02 / req08 | 1e-5 |
| T4b lrv col 3 / cols 0-2 | abs | 1.2e-7 / 4.9e-5 | req04 / req02 | 1e-5 / 1e-4 |
| T5 conditions | exact | exact | all | exact |
| T6 rot6d | deg | 0 | all | 0.01 |
| T7 decode | mm / deg / contacts | 0.00068 / 2.5e-5 / 0 | req08 / req05 | 0.01 / 0.01 / 1e-6 |
| T8 endpoint deltas | mm / deg | 0 / 4.2e-6 | req02 | 0.01 / 0.01 |
| T9 endpoint apply | mm / deg | 0.00049 / 2.1e-5 | req07 / req08 | 0.01 / 0.01 |
| T10 encode end to end | alignment / grv / lp / lrv3 / lrv0-2 | 2.4e-7 / 1.4e-6 / 1.3e-6 / 2.4e-7 / 4.9e-5 | | T4 limits |
| T10 final frames | mm / deg / contacts | 0.0010 / 4.1e-5 / 0 | req07 / req10 | 0.01 / 0.01 / 1e-6 |
| T11 chaining | bitwise | exact (req07<-req00, req11<-req10) | | exact |
| T12 IK pin | mm / deg | 0.0011 / 4.1e-4 | req09 | 0.01 / 0.01 |
| T13 endpoints | mm / deg | 0 / 1.1e-5 | req11 | 0.01 / 0.01 |
| Public `EngineEncoder.Encode` (req10, req11) | abs; lrv0-2 | 1.1e-6; 1.9e-5 | req10 | 1e-5; 1e-4 |
| Public `EngineDecoder.Decode` (req10, req11), decoded joints | mm / deg / contacts | 0.00034 / 4.1e-5 / 0 | | 0.01 / 0.01 / 1e-6 |
| Public `EngineDecoder.Decode`, all 77 joints | deg | 0.36 | req11 | informational |

## Design

- Kernels: `src/SA.Inbetween.Engine/Kernels.cs` (`EngineKernels`, `SomaSkeleton`) and
  `IkKernels.cs`; maths in `src/SA.Inbetween.Soma/RotationMath.cs`. All internal, span-based,
  float32 buffers owned by the caller, float64 arithmetic inside (warp formulas kept: `qrot` with
  `c = 2w^2-1`, warp slerp, warp `quat_from_matrix` branches).
- Zero allocation (VERIFIED, `EngineAllocationSpec`, `GC.GetAllocatedBytesForCurrentThread`,
  10 calls after 3 warmups): Encode+Conditions+PadToken 0 B, Decode(58)+EndpointDeltas+Apply 0 B,
  IK Capture+Apply 0 B, public rotation/space/unit functions 0 B. Public `EngineEncoder.Encode` and
  `EngineDecoder.Decode` allocate (they build `Block`s and `Pose`s for the frozen contract types).
- Public poses are `SomaModel`, so the public API runs with `model_to_runtime = identity`. The
  kernels take any `model_to_runtime` (goldens req00-09 use MuJoCo +90 deg X and pass).
- Skeleton topology (77 parents) is embedded in `SomaSkeleton`; `pose_to_model` is derived from it
  exactly as model/soma/engine.py:108-114.

## Deviations and risks

1. Bind pose (INFERRED design choice, VERIFIED effect). The official decode copies the USD bind
   pose (`reference_local`) into non-decoded joints and takes non-root translations from it. The
   bind pose is NVIDIA-derived, so it is not embedded, and the frozen `Decode` signature has no
   slot for it. Public `Decode` uses the last context pose instead. Translations are identical for
   SOMA (all golden inputs share bind translations: VERIFIED diff 0.0). Decoded joints match the
   goldens to 4.1e-5 deg; finger and hand-end joints differ by up to 0.36 deg mid-clip (frames 0
   and n-1 are still exact after correction). With `correct_generated_diff = false` they hold the
   context pose instead of the A-pose bind (better for an app, not bit-parity). Exact parity
   would need a public `SkeletonBinding` input (contract change, Smart decision).
2. Embedding the 77-entry parent table: topology only, no motion or bind data. INFERRED to be
   allowed (the 30-entry `SomaJoints` table is already in src); confirm with the licence owner.
3. Tokens outside the allowed set: public `Decode` returns `Fault(NetworkShapeMismatch, 1, tokens)`
   (the official engine raises). Contract table does not name a code for this case.
4. `Timing` is `(tokens, 0, 0)` from `Decode`; wall-clock timing belongs to the generator (P12).
5. IK is a kernel only (`IkKernels.Capture/Apply/Weight`), not wired into `Decode`: the official
   corrector is app-level and runs per played frame with a progress-based weight (spec K3).
   `TargetSpec.IkPinMask` bit order LeftHand=1, RightHand=2, LeftFoot=4, RightFoot=8 matches
   MotionBricks (VERIFIED by T12 with mask 15; single-bit masks not separately covered).
6. `TransitionEarlyByFrames` is not used by the engine (app playback concern).
7. Recorder history resampling (spec section 10 GAP) is not ported.

## Follow-ups (literal steps in docs/small-agent-steps.md)

- P12 PortedGenerator wiring around `EngineEncoder`/`EngineDecoder`.
- P19 BenchmarkDotNet table for the kernels.
- P20 Stryker mutation run on Engine and Soma maths.
- docs/engine/small-agent-steps.md E8: network parity once P11 OnnxNetwork lands.
- Point the `Golden_corpus_*` placeholders in `EngineSpec.cs` at the goldens or the P03 corpus
  (test edit, needs the lead).
