# Status

What is built, what is verified, and what is not. Updated 2026-11-15 (0.10.0).

## Production-ready
- Contract, wire codec, motion file (`.saib`), recipe hashing: tested, property-tested, zero-allocation kernels.
- Engine (encode, decode, endpoint correction, IK pin): matches NVIDIA's TensorRT goldens to 0.06 mm and 0.002 rotation components (see `docs/engine/status.md`). Parity tests need the NVIDIA evaluation data and are skipped in CI.
- ONNX CPU backend (`PortedGenerator`): bit-deterministic, about 4.6 s cold for 50 frames.
- CLI `sa-inbetween` and bake tool `sa-soma-bake` (sa-motion repo): error handling, exit codes, tests.
- Unity packages `com.sa.inbetween` (core) and `com.sa.inbetween.sa` (importer, `SaClipPlayer`, Inbetween Bridge recipes).
- CI on Linux and Windows. Tagging `vX.Y.Z` builds a release: CLI for linux-x64 and win-x64, both Unity packages, SHA256SUMS (`build/package-release.sh`).

## Not built
- GPU/TensorRT sidecar (P05, P06, P14, P15). The CPU path is the supported one; the sidecar is a speed optimisation only.
- BenchmarkDotNet suite (P19) and mutation testing (P20). Allocation is covered by `EngineAllocationSpec`.
- Root travel in the family bake (P16). Clips are attached at runtime, not through a subscene baker. (Contacts now drive the foot lock
  on generated clips at bake time, 0.10.0.)
- Video poses (P27) and move tuning (P28).

## 0.10.0: chain on one row, seams, Git-URL install (IAFahim/sa-inbetween#4)
- **Chain Track** (`com.sa.inbetween.sa.timeline` 0.3.0): one Timeline row of blocks; every gap is an in-between that bakes on
  its own (digest per gap, progress with Cancel, right-click Bake all / Bake this gap / Regenerate / Reveal outputs), plays on the
  Generated row, and is written per gap and as one whole-chain `.saclip` + Humanoid `.anim` + chain asset (`SaChainPlayer`,
  Whole or Piece By Piece with Next). `SA > Inbetween > Create Chain Demo`. Verified on Synty run, jump, land, crouch (no roll clip
  in the pack) and walk: 4 in-betweens, 8 seams under 0.1 cm and 0.2 degrees.
- **Seams fixed**: the 3.6 / 6.1 cm steps came from playing the raw source takes (their own root-motion import settings) next to
  the exported in-between and from a one-frame lag after token rounding; the Generated row now plays SA-converted pieces and fitted
  in-betweens (0.04-0.05 cm). Every generated clip is inertialized onto both neighbours (critically damped or quintic) and its
  planted feet are locked with leg IK and kept above the ground: hand pop at the joins 22.9 / 30.3 deg -> 0.00, loose landing
  1.09 cm -> 0.00, planted-foot slide 8.9 cm -> 0.0.
- **SA runtime from frame 0**: `SaClipPlayer`/`SaChainPlayer` start a clip with a seek, so head, hands, feet and fingers are where
  the clip puts them on the first frame (they lagged up to 41 cm for 0.2 s after a load hitch). `Assets > SA > Add SA Character That
  Plays This Clip` sets up the character in one step; a Linux CoreCLR player build plays a baked clip with no NVIDIA file.
- **Git-URL install** (`com.sa.inbetween.install`): adds the other packages to the manifest, downloads the tools with SHA-256,
  asks once before NVIDIA's model and rest-pose sample, makes `soma_rest.pose` locally, fixes the render pipeline, Setup window
  with Fix all.

## Investor demo (v0.9.4)
- A 10-minute script with exact clicks, timings, known limits and a fallback for every step: `docs/investor-demo.md`.
- Dry run from a clean Unity 7000.0.0a16 project (First Bridge window and Generation Track) found and fixed: a floating
  Humanoid landing, an emptied recipe name after Make demo scene again, the manual SA-runtime setup (now one button),
  frozen poses around the generated clip on the Timeline (sources and hips matching on the Generated track), FBX
  placement order, the bake menus' director lookup; added a step limit and a one-click demo backup. Receipts:
  IAFahim/sa-inbetween#3.
- Generation timelines (`com.sa.inbetween.sa.timeline` 0.2.0) are released with it: `docs/designer-guide.md`
  ("Author a generation on a Timeline"), `docs/timeline-generation.md`.

## Designer pilot (v0.9.3)
- Play-test ready on Linux: first-10-minutes demo (`SA > Inbetween > Create Demo Scene`), plain failure messages with
  Copy diagnostics and a bug-report template, robustness sweep with fixes and tests: `docs/playtest.md`.
- Versions and timings: `docs/compatibility.md`. Honest limits: `docs/known-limitations.md`.
- Clean install on Linux through the whole designer guide (0.9.2): `docs/reports/clean-install-2026-11-14.md`.
- Windows: tools checked statically and started by the release workflow on GitHub's Windows runner; no Generate on
  Windows yet. A tester follows `docs/windows-tester-checklist.md` with `scripts/windows-smoke.ps1`.
- SA.Shape (SH4, wardrobe) is out of the playtest scope.

## Needs a person
- A Windows tester run (`docs/windows-tester-checklist.md`).
- Foot gate in the SOMA retarget: sole is the lower of mid-foot and toe-ball, hip reference lowered to 0.9864 m.
- A rest pose with redistribution rights: `soma_rest.pose` is frame 0 of an NVIDIA evaluation sample, made on the user's computer
  by the installer after they accept NVIDIA's terms. Whether that is fine for production use is the owner's call.
- The Git-URL installer and a chain bake on Windows.
- Arm, forearm and hand twist on a skinned character.
- 77-entry skeleton parent table: confirm the NVIDIA licence allows it in source.
- Finger and hand joints are filled from the last context pose; up to 0.36 degrees from the official engine mid-clip.

## Licences
Code is Apache-2.0. The model weights are under the NVIDIA Open Model Agreement and are not shipped; users download them. Sample motions and goldens are evaluation-only and never committed.
