param([string]$Filter)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$testOutput = Join-Path $root 'test-output'
$output = Join-Path $testOutput 'DesktopTests.exe'
$helperOutput = Join-Path $testOutput 'NoWindowHelper.exe'
$earlyExitHelperOutput = Join-Path $testOutput 'EarlyExitHelper.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$null = New-Item -ItemType Directory -Force -Path $testOutput
& $compiler /nologo /target:exe /out:$helperOutput (Join-Path $root 'tests\NoWindowHelper.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $compiler /nologo /target:exe /out:$earlyExitHelperOutput (Join-Path $root 'tests\EarlyExitHelper.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$sources = @(
  (Join-Path $root 'tests\TestRunner.cs'),
  (Join-Path $root 'tests\StartupPolicyTests.cs'),
  (Join-Path $root 'tests\DesktopUiPolicyTests.cs'),
  (Join-Path $root 'tests\AppAssetsTests.cs'),
  (Join-Path $root 'tests\SplashOverlayTests.cs'),
  (Join-Path $root 'tests\InvisibleWindowPreloaderTests.cs'),
  (Join-Path $root 'tests\DshInstallationTests.cs'),
  (Join-Path $root 'tests\DshReadinessTests.cs'),
  (Join-Path $root 'tests\BackendOwnershipTests.cs'),
  (Join-Path $root 'tests\ActivationProtocolTests.cs'),
  (Join-Path $root 'tests\DesktopApplicationTests.cs'),
  (Join-Path $root 'tests\TrayControllerTests.cs'),
  (Join-Path $root 'src\AppLogger.cs'),
  (Join-Path $root 'src\JobObject.cs'),
  (Join-Path $root 'src\AppPaths.cs'),
  (Join-Path $root 'src\StartupPolicy.cs'),
  (Join-Path $root 'src\DesktopUiPolicy.cs'),
  (Join-Path $root 'src\AppAssets.cs'),
  (Join-Path $root 'src\SplashOverlay.cs'),
  (Join-Path $root 'src\InvisibleWindowPreloader.cs'),
  (Join-Path $root 'src\CommandLocator.cs'),
  (Join-Path $root 'src\DshInstallation.cs'),
  (Join-Path $root 'src\BackendLaunchSpec.cs')
  ,(Join-Path $root 'src\DshReadiness.cs')
  ,(Join-Path $root 'src\BackendSupervisor.cs')
  ,(Join-Path $root 'src\SingleInstanceCoordinator.cs')
  ,(Join-Path $root 'src\TrayController.cs')
  ,(Join-Path $root 'src\DesktopApplication.cs')
)
& $compiler /nologo /target:exe `
  "/reference:$(Join-Path $frameworkRoot 'System.Net.Http.dll')" `
  "/reference:$(Join-Path $frameworkRoot 'System.Xaml.dll')" `
  "/reference:$(Join-Path $frameworkRoot 'WindowsBase.dll')" `
  "/reference:$(Join-Path $frameworkRoot 'PresentationCore.dll')" `
  "/reference:$(Join-Path $frameworkRoot 'PresentationFramework.dll')" `
  /out:$output $sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$arguments = @()
if ($Filter) { $arguments += '--filter'; $arguments += $Filter }
& $output $arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $root 'tests\InstallDesktopShortcutTests.ps1')
exit $LASTEXITCODE
