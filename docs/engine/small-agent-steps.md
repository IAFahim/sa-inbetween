# Small-agent steps: engine unit tests and parity harness (P09/P10)

STATUS: done by the Smart engine packet (branch `packet/engine`). The harness is
`tests/SA.Inbetween.Tests/EngineGoldenSpec.cs` (T1-T13, plus public-API checks) and lives in the
existing test project, not a new `SA.Inbetween.Engine.Tests`. Do not redo sections E1-E7.
Remaining small-agent work: section E step 8 (network parity) once P11 lands. Results:
docs/engine/status.md.

Read `docs/engine/spec.md` first. Section numbers (E1, D4, C3, K2 ...) refer to it.
Do these steps in order. Do not change the spec, the goldens, `tools/`, or public API.
If any STOP condition fires, stop and report: step number, request id, file, field,
index, expected, actual.

## A. Preconditions

1. Run `ls testdata/eval-only/goldens/req00/final_frames.npy`. If it is missing, regenerate with
   `~/GitHub/motionbricks/.venv/bin/python tools/dump_goldens.py` (about 10 s, needs the RTX 3060).
   Never run `uv sync` in motionbricks and never edit it.
2. Run `python3 -c "import json;print(json.load(open('testdata/eval-only/goldens/MANIFEST.sha256.json'))['aggregate'])"`.
   It must print `d3407ecd1ee2ec38a5df53cdd8748163d87a07220fd6a283a657bf4f2698bd41`.
   STOP if it differs.
3. The goldens are evaluation-only NVIDIA-derived data (`testdata/eval-only/README.md`).
   Never commit them, never embed them in source, never copy them into a Unity package.
   Tests load them at run time from `SA_GOLDENS_DIR` (default `testdata/eval-only/goldens`)
   and are tagged `Category("EvalOnly")`; when the folder is absent they report Ignored with
   the message "eval-only goldens not present", never Passed.

## B. Golden file reference (per `reqNN/`, float32 unless noted)

| File | Shape | Spec |
|---|---|---|
| request.json | | token_count, valid_frames n, targets k, config, m2r key |
| context_local / target_local | [4,77,7] / [k,77,7] | 4 inputs |
| context_token / target_token | [4,77,7] | 4 padded |
| guide_world | [8,77,7] | E1 |
| alignment | [1,7] | E3 |
| ref_aligned_world | [8,77,7] | E4 (NumPy reference) |
| net_global_root_values / net_local_root_values / net_local_poses | [1,8,5] / [1,8,4] / [1,8,267] | E5-E6 |
| net_has_global_root_values / net_has_local_root_values / net_has_local_poses | [1,8] int32 | 6 |
| net_allowed_pred_num_tokens | [1,11] int32 | 6 |
| net_pred_global_motions / net_pred_num_tokens | [1,64,366] / [1] int32 | 7 |
| trimmed_pred | [n,366] | D2 |
| ref_global_rotations | [n,30,4] | D4 (NumPy reference) |
| decoded_frames / contacts | [n,77,7] / [n,4] | D5-D6, D3 |
| start_deltas / end_deltas | [77,7] | C1-C2 (absent when correct_generated_diff is false: req06) |
| final_frames / final_contacts | [n,77,7] / [n,4] | C3 |
| ik_frames (int32), ik_weights, ik_input, ik_output, ik_pin_mask (int32) | [4], [4], [4,77,7], [4,77,7], [1] | K1-K2, req09 only |

Shared: `skeleton.json` (names, parents, model_to_pose, pose_to_model, leg indices,
model_to_runtime per key), `reference_local.npy` [77,7]. `.npy` is NumPy format v1,
little-endian, C order; write a 40-line `NpyReader` in the test project (parse the header
dict for `descr` `<f4`/`<i4` and `shape`). STOP if any header has another dtype or `fortran_order: True`.

## C. Error metrics (all tests use exactly these)

- Scalar arrays: max absolute difference.
- Position error per transform: `|pa - pb|` in mm (x1000). Report the max.
- Rotation error per transform: `2*acos(clamp(|dot(qa/|qa|, qb/|qb|)|, 0, 1))` in degrees (sign-agnostic). Report the max.
- Compute metrics in double. The implementation under test runs in float32.

## D. Thresholds

| Test | Input from golden | Compare with | Threshold |
|---|---|---|---|
| T1 PadToken | context_local, target_local | context_token, target_token | exact |
| T2 ForwardKinematics (E1) | *_token, m2r | guide_world | 0.01 mm, 0.01 deg |
| T3 Heading + Alignment (E2-E3) | guide_world, heading_alignment | alignment | 1e-5 abs |
| T4 Encode (E4-E6) | guide_world, alignment | net_global_root_values, net_local_poses | 1e-5 abs |
| T4b Encode velocities | same | net_local_root_values | 1e-5 abs on column 3; 1e-4 abs on columns 0-2 (they are float32 differences x30; reference float64 already shows 4.9e-5) |
| T5 Conditions (6) | targets, allowed_token_counts | four mask tensors | exact |
| T6 Rot6D decode (D4) | trimmed_pred | ref_global_rotations | 0.01 deg |
| T7 Decode (D1-D6) | net_pred_global_motions, net_pred_num_tokens, alignment, m2r | decoded_frames; contacts | 0.01 mm, 0.01 deg; contacts 1e-6 |
| T8 Endpoint deltas (C1-C2) | decoded_frames, context_token[3], target_token[0] | start_deltas, end_deltas | 0.01 mm, 0.01 deg |
| T9 Endpoint apply (C3) | decoded_frames, start/end deltas | final_frames | 0.01 mm, 0.01 deg |
| T10 Engine logic end to end | context_local, target_local, config, plus golden network outputs in place of the network | net_* inputs (T4 thresholds) and final_frames, final_contacts | 0.01 mm, 0.01 deg; contacts 1e-6 |
| T11 Chaining | req07: context_local equals req00 final_frames[n-4:n]; req11 from req10 | exact equality of the arrays | exact |
| T12 IK (K1-K2) | target_local[0], ik_input, ik_weights, ik_pin_mask | ik_output | 0.01 mm, 0.01 deg |
| T13 Endpoint identities | final_frames | frame 0 vs context_token[3]; frame n-1 vs target_token[0] | 0.01 mm, 0.01 deg |

Every test runs over all 12 requests (T12 only req09, T8/T13 skip req06).

## E. Steps

1. Create the test project only if P01 has not (`tests/SA.Inbetween.Engine.Tests`, net10.0, same test framework as the solution). Add `NpyReader`, `Goldens` (loads one `reqNN` folder) and `Metrics` (section C). Commit.
2. Write T1 and T5 (no math). Run `dotnet test --filter Category=EvalOnly`. They must fail only because the engine functions do not exist yet (compile error or NotImplementedException). Commit.
3. Write T2, T3, T4, T4b, T6. One test method per spec step, parameterised by request id 0..11. Commit.
4. Write T7, T8, T9, T13. Commit.
5. Write T10 with a fake network that returns `net_pred_global_motions` and `net_pred_num_tokens` of that request, and that also asserts its 7 inputs against the `net_*` input files (T4 thresholds, masks exact). Commit.
6. Write T11 and T12. Commit.
7. When the C# functions exist, run `dotnet test --filter Category=EvalOnly` and record per test the max error and request id in the PR description.
8. Network parity (informational, separate test `Category("EvalOnlyNetwork")`): feed `net_*` inputs to the `.sentis`/ONNX network; compare `pred_num_tokens` (exact) and `pred_global_motions` (report max abs; L3 measured 2.1e-3 to 4.4e-3). Then run T10 with the real network: expect about 0.09 mm and 0.1 deg (L3), so use 0.5 mm and 0.5 deg there, NOT 0.01.

## F. STOP-AND-REPORT conditions

- Manifest aggregate differs from section A step 2.
- Any threshold in section D is exceeded for any request, after re-checking you used the exact spec formula (do not loosen thresholds, do not "fix" goldens).
- T5 or T11 not exact.
- Network `pred_num_tokens` differs from the golden for any request (all downstream frame counts change).
- A spec step is ambiguous, or the goldens contradict the spec.
- A test would need public API changes or edits to existing tests.
- Anything would require editing `~/GitHub/motionbricks`, running `uv sync`, or committing files under `testdata/eval-only/` other than its README.
