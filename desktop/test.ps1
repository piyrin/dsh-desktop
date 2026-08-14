param([string]$Filter)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path $root 'tests\Task1Tests.exe'
$testOutput = Join-Path $root 'test-output'
$helperOutput = Join-Path $testOutput 'NoWindowHelper.exe'
$earlyExitHelperOutput = Join-Path $testOutput 'EarlyExitHelper.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$null = New-Item -ItemType Directory -Force -Path $testOutput
& $compiler /nologo /target:exe /out:$helperOutput (Join-Path $root 'tests\NoWindowHelper.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $compiler /nologo /target:exe /out:$earlyExitHelperOutput (Join-Path $root 'tests\EarlyExitHelper.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$sources = @(
  (Join-Path $root 'tests\TestRunner.cs'),
  (Join-Path $root 'tests\StartupPolicyTests.cs'),
  (Join-Path $root 'tests\DshInstallationTests.cs'),
  (Join-Path $root 'tests\DshReadinessTests.cs'),
  (Join-Path $root 'tests\BackendOwnershipTests.cs'),
  (Join-Path $root 'tests\ActivationProtocolTests.cs'),
  (Join-Path $root 'src\AppLogger.cs'),
  (Join-Path $root 'src\JobObject.cs'),
  (Join-Path $root 'src\AppPaths.cs'),
  (Join-Path $root 'src\StartupPolicy.cs'),
  (Join-Path $root 'src\CommandLocator.cs'),
  (Join-Path $root 'src\DshInstallation.cs'),
  (Join-Path $root 'src\BackendLaunchSpec.cs')
  ,(Join-Path $root 'src\DshReadiness.cs')
  ,(Join-Path $root 'src\BackendSupervisor.cs')
  ,(Join-Path $root 'src\SingleInstanceCoordinator.cs')
)
& $compiler /nologo /target:exe /reference:System.Net.Http.dll /out:$output $sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$arguments = @()
if ($Filter) { $arguments += '--filter'; $arguments += $Filter }
& $output $arguments
exit $LASTEXITCODE
