# AGENTS.md · sa-inbetween

Domain: turn key poses into SOMA in-between motion, and hand it on as data.

## Start here (Unity work)
Pull and read `IAFahim/unity-cli-skill` `skills/unity-cli-field-notes/SKILL.md` (owner rules 1-20), then the SA companion
`sa-motion/.claude/skills/sa-unity-cli/SKILL.md` (Save-dialog rules, `eng/unity.sh` lock). Hard rules: newest installed
Unity 7 editor, one Unity process at a time under the lock, batchmode only, never hand-edit Unity-serialized files or
`.meta`, every new file in `unity/` ships its Unity-generated `.meta`, test the packages the way a user installs them
(`docs/install.md` into a new project), and judge runs by the NUnit counts, never the exit code alone.

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
