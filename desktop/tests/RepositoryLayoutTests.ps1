$ErrorActionPreference = 'Stop'

function Assert-PathExists([string]$path, [string]$message) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "$message Missing: $path"
    }
}

function Assert-PathAbsent([string]$path, [string]$message) {
    if (Test-Path -LiteralPath $path) {
        throw "$message Unexpected: $path"
    }
}

$desktopRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$repositoryRoot = Split-Path -Parent $desktopRoot

$requiredFiles = @(
    'assets\branding\dsh-whale.ico',
    'assets\branding\dsh-whale.png',
    'tools\make-icon.ps1',
    'docs\vision-setup.md',
    'samples\vision\.gitkeep',
    'dsh-vision-mcp.cjs',
    'start-dsh-web.cmd',
    'open-dsh-web.cmd'
)
foreach ($relativePath in $requiredFiles) {
    Assert-PathExists (Join-Path $repositoryRoot $relativePath) 'Repository layout is incomplete.'
}

$retiredRootPaths = @(
    'dsh-whale.ico',
    'dsh-whale.png',
    'make-icon.ps1',
    ('README' + '-vision-setup.md'),
    ('upload' + '-large.png'),
    ('upload' + '-large-small.jpg'),
    ('upload' + '-small.png')
)
foreach ($relativePath in $retiredRootPaths) {
    Assert-PathAbsent (Join-Path $repositoryRoot $relativePath) 'Repository layout left a retired root path behind.'
}

$ignoredSamples = @(
    ('samples/vision/upload' + '-large.png'),
    ('samples/vision/upload' + '-large-small.jpg'),
    ('samples/vision/upload' + '-small.png')
)
foreach ($relativePath in $ignoredSamples) {
    & git -C $repositoryRoot check-ignore --quiet --no-index -- $relativePath
    if ($LASTEXITCODE -ne 0) {
        throw "Local sample path is not ignored: $relativePath"
    }
}

& git -C $repositoryRoot check-ignore --quiet --no-index -- 'samples/vision/.gitkeep'
if ($LASTEXITCODE -eq 0) {
    throw 'samples/vision/.gitkeep must remain trackable.'
}

Write-Host 'PASS repository layout uses organized paths and keeps local vision samples ignored'
$global:LASTEXITCODE = 0
