# Unity Editor tests on Windows, 2026-11-16

First run of the SA Inbetween Editor test assemblies on Windows. Run by an agent in batch mode, not by a person, and with fake tools: no real Generate and no NVIDIA model.

## Setup

| | |
|---|---|
| Machine | Windows 11 Pro 22631, AMD Ryzen 5 8500G, NVIDIA GeForce RTX 3060, Direct3D 12 |
| Unity | 7000.0.0a16, URP blank template, batch mode |
| `com.sa.inbetween.sa`, `com.sa.inbetween.sa.timeline` | this repository at `packet/windows-fake-tools`, referenced with `file:` and listed under `testables` |
| `com.sa.unity` | `packet/C19-shape-jobs` (0.3.7 plus the Burst job work), not the released 0.3.7 |
| `com.sa.motion.unity` | sa-motion `packet/C17-segment-sampling`, not the pinned `e56b12f` |

So this run also shows that SA Inbetween works against the next com.sa.unity and sa-motion.

## Result

| Assembly | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| `SA.Inbetween.Clips.Tests` | 86 | 71 | 0 | 15 |
| `SA.Inbetween.Timeline.Tests` | 81 | 78 | 0 | 3 |

Every skip is a test that needs real assets this project does not have (the Synty character, a Mixamo Neophyte copy, a Humanoid jump clip, a `.saclip` in the project). None is skipped for being on Windows.

## What changed to get there

Five tests were skipped on Windows because their fake generator and baker are shell scripts:

- `PlaytestTests.Generate_bakes_outside_Assets_and_one_undo_takes_it_back`
- `PlaytestTests.Cancel_stops_the_tool_and_leaves_nothing_in_Assets`
- `PlaytestTests.A_script_reload_while_generating_stops_the_tool_and_says_so_afterwards`
- `DemoReadinessTests.A_hung_step_is_stopped_at_the_step_limit_and_the_First_Bridge_window_says_so`
- `DemoReadinessTests.The_demo_backup_puts_the_finished_result_back_in_one_click`

On Windows the two fake tools are now small C# programs that do what the scripts do. The test helper builds them once per Editor session with `csc.exe` from `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`, which Windows 10 and 11 ship. If that compiler is missing the tests skip and say so. All five pass.

Two Windows differences showed up in the test helper, not in the product:

- Reading a tool's pid file while the tool is still writing it fails with a sharing violation. The helper now reads again on the next poll.
- A tools folder cannot be deleted while a tool in it still runs. The helper stops its own leftover fake tool before it removes the folder, so one failed test no longer fails the next ones.

No product code changed. Cancel, the reload handling, the step limit and the bake outside Assets already behaved correctly on Windows.

## Not covered

- A real Generate on Windows, the installer, the tools archive and the model. Those need a person to accept NVIDIA's terms; `docs/windows-tester-checklist.md` is still the way.
- `com.sa.inbetween.install` tests were not in this project.
- The .NET suites are covered separately by CI on GitHub's Windows runner.
