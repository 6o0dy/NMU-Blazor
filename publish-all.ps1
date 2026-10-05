#Requires -Version 5.1
<#
.SYNOPSIS
  One-click FINAL builds for the NMU student app — no Visual Studio clicking needed.
.DESCRIPTION
  Builds (into ./release, version-stamped from the csproj):
    - Android arm64 APK, signed with YOUR keystore    -> NMU-CE-AIE-Android-vX-arm64.apk
    - Android universal APK, signed with YOUR keystore -> NMU-CE-AIE-Android-vX-universal.apk
      (emulators + old 32-bit + new devices)
    - Windows x64 as ONE setup installer               -> NMU-CE-AIE-Setup-vX-x64.exe
      (double-click install: Start Menu entry + uninstaller, no admin needed;
       requires Inno Setup 6 once on THIS machine; setup warns if the student
       PC lacks the .NET 10 Desktop Runtime)
  FULLY non-interactive: signing credentials are embedded below (env vars
  NMU_KS_PASS / NMU_KEY_ALIAS / NMU_KEY_PASS override them when set).
  Apple targets (iOS/Mac) can ONLY be published from a Mac (Apple restriction);
  on other systems they are skipped automatically with a note.
.USAGE
  powershell -ExecutionPolicy Bypass -File publish-all.ps1 [-SkipAndroid] [-SkipWindows]
  First run takes several minutes (especially Android). Just wait for the summary.
#>
[CmdletBinding()]
param(
  [switch]$SkipAndroid,   # skip Android builds
  [switch]$SkipWindows    # skip Windows build
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $root
$started = Get-Date

# Any failure lands here with a READABLE message (window never just vanishes).
trap {
  Write-Host ""
  Write-Host "[FAILED] $($_.Exception.Message)" -ForegroundColor Red
  if ([Environment]::UserInteractive) { Read-Host "Press Enter to close the window" | Out-Null }
  exit 1
}

function Info($m) { Write-Host "[..] $m" -ForegroundColor Cyan }
function Ok($m) { Write-Host "[OK] $m" -ForegroundColor Green }
function Warn($m) { Write-Host "[!!] $m" -ForegroundColor Yellow }

function Invoke-DotnetPublish($fw, $profile, $label) {
  Info "$label ... (takes minutes, do NOT close this window)"
  $logName = ($profile -replace '[^A-Za-z0-9]+', '-')
  $logPath = Join-Path $root "release/last-$logName.log"
  for ($attempt = 1; $attempt -le 2; $attempt++) {
    if ($attempt -gt 1) {
      Warn "Retrying $label in 20s (a background build may be holding files - stop debugging in Visual Studio) ..."
      Start-Sleep -Seconds 20
    }
    & dotnet publish NMU.Platform/NMU.Platform.csproj -f $fw -c Release /p:PublishProfile=$profile --nologo -v m 2>&1 | Tee-Object -FilePath $logPath
    if ($LASTEXITCODE -eq 0) {
      Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
      return
    }
    $tail = ""
    try { $tail = Get-Content -LiteralPath $logPath -Raw -ErrorAction SilentlyContinue } catch { }
    $locked = $tail -match 'locked by|being used by another process|XARLP7024|MSB3021|MSB3026|MSB3027'
    if ($attempt -eq 1 -and $locked) { continue }
    throw "$label failed (exit $LASTEXITCODE). Full log: $logPath. Close Visual Studio debugging and re-run."
  }
}

function Show-File($path) {
  $f = Get-Item -LiteralPath $path
  Ok ("{0}  ({1} MB)" -f $f.Name, [math]::Round($f.Length / 1MB, 1))
}

# --- signing credentials (ZERO prompts by design) ------------------------------
# SECURITY WARNING: the keystore password lives in this file. Anyone with
# access to this repo can sign apps as you. If this repo is ever shared or
# published, generate a NEW keystore + passwords and update these values.
# Env vars override the embedded values when set (useful for CI machines).
$DefaultKsPass = "Abdo@12574"
$DefaultKeyPass = $DefaultKsPass   # same as store password; override via NMU_KEY_PASS

function Get-Cred($envName, $default) {
  $v = [Environment]::GetEnvironmentVariable($envName)
  if (-not [string]::IsNullOrEmpty($v)) { return $v }
  return $default
}

function Get-KeyAlias($ksPath, $ksPass) {
  $wanted = [Environment]::GetEnvironmentVariable("NMU_KEY_ALIAS")
  if (-not [string]::IsNullOrEmpty($wanted)) { return $wanted }
  # Auto-detect: list the keystore (store password only) and take the key alias.
  $out = & keytool -list -keystore $ksPath -storepass $ksPass 2>&1 | Out-String
  foreach ($ln in ($out -split '\r?\n')) {
    if ($ln -match 'PrivateKeyEntry') { return (($ln -split ',')[0].Trim()) }
  }
  throw "Could not detect the key alias in the keystore. Set NMU_KEY_ALIAS and retry."
}

function Find-AndroidTools() {
  $sdk = $env:ANDROID_HOME
  if ([string]::IsNullOrEmpty($sdk)) { $sdk = Join-Path $env:LOCALAPPDATA "Android\Sdk" }
  if (-not (Test-Path -LiteralPath (Join-Path $sdk "build-tools"))) { throw "Android SDK build-tools not found under $sdk." }
  $bt = Get-ChildItem -LiteralPath (Join-Path $sdk "build-tools") -Directory `
    | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "apksigner.bat") } `
    | Sort-Object Name -Descending | Select-Object -First 1
  if ($null -eq $bt) { throw "apksigner.bat not found in Android SDK build-tools." }
  # apksigner needs java: use PATH, else hunt the usual JDK homes.
  $hasJava = $false
  try { Get-Command java -ErrorAction Stop | Out-Null; $hasJava = $true } catch { }
  if (-not $hasJava) {
    $roots = @()
    if ($env:JAVA_HOME) { $roots += (Join-Path $env:JAVA_HOME "bin") }
    foreach ($p in @("C:\Program Files\Microsoft\jdk-*", "C:\Program Files\Android\openjdk\*", "C:\Program Files\Android\Android Studio\jbr", "C:\Program Files\Eclipse Adoptium\*", "C:\Program Files\Eclipse Foundation\*")) {
      try { foreach ($d in (Resolve-Path -LiteralPath $p -ErrorAction SilentlyContinue)) { $roots += (Join-Path $d.Path "bin") } } catch { }
    }
    foreach ($b in $roots) {
      if (Test-Path -LiteralPath (Join-Path $b "java.exe")) { $env:PATH = "$b;" + $env:PATH; $hasJava = $true; break }
    }
  }
  if (-not $hasJava) { throw "java.exe not found. Install a JDK (or set JAVA_HOME) and retry." }
  return @{ ApkSigner = (Join-Path $bt.FullName "apksigner.bat"); ZipAlign = (Join-Path $bt.FullName "zipalign.exe") }
}

function Invoke-SignApk($unsignedApk, $finalApk, $tools, $ksPath, $ksPass, $alias, $keyPass) {
  $tmp = [System.IO.Path]::GetTempFileName() + ".apk"
  try {
    & $tools.ZipAlign -c -p 4 $unsignedApk
    if ($LASTEXITCODE -ne 0) {
      Info "aligning $(Split-Path -Leaf $unsignedApk) ..."
      & $tools.ZipAlign -f -p 4 $unsignedApk $tmp
      if ($LASTEXITCODE -ne 0) { throw "zipalign failed for $(Split-Path -Leaf $unsignedApk)." }
      $signInput = $tmp
    }
    else { $signInput = $unsignedApk }
    Info "signing $(Split-Path -Leaf $finalApk) with your keystore ..."
    $signArgs = @('sign', '--ks', $ksPath, '--ks-pass', "pass:$ksPass", '--ks-key-alias', $alias, '--key-pass', "pass:$keyPass",
      '--out', $finalApk, '--in', $signInput, '--v1-signing-enabled', 'true', '--v2-signing-enabled', 'true')
    & $tools.ApkSigner @signArgs
    if ($LASTEXITCODE -ne 0) { throw "apksigner failed (wrong password/alias?)." }
    # NOTE: capture first, never pipe a native check straight into Select -First
    # (early pipeline stop corrupts $LASTEXITCODE and would false-fail the build).
    & $tools.ApkSigner verify $finalApk
    if ($LASTEXITCODE -ne 0) { throw "signature verification failed for $(Split-Path -Leaf $finalApk)." }
    $certLine = (& $tools.ApkSigner verify --print-certs $finalApk 2>&1 | Out-String) -split '\r?\n' |
      Where-Object { $_ -match 'Signer.*DN' } | Select-Object -First 1
    if ($certLine) { Info ($certLine.Trim()) }
    # v2/v3 signature sidecar: regenerable, useless for sideloading.
    $idsig = "$finalApk.idsig"
    if (Test-Path -LiteralPath $idsig) { Remove-Item -LiteralPath $idsig -Force }
    Ok "signed + verified: $(Split-Path -Leaf $finalApk)"
  }
  finally { if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue } }
}

# --- prerequisites ---------------------------------------------------------
try { Get-Command dotnet -ErrorAction Stop | Out-Null }
catch { Write-Host "[!!] dotnet SDK not found in PATH. Install .NET 10 SDK and retry." -ForegroundColor Yellow; exit 1 }

# Stale MSBuild/Roslyn servers from previous runs hold obj/ locks and break
# the next publish (XARLP7024). Shut them down and don't let new ones linger.
try { & dotnet build-server shutdown 2>&1 | Out-Null } catch { }
$env:MSBUILDDISABLENODEREUSE = '1'
Info "Tip: stop any debugging session in Visual Studio while this runs - its background builds lock the same files."

$ver = "1.0"
$csprojText = Get-Content -LiteralPath "NMU.Platform/NMU.Platform.csproj" -Raw
if ($csprojText -match '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>') { $ver = $Matches[1].Trim() }
Info "App version: $ver"
Info "Plan: 1) arm64 APK (~4 min)  2) universal APK (~4 min)  3) Windows setup (~3 min). Total ~10 min - leave this window open."
New-Item -ItemType Directory -Path "release" -Force | Out-Null

$ksPath = Join-Path $root "Keys\NMU Platform\NMU Platform.keystore"
if (-not (Test-Path -LiteralPath $ksPath)) { Write-Host "[!!] Keystore not found: $ksPath" -ForegroundColor Yellow; exit 1 }

# --- Android (both APKs, your signature) -------------------------------------
if (-not $SkipAndroid) {
  $ksPass = Get-Cred "NMU_KS_PASS" $DefaultKsPass
  $keyPass = Get-Cred "NMU_KEY_PASS" $DefaultKeyPass
  $tools = Find-AndroidTools
  $alias = Get-KeyAlias $ksPath $ksPass
  Info "Signing as: $alias"

  Invoke-DotnetPublish "net10.0-android" "Android-APK-arm64" "Android arm64 APK"
  Invoke-SignApk "NMU.Platform/bin/Release/net10.0-android/publish-arm64/com.nmu.learn.apk" `
    (Join-Path $root "release/NMU-CE-AIE-Android-v$ver-arm64.apk") $tools $ksPath $ksPass $alias $keyPass
  Show-File "release/NMU-CE-AIE-Android-v$ver-arm64.apk"

  Invoke-DotnetPublish "net10.0-android" "Android-APK" "Android universal APK"
  Invoke-SignApk "NMU.Platform/bin/Release/net10.0-android/publish/com.nmu.learn.apk" `
    (Join-Path $root "release/NMU-CE-AIE-Android-v$ver-universal.apk") $tools $ksPath $ksPass $alias $keyPass
  Show-File "release/NMU-CE-AIE-Android-v$ver-universal.apk"

  $ksPass = $null; $keyPass = $null
}
else { Info "Android skipped." }

# --- Windows (ONE setup installer, x64) ------------------------------------------
function Find-InnoSetup() {
  # 1) Installed location from the uninstall registry (any Inno version: 6, 7, ...).
  foreach ($h in @("HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                  "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")) {
    try {
      $keys = Get-ChildItem -LiteralPath $h -ErrorAction SilentlyContinue
      foreach ($k in $keys) {
        try {
          $p = Get-ItemProperty -LiteralPath $k.PSPath -ErrorAction SilentlyContinue
          if ($p.DisplayName -like "Inno Setup*" -and $p.InstallLocation) {
            $cand = Join-Path $p.InstallLocation "ISCC.exe"
            if (Test-Path -LiteralPath $cand) { return $cand }
          }
        } catch { }
      }
    } catch { }
  }
  # 2) PATH, 3) well-known folders.
  try { $c = Get-Command iscc -ErrorAction Stop; return $c.Source } catch { }
  foreach ($p in @("C:\Program Files (x86)\Inno Setup 6\ISCC.exe", "C:\Program Files\Inno Setup 6\ISCC.exe",
                  "C:\Program Files (x86)\Inno Setup 7\ISCC.exe", "C:\Program Files\Inno Setup 7\ISCC.exe")) {
    if (Test-Path -LiteralPath $p) { return $p }
  }
  return $null
}

if (-not $SkipWindows) {
  Invoke-DotnetPublish "net10.0-windows10.0.19041.0" "Windows-Folder" "Windows x64 app folder"
  $appDir = Join-Path $root "NMU.Platform/bin/Release/net10.0-windows10.0.19041.0/publish"
  # Tidy: collect the loose generated tile/splash/store images into Assets\
  # (the unpackaged output ships no manifest referencing them, so plain
  # moving is safe and the installer picks them up via recursesubdirs).
  $assetsDir = Join-Path $appDir "Assets"
  New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
  Get-ChildItem -LiteralPath $appDir -Filter "appicon*" -File | Move-Item -Destination $assetsDir -Force
  $setupOut = Join-Path $root "release/NMU-CE-AIE-Setup-v$ver-x64.exe"
  $iscc = Find-InnoSetup
  if ($null -eq $iscc) {
    Write-Host "[!!] NO SETUP INSTALLER THIS RUN: Inno Setup is not installed." -ForegroundColor Red
    Write-Host "     Install it once from https://jrsoftware.org/isdl.php (Next/Next/Install), then re-run." -ForegroundColor Red
    Warn "Fallback: shipping the app folder as a single .zip instead."
    $zipPath = Join-Path $root "release/NMU-CE-AIE-Windows-v$ver-x64.zip"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $appDir "*") -DestinationPath $zipPath
    Show-File "release/NMU-CE-AIE-Windows-v$ver-x64.zip"
    Info "Student unzips and runs NMU.Platform.exe (needs .NET 10 Desktop Runtime)."
  }
  else {
    Info "building Windows setup installer ..."
    $iss = Join-Path $root "NMU.Platform/Packaging/WindowsSetup.iss"
    $releaseAbs = Join-Path $root "release"
    $iconAbs = Join-Path $root "NMU.Platform/Packaging/appicon.ico"
    & $iscc $iss /DAppVersion="$ver" /DSourceDir="$appDir" /DOutDir="$releaseAbs" /DOutBase="NMU-CE-AIE-Setup-v$ver-x64" /DIconFile="$iconAbs"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed (exit $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $setupOut)) { throw "Setup exe not produced: $setupOut" }
    Show-File "release/NMU-CE-AIE-Setup-v$ver-x64.exe"
    Info "Student double-clicks setup, installs (no admin), finds it in Start Menu."
  }
}
else { Info "Windows skipped." }

# --- Apple targets (Mac only) --------------------------------------------------
$onMac = $false
try { $onMac = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX) } catch { }
if ($onMac) {
  Info "macOS detected: building Mac Catalyst bundle ..."
  & dotnet publish NMU.Platform/NMU.Platform.csproj -f net10.0-maccatalyst -c Release --nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "Mac Catalyst publish failed." }
  Ok "Mac Catalyst build done (see bin/Release/net10.0-maccatalyst)."
  Info "iPhone (iOS) publish additionally needs Apple signing (certificate + provisioning profile)."
}
else {
  Info "iOS/Mac skipped: Apple allows publishing those ONLY from a Mac."
}

# --- summary -------------------------------------------------------------------
$mins = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
Write-Host ""
Ok "Done in $mins min. Final files in ./release :"
Get-ChildItem -LiteralPath "release" -File | ForEach-Object {
  Write-Host ("  - {0}  ({1} MB)" -f $_.Name, [math]::Round($_.Length / 1MB, 1))
}
if ($env:OS -eq 'Windows_NT') {
  try { explorer.exe "release" } catch { }
}
