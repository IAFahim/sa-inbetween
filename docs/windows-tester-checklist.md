# Windows tester checklist (one page)

About 30 minutes. You need Windows 10 or 11 (64-bit), Unity 7000.0.0a16 from Unity Hub and Git for Windows. For part A,
the two NVIDIA files from your technical artist: `mmm_v6_soma_fp32.onnx` (757,864,273 bytes) and `soma_rest.pose` (2,156 bytes);
part B downloads them itself after asking you.
Write down anything that is unclear, not only what fails.

## A. The tools, without Unity (5 minutes)

1. From the [v0.10.0 release](https://github.com/IAFahim/sa-inbetween/releases/tag/v0.10.0) download
   `SaInbetween-tools-v0.10.0-win-x64.zip`, `SHA256SUMS` and `windows-smoke.ps1` into one folder. Put the two NVIDIA
   files in the same folder.
2. In that folder, Shift + right-click > **Open PowerShell window here**, and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\windows-smoke.ps1 -Model .\mmm_v6_soma_fp32.onnx -RestPose .\soma_rest.pose
   ```

3. Every line should say **PASS**, ending with `RESULT: PASS`. Keep `windows-smoke-report.txt` from that folder.
   - `4 Visual C++ runtime: FAIL`, or exit `-1073741515`: install https://aka.ms/vs/17/release/vc_redist.x64.exe and run step 2 again.
   - `1 SHA-256: FAIL`: download the zip again.

## B. In Unity (20 minutes)

1. Unity Hub > New project > **Universal 3D**, Unity 7000.0.0a16.
2. Follow `docs/install.md` step 1: Package Manager > **+** > **Install package from git URL**, paste the installer URL. Accept
   the NVIDIA question. **SA > Inbetween > Setup** says *ready* on every line (the tools are the `win-x64.zip`).
3. `Edit > Project Settings > SA Inbetween`: five lines say **found**, Generator is `sa-inbetween.exe`.
4. Download a character and a jump from Mixamo (FBX for Unity, the character with skin, the jump without skin, or
   both with skin) and drag both into `Assets`.
5. Choose **SA > Inbetween > Create Demo Scene**. In the **First Bridge** window press **Set to Humanoid** if it
   asks, then **Make demo scene**. Note what it says it picked.
6. Press **1 Generate**. Note the time from pressing to *Done*.
7. Press **2 Preview**: the character moves from mid-air to standing. Then **3 Save**, then **Play** at the top of
   Unity: the character plays the bridge in a loop.
8. Select the recipe (`Assets/SA Inbetween Demo/Jump_MidAir_to_Landing`) and press **Export > Humanoid clip** and
   **Export > FBX** in the Inspector.

## C. Report

Open a [bug report](https://github.com/IAFahim/sa-inbetween/issues/new?template=bug-report.md) (even when all
passed; title it `[playtest] Windows result`) with:

- `windows-smoke-report.txt` from part A,
- **SA > Inbetween > Copy Diagnostics** pasted into the Diagnostics section,
- the Generate time from B6, and a sentence on B7 (did it look like a landing?),
- any message you saw, word for word, and any step where you had to guess.
