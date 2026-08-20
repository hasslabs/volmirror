<#
    Publishes VolMirror and installs it to %LOCALAPPDATA%\VolMirror, then registers
    it to start with Windows.

    Installed out of the repo on purpose: publish\ is gitignored, so a git clean
    would delete the very executable the Run key points at.

    Usage:  .\install.ps1          install / update and enable autostart
            .\install.ps1 -Uninstall
#>
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'

$target  = Join-Path $env:LOCALAPPDATA 'VolMirror'
$exe     = Join-Path $target 'VolMirror.exe'
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$name    = 'VolMirror'

function Stop-Running {
    Get-Process VolMirror -ErrorAction SilentlyContinue | ForEach-Object {
        $_.Kill(); $_.WaitForExit(5000)
    }
}

if ($Uninstall) {
    Stop-Running
    Remove-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Write-Host "VolMirror removed. Set Preamp: 0 dB in volume.txt if it is still attenuating." -ForegroundColor Yellow
    return
}

Write-Host "Publishing..." -ForegroundColor Cyan
dotnet publish (Join-Path $PSScriptRoot 'src\VolMirror') -c Release -r win-x64 `
    --self-contained false -p:PublishSingleFile=true `
    -o (Join-Path $PSScriptRoot 'publish') | Out-Null
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Stop-Running
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'publish\VolMirror.exe') $exe -Force

# Quoted: the path contains no spaces today, but %LOCALAPPDATA% can.
Set-ItemProperty -Path $runKey -Name $name -Value ('"{0}"' -f $exe)

Start-Process $exe

Write-Host ""
Write-Host "Installed to $exe" -ForegroundColor Green
Write-Host "Autostart registered under HKCU Run, and VolMirror is now running."
