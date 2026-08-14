$ErrorActionPreference = 'Stop'

function Assert-Equal([object]$expected, [object]$actual, [string]$message) {
    if (-not [object]::Equals($expected, $actual)) {
        throw "$message Expected '$expected', got '$actual'."
    }
}

$desktopRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$installer = Join-Path $desktopRoot 'install-desktop-shortcut.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DshDesktopShortcutTests-" + [Guid]::NewGuid().ToString('N'))
$fakeDesktop = Join-Path $testRoot 'Desktop'
$fakePublish = Join-Path $testRoot 'publish'
$fakeExecutable = Join-Path $fakePublish 'DeepSeek Harness.exe'
$shortcutPath = Join-Path $fakeDesktop 'DeepSeek Harness.lnk'
$unrelatedShortcut = Join-Path $fakeDesktop 'Other Application.lnk'

try {
    $null = New-Item -ItemType Directory -Path $fakeDesktop
    $null = New-Item -ItemType Directory -Path $fakePublish
    Set-Content -LiteralPath $fakeExecutable -Value 'test executable placeholder' -NoNewline
    Set-Content -LiteralPath $unrelatedShortcut -Value 'unrelated shortcut sentinel' -NoNewline

    $setupShell = New-Object -ComObject WScript.Shell
    try {
        $oldShortcut = $setupShell.CreateShortcut($shortcutPath)
        $oldShortcut.TargetPath = Join-Path $testRoot 'Old DeepSeek Harness.exe'
        $oldShortcut.Save()
    }
    finally {
        if ($null -ne $oldShortcut) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($oldShortcut) }
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($setupShell)
    }

    & $installer -DesktopDirectoryOverride $fakeDesktop -ApplicationPathOverride $fakeExecutable

    if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
        throw "Installer did not create $shortcutPath."
    }
    Assert-Equal 'unrelated shortcut sentinel' (Get-Content -Raw -LiteralPath $unrelatedShortcut) 'Installer modified an unrelated shortcut.'

    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        Assert-Equal ([System.IO.Path]::GetFullPath($fakeExecutable)) $shortcut.TargetPath 'TargetPath mismatch.'
        Assert-Equal ([System.IO.Path]::GetFullPath($fakePublish)) $shortcut.WorkingDirectory 'WorkingDirectory mismatch.'
        Assert-Equal (([System.IO.Path]::GetFullPath($fakeExecutable)) + ',0') $shortcut.IconLocation 'IconLocation mismatch.'
        Assert-Equal 1 $shortcut.WindowStyle 'WindowStyle mismatch.'
    }
    finally {
        if ($null -ne $shortcut) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }

    Write-Host 'PASS desktop shortcut installer creates exact metadata and preserves unrelated shortcuts'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
