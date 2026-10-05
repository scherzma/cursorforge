<#
.SYNOPSIS
  Builds CursorForge into .\dist  (CursorForge.exe = settings UI, CursorForge.Agent.exe = background agent).
.PARAMETER Install
  Also copies the build to %LOCALAPPDATA%\Programs\CursorForge, adds a Start-menu shortcut and launches it.
.PARAMETER Release
  Also builds .\release\CursorForge.exe: one self-contained download (no .NET needed) with the agent embedded.
  It installs itself for the current user on first run.
.PARAMETER Uninstall
  Stops the agent, removes autostart, the shortcut and the installed files (settings in %APPDATA% are kept).
#>
param([switch]$Install, [switch]$Uninstall, [switch]$Release)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\CursorForge'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'CursorForge.lnk'

function Stop-Agent([string]$dir) {
    $agent = Join-Path $dir 'CursorForge.Agent.exe'
    if (Test-Path $agent) { & $agent --exit | Out-Null }   # graceful: restores Windows cursors
    Get-Process CursorForge -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir*" } | Stop-Process -Force
}

if ($Uninstall) {
    Stop-Agent $installDir
    Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CursorForge -ErrorAction SilentlyContinue
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'CursorForge uninstalled.'
    return
}

# NativeAOT needs the MSVC linker; ILCompiler locates it through vswhere.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path $vsInstaller) -and -not ($env:PATH -split ';' -contains $vsInstaller)) { $env:PATH += ";$vsInstaller" }

if (-not (Test-Path "$root\assets\app.ico")) {
    dotnet run --project "$root\tools\IconGen" -c Release -- "$root\assets\app.ico"
    if ($LASTEXITCODE) { throw 'Icon generation failed' }
}

Stop-Agent $dist
dotnet publish "$root\src\CursorForge.Agent" -c Release -o $dist --nologo
if ($LASTEXITCODE) { throw 'Agent build failed (see errors above; NativeAOT also needs Visual Studio with the "Desktop development with C++" workload)' }
dotnet publish "$root\src\CursorForge.App" -c Release -o $dist --nologo
if ($LASTEXITCODE) { throw 'UI build failed' }
Get-ChildItem $dist -Filter *.pdb | Remove-Item

Write-Host "`nBuilt:" -ForegroundColor Green
Get-ChildItem $dist -Filter *.exe | ForEach-Object { '  {0,-24} {1,8:N0} KB' -f $_.Name, ($_.Length / 1KB) }

if ($Release) {
    $rel = Join-Path $root 'release'
    if (Test-Path $rel) { Get-ChildItem $rel | Remove-Item -Recurse -Force }
    dotnet publish "$root\src\CursorForge.App" -c Release -o $rel --nologo `
        -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        "-p:EmbedAgent=$dist\CursorForge.Agent.exe"
    if ($LASTEXITCODE) { throw 'Release build failed' }
    Get-ChildItem $rel -Exclude 'CursorForge.exe' | Remove-Item -Recurse -Force
    $size = (Get-Item "$rel\CursorForge.exe").Length / 1MB
    Write-Host ("`nRelease: {0} ({1:N1} MB, self-contained, agent embedded)" -f "$rel\CursorForge.exe", $size) -ForegroundColor Green
}

if ($Install) {
    Stop-Agent $installDir
    New-Item -ItemType Directory -Force $installDir | Out-Null
    Copy-Item "$dist\*.exe" $installDir -Force
    $ws = New-Object -ComObject WScript.Shell
    $lnk = $ws.CreateShortcut($shortcut)
    $lnk.TargetPath = Join-Path $installDir 'CursorForge.exe'
    $lnk.WorkingDirectory = $installDir
    $lnk.Save()
    Start-Process (Join-Path $installDir 'CursorForge.exe')
    Write-Host "Installed to $installDir (Start menu: CursorForge)" -ForegroundColor Green
}
