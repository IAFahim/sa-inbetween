# MotionBricks service wire schema (draft v1)

Transport: TCP loopback, little-endian. Every message = 12-byte header + body.

Header: `magic u16 = 0x4D42 | version u8 | type u8 | request_id u32 | body_len u32`

Types: 1 HELLO, 2 HELLO_ACK, 3 GENERATE, 4 GENERATE_RESULT, 5 ERROR, 6 RESET, 7 PING/PONG, 8 CANCEL

Pose = 30 joints x (px,py,pz,qx,qy,qz,qw) f32 = 840 B. Local joint transforms, root first, SOMA order, model space declared in HELLO_ACK
(Y up for SOMA; Unity sends in a declared space and the service applies model_to_runtime if `space` says so). Quaternion XYZW.

## HELLO (Unity -> service)
`client_version u16, rig u8 (0 SOMA, 1 G1), precision u8 (0 fp32, 1 fp16-mixed), flags u32`
## HELLO_ACK
`server_version u16, rig u8, njoints u8 (30), fps u8 (30), space u8 (0 model Y-up, 1 world Z-up), engine_ready u8, min_frames u8 (18), max_frames u8 (58)`
Sent only after the TensorRT plan is loaded (first build takes minutes, so send STATUS-less keepalive PINGs meanwhile).

## GENERATE
```
request_id        u32 (header)
config:
  allowed_token_min u8     (6..16, default 8)
  allowed_token_max u8     (default 14)
  correct_generated_diff u8 (bool, default 1)
  flags u8                 (bit0 want_timing, bit1 f16_response)
  heading_alignment f32 x2 (x,z)  default (1,0)
context:
  n_poses u8 = 4
  poses[4]  Pose           (oldest first, last = most recent shown)
target_count u8            (1..N; a sequence of N targets = N chained generations)
targets[target_count]:
  num_poses_to_use u8      (1..4)
  transition_early_by_frames u16
  allowed_token_count_override u8 (0 = none; else single count)
  ik_pin_mask u16          (SOMA only; informational, applied client-side)
  poses[4] Pose            (only first num_poses_to_use are read, rest may be omitted: n_sent u8 precedes)
chain_mode u8              (0 = one generation, client re-sends context after playing;
                            1 = service chains internally, feeding last 4 output frames as the next context
                                 -- ignores transition_early_by_frames, so only for preview)
```
## GENERATE_RESULT (one per generation; chain_mode 1 streams one per target, `seq u8`, `last u8`)
```
seq u8, last u8, num_frames u8 (18..58), reserved u8
timing: tokens_chosen u8, gen_ms f32, total_ms f32 (0 if not requested)
frames[num_frames]: Pose(840 B) | root_world pos+quat (28 B) | contacts f32 x4 (LH,LT,RH,RT; 16 B)  = 884 B/frame
```
Valid-frame count is `num_frames`; boundary frames are already trimmed by the engine.
## ERROR
`code u16 (1 bad_request, 2 not_ready, 3 gpu_oom, 4 internal, 5 cancelled), msg_len u16, msg utf8`
## Notes
- request_id echoes; Unity discards results whose id is not the latest (stale after play-mode change).
- Framing is fixed-layout so C# can read with Span/MemoryMarshal and Python with numpy.frombuffer, zero JSON on the hot path.
- A JSON sidecar (HELLO only) is acceptable; hot path stays binary.
