param([string]$Filter)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path $root 'tests\Task1Tests.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(
  (Join-Path $root 'tests\TestRunner.cs'),
  (Join-Path $root 'tests\StartupPolicyTests.cs'),
  (Join-Path $root 'src\AppPaths.cs'),
  (Join-Path $root 'src\StartupPolicy.cs')
)
& $compiler /nologo /target:exe /out:$output $sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$arguments = @()
if ($Filter) { $arguments += '--filter'; $arguments += $Filter }
& $output $arguments
exit $LASTEXITCODE
