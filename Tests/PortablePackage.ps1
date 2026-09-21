#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [string]$WorkDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'App/JixModMaker.csproj') -Raw -Encoding UTF8
$launcher = $project.SelectSingleNode('//PortableLauncherName').InnerText + '.exe'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (!$WorkDirectory) { $WorkDirectory = Join-Path $repo ('artifacts/portable-tests-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $WorkDirectory) { throw "Test directory already exists: $WorkDirectory" }
$work = (New-Item -ItemType Directory -Path $WorkDirectory).FullName
$relocated = Join-Path $work ($project.SelectSingleNode('//PortableLauncherName').InnerText + ' moved package')
Copy-Item -LiteralPath $package -Destination $relocated -Recurse
$data = Join-Path $relocated 'data'
$entries = @(Get-ChildItem -LiteralPath $relocated -Force)
if ($entries.Count -ne 2 -or !(Test-Path -LiteralPath (Join-Path $relocated $launcher)) -or
    (Test-Path -LiteralPath (Join-Path $data 'JixModMaker.exe'))) { throw 'Unexpected portable layout.' }

# A GUI-subsystem apphost must not open a console, and keeps the product's resources.
$exe = Join-Path $relocated $launcher
$bytes = [IO.File]::ReadAllBytes($exe)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
if ([BitConverter]::ToUInt16($bytes, $peOffset + 24 + 68) -ne 2) { throw 'Launcher is not a GUI executable.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion -notlike ($project.SelectSingleNode('//Version').InnerText + '*')) {
    throw 'Launcher version resources do not match the application.'
}
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::ExtractAssociatedIcon($exe)
if ($null -eq $icon) { throw 'Launcher icon is missing.' }
$icon.Dispose()

$empty = (New-Item -ItemType Directory -Path (Join-Path $work 'empty resources')).FullName
$trace = Join-Path $work 'host-trace.log'
$environment = @{}
foreach ($key in @('COREHOST_TRACE', 'COREHOST_TRACEFILE', 'DOTNET_ROOT', 'DOTNET_ROOT_X64')) {
    $environment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}
$process = $null
try {
    $env:COREHOST_TRACE = '1'
    $env:COREHOST_TRACEFILE = $trace
    $env:DOTNET_ROOT = Join-Path $work 'no installed runtime'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $process = Start-Process -FilePath $exe -ArgumentList ('"' + $empty + '"') `
        -WorkingDirectory $work -WindowStyle Hidden -PassThru
    if (!$process.WaitForInputIdle(15000)) { throw 'Launcher did not reach the GUI message loop.' }
    Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited -or !$process.Responding) { throw 'Moved package failed to start.' }
    $runtime = @($process.Modules | Where-Object { $_.ModuleName -in @('coreclr.dll', 'hostfxr.dll') })
    if ($runtime.Count -ne 2) { throw 'Bundled runtime modules were not loaded.' }
    foreach ($module in $runtime) {
        if (![string]::Equals((Split-Path -Parent $module.FileName), $data, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Loaded a runtime outside data: $($module.FileName)"
        }
    }
} finally {
    if ($null -ne $process) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    foreach ($key in $environment.Keys) { [Environment]::SetEnvironmentVariable($key, $environment[$key], 'Process') }
}
if (!(Select-String -LiteralPath $trace -SimpleMatch 'data/JixModMaker.dll' -Quiet)) {
    throw 'Launcher is not bound to the relative managed application.'
}
Write-Host 'PASS two-entry layout, GUI launcher, version and icon'
Write-Host 'PASS moved package, Chinese and spaced paths, unrelated working directory'
Write-Host 'PASS app-local runtime and relative data binding'
Write-Host "Test copy: $relocated"
