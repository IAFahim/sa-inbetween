# sa-inbetween

Generate natural character motion between key poses, on the CPU, from a .NET command line or the Unity Editor.

You give it where a character is (four recent poses) and where it should go (a target pose). It returns a 30 fps, 77-joint motion that gets you there, using NVIDIA's MotionBricks network through ONNX Runtime. No Python, no GPU, and the same input always gives the same bytes.

```text
context poses + target pose(s)
        |  sa-inbetween generate        (CPU, about 5 s for 50 frames)
        v
   walk.saib                            SOMA rig, Y-up, metres, 30 fps
        |  sa-soma-bake                 (retarget to the SA humanoid-v1 rig)
        v
   walk.saclip                          drop into Unity
        |  SaClipPlayer                 plays on an SA shared-family character in ECS
        v
   character moves
```

Generation never runs in an ECS system. It makes a finished clip once, and only that clip enters the animation runtime.

## What is in this repository

| You want | Use |
| --- | --- |
| Motion files from the command line | `sa-inbetween` (download from Releases) |
| A .NET library | `SA.Inbetween.*` projects under `src/` |
| Unity Editor window and clip playback for SA characters | `unity/com.sa.inbetween.sa` |

The retarget and bake step (`sa-soma-bake`) lives in the SA motion repository, not here, so the last two stages above need that repository and the `com.sa.unity` package. Everything up to the `.saib` file works on its own.

## Install

1. Download the CLI for your platform from the latest release. It is self-contained, so you do not need .NET installed.
2. Download NVIDIA's MotionBricks ONNX model (`mmm_v6_soma_fp32.onnx`) from the MotionBricks repository and accept its licence. This repository does not ship the model. The tool checks its sha256 before use.

## Use

```bash
sa-inbetween generate \
  --model mmm_v6_soma_fp32.onnx \
  --context ctx0.pose --context ctx1.pose --context ctx2.pose --context ctx3.pose \
  --target goal.pose \
  --out walk.saib

sa-inbetween inspect walk.saib
```

- A `.pose` file is one pose: 77 joints x (px, py, pz, qx, qy, qz, qw), float32 little endian, 2156 bytes, local transforms, metres, SOMA model space (Y up, +Z forward).
- `--context` takes four poses (oldest first) or one `.saib` (its last four frames, or `--context-end N`).
- `--target` takes `file.pose`, `file.saib:FRAME`, or up to four comma-separated poses. Repeat `--target` to chain several generations; each starts from the previous one's last four frames.
- `pose file.saib --frame N --out x.pose` extracts a frame, which is the easy way to make poses from an existing motion.
- Exit code 0 is success, 1 is an error with a message, 2 is wrong usage.

### In Unity (SA characters)

Install first: `docs/install.md`. Then add `unity/com.sa.inbetween.sa` to your project (it needs `com.sa.unity` 0.3.7). New here: choose `SA > Inbetween > Create Demo Scene` and press the three buttons. Otherwise select two clips in the Project window, choose `Assets > Create > SA > Inbetween Bridge` and press **Generate** in the Inspector. The tools folder is set once in `Edit > Project Settings > SA Inbetween`. Step by step: `docs/designer-guide.md`.

To author generations in the Timeline window, add `unity/com.sa.inbetween.sa.timeline` (0.2.0): Source, Generate, Start/Goal Pose, Retarget / Bake, Export and Label clips on an **SA Inbetween/Generation Track**, then **Bake Timeline** writes the same files and puts the result on the timeline. Try `SA > Inbetween > Create Generation Demo`. Designers: `docs/designer-guide.md` ("Author a generation on a Timeline"); programmers adding clip types: `docs/timeline-generation.md`.

For use from your own C# code, read `apps/SA.Inbetween.Cli/Program.cs` (a complete caller of `PortedGenerator`) and `docs/ecs-integration.md` (where generation may and may not run).

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
| `unity/com.sa.inbetween.sa` | Unity package: Inbetween Bridge recipes, `.saclip` importer and `SaClipPlayer` (needs `com.sa.unity`) |
| `unity/com.sa.inbetween.sa.timeline` | Unity package: generation timelines (author and bake in the Timeline window) and the right-click Timeline bridge |
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

Apache-2.0 for the code in this repository. The MotionBricks weights are NVIDIA's, under the NVIDIA Open Model Agreement, and are not included. NVIDIA's sample motions and test recordings are evaluation-only and are never committed here. See `NOTICE`.

What is finished, what is not, and what still needs review: `docs/status.md`. Playtest readiness, versions and known limits: `docs/playtest.md`, `docs/compatibility.md`, `docs/known-limitations.md`.
