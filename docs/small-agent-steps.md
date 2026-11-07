# Small-agent steps

Literal steps for Small agents. Do them in order. Do not improvise. If a step does not work exactly
as written, stop and report (see STOP-AND-REPORT at the end).

## 0. Baseline (all packets)

Spec test counts at the end of P02 (`dotnet test`, project `tests/SA.Inbetween.Tests`):

| Trait | Total | Fail (not implemented) | Pass | Skipped (no corpus) | Owner |
| --- | --- | --- | --- | --- | --- |
| P02 | 60 | 0 | 60 | 0 | done (types, Parse, Pose parse, joints) |
| P04 | 41 | 35 | 6 | 0 | wire codec, sha256, recipe, motion file |
| P06 | 2 | 1 | 0 | 1 | sidecar client |
| P07 | 22 | 21 | 1 | 0 | units, rotations, spaces, pose mapping |
| P09 | 6 | 5 | 0 | 1 | engine encode (Smart) |
| P10 | 6 | 5 | 0 | 1 | engine decode (Smart) |
| P11 | 4 | 3 | 1 | 0 | network tensor shapes |
| P12 | 2 | 2 | 0 | 0 | ported generator |
| all | 143 | 72 | 68 | 3 | |

Architecture tests: 6 of 6 pass (`tests/SA.Inbetween.Architecture.Tests`). The P04 tests that already
pass are totality tests (random bytes never throw) and constants; they must keep passing.

Common commands (run from the repo root of your worktree):

```bash
dotnet build SA.Inbetween.sln -warnaserror
dotnet test --project tests/SA.Inbetween.Tests --filter-trait "Packet=P04"
dotnet test --project tests/SA.Inbetween.Architecture.Tests
dotnet format SA.Inbetween.sln --verify-no-changes
```

The test runner is Microsoft.Testing.Platform (set in `global.json`), so the filter flag is
`--filter-trait "Packet=Pxx"`, not `--filter`. A failing property test prints a seed; rerun it with
`CsCheck_Seed=<seed> dotnet test ...`.

Rules for every packet:

1. Work only in your worktree on branch `packet/<ID>`. Edit only the files listed for your packet.
2. Never edit `PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`, any file under `tests/` that
   already exists, `Directory.Build.props`, `build/BannedSymbols.txt` or `.editorconfig`.
3. Replace stub bodies only. Keep every signature exactly. Add `private` or `internal` helpers in the
   same file if you need them. A new `public` member is a public API change: stop and report.
4. No comments in code. No exceptions thrown or escaping. No `null`. No `DateTime.Now`, `Random`
   without a seed, `Guid.NewGuid`, `Environment`, culture-sensitive `ToString`/`Parse`.
5. Read and write binary with `System.Buffers.Binary.BinaryPrimitives.*LittleEndian` and
   `BitConverter.SingleToInt32Bits` / `Int32BitsToSingle` (both exist on netstandard2.1). Never use
   `MemoryMarshal.Cast` on structs, `unsafe`, or `BitConverter.GetBytes` in src.
6. Commit with messages ending in the attribution line the lead gives you.

## P04 · Wire codec, sha256, recipe, motion file

Files you may change: `src/SA.Inbetween.Contract/Wire.cs`, `Recipes.cs`, `MotionFiles.cs`.
Spec: docs/contract.md sections Fault, Recipe, Motion file, Wire.

1. `dotnet test --project tests/SA.Inbetween.Tests --filter-trait "Packet=P04"`; confirm 35 failed, 6 passed.
2. In `Recipes.cs` implement `Sha256s.Of` with `System.Security.Cryptography.SHA256.Create()` and
   `ComputeHash(byte[])` (netstandard2.1 has no span `HashData`; copy the span to an array). Build the
   result with `Sha256s.FromBytes`.
3. Implement `Sha256s.ToHex` (lowercase, use a `"0123456789abcdef"` lookup, no `ToString("x2")`)
   and `ParseHex` (length 64 else `Fault(Sha256Invalid, 0, length)`; first non-hex char at i gives
   `Fault(Sha256Invalid, 1, i)`; accept upper and lower case).
4. Run the filter. `Sha256Spec` must pass (5 tests).
5. Implement `Recipes.CanonicalSize`, `WriteCanonical`, `ReadCanonical`, `Hash` exactly in the
   canonical layout. `WriteCanonical`: first `Requests.Parse(recipe.Inputs)`; on fault return that
   fault; if the destination is shorter than `CanonicalSize` return
   `Fault(WireBufferTooSmall, 0, size)`. `ReadCanonical`: truncated input gives
   `Fault(WireTruncated, 0, source.Length)`, trailing bytes give `Fault(WireBodyLengthMismatch, 0, source.Length)`,
   bad enum gives `Fault(WireFieldOutOfRange, offset, value)`; poses go through `Poses.Parse`;
   the rebuilt request goes through `Requests.Parse` (return its fault). `Hash` = `Sha256s.Of` of the
   canonical bytes (for an invalid recipe return `default`).
6. Run the filter. `RecipeSpec` must pass (6 tests).
7. Implement the `WireCodec` functions in table order of docs/contract.md, Wire. For each: header
   first (`magic, version 1, type, request_id, body_len`), then the body. Faults exactly as in the
   Fault table. A pose on the wire is read with `Poses.ParseModel`; on its fault return
   `Fault(WireFieldOutOfRange, poseStartOffset, (int)innerFault.Code)`.
8. Run the filter after each message type. Order: `ReadHeader`, Hello, HelloAck, Error, `WriteEmpty`,
   Generate, GenerateResult, `ToFault`.
9. Implement `MotionFiles.From`, `SizeOf`, `Write`, `Read` exactly in the motion-file layout.
10. Run the filter: 41 passed, 0 failed. Run the full build with `-warnaserror`, the architecture tests
    and `dotnet format --verify-no-changes`.

Done when: `--filter-trait "Packet=P04"` shows 41 passed, 0 failed; P02 still 60 passed; build has
0 warnings. Allocation-free steady state is measured later in P19 (encode into caller buffers only;
do not allocate in `Write*` except through `Block` reads).

## P07 easy half · Units, rotations, spaces, pose mapping

Files you may change: `src/SA.Inbetween.Soma/Units.cs`, `Rotations.cs`, `Poses.cs` (only
`ToModelPose` and `WithModelPose`). Heading, alignment and IK stay with the Smart half of P07.

1. `dotnet test --project tests/SA.Inbetween.Tests --filter-trait "Packet=P07"`; confirm 21 failed, 1 passed.
2. `Units`: centimetres = metres x 100; metres = centimetres / 100; degrees = radians x (180 / pi);
   radians = degrees x (pi / 180). Use `float` arithmetic and the `(float)Math.PI` constant (not
   `MathF`, which netstandard2.1 lacks).
3. `Rotations` (quaternions are XYZW, Hamilton):
   - `Multiply(a, b)`: `x = aw*bx + ax*bw + ay*bz - az*by`, `y = aw*by - ax*bz + ay*bw + az*bx`,
     `z = aw*bz + ax*by - ay*bx + az*bw`, `w = aw*bw - ax*bx - ay*by - az*bz`.
   - `Conjugate`: `(-x, -y, -z, w)`.
   - `Normalize`: divide by `sqrt(x²+y²+z²+w²)` computed in `double`; a zero quaternion returns `Identity`.
   - `Rotate(q, v)`: `t = 2 * cross(q.xyz, v)`; `v' = v + w*t + cross(q.xyz, t)`.
   - `FromAxisAngle(axis, angle)`: `(axis * sin(angle/2), cos(angle/2))`.
   - `AngleBetween(a, b)`: `2 * acos(min(1, |dot(a, b)|))` in `double`, returned as `Radians`.
   - `ToRotation6D(q)`: `XAxis = Rotate(q, (1,0,0))`, `YAxis = Rotate(q, (0,1,0))`.
   - `FromRotation6D(r)`: `x = normalize(XAxis)`, `z = normalize(cross(x, YAxis))`, `y = cross(z, x)`;
     matrix columns (x, y, z) to quaternion with the standard trace method (Shepperd); if `w < 0`
     negate all four components.
4. `SpaceConversions` (formulas in docs/contract.md, Soma): `ModelToRuntime` vector `(x, -z, y)`,
   quaternion `Multiply(c, q)` with `c = (sqrt(0.5), 0, 0, sqrt(0.5))`; `RuntimeToModel` vector
   `(x, z, -y)`, quaternion `Multiply(conjugate(c), q)`; `ModelToUnity` and `UnityToModel` both
   `(-x, y, z)` and `(qx, -qy, -qz, qw)`. The phantom type parameter changes, so build new structs;
   do not cast.
5. `Poses.ToModelPose`: copy 7 floats for each model joint i from skeleton joint
   `SomaJoints.ToSkeleton(i)` into slot i; `WithModelPose`: copy the reference floats, then write each
   model joint's 7 floats back to its skeleton slot. Build results with `Poses.Parse`/`ParseModel`
   (inside `Poses.cs` you may call the internal constructors directly instead, after the copy).
6. Run the filter: 22 passed, 0 failed. Then build `-warnaserror`, architecture tests, format.

Done when: `--filter-trait "Packet=P07"` shows 22 passed; P02 still 60 passed.

## P11 · OnnxNetwork on the sha-pinned ONNX (tests and tools only)

Files you may create or change: `src/SA.Inbetween.Engine/Network.cs` (only `NetworkTensors`
bodies), new `src/SA.Inbetween.Onnx/**`, new `tests/SA.Inbetween.Onnx.Tests/**`, `SA.Inbetween.sln`.
The package `Microsoft.ML.OnnxRuntime` version `1.30.0` is the only new dependency; if your brief does
not approve it, stop and report.

1. `dotnet test --project tests/SA.Inbetween.Tests --filter-trait "Packet=P11"`; confirm 3 failed, 1 passed.
2. Implement `NetworkTensors.ParseInputs`: check lengths in ONNX order
   (0 global_root 40, 1 has_global 8, 2 local_root 32, 3 has_local_root 8, 4 local_poses 2136,
   5 has_local_poses 8, 6 allowed 11); the first wrong one gives
   `Fault(NetworkShapeMismatch, position, actualLength)`. `ParseOutputs`: motions length 23424 else
   `(NetworkShapeMismatch, 0, length)`; tokens 6..16 else `(NetworkShapeMismatch, 1, tokens)`.
   `FrameCountFor(tokens)` = `4 * tokens - 6`. (FrameCountFor is tagged P10; implementing it here is allowed.)
3. Run the P11 filter: 4 passed.
4. Check the model file (do not copy it into the repo):
   ```bash
   sha256sum ~/GitHub/motionbricks/motionbricks/assets/onnx/soma/mmm_v6_soma_fp32.onnx
   ```
   It must print `856419dc72aeb14e591eebf4dd0ea05734a2506ddb00bcd39dd6dac2ad60f4fb`
   (`ModelPins.SomaFp32OnnxSha256`). Anything else: stop and report.
5. Create the project:
   ```bash
   mkdir -p src/SA.Inbetween.Onnx tests/SA.Inbetween.Onnx.Tests
   ```
   `src/SA.Inbetween.Onnx/SA.Inbetween.Onnx.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFrameworks>net10.0</TargetFrameworks>
     </PropertyGroup>
     <ItemGroup>
       <ProjectReference Include="../SA.Inbetween.Engine/SA.Inbetween.Engine.csproj" />
       <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.30.0" />
     </ItemGroup>
   </Project>
   ```
   Create empty `PublicAPI.Shipped.txt` (first line `#nullable enable`) and `PublicAPI.Unshipped.txt`
   in that folder; new public symbols of this new project go into `PublicAPI.Unshipped.txt`
   (run `build/update-public-api.sh`, then move the new lines of this project only from Shipped
   to Unshipped).
6. Write `OnnxNetwork : INetwork, IDisposable` (`sealed class`) with
   `public static Result<OnnxNetwork, Fault> Load(string modelPath)`: hash the file with SHA256 and
   compare with `ModelPins.SomaFp32OnnxSha256` (mismatch: `Fault(BackendUnavailable, 0, 1)`; missing
   file: `Fault(BackendUnavailable, 0, 0)`), then create one `InferenceSession` with
   `SessionOptions { GraphOptimizationLevel = ORT_ENABLE_ALL, IntraOpNumThreads = 1,
   InterOpNumThreads = 1, ExecutionMode = ORT_SEQUENTIAL }` on the CPU provider. `RunAsync`: first
   `NetworkTensors.ParseInputs`; build `DenseTensor` inputs with batch 1 and names exactly
   `global_root_values, has_global_root_values, local_root_values, has_local_root_values,
   local_poses, has_local_poses, allowed_pred_num_tokens` (shapes `[1,8,5] [1,8] [1,8,4] [1,8]
   [1,8,267] [1,8] [1,11]`, int32 for masks and allowed); read `pred_global_motions` (float
   `[1,64,366]`) and `pred_num_tokens` (int32); return `NetworkTensors.ParseOutputs`. Catch
   `OnnxRuntimeException` inside and return `Fault(BackendUnavailable, 0, 2)`; honour cancellation
   before running with `Fault(Cancelled, 0, 0)`.
7. Tests in `tests/SA.Inbetween.Onnx.Tests` (csproj like `tests/SA.Inbetween.Tests`, reference the Onnx
   project), trait `Packet=P11`:
   - model missing gives `Fault(BackendUnavailable, 0, 0)`;
   - with the real model (skip with `Assert.Skip` if the file at
     `~/GitHub/motionbricks/motionbricks/assets/onnx/soma/mmm_v6_soma_fp32.onnx` is absent; build the
     path with `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ...)`,
     allowed in tests only): the identity input of the ONNX example (all zeros except 6D blocks of
     each joint = first two identity columns, all masks 1, allowed one-hot slot 0) runs and
     returns 23424 finite floats and tokens in 6..16; two runs are bitwise equal;
   - corpus parity: `Corpus.RequireManifest()` then raw outputs within `5e-3` max abs of the corpus
     raw outputs (P03 format; if the format is not documented yet, keep this test skipped with that
     reason and report).
8. Add both projects to the sln (`dotnet sln add ...`). Run build `-warnaserror`, architecture tests
   (Onnx must not be referenced by any core project), `dotnet test`.

Done when: P11 trait 4 passed in `SA.Inbetween.Tests`; Onnx tests pass or skip with a reason; one
generation on CPU recorded in the PR (median ms over 20 runs); corpus parity within 5e-3 once P03 exists.

## P13 · Unity package `com.sa.inbetween`

Files you may create: `unity/com.sa.inbetween/**`, `build/pack-unity.sh`. Unity editor:
`~/Unity/Hub/Editor/7000.0.0a12/Editor/Unity`. Do not open or modify any other Unity project.

1. Create `build/pack-unity.sh` (chmod +x):
   ```bash
   #!/usr/bin/env bash
   set -euo pipefail
   cd "$(dirname "$0")/.."
   out=unity/com.sa.inbetween/Plugins
   mkdir -p "$out"
   dotnet build SA.Inbetween.sln -c Release -warnaserror
   for p in Soma Contract Engine Generators; do
     cp "src/SA.Inbetween.$p/bin/Release/netstandard2.1/SA.Inbetween.$p.dll" "$out/"
   done
   (cd "$out" && sha256sum SA.Inbetween.*.dll > SHA256SUMS)
   cat "$out/SHA256SUMS"
   ```
2. Create `unity/com.sa.inbetween/package.json`:
   ```json
   {
     "name": "com.sa.inbetween",
     "version": "0.1.0",
     "displayName": "SA Inbetween",
     "description": "SOMA in-between motion: prebuilt core DLLs and Unity adapters.",
     "unity": "7000.0"
   }
   ```
3. `unity/com.sa.inbetween/Runtime/SA.Inbetween.Unity.asmdef`:
   ```json
   {
     "name": "SA.Inbetween.Unity",
     "rootNamespace": "SA.Inbetween.Unity",
     "references": [],
     "overrideReferences": true,
     "precompiledReferences": ["SA.Inbetween.Soma.dll", "SA.Inbetween.Contract.dll", "SA.Inbetween.Engine.dll", "SA.Inbetween.Generators.dll"],
     "autoReferenced": true,
     "noEngineReferences": false
   }
   ```
   Add `Runtime/SomaUnity.cs` with one static class `SomaUnity` and one method
   `public static UnityEngine.Vector3 ToUnity(SA.Inbetween.Soma.Vector3<SA.Inbetween.Soma.UnityWorld> v) => new UnityEngine.Vector3(v.X, v.Y, v.Z);`
4. `unity/com.sa.inbetween/Tests/Editor/SA.Inbetween.Unity.Tests.asmdef`:
   ```json
   {
     "name": "SA.Inbetween.Unity.Tests",
     "references": ["SA.Inbetween.Unity", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
     "includePlatforms": ["Editor"],
     "overrideReferences": true,
     "precompiledReferences": ["nunit.framework.dll", "SA.Inbetween.Soma.dll", "SA.Inbetween.Contract.dll"],
     "autoReferenced": false,
     "defineConstraints": ["UNITY_INCLUDE_TESTS"]
   }
   ```
   Add `Tests/Editor/ContractInUnityTests.cs` (NUnit): (a) `Requests.Parse` of a valid request built
   from identity poses is ok; (b) `SomaJoints.Name(SomaJoints.Hips) == "Hips"`;
   (c) `SomaUnity.ToUnity(new Vector3<UnityWorld>(1,2,3))` equals `new Vector3(1,2,3)`;
   (d) `typeof(SA.Inbetween.Soma.Pose).Assembly.ImageRuntimeVersion` is not empty.
5. Run `build/pack-unity.sh`. Then create a scratch project outside the repo:
   `mkdir -p /tmp/sa-inbetween-unity/Packages` and write
   `/tmp/sa-inbetween-unity/Packages/manifest.json`:
   ```json
   { "dependencies": { "com.sa.inbetween": "file:<ABSOLUTE PATH TO>/unity/com.sa.inbetween", "com.unity.test-framework": "1.5.1" }, "testables": ["com.sa.inbetween"] }
   ```
6. Run:
   ```bash
   ~/Unity/Hub/Editor/7000.0.0a12/Editor/Unity -batchmode -nographics -projectPath /tmp/sa-inbetween-unity \
     -runTests -testPlatform EditMode -testResults /tmp/sa-inbetween-unity/results.xml -logFile /tmp/sa-inbetween-unity/editor.log
   ```
   Record: exit code, `grep -c 'result="Passed"' /tmp/sa-inbetween-unity/results.xml`, any `error CS`
   lines in the log, and Unity's C# version: `grep -m1 -o "langversion:[0-9a-z.]*" -r /tmp/sa-inbetween-unity/Library/Bee 2>/dev/null`
   (if empty, report the Roslyn version from `ls ~/Unity/Hub/Editor/7000.0.0a12/Editor/Data/DotNetSdkRoslyn`
   or `.../DotNetSdk`).
7. Commit the package with the generated `.meta` files that Unity wrote inside `unity/com.sa.inbetween`
   and the `Plugins/SHA256SUMS`. Do not commit `Plugins/*.dll` (add `unity/com.sa.inbetween/Plugins/*.dll`
   to `.gitignore`).

Done when: batchmode EditMode tests 4 of 4 pass on 7000.0.0a12 with zero `error CS`; C# version
recorded in the PR; `SHA256SUMS` matches a second `build/pack-unity.sh` run (deterministic build).

## STOP-AND-REPORT

Stop, write what you saw, and report to the lead instead of guessing when:

1. A test seems wrong, contradicts docs/contract.md, or would need editing to pass.
2. You would need a new public member, a changed signature, or an edit to a `PublicAPI.*.txt` file
   of an existing project.
3. A spec value is missing (a byte offset, a fault Index/Value, a tolerance, a tensor layout).
4. You need a new NuGet package or version not named in your packet section.
5. A command in this file fails in a way not covered here (restore, SDK, Unity licence, missing
   model file, sha256 mismatch).
6. You would need `sudo`, a sign-in, a download over 2 GB, the GPU, `uv sync`, or to touch
   `~/GitHub/motionbricks`, any SA repo or any Unity project other than the scratch one.
7. Anything would commit or ship files from `testdata/eval-only/` or NVIDIA sample motions.
8. The architecture tests fail, or a warning appears that you can only silence with a suppression.
9. A property test fails on a seed you cannot explain after one fix attempt.
