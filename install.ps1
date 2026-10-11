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

# Explorer starts the Run value from the real profile. A shell launched from an
# MSIX-packaged app can have new AppData files redirected into that app's
# Packages\...\LocalCache - even after the shell has dropped the package
# identity - so the exe exists only in that app's view and autostart silently
# never fires. Ask the file system where a new file actually lands.
function Assert-AppDataNotRedirected {
    if (-not ('VolMirrorInstall.Native' -as [type])) {
        Add-Type -Namespace VolMirrorInstall -Name Native -MemberDefinition @'
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern uint GetFinalPathNameByHandle(
    Microsoft.Win32.SafeHandles.SafeFileHandle file, System.Text.StringBuilder path, uint length, uint flags);
'@
    }

    $probe = Join-Path $env:LOCALAPPDATA ('VolMirror-install-probe-{0}.tmp' -f [guid]::NewGuid())
    $final = New-Object System.Text.StringBuilder 1024
    $stream = [System.IO.File]::Create($probe, 1, [System.IO.FileOptions]::DeleteOnClose)
    try {
        $length = [VolMirrorInstall.Native]::GetFinalPathNameByHandle(
            $stream.SafeFileHandle, $final, $final.Capacity, 0)
    }
    finally {
        $stream.Dispose()
    }

    if ($length -eq 0 -or $length -ge $final.Capacity) {
        throw "Could not tell where files written to $env:LOCALAPPDATA end up."
    }

    if ($final.ToString() -match '\\Packages\\([^\\]+)\\LocalCache\\') {
        throw ("This shell's AppData writes are redirected into the MSIX app '{0}', " +
               "where Windows cannot autostart them. Run install.ps1 from a normal " +
               "PowerShell window instead.") -f $Matches[1]
    }
}

function Stop-Running {
    Get-Process VolMirror -ErrorAction SilentlyContinue | ForEach-Object {
        $_.Kill(); $_.WaitForExit(5000)
    }
}

Assert-AppDataNotRedirected

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
