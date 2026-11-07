# sa-inbetween contract (v1)

The public API is frozen by `src/*/PublicAPI.Shipped.txt` (Microsoft.CodeAnalysis.PublicApiAnalyzers;
RS0016/RS0017 are errors). Small agents never edit those files. Spec tests live in
`tests/SA.Inbetween.Tests` and are tagged `[Trait("Packet", "Pxx")]`.

## Conventions

- All data types are `readonly record struct` or enums. Behaviour is static functions in a static
  class named after the type, plural (`Poses`, `Requests`, `MotionFiles`).
- No nulls, no exceptions across public API. Fallible functions return `Result<T, Fault>`.
- Total functions that cannot fail return the value directly (unit conversion, rotation maths).
- Stub bodies, until a packet implements them: fallible functions return `Faults.NotImplemented`;
  value functions return a NaN-poisoned value or `default`; size functions return 0.
- `Block<T>`: immutable array with structural equality (`T.Equals`; float `NaN == NaN`, `0 == -0`).
  `default(Block<T>)` is empty. Use `Blocks.BitwiseEqual` for byte-exact float comparison.
- Language: C# 10 (`LangVersion` pinned) because `record struct` needs it; only compile-time
  features, no runtime-only features (law 14). Core targets `netstandard2.1;net10.0`;
  `IsExternalInit` is polyfilled for netstandard2.1.
- All binary formats are little-endian, fixed layout, f32 IEEE 754, no padding.

## Fault

`Fault(FaultCode Code, int Index, int Value)`, one closed enum `FaultCode` for every layer
(lives in Soma, the lowest layer). Index and Value meanings:

| Codes | Index | Value |
| --- | --- | --- |
| JointIndexOutOfRange | 0 | offending index |
| PoseLength | 0 | actual float count |
| PoseValueNotFinite | float index | 0 |
| PoseQuaternionNotUnit | joint index (`abs(norm-1) > 1e-3`) | 0 |
| AllowedTokenMin/MaxOutOfRange | 0 | offending value |
| HeadingAlignmentInvalid | 0 | 0 |
| ContextPoseCount, TargetCount | 0 | actual count |
| ContextPoseLength | context index | pose float count |
| TargetPoseCount | target index | actual count |
| TargetPoseLength | target index | pose index |
| PosesToUse/TransitionEarly/TokenCountOverride OutOfRange, IkPinMaskInvalid | target index | offending value |
| FrameCountOutOfRange, ContactCountMismatch | 0 | actual count |
| FramePoseLength | frame index | pose float count |
| ContactOutOfRange | frame index | slot 0..3 (LH, LT, RH, RT) |
| WireTruncated | 0 | available bytes |
| WireBadMagic | 0 | magic read |
| WireUnsupportedVersion | 2 | version read |
| WireUnknownMessageType | 3 | type read |
| WireBodyLengthMismatch | 0 | body length given |
| WireBufferTooSmall | 0 | bytes required |
| WireFieldOutOfRange (read) | body offset of field | value read; for an invalid pose: offset of pose start, value `(int)` pose FaultCode |
| WireFieldOutOfRange (write) | 0 | offending length (count > 255, utf8 > 65535, pose not 210 floats) |
| WireUnexpectedMessageType | 0 | `(int)` type |
| MotionFileTruncated | 0 | available bytes |
| MotionFileBadMagic | 0 | magic read |
| MotionFileUnsupportedVersion | 4 | version read |
| MotionFileFieldOutOfRange | byte offset | value read |
| RecipeHashMismatch | 16 | 0 |
| Sha256Invalid | 0 = length (Value = length), 1 = bad hex char (Value = char index) | see Index |
| Remote* (from wire ERROR) | 0 | 0 |
| NetworkShapeMismatch | tensor position (inputs 0..6 in ONNX order; outputs 0 motions, 1 tokens) | actual length or token count |
| TargetIndexOutOfRange | 0 | index |
| BackendUnavailable, Timeout, Cancelled | 0 | 0 |

## Soma (`SA.Inbetween.Soma`)

| Type | Meaning |
| --- | --- |
| `SomaJoint` | model joint 0..29, only from `SomaJoints.FromIndex` or named fields (L1 order) |
| `SkeletonJoint` | USD skeleton joint 0..76; `SkeletonJoints.Root` = 0 |
| `SomaJoints.ToSkeleton` | L1 Q2 table `[0,1,2,3,4,5,6,8,9,10,11,12,13,14,18,28,39,40,41,42,46,56,67,68,69,70,72,73,74,75]` |
| `Meters, Centimeters, Radians, Degrees` | unit wrappers; `Units.To*` converts |
| `SomaModel` | Y up, right-handed, character left = +X, forward +Z, metres |
| `SomaRuntimeZUp` | `SomaModel` rotated +90 deg about X: `(x,y,z) -> (x,-z,y)`; quaternion `c * q`, `c = (sin45,0,0,cos45)` |
| `UnityWorld` | X mirror of `SomaModel`: `(x,y,z) -> (-x,y,z)`, `(qx,qy,qz,qw) -> (qx,-qy,-qz,qw)` |
| `Vector3<S>, Quaternion<S>` | phantom-typed; quaternion XYZW, Hamilton product, `Multiply(a,b)` applies b then a |
| `Rotation6D<S>` | first two rotation-matrix columns (`XAxis`, `YAxis`); decode = MotionBricks `_rotation_from_6d`: x = norm(x), z = norm(x cross y), y = z cross x; result has `W >= 0` |
| `RigidTransform<S>` | translation + rotation |
| `Pose` | 77 joints x `(px,py,pz,qx,qy,qz,qw)` = 539 floats, local transforms, root first, metres, `SomaModel`; only via `Poses.Parse` |
| `ModelPose` | the 30 model joints, 210 floats, same layout; only via `Poses.ParseModel` |
| `Poses.ToModelPose / WithModelPose` | gather the 30 from 77 / scatter 30 into a reference 77 |

## Contract (`SA.Inbetween.Contract`)

- `GenerationRequest(Context, Targets, Config)`: raw input. `Requests.Parse` is the only way to a
  `ValidRequest`. Check order (first failure wins): token min, token max, heading, context count,
  context poses, target count, then per target in order: pose count, pose lengths, poses to use,
  transition early, token override, IK mask.
- Limits: context 4 poses; 1..4 targets; 1..4 poses per target, `PosesToUse <= Poses.Length`;
  tokens 6..16 (`AllowedTokenMax >= AllowedTokenMin`); override 0 = none or 6..16;
  transition early 0..58 (proposed); IK mask bits 0..3 = LeftHand, RightHand, LeftFoot, RightFoot
  (proposed, P10 confirms against MotionBricks); heading `(x,z)` finite and not both zero.
  Defaults (L5): tokens 8..14, correction on, timing off, heading (1, 0).
- `GenerationResult`: frames (n x 77 x 7), contacts n x `FootContacts(LeftHeel, LeftToe, RightHeel,
  RightToe)` in [0,1], n in 18..58, `Timing(TokensChosen, GenerationMilliseconds, TotalMilliseconds)`.
  Only via `GenerationResults.Parse`. Frame count is `4 * tokens - 6`.
- `IMotionGenerator.GenerateAsync(ValidRequest, CancellationToken)` returns one `GenerationResult`
  per target, chained (target k+1 starts from the last 4 frames of result k). Implementations:
  `SidecarGenerator` (Python sidecar over TCP loopback) and `PortedGenerator` (C# engine + `INetwork`).
  Cancellation returns `Fault(Cancelled)`; never throws.
- `Recipe(Backend, Precision, Model sha256, Inputs)`. Canonical bytes (all LE):
  `backend u8, precision u8, model[32], config: token_min u8, token_max u8, correct u8,
  want_timing u8, heading_x f32, heading_z f32, context_count u8, context poses (539 f32 each),
  target_count u8, per target: pose_count u8, poses_to_use u8, transition_early u16,
  token_override u8, ik_mask u16, poses (539 f32 each)`. `WriteCanonical` first runs
  `Requests.Parse(Inputs)` and returns its fault if invalid. `Recipes.Hash` = sha256 of the
  canonical bytes. `Sha256s.ToHex` is lowercase; `ParseHex` accepts either case.
- Pins: `ModelPins` holds the sha256 and size of `mmm_v6_soma_fp32.onnx`, the sha256 of
  `soma_n1.json`, and the motionbricks commit they come from.

### Motion file (`.saib`, `MotionFiles`)

```text
offset size
0      4   magic "SAIB" (u32 0x42494153)
4      2   version u16 = 1
6      1   axes u8 (0 = SomaModel Y up right-handed)
7      1   units u8 (0 = metres)
8      2   fps u16 (30)
10     2   frame_count u16
12     2   joint_count u16 (77)
14     2   reserved u16 = 0
16     32  recipe sha256
48     4   recipe_len u32
52     n   canonical recipe bytes
...        frames: frame_count x 77 x 7 f32
...        root world: frame_count x 7 f32 (px,py,pz,qx,qy,qz,qw; = joint 0 of each frame)
...        contacts: frame_count x 4 f32 (LH, LT, RH, RT)
```

Read order: length >= 48, magic, version, axes, units, joint_count (field faults), total length,
recipe, recipe hash (`RecipeHashMismatch`), frames (via `Poses.Parse`), root, contacts.
`MotionFiles.From(result, recipe)` sets fps 30, axes 0, units 0, root = joint 0 per frame,
hash = `Recipes.Hash(recipe)`.

### Wire (`SA.Inbetween.Contract.Wire`, after docs/reports/L5-schema.md)

Header (12 B): `magic u16 0x4D42, version u8 1, type u8, request_id u32, body_len u32`.
Types: 1 Hello, 2 HelloAck, 3 Generate, 4 GenerateResult, 5 Error, 6 Reset, 7 Ping (and Pong),
8 Cancel. Reset, Ping and Cancel have empty bodies (`WriteEmpty`). Pose on the wire is a
`ModelPose` (840 B).

| Body | Layout |
| --- | --- |
| Hello (8) | client_version u16, rig u8 (0 Soma, 1 G1), precision u8 (0 fp32, 1 fp16-mixed), flags u32 |
| HelloAck (9) | server_version u16, rig u8, njoints u8, fps u8, space u8 (0 model Y up, 1 world Z up), engine_ready u8 (0/1), min_frames u8, max_frames u8 |
| Generate | config (12: token_min u8, token_max u8, correct u8 0/1, flags u8 (bit0 want_timing; any other bit is a field fault in v1), heading_x f32, heading_z f32), n_context u8, poses, target_count u8, per target: poses_to_use u8, transition_early u16, token_override u8, ik_mask u16, n_sent u8, poses; chain_mode u8 (0 single, 1 service-chained) |
| GenerateResult | seq u8, last u8 (0/1), num_frames u8, reserved u8 (written 0, ignored), tokens_chosen u8, gen_ms f32, total_ms f32, then num_frames x (ModelPose 840 B, root_world 7 f32, contacts 4 f32) = 884 B |
| Error | code u16 (1 bad_request, 2 not_ready, 3 gpu_oom, 4 internal, 5 cancelled), msg_len u16, utf8 bytes |

`Write*` writes header + body and returns the total byte count; `Read*` takes the body only
(after `ReadHeader`) and must consume it exactly (`WireBodyLengthMismatch` otherwise). The codec is
structural: it checks sizes, enums, booleans and pose validity, not request semantics; turning a
`Generate` into a `ValidRequest` goes through `Requests.Parse`. FP16 responses are not part of v1.

## Engine (`SA.Inbetween.Engine`) and Generators

- ONNX tensors (batch 1, int32 masks): `global_root_values [8,5]`, `has_global_root_values [8]`,
  `local_root_values [8,4]`, `has_local_root_values [8]`, `local_poses [8,267]`,
  `has_local_poses [8]`, `allowed_pred_num_tokens [11]` (slot k = k+6 tokens);
  outputs `pred_global_motions [64,366]`, `pred_num_tokens` (scalar int32).
- `EngineEncoder.Encode(request, target, context)` (P09) and `EngineDecoder.Decode` (P10) are owned
  by Smart packets and may gain internal helpers; their public signatures are frozen here.
- `INetwork.RunAsync` has two implementations: `OnnxNetwork` (P11, tests and tools only, project
  `SA.Inbetween.Onnx`) and the Unity Inference Engine network (P14).
