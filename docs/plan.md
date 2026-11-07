# MotionBricks Library and Demo: Build Plan

Nov 7, 2026 · @Fahim

## Plan in short

Build the MotionBricks integration as its own repo, sa-inbetween: a pure C# core, one generation contract, and two backends that can swap. Reach the interactive demo first through the Python sidecar, which L1 already measured at 4.7 ms per generation on your 3060. Then remove Python with the C# port.

Why this order: the sidecar runs NVIDIA's own engine, so the demo cannot drift from the reference. The C# port, which L3 estimated at 3 to 5 days including a parity test, runs in parallel and plugs in later as a second backend without touching the demo.

Milestones:

1. **M1 · Headless truth.** One request goes through the sidecar, becomes a motion file, then an SA clip, and reads back within SA's codec limits (0.15°, 2 mm). This replaces spikes S1 and S2 with production code.
2. **M2 · Interactive demo.** Inbetween Studio in the Unity Editor: drag a target, see the new motion on an SA character, bake a clip.
3. **M3 · No Python.** The same Studio runs on the C# port and Unity's Inference Engine, at parity with the sidecar.
4. **M4 · Perfection.** Zero allocations in steady state, a mutation-tested core, a determinism and platform matrix, a license guard, a 500-character crowd scene, and a standalone demo build.

Speed comes from parallel agents. A smart agent writes each contract and its tests first; then several small agents fill in the code at once, each in its own worktree. Perfection here is measurable: every claim in this plan has a test or a number behind it in the quality bar.

## Architecture

One stable contract sits in the middle. The core is pure .NET with no Unity, no Python and no SA. Effects live at the edges, and SA learns SOMA as one more source rig, the way it already handles Synty, UAL and Kyle.

```text
sa-inbetween/
  src/
    SA.Inbetween.Soma/        joints, spaces, units, rotations
    SA.Inbetween.Contract/    requests, results, faults, motion file, recipe, wire codec
    SA.Inbetween.Engine/      the C# port: encode, decode, correct, chain
    SA.Inbetween.Generators/  SidecarGenerator, PortedGenerator, the network port
    SA.Inbetween.Onnx/        OnnxNetwork for tests and tools; never shipped to Unity
  sidecar/                    Python: NVIDIA's engine on TensorRT, corpus tool, own lock file
  unity/com.sa.inbetween/     Inference Engine network, sidecar lifecycle, Studio, demo scenes
  tests/                      unit, property, parity and architecture tests
  bench/                      BenchmarkDotNet suites
  docs/                       contract spec, decision records, status
```

- **Targets.** The core projects build for netstandard2.1, which Unity loads, and net10.0, which runs tests, tools and benchmarks. Unity gets them as prebuilt DLLs with pinned hashes, the same way your SA bridge DLLs work, so Unity-side code stays thin.
- **SA side.** Three pieces land in your SA repos, not here: the SOMA rig template from L4's Route D in sa-motion, authored foot contacts into leg IK, and explicit root travel in the family bake. They branch from your baseline: com.sa.unity packet/RESIDUAL-gates with sa-motion packet/RAGDOLL-pilot.
- **GPU checks.** Cloud CI has no GPU, so TensorRT and Inference Engine checks run on your 3060, as a self-hosted runner or a nightly local job. Everything else, including ONNX parity on the CPU, runs in normal CI.

&#91;embedded content: architecture · what uses what across the new repo, the sidecar and your SA repos\]

The Unity package also uses sa-motion's SOMA rig for the live SA preview. Delete the Unity package or the sidecar, and the core still builds and passes its tests.

## Engineering rules

Your principles, written as rules an agent can check, each backed by an analyzer, a test or a review step. The lead agent puts this block into AGENTS.md at the root of the new repo before any packet starts.

```markdown
# AGENTS.md · sa-inbetween

Domain: turn key poses into SOMA in-between motion, and hand it on as data.

## Laws
1. Data and behavior are separate. Types hold data; static pure functions hold behavior.
2. Immutable by default: readonly record structs and immutable collections. Mutation only
   inside a function, on buffers that function owns.
3. Total functions. Every input maps to a value or a typed fault. No null, and no exception
   crosses a public boundary.
4. Parse, don't validate. Raw input becomes a valid type once, at the edge. Inner code never
   checks again.
5. Deterministic always. Same input, same bytes. Banned: DateTime.Now, unseeded Random,
   Guid.NewGuid, culture-sensitive formatting, unordered parallel reductions, hash-order output.
6. Spaces, units and joint sets are types. SomaModel and UnityWorld, Meters and Centimeters,
   SomaJoint and HumanoidBone never mix. Converting is a named function.
7. No comments. Names carry intent; docs/decisions carries the why.
8. No implicit anything: no implicit conversions, static mutable state, hidden I/O or ambient
   context.
9. Effects at the edges. GPU, sockets, files and Unity live in adapters. The core is pure.
10. Every abstraction earns its name. An interface needs two implementations or an effect
    behind it.
11. Deletion shrinks, never breaks. Dependencies point inward:
    Soma <- Contract <- Engine <- Generators <- edges. Architecture tests enforce it.
12. Zero allocations in steady state for codec, engine and retarget, proven by BenchmarkDotNet.
13. Tests are the spec: golden parity, a property test for every law, a mutation-score gate.
14. Unity limits. The core targets netstandard2.1 and net10.0. Shipped code uses compile-time
    language features only: no InlineArray, static abstract members, generic math or ref
    fields until verified on Unity 7000 in the Editor and in IL2CPP.

## Workflow
- One packet = one branch = one worktree = one pull request.
- Small agents implement against existing tests. They never change public API or existing
  tests, and they stop on ambiguity.
- Smart agents own contracts, the hard math and reviews, and never review their own code.
- main stays green: warnings as errors, format, analyzers, tests, parity, architecture tests.
- One GPU job at a time on the RTX 3060.

## Never
- Ship NVIDIA sample motions, RGBMoCap or SMPL-fitted motion, or anything made from them.
- Bring SA.ReactionControl (GPL) into this repo.
- Run `uv sync` in the motionbricks checkout. The sidecar has its own locked environment.
- Use sudo, or sign in to anything.
```

## C# craft that earns its place

Each technique from your curriculum is in the plan only because it removes a bug class the reports actually found.

| Technique | Bug class it removes | Where |
| --- | --- | --- |
| Phantom-typed spaces: `Vector3<SomaModel>`, `Vector3<UnityWorld>` | The axis, mirror and handedness errors L2 found: SOMA is Y-up and right-handed with its left at +X, so SA needs an X mirror | Soma, SOMA rig |
| Unit types: `Meters`, `Centimeters`, `Degrees`, `Radians` | Sample BVH on disk in centimeters, model space in meters | Soma, file reading |
| Typed joint indices: `SomaJoint`, `HumanoidBone` | Indexing a 56-bone array with a 30-joint id | Soma, SOMA rig |
| `Rotation6D` and `Quaternion` as separate types | Reading a 6D block as a quaternion, or the reverse | Engine |
| A `ValidRequest` made only by parsing, with `Result` and LINQ query syntax | Bad masks, token counts or pose counts reaching the network | Contract |
| Structure-of-arrays buffers and span kernels | Garbage-collector hitches while you drag targets in the Editor | Engine, codec |
| Golden corpus, bitwise for the same backend | The C# port drifting from NVIDIA's engine | Engine, generators |
| Property tests with CsCheck, one per law | Edge cases nobody listed | Everywhere |
| Mutation testing with Stryker.NET | Tests that can never fail | Core |
| Architecture tests | Dependency creep that breaks deletion | Solution |
| A build-time license guard | Evaluation-only assets in a player build | Unity package |

Left out on purpose: source generators, runtime IL emission and native AOT exports. None of them removes a bug here yet, so none earns its name. Runtime-only features such as InlineArray and static abstract members wait until P13 shows Unity 7000 supports them in the Editor and in IL2CPP.

## Agent model

Smart agents decide and verify; small agents build against tests. Because the contract and its tests come first, many small agents can work at once without the design drifting.

| Tier | Model | Owns |
| --- | --- | --- |
| Smart | Opus-class. Fable 5.1 for P02, P08, P09 and P22 if you have it | Contracts and spec tests, the hard math, every review |
| Small | Sonnet-class | Implementing against failing tests, the sidecar, Unity wiring, benchmarks |
| Small, clerical | Haiku-class | Environment notes, notices, docs, test matrices |

The protocol:

1. A smart agent writes a packet's public types and failing tests, gets them reviewed, and merges them. The tests are the spec.
2. Small agents implement in parallel, each in its own git worktree, until the tests pass. They may add tests but never edit public API or existing tests.
3. A different smart agent reviews against AGENTS.md and merges. Nobody reviews their own code.
4. Ambiguity stops work. A small agent that would have to guess reports instead, and the lead updates the packet.
5. The lead session dispatches packets wave by wave and keeps docs/status.md current.

Brief template; the lead fills it from a packet row:

```text
ROLE: {Smart|Small} agent, packet {ID}, repo sa-inbetween.
READ FIRST: AGENTS.md, docs/contract.md, and packet {ID} in docs/plan.md.
WORK IN: your own git worktree, branch packet/{ID}.
SCOPE: you may change only {paths}.
GIVEN: {types, failing tests, corpus files, reports}
DELIVER: {the packet's Delivers cell}
DONE WHEN: {the packet's Done when cell, as commands and thresholds}
RULES: no comments; Small agents never change public API or existing
tests; deterministic code only; no new dependencies without approval;
no sudo, no sign-ins; if anything is ambiguous, stop and report.
REPORT (under 300 words): files changed; evidence (commands and output);
what you could not do; risks.
```

Kickoff: export this doc as Markdown, then paste this into your lead Claude Code session.

```text
ROLE: Lead agent (Smart tier) for the sa-inbetween build. You orchestrate
and review; you do not write production code yourself.
INPUTS: this plan exported as Markdown, and the feasibility reports in
reports/L1.md to reports/L9.md.
DO
1. Create ~/GitHub/sa-inbetween as a new private git repo. Put the plan's
   AGENTS.md block at its root, unchanged, and the plan at docs/plan.md.
2. Run the packets wave by wave. Start every packet whose Needs are met,
   in parallel, each in its own git worktree on branch packet/<ID>.
3. Tiers: Smart packets on an Opus-class agent, Small packets on a
   Sonnet-class agent, clerical packets on a Haiku-class agent. Build
   each brief from the plan's template plus the packet row.
4. Contract first: a packet's types and failing tests merge before any
   Small agent implements it.
5. A different Smart agent reviews every pull request against AGENTS.md
   before merge. main stays green.
6. Keep docs/status.md current (packet, status, evidence link) and send
   me two lines after each packet.
STOP AND ASK ME BEFORE: downloads over 2 GB; anything that needs sudo or
a sign-in; merging into main of sa-motion or com.sa.unity; any decision
the plan still marks Open.
GPU: one GPU job at a time on the RTX 3060. Never run uv sync in
~/GitHub/motionbricks.
```

## Work packets

Twenty-nine packets. Needs lists what must be merged first. Done when is the acceptance test, and a packet counts as done only when its evidence is attached to the pull request. Thresholds come from your reports unless marked proposed.

| ID | Packet | Tier | Needs | Delivers | Done when | Status |
| --- | --- | --- | --- | --- | --- | --- |
| P00 | Housekeeping | Small (Haiku) | none | Pinned motionbricks commit; TensorRT and CUDA versions; sidecar lock file; NVIDIA's ONNX sha256 compared with the community copy; why some sample BVH files show as modified | SA baseline builds and tests green; the BVH changes are explained | Not started |
| P01 | Repo scaffold | Small | none | Solution, shared build props, netstandard2.1 polyfills, CI on Linux and Windows, analyzers, banned-API list, architecture tests, NOTICE | Builds with warnings as errors; CI green; a planted wrong-way reference fails the architecture test | Not started |
| P02 | Contract and spec tests | Smart | P01 | Public types for Soma and Contract; failing tests for every behavior; an API baseline file | Review passes; every public member has a test; each test fails for the right reason | Not started |
| P03 | Golden corpus | Small | P00 | Corpus tool: 200 seeded requests through NVIDIA's FP32 engine, saved with raw tensors, frames and contacts, plus a sha256 manifest | Two runs give the same hashes; covers 1 to 4 targets, token counts 6 to 16, chaining and IK pins; kept evaluation-only and never public | Not started |
| P04 | Wire codec | Small | P02 | Span-based, versioned, length-prefixed binary codec on L5's schema | Round-trip property holds; random bytes give a typed fault, never an exception; 0 B allocated | Not started |
| P05 | Sidecar service | Small | P02, P03 | One engine behind a queue, FP32, TCP, a health and version endpoint, warmup, a parent watchdog | Serves every corpus request; exits within 2 s when its parent dies; p95 under 10 ms on the 3060 (proposed) | Not started |
| P06 | Sidecar client | Small | P04, P05 | SidecarGenerator with timeouts and typed faults | Every corpus case comes back bitwise equal | Not started |
| P07 | SOMA math | Smart, then Small | P02 | 6D and quaternion conversion, heading, space changes, mirror, all phantom-typed | Laws hold under CsCheck; matches corpus math within 1e-6 | Not started |
| P08 | SOMA rig in SA | Smart | P02 | In sa-motion: the SOMA source rig, A-pose calibration, X mirror, bone-axis remap, 30-to-56 map, finger defaults, root travel | L4's test: within 0.15° and 2 mm; sole under 2.5 cm while contact is at least 0.5; contacts survive | Not started |
| P09 | Engine encode | Smart | P03, P07 | Placement removal, heading alignment, 6D inputs, masks, token counts | Matches corpus raw inputs within 1e-5 | Not started |
| P10 | Engine decode | Smart, then Small | P03, P07 | 6D to quaternion, contacts, joint remap, trimming, endpoint correction, chaining, IK pins | From corpus raw outputs, frames within 0.01 mm and 0.01° | Not started |
| P11 | ONNX network | Small | P02 | OnnxNetwork on NVIDIA's sha-pinned ONNX, for tests and tools only | Corpus raw outputs within 5e-3 | Not started |
| P12 | Ported generator | Small | P09, P10, P11 | PortedGenerator: the C# engine plus any network | Within 0.15° and 0.15 mm of the sidecar on the corpus; bitwise repeatable | Not started |
| P13 | Unity package | Small | P01 | com.sa.inbetween with asmdefs, a pack script for sha-pinned DLLs, EditMode tests | Imports clean on 7000.0.0a12; batchmode tests pass; Unity's C# version recorded | Not started |
| P14 | Inference Engine backend | Small, Smart review | P11, P13 | Network on Unity Inference Engine with a Vulkan check, background warmup and async readback | Corpus within 5e-3; no stall over one frame after warmup; under 50 ms per generation; once P17 lands, Studio passes its checks on this backend | Not started |
| P15 | Sidecar lifecycle | Small | P06, P13 | The Editor starts, checks and stops the sidecar across domain reloads | No orphan after a reload, play-mode change or quit; reconnects within 1 s (proposed) | Not started |
| P16 | Contacts and root in SA | Smart | P08 | Bakers pass authored contacts to leg IK; the family bake keeps root travel | Planted feet under 20 mm/s median on generated clips; root path within 2 mm | Not started |
| P17 | Inbetween Studio | Smart, then Small | P08, P13, P15 | The Editor window in the demo spec, with a backend switch | Every demo check passes on the sidecar backend | Not started |
| P18 | Scenarios | Small | P17 | Wall jump, vault, walk-to-mark and turn-and-stop presets, stored as data | Each opens and generates in one click; recipes replay bitwise | Not started |
| P19 | Benchmarks | Small | P04, P08, P10 | BenchmarkDotNet suites for codec, engine and retarget | 0 B allocated in steady state; report committed | Not started |
| P20 | Mutation testing | Small | P12 | Stryker.NET on Soma, Contract and Engine | Score at least 80% (proposed); survivors triaged | Not started |
| P21 | Platform matrix | Small (Haiku) | P13 | Linux and Windows; net10.0 and Unity; invariant-culture float checks | All green; float text round-trips on every runtime | Not started |
| P22 | Adversarial review | Smart, fresh | M2 | A reviewer who wrote none of the code checks the repo against AGENTS.md and this plan | Findings filed; no blocker left open | Not started |
| P23 | Crowd demo | Small | P16, P18 | Baked clips on 50 and 500 characters through SA's GPU path | Frame time recorded at both sizes | Not started |
| P24 | License guard | Small (Haiku) | P13 | NOTICE, a copy of NVIDIA's agreement, attributions, and a build check for evaluation-only or non-commercial assets | A planted sample-motion reference fails the build | Not started |
| P25 | Docs and decisions | Small (Haiku) | M2 | README, contract spec, one decision record per choice | A new agent runs the demo from the README alone | Not started |
| P26 | Standalone demo | Small, Smart review | P14, P24 | A player build of Studio on the Inference Engine backend, with your own poses only | Runs on a clean machine; NOTICE inside; no evaluation-only data | Not started |
| P27 | Video pose import | Small | L6, G5 | GEM-X 77-joint output turned into SOMA model poses | Waits for L6 and G5 | Not started |
| P28 | Move tuning | Small, with you | L8 | Scenario tuning from your judgment of the captures | Waits for L8 | Not started |

## Build order

Start Wave 0 today. The packets the demo needs finish first, the C# port runs beside them, and the perfection work lands last.

&#91;embedded content: build order · 4 waves, coloured by milestone\]

Inside a wave, start every packet whose needs are met; a few wait on a neighbour in the same wave, such as P12 on P09 and P10. GPU work (P03, P05, P14 and the latency runs) goes one job at a time on the 3060. P27 and P28 start when L6 and L8 report.

## Interactive demo

One tool and two scenes. Inbetween Studio is where you author motion; the crowd scene shows the baked result running like a game.

**Inbetween Studio (Editor window)**

- **Scene View.** An SA character (the UAL mannequin or a Synty character), the start pose, and up to four target ghosts with move and rotate handles. A handle moves only that target's root, the way NVIDIA's Inbetweener places poses.
- **Pose library.** Pick a body pose for each target from clips, or snapshot the current frame as a new target.
- **Per target.** Poses to use (1 to 4), transition early by, token count, and IK pins for hands and feet once P10 lands.
- **Live.** Every change regenerates on the next Editor frame. A small display shows the backend, the precision, and milliseconds per generation and end to end.
- **Timeline.** Scrub, play and loop, with a lane for the four foot contacts and a marker where each target is reached.
- **Views.** The SOMA skeleton, the SA character, or both overlaid.
- **Bake.** Writes an SA clip plus its recipe. Replay re-runs the recipe and shows a badge when the frames come back bitwise identical.
- **Backend switch.** Sidecar (TensorRT) or Inference Engine, so you can compare them side by side.

**Scenario presets (P18):** a wall jump with a ledge-height slider, a vault over a box, a walk to a mark, and a turn and stop.

**Crowd scene (P23):** baked clips on 50 and 500 characters through SA's GPU path, with frame time on screen.

The demo passes when:

| Check | Pass |
| --- | --- |
| Drag a target and see the new motion, sidecar backend | Under 100 ms (proposed) |
| The same on Inference Engine, after warmup | Under 250 ms; warmup runs in the background with progress shown |
| Editor hitches while dragging | None longer than one frame after warmup |
| Baked clip in play mode against the preview | Within SA's codec limits: 0.15° and 2 mm |
| Recipe replay | Bitwise identical on the same backend |
| Characters | Works on the UAL mannequin and one Synty character |
| Feet | Planted feet under 20 mm/s median |

The pose library may use NVIDIA's sample motions inside your Editor, since their license allows evaluation and testing. Any build you share uses your own poses only (P26).

## Quality bar

Every pull request passes the fast checks before merge; nightly and weekly runs add the slow ones on your 3060. Thresholds marked proposed are yours to change.

| Check | Pass | When |
| --- | --- | --- |
| Build | Zero warnings with warnings as errors; format and analyzers clean | Every pull request |
| Architecture | Dependencies point inward only | Every pull request |
| Unit and property tests | All green; fixed seeds in CI, fresh seeds nightly | Every pull request |
| Same-backend parity | Sidecar results bitwise equal to the golden corpus | Every pull request touching generation |
| Port parity | Within 0.15° and 0.15 mm of the sidecar; L3 measured 0.10° and 0.09 mm | Every pull request touching the engine |
| Retarget | L4's limits: 0.15°, 2 mm, sole under 2.5 cm while in contact | Every pull request touching the SOMA rig |
| Allocation | 0 B in steady state for codec, engine and retarget | Nightly |
| Latency on the 3060 | Sidecar p95 under 10 ms; Inference Engine p95 under 50 ms (proposed) | Nightly |
| Determinism | Every recipe replays bitwise on its backend | Nightly |
| Mutation score | At least 80% on the core (proposed) | Weekly |
| License guard | No evaluation-only or non-commercial asset in a player build | Every player build |

## Carried risks

Nothing in the scan blocks the build. These open items are each owned by a packet or a decision.

| Open item | What it affects | How the plan handles it |
| --- | --- | --- |
| L6 (GEM-X) still running | Video key poses, gate G5 | P27 waits; Studio uses the pose library and snapshots meanwhile |
| L8 needs your judgment | Move coverage, gate G6 | P28 waits; Studio becomes the tool you judge with |
| Unity 7000 is an alpha | Inference Engine 2.6.1 officially needs Unity 6000.0 and ran only in batchmode; IL2CPP builds fail in batch mode | P13 and P14 test in the interactive Editor; P21 tracks IL2CPP |
| Unity's C# version is unknown | Unity-side code may reject newer C# | Logic stays in prebuilt DLLs; P13 records the version |
| SA baseline is not on main | RESIDUAL-gates and RAGDOLL-pilot hold the code P08 and P16 build on | Branch from them; merge them before M4 |
| Community Unity package is not public | No source, version or license | Not used; NVIDIA's own ONNX, sha-pinned (B6) |
| Orphaned sidecar | Holds GPU memory after a crash | Parent watchdog (P05) and lifecycle tests (P15) |
| FP16 and FP32 differ | Up to 2.7 mm at the root, growing when chained (L1) | FP32 everywhere (B4) |
| Modified BVH files in the motionbricks checkout | Unknown edits under the reference | P00 explains them before anything is pinned |
| 20 s first-call warmup in Unity (L3) | A frozen Editor on first use | Background warmup with progress (P14) |
| Licenses | Evaluation-only and non-commercial items | Never-ship list in AGENTS.md; build guard (P24) |
| Contact layouts differ | SA stores left foot, right foot, left hand, right hand; MotionBricks gives heel and toe per foot | Decision B7 |
| 21° foot pitch at rest (L2) | Where leg IK finds the sole | Calibrated in P08, measured in P16 |

## Decisions for Fahim

Agents find facts; these choices are yours. Set a dropdown to Accepted, or edit the row and mark it Changed. The lead agent stops at any row still marked Open.

| # | Decision | Recommended | Your call |
| --- | --- | --- | --- |
| B1 | Library name | sa-inbetween, namespace SA.Inbetween. It names the domain, not the vendor, so swapping the model later renames nothing, and it sits beside SA.Animation and SA.Motion. | Open |
| B2 | Where SOMA-to-SA retarget lives | In sa-motion as a SOMA rig template (L4's Route D), reusing SA's canonical tools. The new repo stays free of SA except for the demo. | Open |
| B3 | Build now, or finish the spikes first | Build now. M1 does spikes S1 and S2 as production code, and the open gates (G5 video, G6 coverage) don't block the library. | Open |
| B4 | Precision | FP32 everywhere, preview and bake. 4.7 ms on the 3060 is fast enough, and the preview then always matches the bake. | Open |
| B5 | First backend for the demo | The Python sidecar for M2. The C# port on Inference Engine follows at M3 as a second backend. | Open |
| B6 | Model files | NVIDIA's own ONNX from the motionbricks repo, sha-pinned. Not the community copy, and not the unpublished package. | Open |
| B7 | Foot contacts into SA | Keep all four heel and toe values in the motion file. Into SA's foot slots, write the larger of heel and toe. Revisit if leg IK wants toe pivots. | Open |
| B8 | Agent tiers | Smart on Opus-class, with Fable 5.1 for P02, P08, P09 and P22 if you have it; Small on Sonnet-class; Haiku only for clerical packets. | Open |
| B9 | Sharing the demo | The internal Editor demo first. A standalone build only after M3 and the license guard, with your own poses. | Open |

## Sources

- The feasibility plan this builds on: MotionBricks to Unity: Lookup and Feasibility Plan
- Your feasibility reports L1 to L9 from the Claude Code session (reports/L1.md to reports/L9.md), with L3.md shared in this chat
- Your engineering principles and C# curriculum, shared in this chat
- [MotionBricks README](https://github.com/NVlabs/motionbricks)
- [MotionBricks Runtime API](https://github.com/NVlabs/motionbricks/blob/main/docs/runtime_api.md)
- [MotionBricks SOMA model card](https://github.com/NVlabs/motionbricks/blob/main/docs/soma_model.md)
- [SOMA-X model card](https://nvlabs.github.io/SOMA-X/stable/model_card.html)
