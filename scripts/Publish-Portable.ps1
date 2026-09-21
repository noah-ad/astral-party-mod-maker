#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$WithoutVideoRuntime,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Build the Windows portable package on Windows.'
}
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'App/JixModMaker.csproj'
[xml]$config = Get-Content -LiteralPath $project -Raw -Encoding UTF8
$version = $config.SelectSingleNode('//Version').InnerText
$launcher = $config.SelectSingleNode('//PortableLauncherName').InnerText + '.exe'
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $repo "artifacts/AstralPartyModMaker-v$version-win64"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$data = Join-Path $output 'data'
$archive = $output.TrimEnd([IO.Path]::DirectorySeparatorChar) + '.zip'
if (Test-Path -LiteralPath $output) {
    throw "Output already exists. Choose a new directory; existing files are never replaced: $output"
}
if ($Zip -and (Test-Path -LiteralPath $archive)) {
    throw "Archive already exists: $archive"
}
if (!$WithoutVideoRuntime) {
    foreach ($relative in @('ffmpeg.exe', 'python/python.exe', 'mux.py')) {
        $file = Join-Path $repo "App/Tools/video/$relative"
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Missing local video component: $file. See App/Tools/video/DEPENDENCIES.md, or use -WithoutVideoRuntime for a developer package."
        }
    }
    & (Join-Path $repo 'App/Tools/video/python/python.exe') -c 'from cricodecs import usm, video'
    if ($LASTEXITCODE -ne 0) { throw 'The local video runtime is incomplete.' }
}

$includeVideo = if ($WithoutVideoRuntime) { 'false' } else { 'true' }
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $data `
    "-p:PortableRoot=$output" "-p:IncludeVideoRuntime=$includeVideo" -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed; output has not been archived.' }

$entries = @(Get-ChildItem -LiteralPath $output -Force)
if ($entries.Count -ne 2 -or !(Test-Path -LiteralPath (Join-Path $output $launcher) -PathType Leaf) -or
    !(Test-Path -LiteralPath $data -PathType Container)) {
    throw 'Invalid portable layout: expected only the launcher and data directory.'
}
foreach ($relative in @('JixModMaker.dll', 'JixModMaker.deps.json', 'JixModMaker.runtimeconfig.json',
    'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'classdata.tpk', 'Assets/app_icon.ico')) {
    if (!(Test-Path -LiteralPath (Join-Path $data $relative) -PathType Leaf)) { throw "Missing packaged file: $relative" }
}
if ($WithoutVideoRuntime -and ((Test-Path -LiteralPath (Join-Path $data 'Tools/video/ffmpeg.exe')) -or
    (Test-Path -LiteralPath (Join-Path $data 'Tools/video/python')))) {
    throw 'Excluded video binaries leaked into the developer package.'
}
if ($Zip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($output, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host "ZIP: $archive"
}
Write-Host "Launch: $(Join-Path $output $launcher)"
