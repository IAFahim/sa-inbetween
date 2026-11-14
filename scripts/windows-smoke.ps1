<#
.SYNOPSIS
  Smoke test for the SA Inbetween tools archive, for a Windows tester. No Unity needed.

.DESCRIPTION
  Checks, in order, and prints PASS / FAIL / SKIP for each:
    1. the archive's SHA-256 against SHA256SUMS
    2. the archive unpacks to one SaInbetween folder with the generator, the baker and the family file
    3. both programs are 64-bit Windows executables
    4. the Microsoft Visual C++ Redistributable 2015-2022 (x64) is installed
    5. both programs start (exit 2 = they printed their usage)
    6. the NVIDIA model has the expected size and SHA-256
    7. one generate (rest pose to rest pose), one inspect and one bake, with the time it took
  Writes everything to windows-smoke-report.txt next to the archive. Attach that file to your report.
  Nothing is installed and your Unity project is not touched; the archive is unpacked into a temporary folder.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File windows-smoke.ps1 -Archive .\SaInbetween-tools-v0.9.3-win-x64.zip -Model D:\nvidia\mmm_v6_soma_fp32.onnx -RestPose D:\nvidia\soma_rest.pose
#>
[CmdletBinding()]
param(
    [string]$Archive = "",
    [string]$Sums = "",
    [string]$Model = "",
    [string]$RestPose = "",
    [switch]$Keep
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$ModelSha256 = "856419dc72aeb14e591eebf4dd0ea05734a2506ddb00bcd39dd6dac2ad60f4fb"
$ModelBytes = 757864273
$RestBytes = 2156
$VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
$DllNotFound = -1073741515
$OnWindows = ($env:OS -eq "Windows_NT")

$results = New-Object System.Collections.Generic.List[string]
$failures = 0

function Report([string]$status, [string]$check, [string]$detail) {
    $line = "{0,-4}  {1}{2}" -f $status, $check, $(if ($detail) { ": " + $detail } else { "" })
    $script:results.Add($line)
    if ($status -eq "FAIL") { $script:failures++ ; Write-Host $line -ForegroundColor Red }
    elseif ($status -eq "SKIP") { Write-Host $line -ForegroundColor Yellow }
    else { Write-Host $line -ForegroundColor Green }
}

function Sha256([string]$path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
}

function Run([string]$file, [string[]]$arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $file
    $info.Arguments = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join " "
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.CreateNoWindow = $true
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($info)
    $out = $process.StandardOutput.ReadToEndAsync()
    $err = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(600000)) { $process.Kill(); throw "timed out after 10 minutes: $file" }
    $process.WaitForExit()
    $watch.Stop()
    [pscustomobject]@{ Code = $process.ExitCode; Out = $out.Result; Err = $err.Result; Seconds = $watch.Elapsed.TotalSeconds }
}

function ExitText([int]$code) {
    if ($code -eq $DllNotFound) { return "exit $code (a DLL is missing: install the Visual C++ Redistributable from $VcRedistUrl)" }
    return "exit $code"
}

Write-Host "SA Inbetween tools smoke test"
$results.Add("SA Inbetween tools smoke test")
$results.Add("Date (UTC): " + [DateTime]::UtcNow.ToString("yyyy-MM-dd HH:mm"))
$results.Add("OS: " + [System.Environment]::OSVersion.VersionString + ", 64-bit OS: " + [System.Environment]::Is64BitOperatingSystem)
$results.Add("PowerShell: " + $PSVersionTable.PSVersion.ToString())
try { $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name; $results.Add("CPU: " + $cpu) } catch { }

if (-not $Archive) {
    $found = Get-ChildItem -Path . -Filter "SaInbetween-tools-*" | Where-Object { $_.Name -match '\.(zip|tar\.gz)$' } | Sort-Object Name | Select-Object -Last 1
    if ($found) { $Archive = $found.FullName }
}
if (-not $Archive -or -not (Test-Path -LiteralPath $Archive)) {
    Report "FAIL" "archive" "pass -Archive <SaInbetween-tools-...-win-x64.zip>, or run this in the folder you downloaded it to"
    exit 1
}
$Archive = (Resolve-Path -LiteralPath $Archive).Path
$folder = Split-Path -Parent $Archive
$reportPath = Join-Path $folder "windows-smoke-report.txt"
$results.Add("Archive: " + (Split-Path -Leaf $Archive))
$WindowsArchive = $Archive.EndsWith(".zip")
$Exe = if ($WindowsArchive) { ".exe" } else { "" }
$CanRun = ($WindowsArchive -eq $OnWindows)

if (-not $Sums) { $Sums = Join-Path $folder "SHA256SUMS" }
$archiveHash = Sha256 $Archive
if (Test-Path -LiteralPath $Sums) {
    $name = Split-Path -Leaf $Archive
    $line = Get-Content -LiteralPath $Sums | Where-Object { $_ -match ('\s\*?' + [regex]::Escape($name) + '$') } | Select-Object -First 1
    if (-not $line) { Report "FAIL" "1 SHA-256" "$name is not listed in SHA256SUMS (download both from the same release)" }
    elseif ($line.Split(" ")[0].ToLowerInvariant() -eq $archiveHash) { Report "PASS" "1 SHA-256" $archiveHash }
    else { Report "FAIL" "1 SHA-256" "archive $archiveHash, SHA256SUMS $($line.Split(' ')[0]): download it again" }
} else {
    Report "FAIL" "1 SHA-256" "SHA256SUMS not found next to the archive (download it from the same release page); archive hash $archiveHash"
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("sa-inbetween-smoke-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    if ($Archive.EndsWith(".zip")) { Expand-Archive -LiteralPath $Archive -DestinationPath $work }
    else { & tar -xzf $Archive -C $work; if ($LASTEXITCODE -ne 0) { throw "tar failed" } }
    $tops = @(Get-ChildItem -LiteralPath $work)
    $root = Join-Path $work "SaInbetween"
    $cli = Join-Path $root ("cli/sa-inbetween" + $Exe)
    $bake = Join-Path $root ("bake/sa-soma-bake" + $Exe)
    $family = Join-Path $root "humanoid-v1.safamily"
    $missing = @(@($cli, $bake, $family) | Where-Object { -not (Test-Path -LiteralPath $_) })
    if ($tops.Count -ne 1 -or $tops[0].Name -ne "SaInbetween") { Report "FAIL" "2 unpack" ("expected one folder SaInbetween, found: " + (($tops | ForEach-Object { $_.Name }) -join ", ")) }
    elseif ($missing.Count -gt 0) { Report "FAIL" "2 unpack" ("missing " + (($missing | ForEach-Object { $_.Substring($root.Length + 1) }) -join ", ")) }
    else {
        $count = @(Get-ChildItem -LiteralPath $root -Recurse -File).Count
        Report "PASS" "2 unpack" "SaInbetween with cli, bake and humanoid-v1.safamily ($count files)"
    }

    foreach ($exe in @($cli, $bake)) {
        if (-not (Test-Path -LiteralPath $exe)) { continue }
        $bytes = [System.IO.File]::ReadAllBytes($exe)
        $label = "3 " + (Split-Path -Leaf $exe)
        if ($WindowsArchive) {
            $pe = [BitConverter]::ToInt32($bytes, 0x3C)
            $machine = [BitConverter]::ToUInt16($bytes, $pe + 4)
            if ($bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A -and $machine -eq 0x8664) { Report "PASS" $label "PE32+ x64" }
            else { Report "FAIL" $label ("not a 64-bit Windows program (machine 0x{0:X4}): wrong archive?" -f $machine) }
        } elseif ($bytes[0] -eq 0x7F -and $bytes[1] -eq 0x45 -and $bytes[2] -eq 0x4C -and $bytes[3] -eq 0x46 -and $bytes[4] -eq 2) { Report "PASS" $label "ELF 64-bit" }
        else { Report "FAIL" $label "not a 64-bit Linux program: wrong archive?" }
    }

    if ($OnWindows -and $WindowsArchive) {
        $keys = @("HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64", "HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64")
        $runtime = $null
        foreach ($key in $keys) { if (Test-Path $key) { $runtime = Get-ItemProperty $key; break } }
        $system = Join-Path $env:WINDIR "System32"
        $dlls = @(@("vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "msvcp140_1.dll") | Where-Object { -not (Test-Path -LiteralPath (Join-Path $system $_)) })
        if ($runtime -and $runtime.Installed -eq 1 -and $dlls.Count -eq 0) { Report "PASS" "4 Visual C++ runtime" ("version " + $runtime.Version) }
        elseif ($dlls.Count -eq 0) { Report "PASS" "4 Visual C++ runtime" "DLLs present (no registry entry)" }
        else { Report "FAIL" "4 Visual C++ runtime" ("missing " + ($dlls -join ", ") + ": install $VcRedistUrl and run this again") }
    } else { Report "SKIP" "4 Visual C++ runtime" "only checked on Windows with the win-x64 zip" }

    if (-not $CanRun) { Report "SKIP" "5 start" "this archive's programs cannot run on this system" }
    foreach ($exe in @($cli, $bake)) {
        if (-not $CanRun -or -not (Test-Path -LiteralPath $exe)) { continue }
        $run = Run $exe @()
        $label = "5 start " + (Split-Path -Leaf $exe)
        if ($run.Code -eq 2) { Report "PASS" $label "printed its usage (exit 2)" }
        else { Report "FAIL" $label ((ExitText $run.Code) + " " + ($run.Err.Trim() -split "`n" | Select-Object -First 1)) }
    }

    if (-not $Model) {
        foreach ($candidate in @((Join-Path $folder "mmm_v6_soma_fp32.onnx"), (Join-Path $folder "SaInbetween/model/mmm_v6_soma_fp32.onnx"))) { if (Test-Path -LiteralPath $candidate) { $Model = $candidate; break } }
    }
    $modelOk = $false
    if ($Model -and (Test-Path -LiteralPath $Model)) {
        $size = (Get-Item -LiteralPath $Model).Length
        $hash = Sha256 $Model
        if ($size -eq $ModelBytes -and $hash -eq $ModelSha256) { Report "PASS" "6 model" "$size bytes, SHA-256 matches"; $modelOk = $true }
        else { Report "FAIL" "6 model" "$size bytes, SHA-256 $hash (expected $ModelBytes bytes, $ModelSha256): download it again" }
    } else { Report "SKIP" "6 model" "pass -Model <mmm_v6_soma_fp32.onnx> to check it and run step 7" }

    if (-not $RestPose) {
        foreach ($candidate in @((Join-Path $folder "soma_rest.pose"), (Join-Path $folder "SaInbetween/soma_rest.pose"))) { if (Test-Path -LiteralPath $candidate) { $RestPose = $candidate; break } }
    }
    if (-not $CanRun) { Report "SKIP" "7 generate" "this archive's programs cannot run on this system" }
    elseif (-not $modelOk) { Report "SKIP" "7 generate" "needs a valid model (step 6)" }
    elseif (-not $RestPose -or -not (Test-Path -LiteralPath $RestPose) -or (Get-Item -LiteralPath $RestPose).Length -ne $RestBytes) { Report "SKIP" "7 generate" "pass -RestPose <soma_rest.pose> ($RestBytes bytes)" }
    elseif ((Test-Path -LiteralPath $cli) -and (Test-Path -LiteralPath $bake)) {
        $saib = Join-Path $work "smoke.saib"
        $clip = Join-Path $work "smoke.saclip"
        $gen = Run $cli @("generate", "--model", $Model, "--context", $RestPose, "--context", $RestPose, "--context", $RestPose, "--context", $RestPose, "--target", $RestPose, "--out", $saib)
        if ($gen.Code -ne 0 -or -not (Test-Path -LiteralPath $saib)) { Report "FAIL" "7 generate" ((ExitText $gen.Code) + " " + $gen.Err.Trim()) }
        else {
            Report "PASS" "7 generate" ("{0:N1} s: {1}" -f $gen.Seconds, $gen.Out.Trim())
            $inspect = Run $cli @("inspect", $saib)
            $frames = ($inspect.Out -split "`n" | Where-Object { $_ -match '^frames' } | Select-Object -First 1)
            if ($inspect.Code -eq 0 -and $frames) { Report "PASS" "7 inspect" $frames.Trim() } else { Report "FAIL" "7 inspect" ((ExitText $inspect.Code) + " " + $inspect.Err.Trim()) }
            $baked = Run $bake @($family, $saib, $clip)
            if ($baked.Code -eq 0 -and (Test-Path -LiteralPath $clip) -and (Get-Item -LiteralPath $clip).Length -gt 0) { Report "PASS" "7 bake" ("{0:N1} s: {1}" -f $baked.Seconds, $baked.Out.Trim()) }
            else { Report "FAIL" "7 bake" ((ExitText $baked.Code) + " " + $baked.Err.Trim()) }
        }
    }
}
catch {
    Report "FAIL" "script" $_.Exception.Message
}
finally {
    if (-not $Keep) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
    else { $results.Add("Unpacked folder kept: $work") }
}

$summary = if ($failures -eq 0) { "RESULT: PASS" } else { "RESULT: FAIL ($failures)" }
$results.Add($summary)
Set-Content -LiteralPath $reportPath -Value $results -Encoding UTF8
Write-Host $summary
Write-Host "Report: $reportPath"
if ($failures -eq 0) { exit 0 } else { exit 1 }
