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
    'README.md',
    'assets\branding\dsh-whale.ico',
    'assets\branding\dsh-whale.png',
    'desktop\README.md',
    'desktop\build.ps1',
    'desktop\install-desktop-shortcut.ps1',
    'tools\make-icon.ps1',
    'desktop\runtime\dsh-desktop-bootstrap.mjs'
)
foreach ($relativePath in $requiredFiles) {
    Assert-PathExists (Join-Path $repositoryRoot $relativePath) 'Repository layout is incomplete.'
}

$retiredPaths = @(
    'dsh-whale.ico',
    'dsh-whale.png',
    'make-icon.ps1',
    ('README' + '-vision-setup.md'),
    ('upload' + '-large.png'),
    ('upload' + '-large-small.jpg'),
    ('upload' + '-small.png'),
    'docs\vision-setup.md',
    'docs\superpowers\plans\2026-08-14-dsh-desktop-shell.md',
    'docs\superpowers\specs\2026-08-14-dsh-desktop-shell-design.md',
    'samples\vision\.gitkeep',
    'start-dsh-web.cmd',
    'open-dsh-web.cmd'
)
foreach ($relativePath in $retiredPaths) {
    Assert-PathAbsent (Join-Path $repositoryRoot $relativePath) 'Repository layout left an unrelated or retired path behind.'
}

& git -C $repositoryRoot check-ignore --quiet --no-index -- 'dsh-vision-mcp.cjs'
if ($LASTEXITCODE -ne 0) {
    throw 'The local vision compatibility script must be ignored by this desktop-only repository.'
}

$publicDocuments = @(& git -C $repositoryRoot ls-files '*.md')
if ($LASTEXITCODE -ne 0) {
    throw 'Could not enumerate tracked public documentation.'
}
$expectedDocuments = @('README.md', 'desktop/README.md')
$documentDifference = @(Compare-Object -ReferenceObject $expectedDocuments -DifferenceObject $publicDocuments)
if ($documentDifference.Count -ne 0) {
    throw "Tracked Markdown must contain only the two Chinese delivery guides. Actual: $($publicDocuments -join ', ')"
}
foreach ($relativePath in $publicDocuments) {
    $documentPath = Join-Path $repositoryRoot $relativePath
    $content = Get-Content -LiteralPath $documentPath -Raw
    $machinePath = [regex]::Match($content, '(?<![A-Za-z0-9])[A-Za-z]:[\\/]')
    if ($machinePath.Success) {
        throw "Public documentation contains a checkout-specific drive path: $relativePath"
    }
    if ($content -notmatch '[\p{IsCJKUnifiedIdeographs}]') {
        throw "Tracked Markdown does not contain Chinese delivery documentation: $relativePath"
    }
}

Write-Host 'PASS repository layout contains only the DSH desktop deliverable and two Chinese Markdown guides'
$global:LASTEXITCODE = 0
