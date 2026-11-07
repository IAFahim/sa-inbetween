# MotionBricks SOMA engine logic: port specification (P09/P10)

Scope: everything the official SOMA engine does around the network, i.e. what the
`.sentis` file does NOT contain. Source of truth: `~/GitHub/motionbricks` v0.2.0 (read-only).
Citations are `file:line` relative to `motionbricks/` (the package dir) unless they start
with `docs/` or `app/`. Executable form of this spec: `tools/engine_reference.py`
(NumPy, float64, imports nothing from motionbricks). `tools/dump_goldens.py` checks it
against the official engine on 12 requests; results in section 13.

Tags: **VERIFIED** = read in source and matched numerically by the goldens.
**INFERRED** = not proven by code or goldens; reason given.

## 0. Constants (VERIFIED, model/contract.py:21-46, model/config.py:12,32-34)

| Name | Value | Meaning |
|---|---|---|
| SAMPLE_RATE | 30.0 | Hz, input and output |
| POSES_PER_TOKEN (P) | 4 | |
| CONTEXT_AND_TARGET | 8 | rows 0..3 context, rows 4..7 target |
| MIN_TOKENS / MAX_TOKENS | 6 / 16 | |
| BOUNDARY (B) | 3 (= P-1) | frames trimmed at each end |
| valid frames n | `4*tokens - 6` (18..58) | model/contract.py:32-43 |
| MAX_VALID_FRAMES | 58 | output capacity |
| NUM_MODEL_JOINTS (M) | 30 | |
| FOOT_CONTACTS | 4 | |
| DEFAULT allowed tokens | 8..14 | model/config.py:12 |
| DEFAULT heading_alignment | (1, 0) | (x, z) in model space, faces +X |
| DEFAULT correct_generated_diff | true | |

## 1. Data types and math conventions (VERIFIED against warp 1.17 native/quat.h)

- Transform = 7 floats `[px,py,pz, qx,qy,qz,qw]` (quaternion XYZW), metres. All arrays float32.
- `qmul(a,b)` = Hamilton product, `a*b` applies b first then a. `qconj` = (-x,-y,-z,w);
  warp's `quat_inverse` IS the conjugate (no division by norm).
- `qrot(q,v)` and `qrot_inv(q,v)` exactly as engine_reference.py:38-55 (warp formula with
  `c = 2w^2-1`; it is only a pure rotation for unit q, keep the formula for bit-closeness).
- `tmul(a,b) = (a.p + qrot(a.q,b.p), a.q*b.q)`; `tinv(t) = (-qrot(conj q, p), conj q)`.
- `normalize(v)`: v/|v|, zero vector if |v| == 0 (INFERRED threshold: warp returns zero for
  length <= its epsilon; only matters for degenerate input).
- `q_axis_angle(axis, a) = (axis*sin(a/2), cos(a/2))`.
- `slerp(q0,q1,t)` (native/quat.h:324-330): `d = conj(q0)*q1`; `axis = normalize(d.xyz)`,
  negated if `d.w < 0`; `angle = 2*atan2(|d.xyz|, |d.w|)`; result `q0 * q_axis_angle(axis, t*angle)`.
  This takes the short path. slerp(identity, identity, t) = identity.
- `blend_transform(t0,t1,w)` = (lerp(p0,p1,w), slerp(q0,q1,w)) (utils/pose_utils.py:206-215).
- `quat_from_matrix(m)` (native/quat.h:342-392): trace branch / largest-diagonal branch exactly
  as engine_reference.py:85-121, then normalize. `m[r][c]` is row r, column c.
- `quat_between_vectors(a,b)` (warp/_src/utils.py:38-45): a,b normalized,
  `normalize((a x b, 1 + a.b))`.
- Forward kinematics (utils/pose_utils.py:72-92): `world[0] = root_tx * local[0]`;
  `world[j] = world[parent[j]] * local[j]` for j = 1..J-1 (parents precede children).

## 2. Spaces

- Runtime/world space: what the caller's poses use. Joint 0 (Hips) local transform is its
  world transform in runtime space; all other joints are parent-relative.
- Model space: Y up (docs/soma_model.md). `model_to_runtime` (m2r) is an engine constructor
  argument (model/engine.py:42,85-86), default identity; `runtime_to_model = tinv(m2r)`.
  The SOMA examples use MuJoCo space: m2r = rotation +90 deg about X =
  `[0,0,0, 0.70710677,0,0,0.70710677]` (utils/space_conversion_utils.py:30, docs/examples/_rig.py:59).
  Goldens req00-09 use that m2r; req10-11 use identity.
- Aligned space: model space translated so the last context hips sit at ground origin and
  rotated about +Y so they face `heading_alignment` (section 5).

## 3. Skeleton binding (VERIFIED, model/soma/engine.py:90-127, soma/contract.py:15-17)

Input skeleton: 77 joints (SOMA USD `soma_base_skel_minimal_no_root.usd`), root at index 0.
Model joints are found by name. `model_to_pose[m]` = skeleton index of model joint m.
`pose_to_model[j]` = m if joint j is model joint m AND (j is the root or its parent already
maps to a model joint), else -1 (loop in model order, engine.py:108-114).

| m | name | skel idx | p2m | | m | name | skel idx | p2m |
|---|---|---|---|---|---|---|---|---|
| 0 | Hips | 0 | 0 | | 15 | LeftHandMiddleEnd | 28 | **-1** |
| 1 | Spine1 | 1 | 1 | | 16 | RightShoulder | 39 | 16 |
| 2 | Spine2 | 2 | 2 | | 17 | RightArm | 40 | 17 |
| 3 | Chest | 3 | 3 | | 18 | RightForeArm | 41 | 18 |
| 4 | Neck1 | 4 | 4 | | 19 | RightHand | 42 | 19 |
| 5 | Neck2 | 5 | 5 | | 20 | RightHandThumbEnd | 46 | **-1** |
| 6 | Head | 6 | 6 | | 21 | RightHandMiddleEnd | 56 | **-1** |
| 7 | Jaw | 8 | 7 | | 22 | LeftLeg | 67 | 22 |
| 8 | LeftEye | 9 | 8 | | 23 | LeftShin | 68 | 23 |
| 9 | RightEye | 10 | 9 | | 24 | LeftFoot | 69 | 24 |
| 10 | LeftShoulder | 11 | 10 | | 25 | LeftToeBase | 70 | 25 |
| 11 | LeftArm | 12 | 11 | | 26 | RightLeg | 72 | 26 |
| 12 | LeftForeArm | 13 | 12 | | 27 | RightShin | 73 | 27 |
| 13 | LeftHand | 14 | 13 | | 28 | RightFoot | 74 | 28 |
| 14 | LeftHandThumbEnd | 18 | **-1** | | 29 | RightToeBase | 75 | 29 |

Hand-end joints 14,15,20,21 are encoded (they are model joints) but NOT decoded (their
parents, finger joints, are not model joints). All 77 parents and names are in
`goldens/skeleton.json`. Heading joints: RIGHT = RightLeg (72), LEFT = LeftLeg (67)
(soma/contract.py:17). Foot contact joint names, in channel order: LeftFoot, LeftToeBase,
RightFoot, RightToeBase (soma/contract.py:15; channel order INFERRED from that tuple, the
decode only reads the last 4 channels in order).

## 4. Tokens (VERIFIED, model/generation.py:38-68, model/kernels.py:14-21, model/generation.py:85-94)

- Context: exactly 4 local poses (77x7), time order at 30 Hz, the last one is "now".
- Target: k = 1..4 local poses; row r of the 4-row token = `poses[min(r, k-1)]` (pad with last).
- Root (joint 0) of both is in runtime world space.
- Validation: context k must be 4, target 1..4, else error.

## 5. Encode (VERIFIED, model/soma/engine.py:129-173, model/soma/kernels.py:14-25,55-160)

E1. `world[0..3] = FK(context_token, root_tx = runtime_to_model)`; `world[4..7] = FK(target_token, runtime_to_model)` (engine.py:136-142). Shape 8x77x7, model space. Golden: `guide_world.npy`.

E2. `heading(right, left)` (kernels.py:14-25): `ax = right.x-left.x`, `az = right.z-left.z`;
if `sqrt(ax^2+az^2) <= 1e-6` return 0; normalize (ax,0,az); `forward = (0,1,0) x across = (az, 0, -ax)`;
return `atan2(forward.x, forward.z)` = `atan2(az, -ax)` (normalized values).

E3. Alignment (kernels.py:55-78): `hips = world[3][0].p`;
`cur = heading(world[3][72].p, world[3][67].p)`; `des = atan2(h.x, h.z)` where `h = heading_alignment` = (x, z) (need not be unit);
`alignment = ( (hips.x, 0, hips.z), q_axis_angle((0,1,0), cur - des) )`. Golden: `alignment.npy` [1,7].

E4. Aligned position `ap(p) = qrot_inv(alignment.q, p - alignment.p)` (subtract first, kernels.py:81-84);
aligned rotation `ar(q) = conj(alignment.q) * q` (kernels.py:143). Golden (reference-computed, INFERRED-labelled only because the engine never stores it): `ref_aligned_world.npy`.

E5. For each row f = 0..7 (kernels.py:104-137):
`nf = f if f == 3 else min(f+1, 7)` (so rows 3 and 7 have zero velocity);
`ch = ap(world[f][0].p)`, `nh = ap(world[nf][0].p)`;
`hc = heading(ap(world[f][72].p), ap(world[f][67].p))`, `hn` same for nf;
`dh = atan2(sin(hn-hc), cos(hn-hc))`.
- `global_root_values[0,f,:] = [ch.x, ch.y, ch.z, cos(hc), sin(hc)]` (5 floats)
- `local_root_values[0,f,:] = [dh*30, (nh.x-ch.x)*30, (nh.z-ch.z)*30, ch.y]` (4 floats)

E6. local_poses[0,f,0..266] (kernels.py:139-160), `ground = (ch.x, 0, ch.z)`; for m = 0..29, `j = model_to_pose[m]`:
- if m > 0: `local_poses[(m-1)*3 + 0..2] = ap(world[f][j].p) - ground` (indices 0..86; root has no position slot)
- `R = ar(world[f][j].q)` (a GLOBAL rotation in aligned space, not a local one);
  `o = 87 + 6*m`; `local_poses[o..o+2] = qrot(R,(1,0,0))`, `local_poses[o+3..o+5] = qrot(R,(0,1,0))`.
  6D = first two COLUMNS of the rotation matrix, column X then column Y, each x,y,z.

## 6. Conditions (VERIFIED, model/generation.py:97-120, model/kernels.py:25-51, model/config.py:48-53)

`bits = OR over allowed counts c of (1 << (c-6))`. For i = 0..10:
`allowed_pred_num_tokens[0,i] = (bits >> i) & 1` (index i means i+6 tokens). For i = 0..7 (k = target pose count):
- `has_global_root_values[0,i] = has_local_poses[0,i] = (i < 4 + k) ? 1 : 0`
- `has_local_root_values[0,i] = 0 if i == 3 or i == 3 + k, else the value above`.
All int32. Example k=1: hg = hp = [1,1,1,1,1,0,0,0], hl = [1,1,1,0,0,0,0,0].
The network inputs are then: global_root_values [1,8,5], local_root_values [1,8,4],
local_poses [1,8,267], the three masks [1,8], allowed_pred_num_tokens [1,11]. Masked
rows still carry encoded (padded) values; they are not zeroed (VERIFIED in goldens).

## 7. Network (not part of this port)

Outputs: `pred_global_motions` [1,64,366] float32, `pred_num_tokens` [1] int32.
Feature layout (kernels.py:199-203): `[0:5]` root (`[0:3]` hips position in aligned space,
height included; `[3:5]` presumably cos/sin heading, unused, INFERRED), `[5:92]` 29 child
positions, `[92:272]` 30 global 6D rotations (offset `92 + 6*m`), `[272:362]` 30 local
velocities (unused), `[362:366]` foot contacts.

## 8. Decode and trimming (VERIFIED, model/soma/kernels.py:163-241)

D1. `raw = clamp(tokens*4, 0, 64)`; `n = max(0, raw - 6)`. The engine then requires
`tokens` in the allowed set, else error (model/generation.py:123-137), and sets
`num_frames = 4*tokens - 6`.
D2. For output frame f = 0..n-1, raw row `r = 3 + f` (trim 3 at each end). Golden: `trimmed_pred.npy` = pred[0, 3:3+n].
D3. `contacts[f,c] = clamp(pred[r, 362+c], 0, 1)`, c = 0..3.
D4. `rot6d(a,b)`: `x = normalize(a)`; `z = normalize(cross(x, b))`; `y = cross(z, x)`;
`q = quat_from_matrix(matrix with COLUMNS x, y, z)` (kernels.py:28-33). `G[m] = rot6d(pred[r,92+6m..+2], pred[r,95+6m..+2])`. Golden (reference): `ref_global_rotations.npy` [n,30,4].
D5. Root: `out[f,0] = m2r * (alignment * (pred[r,0:3], G[0]))` (transform products; this also restores
the ground XZ and heading removed in E3).
D6. For skeleton joint j = 1..76: `m = pose_to_model[j]`; if m < 0: `out[f,j] = reference_local[j]`
(skeleton bind pose, `reference_local.npy`); else `out[f,j] = (reference_local[j].p, normalize(conj(G[pose_to_model[parent[j]]]) * G[m]))`.
Translations of non-root joints ALWAYS come from the reference pose, not from the inputs.
Golden: `decoded_frames.npy`, `contacts.npy`.

## 9. Endpoint correction, `correct_generated_diff` (VERIFIED, model/soma/kernels.py:244-317, utils/pose_utils.py:239-299, model/engine.py:127-129)

Only when the flag is true and n >= 2. `C = context_token[3]` (last context pose, LOCAL), `T = target_token[0]` (first target pose, LOCAL), `F` = decoded frames.
C1. Root deltas (pose_utils.py:265-275): `start0 = (C[0].p - F[0][0].p, normalize(conj(normalize(F[0][0].q)) * normalize(C[0].q)))`; `end0` same with `F[n-1][0]` and `T[0]`. Position delta is a world-space offset.
C2. Other joints j >= 1 (all 76, including non-model joints): `start[j] = tinv(F[0][j]) * C[j]`, `end[j] = tinv(F[n-1][j]) * T[j]` (translations included).
C3. For f = 0..n-1: `ew = f/(n-1)` (float32), `sw = 1 - ew`.
- Root (pose_utils.py:279-299): `p = F[f][0].p + start0.p*sw + end0.p*ew`;
  `q = normalize( F[f][0].q * slerp(I, start0.q, sw) * slerp(I, end0.q, ew) )`.
- j >= 1: `out = (F[f][j] * blend_transform(identity, start[j], sw)) * blend_transform(identity, end[j], ew)` (no normalize).
Result: frame 0 equals C, frame n-1 equals T (to float precision). Non-model joints (fingers)
therefore blend from context to target. Contacts are untouched.
Golden: `start_deltas.npy`, `end_deltas.npy` [77,7], `final_frames.npy`, `final_contacts.npy`.

## 10. Chaining (VERIFIED in docs/examples/generate_once.py:63, docs/runtime_api.md:229-237)

Next context = the last 4 frames actually shown: if the motion played to the end,
`final_frames[n-4:n]` (local poses as output, root in runtime space). An app that leaves
early uses the last 4 poses it played. Golden req07 chains from req00, req11 from req10.
The recorder path (`PoseToken.update_from_recorder`, soma/buffers.py:78-109) resamples 4
poses at 30 Hz from a variable-rate history with `blend_transform`; its sample-index rule
(`utils/time_utils.history_sample_indices`) was NOT read: GAP, out of scope here.

## 11. Optional IK-pin corrector (VERIFIED, runtime/target_pose.py:27-35,132-137,223-300, animation/ik.py:15-143)

App-level, not part of `generate_animation`. Pins: LEFT_HAND=1, RIGHT_HAND=2, LEFT_FOOT=4, RIGHT_FOOT=8.
Chains (root, mid, tip, hint in mid-local metres): LeftArm 12, LeftForeArm 13, LeftHand 14, (0,-0.25,0);
RightArm 40, RightForeArm 41, RightHand 42, (0,0.25,0); LeftLeg 67, LeftShin 68, LeftFoot 69, (0,-0.25,0);
RightLeg 72, RightShin 73, RightFoot 74, (0,0.25,0).
K1. Capture: `pinned[k] = FK(target_pose_local, identity)[tip_k]` (runtime world).
K2. Apply to one local pose with `weight`: return unchanged if mask == 0 or weight <= 0.
`W = FK(pose, identity)`. For each chain whose pin bit is set (chains are disjoint, all
read the same W): `pa = W[parent[root]]`, `hint = tmul(W[mid], (hint_local, I)).p`, solve
(ik.py:35-95, EPS = 1e-3, use_hint always true) exactly as engine_reference.py:348-376, then
`local[root] = tinv(pa)*A`, `local[mid] = tinv(A)*Bw`, `local[tip] = tinv(Bw)*Cw` (ik.py:141-143).
K3. App weight (app/runtime/common/character.py:50,287-293): `progress = 1 - time_to_transition/playback_duration`, `weight = smoothstep(0.5, 1, progress)` (1 if duration <= 0); smoothstep(e0,e1,x): `t = clamp((x-e0)/(e1-e0),0,1)`, `t*t*(3-2t)`.
Golden req09: `ik_frames`, `ik_weights`, `ik_input`, `ik_output`, `ik_pin_mask` (=15, all four pins, captured from target pose 0).

## 12. Full call order (VERIFIED, model/engine.py:114-135)

validate (sec 4) -> E1..E6 + conditions -> network -> D1..D6 -> (if flag) C1..C3 -> check token count in allowed set -> `num_frames = 4*tokens - 6`.

## 13. Kernel coverage and evidence

| Kernel / func | Spec | Golden check (max vs official FP32, worst of 12 requests) |
|---|---|---|
| pad_token_poses_kernel (model/kernels.py:14) | 4 | exact (0.0) |
| _compute_world_poses_kernel (pose_utils.py:96) | 1, E1 | 0.0012 mm, 3e-5 deg |
| compute_guide_alignment_kernel (soma/kernels.py:55) | E2-E3 | 7.8e-8 abs |
| encode_motionbricks_inputs_kernel (soma/kernels.py:87) | E4-E6 | grv 1.4e-6, local_poses 5.5e-7, lrv 6.8e-5 abs |
| write_motionbricks_conditions_kernel (model/kernels.py:25) | 6 | exact |
| decode_motionbricks_output_kernel (soma/kernels.py:163) | 8 | 0.00072 mm, 2.1e-5 deg, contacts exact |
| compute_endpoint_deltas_kernel (soma/kernels.py:244) | C1-C2 | 1.1e-6 mm, 4.5e-6 deg |
| apply_endpoint_deltas_kernel (soma/kernels.py:284) | C3 | 0.00038 mm, 1.9e-5 deg |
| encode->final end to end from net outputs | all | 0.00098 mm, 3.5e-5 deg |
| apply_two_bone_ik_chains_kernel (animation/ik.py:98), _capture_pinned_transforms_kernel (target_pose.py:155) | 11 | 0.0011 mm, 0.00028 deg |
| _sample_recorder_kernel (soma/buffers.py:15) | 10 | NOT MAPPED (history resampling) |
| target_pose.py speed/placement kernels (:355,:463) | - | out of scope (app target placement) |

All kernels in `model/soma/kernels.py` and `model/kernels.py` are mapped. The stepwise
private-method run is bit-identical to the public `generate_animation` for all 12 requests.
lrv error is larger because it is a float32 position difference times 30.
