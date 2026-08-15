param(
    [string]$DesktopDirectoryOverride,
    [string]$ApplicationPathOverride,
    [string]$IconPathOverride
)

$ErrorActionPreference = 'Stop'

function Resolve-DesktopDirectory([string]$overridePath) {
    if (-not [String]::IsNullOrWhiteSpace($overridePath)) {
        return [System.IO.Path]::GetFullPath($overridePath)
    }

    $desktopDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    if ([String]::IsNullOrWhiteSpace($desktopDirectory)) {
        $userShellFolders = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
        $desktopDirectory = [Environment]::ExpandEnvironmentVariables([string]$userShellFolders.Desktop)
    }
    if ([String]::IsNullOrWhiteSpace($desktopDirectory)) {
        throw 'Windows did not provide a DesktopDirectory path.'
    }

    return [System.IO.Path]::GetFullPath($desktopDirectory)
}

$desktopRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Split-Path -Parent $desktopRoot
$applicationPath = if ([String]::IsNullOrWhiteSpace($ApplicationPathOverride)) {
    Join-Path $desktopRoot 'publish\DeepSeek Harness.exe'
}
else {
    $ApplicationPathOverride
}
$applicationPath = [System.IO.Path]::GetFullPath($applicationPath)

if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw "Build the desktop application first. Executable not found: $applicationPath"
}

$iconPath = if ([String]::IsNullOrWhiteSpace($IconPathOverride)) {
    Join-Path $repositoryRoot 'assets\branding\dsh-whale.ico'
}
else {
    $IconPathOverride
}
$iconPath = [System.IO.Path]::GetFullPath($iconPath)
if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
    throw "Shortcut icon not found: $iconPath"
}

$desktopDirectory = Resolve-DesktopDirectory $DesktopDirectoryOverride
if (-not (Test-Path -LiteralPath $desktopDirectory -PathType Container)) {
    $null = New-Item -ItemType Directory -Path $desktopDirectory
}

$workingDirectory = Split-Path -Parent $applicationPath
$shortcutPath = Join-Path $desktopDirectory 'DeepSeek Harness.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $null
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $applicationPath
    $shortcut.WorkingDirectory = $workingDirectory
    $shortcut.IconLocation = "$iconPath,0"
    $shortcut.WindowStyle = 1
    $shortcut.Save()
}
finally {
    if ($null -ne $shortcut) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}

Write-Host "Desktop shortcut installed: $shortcutPath"
Write-Host "Target: $applicationPath"
Write-Host "Icon: $iconPath"
