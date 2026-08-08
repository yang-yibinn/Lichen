param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$releaseLabel = '0.8.1'
$releaseVersion = '0.8.1.0'
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$rhino = 'C:\Program Files\Rhino 8\System\RhinoCommon.dll'
$grasshopper = 'C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll'
$ghio = 'C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll'
$yak = 'C:\Program Files\Rhino 8\System\Yak.exe'
$pluginIcon = Join-Path $workspace 'src\Lichen.Plugin\Assets\lichen-icon-24.png'
$pluginIconResourceName = 'Lichen.Plugin.Assets.lichen-icon-24.png'
$selectChainIcon = Join-Path $workspace 'src\Lichen.Plugin\Assets\lichen-select-chain.svg'
$selectChainIconResourceName = 'Lichen.Plugin.Assets.lichen-select-chain.svg'
$yakTemplate = Join-Path $workspace 'packaging\yak'
$yakManifest = Join-Path $yakTemplate 'manifest.yml'
$yakReadme = Join-Path $yakTemplate 'README.md'
$yakIcon = Join-Path $workspace 'assets\branding\lichen-icon-transparent-256.png'
$artifacts = Join-Path $workspace 'artifacts'
$output = Join-Path $artifacts 'bin'
$package = Join-Path $artifacts 'package\Lichen'
$yakPackage = Join-Path $artifacts 'yak\Lichen'
$archive = Join-Path $artifacts ('Lichen-' + $releaseLabel + '.zip')
$checksum = Join-Path $artifacts ('Lichen-' + $releaseLabel + '.sha256')

foreach ($required in @($compiler, $rhino, $grasshopper, $ghio, $yak, $pluginIcon, $selectChainIcon, $yakManifest, $yakReadme, $yakIcon)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required build dependency not found: $required" }
}

Add-Type -AssemblyName System.Drawing
$iconBitmap = [System.Drawing.Bitmap]::FromFile($pluginIcon)
try {
    if ($iconBitmap.Width -ne 24 -or $iconBitmap.Height -ne 24) {
        throw "Grasshopper assembly icon must be exactly 24 by 24 pixels."
    }
    if ($iconBitmap.GetPixel(0, 0).A -ne 0) {
        throw "Grasshopper assembly icon must retain a transparent background."
    }
}
finally {
    $iconBitmap.Dispose()
}

[xml]$selectChainSvg = Get-Content -Raw -LiteralPath $selectChainIcon
if ($selectChainSvg.DocumentElement.LocalName -ne 'svg' -or $selectChainSvg.DocumentElement.viewBox -ne '0 0 24 24') {
    throw "Select chain icon must be a valid SVG with a 0 0 24 24 viewBox."
}

function Assert-ArtifactPath([string]$Path) {
    $root = [System.IO.Path]::GetFullPath($artifacts).TrimEnd('\') + '\'
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to alter a path outside the artifacts directory: $resolved"
    }
}

function Reset-BuildDirectory([string]$Path) {
    Assert-ArtifactPath $Path
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Remove-GeneratedFile([string]$Path) {
    Assert-ArtifactPath $Path
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
}

Reset-BuildDirectory $output
Reset-BuildDirectory $package
Reset-BuildDirectory $yakPackage
Remove-GeneratedFile $archive
Remove-GeneratedFile $checksum

function Invoke-Compiler([string[]]$CompilerArguments) {
    & $compiler $CompilerArguments
    if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
}

$compilerDefaults = @('/nologo', '/warn:4', '/warnaserror+', '/optimize+')
$coreSources = Get-ChildItem -LiteralPath (Join-Path $workspace 'src\Lichen.Core') -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName }
Invoke-Compiler ($compilerDefaults + @('/target:library', ('/out:' + (Join-Path $output 'Lichen.Core.dll')), '/r:System.Runtime.Serialization.dll') + $coreSources)

$adapterSources = Get-ChildItem -LiteralPath (Join-Path $workspace 'src\Lichen.Adapters') -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName }
Invoke-Compiler ($compilerDefaults + @('/target:library', ('/out:' + (Join-Path $output 'Lichen.Adapters.dll')), ('/r:' + (Join-Path $output 'Lichen.Core.dll')), ('/r:' + $rhino), ('/r:' + $grasshopper), ('/r:' + $ghio), '/r:System.Drawing.dll') + $adapterSources)

$pluginSources = Get-ChildItem -LiteralPath (Join-Path $workspace 'src\Lichen.Plugin') -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName }
Invoke-Compiler ($compilerDefaults + @('/target:library', ('/out:' + (Join-Path $output 'Lichen.dll')), ('/resource:' + $pluginIcon + ',' + $pluginIconResourceName), ('/resource:' + $selectChainIcon + ',' + $selectChainIconResourceName), ('/r:' + (Join-Path $output 'Lichen.Core.dll')), ('/r:' + (Join-Path $output 'Lichen.Adapters.dll')), ('/r:' + $rhino), ('/r:' + $grasshopper), ('/r:' + $ghio), '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll') + $pluginSources)

$pluginAssembly = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $output 'Lichen.dll'))
if (-not ($pluginAssembly.GetManifestResourceNames() -contains $pluginIconResourceName)) {
    throw "Compiled plugin is missing its embedded Grasshopper icon."
}
if (-not ($pluginAssembly.GetManifestResourceNames() -contains $selectChainIconResourceName)) {
    throw "Compiled plugin is missing its embedded Select chain icon."
}
Copy-Item -LiteralPath (Join-Path $output 'Lichen.dll') -Destination (Join-Path $output 'Lichen.gha') -Force

$testSources = Get-ChildItem -LiteralPath (Join-Path $workspace 'tests\Lichen.Tests') -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName }
Invoke-Compiler ($compilerDefaults + @('/target:exe', ('/out:' + (Join-Path $output 'Lichen.Tests.exe')), ('/r:' + (Join-Path $output 'Lichen.Core.dll')), '/r:System.Runtime.Serialization.dll') + $testSources)

if (-not $SkipTests) {
    & (Join-Path $output 'Lichen.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw "Automated tests failed with exit code $LASTEXITCODE." }
}

Copy-Item -LiteralPath (Join-Path $output 'Lichen.gha') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $output 'Lichen.Core.dll') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $output 'Lichen.Adapters.dll') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE') -Destination (Join-Path $package 'LICENSE.txt') -Force

$expectedPackageFiles = @('LICENSE.txt', 'Lichen.Adapters.dll', 'Lichen.Core.dll', 'Lichen.gha')
$actualPackageFiles = Get-ChildItem -LiteralPath $package -File | Sort-Object Name | ForEach-Object { $_.Name }
if ((Compare-Object $expectedPackageFiles $actualPackageFiles).Count -ne 0) {
    throw "Release package contents differ from the expected plugin files and license notice."
}

foreach ($assemblyFile in @('Lichen.Adapters.dll', 'Lichen.Core.dll', 'Lichen.gha')) {
    $assemblyPath = Join-Path $package $assemblyFile
    $actualVersion = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
    if ($actualVersion -ne $releaseVersion) { throw "$assemblyFile has version $actualVersion; expected $releaseVersion." }
}

Compress-Archive -LiteralPath $package -DestinationPath $archive -Force
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksum -Value ($archiveHash + '  ' + [System.IO.Path]::GetFileName($archive)) -Encoding ASCII

$manifestVersion = Select-String -LiteralPath $yakManifest -Pattern '^version:\s*([^\s]+)\s*$'
if ($manifestVersion.Matches.Count -ne 1 -or $manifestVersion.Matches[0].Groups[1].Value -ne $releaseLabel) {
    throw "Yak manifest version must match release label $releaseLabel."
}

foreach ($runtimeFile in @('Lichen.gha', 'Lichen.Core.dll', 'Lichen.Adapters.dll')) {
    Copy-Item -LiteralPath (Join-Path $package $runtimeFile) -Destination $yakPackage -Force
}
Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE') -Destination (Join-Path $yakPackage 'LICENSE.txt') -Force
Copy-Item -LiteralPath $yakManifest -Destination (Join-Path $yakPackage 'manifest.yml') -Force
Copy-Item -LiteralPath $yakReadme -Destination (Join-Path $yakPackage 'README.md') -Force
Copy-Item -LiteralPath $yakIcon -Destination (Join-Path $yakPackage 'icon.png') -Force

$expectedYakFiles = @('LICENSE.txt', 'Lichen.Adapters.dll', 'Lichen.Core.dll', 'Lichen.gha', 'README.md', 'icon.png', 'manifest.yml')
$actualYakFiles = Get-ChildItem -LiteralPath $yakPackage -File | Sort-Object Name | ForEach-Object { $_.Name }
if ((Compare-Object $expectedYakFiles $actualYakFiles).Count -ne 0) {
    throw "Yak staging contents differ from the expected package files."
}

Push-Location $yakPackage
try {
    & $yak build --platform win
    if ($LASTEXITCODE -ne 0) { throw "Yak build failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }

$yakArtifacts = @(Get-ChildItem -LiteralPath $yakPackage -Filter '*.yak' -File)
if ($yakArtifacts.Count -ne 1) { throw "Expected one generated Yak package; found $($yakArtifacts.Count)." }
$yakArchive = Join-Path $artifacts $yakArtifacts[0].Name
$yakChecksum = $yakArchive + '.sha256'
Remove-GeneratedFile $yakArchive
Remove-GeneratedFile $yakChecksum
Move-Item -LiteralPath $yakArtifacts[0].FullName -Destination $yakArchive
$yakHash = (Get-FileHash -LiteralPath $yakArchive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $yakChecksum -Value ($yakHash + '  ' + [System.IO.Path]::GetFileName($yakArchive)) -Encoding ASCII

Write-Host "Lichen build completed."
Write-Host "Installable folder: $package"
Write-Host "Release archive: $archive"
Write-Host "SHA-256: $checksum"
Write-Host "Package Manager archive: $yakArchive"
Write-Host "Package Manager SHA-256: $yakChecksum"
