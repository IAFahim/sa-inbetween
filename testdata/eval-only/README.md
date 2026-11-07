# testdata/eval-only

Evaluation-only data derived from NVIDIA MotionBricks (golden corpus from packet P03: requests,
raw network tensors, frames, contacts, and `manifest.sha256`).

- Licence: NVIDIA Open Model Agreement and the MotionBricks sample-data terms. Evaluation and testing only.
- Never commit it. Everything here except this README is gitignored.
- Never ship it, and never ship anything made from it (AGENTS.md, Never).
- Tests that need it call `Corpus.RequireManifest()` and are skipped with an explicit reason when
  `testdata/eval-only/manifest.sha256` is absent.
