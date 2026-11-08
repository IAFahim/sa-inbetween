# sa-inbetween

Generate SOMA-rig motion between key poses with NVIDIA MotionBricks, as a small, deterministic .NET
library plus a command-line tool. No Python or GPU needed: the network runs through ONNX Runtime on
the CPU (about 4.6 s for a cold 50-frame generation including model load).

Output is a `.saib` motion file (30 fps, 77 joints, metres, SOMA model space) that the SA pipeline
retargets to the humanoid-v1 rig (`sa-motion`, branch `packet/P08-soma-rig`).

## Quick start

You need the .NET 10 SDK and the MotionBricks ONNX model
(`mmm_v6_soma_fp32.onnx`, sha256 pinned in `SA.Inbetween.Contract.ModelPins`). The model is NVIDIA's;
download it from the MotionBricks repository, it is not included here.

```bash
dotnet run --project apps/SA.Inbetween.Cli -c Release -- generate \
  --model path/to/mmm_v6_soma_fp32.onnx \
  --context ctx0.pose --context ctx1.pose --context ctx2.pose --context ctx3.pose \
  --target goal.pose \
  --out walk.saib

dotnet run --project apps/SA.Inbetween.Cli -c Release -- inspect walk.saib
```

- A `.pose` file is one pose: 77 joints x (px, py, pz, qx, qy, qz, qw), float32 little endian,
  2156 bytes, local transforms, metres, SOMA model space (Y up, +Z forward).
- `--context` takes four poses (oldest first) or one `.saib` (its last four frames, or
  `--context-end N`).
- `--target` takes `file.pose`, `file.saib:FRAME`, or up to four comma-separated poses. Repeat
  `--target` to chain several generations; each starts from the previous one's last four frames.
- The model's sha256 is verified before use (`--skip-verify` to bypass).
- `pose file.saib --frame N --out x.pose` extracts a frame.

## Layout

| Path | What |
| --- | --- |
| `src/SA.Inbetween.Soma` | Units, rotations, 6D, pose types, `Result`/`Fault` (netstandard2.1 + net10.0) |
| `src/SA.Inbetween.Contract` | Requests, results, wire protocol, recipes, `.saib` files |
| `src/SA.Inbetween.Engine` | Encode/decode kernels, endpoint correction, IK kernels, `INetwork` |
| `src/SA.Inbetween.Generators` | `PortedGenerator` (engine + `INetwork`), `SidecarGenerator` (TCP) |
| `src/SA.Inbetween.Onnx` | `OnnxNetwork` (ONNX Runtime, net10.0) |
| `apps/SA.Inbetween.Cli` | `sa-inbetween` tool |
| `unity/com.sa.inbetween` | Unity package of the core libraries (`build/pack-unity.sh` builds the DLLs; they are not committed) |
| `unity/com.sa.inbetween.sa` | Unity package: `.saclip` importer and `SaClipPlayer` for SA shared-family characters (needs `com.sa.unity`) |
| `docs/` | Contract, engine spec and measured parity, reports |

## Accuracy

Measured against NVIDIA's TensorRT engine on 12 recorded requests: encode within 1.4e-6, decoded
frames within 0.001 mm and 4.1e-5 degrees, zero allocations per kernel call. End to end through
the CLI on CPU ONNX: 0.06 mm and 0.2 degrees on a 50-frame clip. Details: `docs/engine/status.md`.

Known deviations (documented, not bugs):

- Finger and hand-end joints hold the last context pose instead of NVIDIA's A-pose bind, so they
  can differ from the reference by up to 0.36 degrees mid-clip. Body joints match.
- IK pin correction is available as kernels; it is not applied inside `Decode`.

## Develop

```bash
dotnet build SA.Inbetween.sln -warnaserror
dotnet test --project tests/SA.Inbetween.Tests
dotnet test --project tests/SA.Inbetween.Architecture.Tests
dotnet format SA.Inbetween.sln --verify-no-changes
```

Golden-based tests skip unless NVIDIA's evaluation-only recordings are placed in
`testdata/eval-only/` (never commit them; see `testdata/eval-only/README.md`). Engineering rules:
`AGENTS.md`.

## Licence

Apache-2.0 (code). See `NOTICE` for third-party terms, in particular the NVIDIA Open Model
Agreement that governs the model weights.
