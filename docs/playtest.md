# Play-test readiness (designer pilot)

Goal: take sa-inbetween v0.9.2 / com.sa.unity v0.3.6 from "works end to end" to ready for a first designer pilot.
Branch `feat/playtest`. Work was paused by the owner on 2026-11-14 before any code change; this page records where it stands.

## Decisions

Made by the owner, to be carried out:

- **SyntyJumpGates G3 foot penetration** (3.45 cm against 3 cm; the source start frame already sits 2.5 cm under the ground):
  the gate becomes relative to the source clip: limit = source penetration at the anchor frames + 1 cm, never below 3 cm.
  It is not loosened in any other way. A test covers the rule. Then Clips tests run in a copy project for 49/49 (or each skip is documented).
- **SA.Shape SH4 / wardrobe is out of playtest scope.** Any Shape UI is hidden or disabled on the designer path.
- **Windows cannot be run here.** A `scripts/windows-smoke.ps1` for a Windows tester (tools zip: exe present, VC++ runtime,
  SHA256, one generate) plus a one-page tester checklist, statically validated.
- The `~/GitHub/sa` workspace repository is not pushed; submodule pointers are committed locally only.

## Done

- Read: `docs/reports/clean-install-2026-11-14.md`, `docs/designer-guide.md`, `docs/install.md`, `docs/status.md`,
  IAFahim/com.sa.unity#4 (body and latest comment), the SA Unity CLI skill and `eng/unity.sh`.
- Read the whole designer path in `unity/com.sa.inbetween.sa/Editor` (recipe Inspector, `RecipeRunner`, `StudioProcess`,
  discovery and settings, convert, all three exports, capture, importers, variations, undo) and found the G3 gate
  (`Tests/Editor/RealJumpTests.cs`, line 427: `gates.Check("G3 foot penetration", ..., 3.0, "cm", enforced)`; the start-anchor
  sole height is already computed on line 429 for the note).
- Located a reusable clean project with the v0.9.2 packages, release tools, NVIDIA model and Mixamo assets (`~/clean-test-project`).
  Unity 7000.0.0a12 and 7000.0.0a16 are installed; no Editor was started in this session.

## Remaining

Nothing below is implemented yet.

1. **First 10 minutes:** menu `SA > Inbetween > Create Demo Scene` (or a `Samples~` entry) that builds a scene and the
   jump-mid-air recipe from the user's own Humanoid clip if one exists, with one-click Generate / Preview / Save and on-screen messages.
2. **Failure UX:** plain-language message plus fix for: tools folder or model missing, wrong rig, unsupported clip, FBX native
   missing, generation error, stale recipe. A **Copy diagnostics** button (versions, paths, tool SHA-256, last error, log tail)
   and `.github/ISSUE_TEMPLATE/bug-report.md`.
3. **Robustness sweep (Linux, clean project), with fixes and tests.** Risks found while reading the code, not yet reproduced:
   - Domain reload during Generate: `RecipeRunner.Runs` lives in memory only, so a reload drops the run while the child
     process keeps going; the recipe says nothing about it. Record the run in `SessionState` and report "interrupted, press Generate again".
   - The bake step writes `<recipe>.saclip` straight into `Assets`; a cancel or kill during the bake can leave a partial file.
     Bake into `Library/SaInbetween/<guid>` and move the file into place.
   - Cancel kills only the current step's process.
   - Renaming the recipe after a generate leaves the old clip as Output while the next Generate writes a new file name.
   - Still to exercise: Undo/redo, re-generating, deleting the output, moving assets, clips longer than 10 s, 60 fps clips,
     root-motion clips, a character without a Humanoid avatar.
4. **G3 gate change** (decision above) and Clips tests 49/49 in a copy project.
5. **Shape UI out of the designer path** (find the SA.Shape menus in com.sa.unity / com.sa.motion.unity and hide them).
6. **Windows smoke script and tester checklist** (decision above).
7. **Docs:** version/compatibility table (Unity versions, package versions, OS, CPU/GPU generation time) and a known-limitations
   page (Rukhanka twist difference, model licence as stated in `README.md`/`NOTICE`, non-Humanoid clips, Windows untested).
8. **Release v0.9.3** (and com.sa.unity if it changes) after Clips, Timeline and SA.Tests pass from a fresh Editor; confirm
   release and CI green; update com.sa.unity#4; commit workspace pointers locally.
