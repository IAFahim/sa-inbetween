# P00 Housekeeping — pinned reference facts

Worktree: `/home/os/GitHub/sa-inbetween-wt/p00`, detached at foundation commit
`e6cb9b4` (= `packet/foundation`). Reference checkout `~/GitHub/motionbricks`
was read only: no `uv sync`, no edits (one experiment used a `/tmp` copy of its
index; see §5).

## 1. MotionBricks pin

- Commit: `54fc563aeaaf819a753d72cc9fdc6713989cdd43`
  (`git -C ~/GitHub/motionbricks rev-parse HEAD`).
- Branch `main`, up to date with `origin/main`. `ModelPins.MotionBricksCommit`
  (`src/SA.Inbetween.Contract/Recipes.cs`) is the same 40-char string. Match.
- `git status --short`: **113 modified + 1 untracked**, all under LFS-managed
  paths. Census of the 113 (`status | grep '^ M' | sed 's/.*\.//'`):

| ext | count | area |
| --- | --- | --- |
| bvh | 38 | `assets/motions/soma/**` |
| csv | 37 | `assets/motions/unitree_g1/**` |
| gif | 15 | `docs/media/` |
| png | 12 | `docs/media/` |
| STL | 9 | `motionbricks/assets/robots/unitree_g1/meshes` |
| jpg | 1 | `docs/media/teaser.jpg` |
| usd | 1 | `motionbricks/assets/human` |

- Untracked: `run.sh` (a launcher helper: `./run.sh [inbetweener|g1]`).
  The `.trt-cache/*.plan` + `*.json` engine artifacts are gitignored
  (`.gitignore:19:.trt-cache/`), not modifications.

## 2. TensorRT + CUDA versions

| Value | Source |
| --- | --- |
| TensorRT **10.8.0.43** (`tensorrt-cu12`, `-bindings`, `-libs` all `==10.8.0.43`) | `pyproject.toml`; same pins in `uv.lock` |
| CUDA **12.x** family (`nvidia-cuda-runtime-cu12` **12.9.79** in `uv.lock`) | `uv.lock`; no finer toolkit pin in `pyproject.toml`/docs |
| Timed with `trtexec` from TensorRT 10.8.0.43 on RTX 4090 | `docs/onnx_inference.md` |
| "Tested inference runtime NVIDIA TensorRT 10.8" | `docs/soma_model.md`, `docs/unitree_g1_model.md` |

## 3. Sidecar / lock-file state

- MotionBricks has **no `sidecar/` directory** (`find -maxdepth 2 -iname
  "*sidecar*"` empty) and **no lock file other than `uv.lock`**
  (beside `pyproject.toml`). The future sidecar (P05) has nothing to pin against
  yet; the TRT/CUDA pins above (via `uv.lock`) are what it will build on.
- SA repo (this worktree): `src/` = Contract, Engine, Generators, Soma —
  no sidecar project yet, as expected pre-P05.

## 4. NVIDIA ONNX sha256 comparison

| File | sha256 | Expected (`ModelPins`) | Bytes |
| --- | --- | --- | --- |
| `motionbricks/assets/onnx/soma/mmm_v6_soma_fp32.onnx` | `856419dc72aeb14e591eebf4dd0ea05734a2506ddb00bcd39dd6dac2ad60f4fb` | `SomaFp32OnnxSha256` — **exact match** | 757864273 = `SomaFp32OnnxBytes` ✓ (`stat`) |
| `motionbricks/assets/onnx/soma/soma_n1.json` | `ed41ab5bf481383fa46bdf0322b0c1b96426973e5038b731bc31276121051aaa` | `SomaN1JsonSha256` — **exact match** | — |

(`sha256sum` on both files.) `soma_n1.json` content sanity: `schema_version`
1, `onnx_filename` mmm_v6_soma_fp32.onnx, opset 17, 30 Hz, 4-frame tokens.
Also present but unpinned: `mmm_v6_soma_fp16.onnx` (380327058 B) and the
`unitree_g1` pair. No second copy of the fp32 ONNX exists in the checkout
(`find -name "*soma*fp32*"` finds only the model + `.trt-cache` plan/json
build artifacts), and the community Unity package is not public (plan risk
table), so there is **no local "community copy" to diff** — the ModelPins
comparison above is the authoritative check.

## 5. Why sample BVH files show as modified

Short version: **no content was edited; every flagged file is Git-LFS-managed
and `git status` verdicts on stale index stat without running the LFS clean
filter.**

Evidence (all read-only against the reference checkout):

- `git diff`, `git diff --cached`, `git diff --stat HEAD` are all **empty** —
  through the LFS clean filter, worktree content is identical to the index.
- `git hash-object` (applies LFS clean) of a sample BVH is stable across runs
  and equals the index blob (`0a91751f…` for `idle_loop_001__A268.bvh`).
- `git lfs status` lists every flagged file as `(LFS: <oid> -> File: <oid>)`
  with **identical OIDs** (e.g. `4560ddf -> 4560ddf`).
- `.gitattributes` pins all affected types to LFS with `-text`
  (`*.[bB][vV][hH] filter=lfs diff=lfs merge=lfs -text`, same for csv/png/gif/
  jpg/stl/usd/onnx/…): **no line-ending conversion is involved**; smudge/clean
  are LFS only.
- Index stat is pointer-era: `ls-files --debug` shows size **132**,
  mtime **2026-10-28 08:46:08 UTC** (clone moment). The worktree holds smudged
  full content (e.g. **2710513 B** = the pointer's own `size` field, mtime
  2026-10-28 08:47:59 UTC) — i.e. clone wrote pointer stubs + index stat, and
  LFS materialization ~2 min later rewrote bytes without touching index stat.
- `GIT_TRACE=1` is decisive: `git status` spawns **no** filter process (verdict
  from `stat()` size mismatch alone), while `git diff` spawns
  `git-lfs filter-process` and reports empty.
- `git update-index --refresh` (run against a **/tmp copy** of the index via
  `GIT_INDEX_FILE`; repo untouched) prints `needs update` and does **not**
  clear the flags — the mismatch re-flags on stat alone.
- All 113 modified files are LFS types (table in §1); zero non-LFS files
  differ.

Remediation (NOT executed — reference checkout must stay untouched): `git add`
on those paths (clean reproduces identical pointers, so only stat heals) or a
fresh clone; until then the flags are safe to ignore. Nothing needs pinning
around them.

## 6. SA baseline build/test (this worktree, `SA.Inbetween.sln`)

- `dotnet build SA.Inbetween.sln`: **Build succeeded. 0 Warning(s) 0 Error(s)**
  (SDK 10.0.403, `global.json` pins 10.0.403 + testing-platform runner).
- `dotnet test SA.Inbetween.sln --nologo` (3 runs, stable): **total 169 —
  112 succeeded, 38 failed, 19 skipped**; Architecture suite passes
  (`SA.Inbetween.Architecture.Tests.dll passed`).
- The 19 skips are all golden-corpus/eval-only tests (`testdata/eval-only/`
  absent by design: DecodeSpec/EncodeSpec/Engine*GoldenSpec/IkGoldenSpec +
  `Golden_corpus_bitwise_through_sidecar`).
- All 38 failures are stub defaults from unimplemented downstream areas, never
  wrong values or exceptions: 30 return `Fault NotImplemented`
  (all 19 WireSpec → P04; MotionFile/Recipe/PortedGenerator/SidecarGenerator
  specs), plus 8 zero-defaults — `Expected: N / Actual: 0` ×5 (sizes/counts),
  sha256 known-vector `Actual: ""`, `Model_pins_are_well_formed`
  `Expected True / Actual False` (ParseHex stub), one default `Sha256` struct.

## 7. Open items for Main

- P00 Done-when says "SA baseline builds and tests green": the build and
  Architecture tests are green; the 38 unit failures are red TDD tests owned by
  downstream packets (P03/P04/P05/P06/P12…), failing "for the right reason".
  Confirm this reading is the intended baseline, or re-target those tests.
- "Community copy" of the ONNX: none exists locally and the package is not
  public — ModelPins-vs-file (exact match) is the only available comparison.
