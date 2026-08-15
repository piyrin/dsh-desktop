param()

$ErrorActionPreference = 'Stop'

$desktopRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Split-Path -Parent $desktopRoot
$packagesRoot = Join-Path $desktopRoot 'packages'
$publishRoot = Join-Path $desktopRoot 'publish'
$objectRoot = Join-Path $desktopRoot 'obj'
$webViewVersion = '1.0.4078.44'
$packageName = "Microsoft.Web.WebView2.$webViewVersion"
$packageRoot = Join-Path $packagesRoot $packageName
$packageUrl = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$webViewVersion/microsoft.web.webview2.$webViewVersion.nupkg"
$packageArchive = Join-Path $packagesRoot "$packageName.nupkg"

function Reset-BuildDirectory([string]$path) {
    $resolvedDesktop = [System.IO.Path]::GetFullPath($desktopRoot).TrimEnd('\') + '\'
    $resolvedPath = [System.IO.Path]::GetFullPath($path)
    if (-not $resolvedPath.StartsWith($resolvedDesktop, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside desktop: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $resolvedPath
}

if (-not (Test-Path -LiteralPath $packageRoot)) {
    $null = New-Item -ItemType Directory -Force -Path $packagesRoot
    Write-Host "Restoring Microsoft.Web.WebView2 $webViewVersion..."
    Invoke-WebRequest -Uri $packageUrl -OutFile $packageArchive
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $extractRoot = "$packageRoot.extracting"
    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
    try {
        [System.IO.Compression.ZipFile]::ExtractToDirectory($packageArchive, $extractRoot)
        Move-Item -LiteralPath $extractRoot -Destination $packageRoot
    }
    finally {
        if (Test-Path -LiteralPath $extractRoot) {
            Remove-Item -LiteralPath $extractRoot -Recurse -Force
        }
    }
}

$frameworkRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$webViewCore = Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'
$webViewWpf = Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Wpf.dll'
$webViewLoader = Join-Path $packageRoot 'build\native\x64\WebView2Loader.dll'
$webViewLicense = Join-Path $packageRoot 'LICENSE.txt'
$manifest = Join-Path $desktopRoot 'assets\app.manifest'
$icon = Join-Path $repositoryRoot 'assets\branding\dsh-whale.ico'

$requiredBuildInputs = @(
    $compiler,
    $frameworkRoot,
    $webViewCore,
    $webViewWpf,
    $webViewLoader,
    $webViewLicense,
    $icon
)
foreach ($inputPath in $requiredBuildInputs) {
    if (-not (Test-Path -LiteralPath $inputPath)) {
        throw "Required build input is missing: $inputPath"
    }
}

Reset-BuildDirectory $objectRoot
Reset-BuildDirectory $publishRoot

$sources = @(
    (Join-Path $desktopRoot 'src\AppLogger.cs'),
    (Join-Path $desktopRoot 'src\AppPaths.cs'),
    (Join-Path $desktopRoot 'src\BackendLaunchSpec.cs'),
    (Join-Path $desktopRoot 'src\BackendSupervisor.cs'),
    (Join-Path $desktopRoot 'src\CommandLocator.cs'),
    (Join-Path $desktopRoot 'src\DshInstallation.cs'),
    (Join-Path $desktopRoot 'src\DshReadiness.cs'),
    (Join-Path $desktopRoot 'src\JobObject.cs'),
    (Join-Path $desktopRoot 'src\SingleInstanceCoordinator.cs'),
    (Join-Path $desktopRoot 'src\StartupPolicy.cs'),
    (Join-Path $desktopRoot 'src\DesktopUiPolicy.cs'),
    (Join-Path $desktopRoot 'src\AppAssets.cs'),
    (Join-Path $desktopRoot 'src\SplashOverlay.cs'),
    (Join-Path $desktopRoot 'src\InvisibleWindowPreloader.cs'),
    (Join-Path $desktopRoot 'src\WebViewRenderSynchronizer.cs'),
    (Join-Path $desktopRoot 'src\MainWindow.cs'),
    (Join-Path $desktopRoot 'src\TrayController.cs'),
    (Join-Path $desktopRoot 'src\DesktopApplication.cs'),
    (Join-Path $desktopRoot 'src\RecoveringDesktopBackend.cs'),
    (Join-Path $desktopRoot 'src\Program.cs')
)

$executable = Join-Path $publishRoot 'DeepSeek Harness.exe'
$compilerArguments = @(
    '/nologo',
    '/noconfig',
    '/target:winexe',
    '/platform:x64',
    '/langversion:5',
    '/nostdlib+',
    '/optimize+',
    "/out:$executable",
    "/win32icon:$icon",
    "/win32manifest:$manifest",
    "/reference:$(Join-Path $frameworkRoot 'mscorlib.dll')",
    "/reference:$(Join-Path $frameworkRoot 'System.dll')",
    "/reference:$(Join-Path $frameworkRoot 'System.Core.dll')",
    "/reference:$(Join-Path $frameworkRoot 'System.Drawing.dll')",
    "/reference:$(Join-Path $frameworkRoot 'System.Windows.Forms.dll')",
    "/reference:$(Join-Path $frameworkRoot 'System.Net.Http.dll')",
    "/reference:$(Join-Path $frameworkRoot 'System.Xml.dll')",
    "/reference:$(Join-Path $frameworkRoot 'System.Xaml.dll')",
    "/reference:$(Join-Path $frameworkRoot 'System.Xml.Linq.dll')",
    "/reference:$(Join-Path $frameworkRoot 'WindowsBase.dll')",
    "/reference:$(Join-Path $frameworkRoot 'PresentationCore.dll')",
    "/reference:$(Join-Path $frameworkRoot 'PresentationFramework.dll')",
    "/reference:$webViewCore",
    "/reference:$webViewWpf"
) + $sources

Write-Host 'Compiling DeepSeek Harness desktop shell...'
& $compiler $compilerArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Copy-Item -LiteralPath $webViewCore -Destination $publishRoot
Copy-Item -LiteralPath $webViewWpf -Destination $publishRoot
Copy-Item -LiteralPath $webViewLoader -Destination $publishRoot
Copy-Item -LiteralPath $webViewLicense -Destination (Join-Path $publishRoot 'Microsoft.Web.WebView2.LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $desktopRoot 'runtime\dsh-desktop-bootstrap.mjs') -Destination $publishRoot
Copy-Item -LiteralPath (Join-Path $desktopRoot 'assets\favicon.svg') -Destination $publishRoot
Copy-Item -LiteralPath $icon -Destination $publishRoot

$requiredPublishedFiles = @(
    'DeepSeek Harness.exe',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.Wpf.dll',
    'WebView2Loader.dll',
    'dsh-desktop-bootstrap.mjs',
    'Microsoft.Web.WebView2.LICENSE.txt',
    'favicon.svg',
    'dsh-whale.ico'
)
foreach ($fileName in $requiredPublishedFiles) {
    $publishedPath = Join-Path $publishRoot $fileName
    if (-not (Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
        throw "Published runtime file is missing: $publishedPath"
    }
}

Write-Host "Published: $executable"
Write-Host "Verified $($requiredPublishedFiles.Count) required publish files."
