param([switch]$Offline)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
[xml]$config = Get-Content -Raw -LiteralPath (Join-Path $workspace 'build\RhinoSdk.props')
$settings = $config.Project.PropertyGroup
$version = [string]$settings.RhinoSdkVersion
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid pinned Rhino SDK version.' }
$sdkRoot = Join-Path $workspace ('.build\rhino-sdk\' + $version)
$packages = @(
    @{ Id = 'rhinocommon'; Hash = [string]$settings.RhinoCommonPackageSha256; Files = @('RhinoCommon.dll', 'RhinoCommon.xml') },
    @{ Id = 'grasshopper'; Hash = [string]$settings.GrasshopperPackageSha256; Files = @('Grasshopper.dll', 'Grasshopper.xml', 'GH_IO.dll', 'GH_IO.xml') }
)
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    if ($package.Hash -notmatch '^[0-9a-f]{64}$') { throw ('Invalid SDK package hash: ' + $package.Id) }
    $archive = Join-Path $sdkRoot ($package.Id + '.' + $version + '.nupkg')
    if (-not (Test-Path -LiteralPath $archive)) {
        if ($Offline) { throw "Pinned SDK package is unavailable offline: $archive" }
        New-Item -ItemType Directory -Force -Path $sdkRoot | Out-Null
        $download = $archive + '.' + [Guid]::NewGuid().ToString('N') + '.download'
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            $url = 'https://api.nuget.org/v3-flatcontainer/' + $package.Id + '/' + $version + '/' + $package.Id + '.' + $version + '.nupkg'
            Write-Host ('Downloading build reference package: ' + $package.Id + ' ' + $version)
            Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $download -TimeoutSec 60
            if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $package.Hash) { throw ('SDK package checksum mismatch: ' + $package.Id) }
            Move-Item -LiteralPath $download -Destination $archive
        }
        finally { if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force } }
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $package.Hash) {
        throw "Cached SDK package checksum mismatch: $archive. Restore a verified package before building."
    }
    $destination = Join-Path $sdkRoot $package.Id
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($file in $package.Files) {
            $entry = $zip.GetEntry('lib/net48/' + $file)
            if ($null -eq $entry) { throw ('Missing SDK reference entry: ' + $file) }
            # Re-extract only named reference files from the verified archive; never ship host DLLs.
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $file), $true)
        }
    }
    finally { $zip.Dispose() }
}
foreach ($file in @('rhinocommon\RhinoCommon.dll', 'grasshopper\Grasshopper.dll', 'grasshopper\GH_IO.dll')) {
    if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $sdkRoot $file)).Version.ToString() -ne $version) {
        throw ('SDK assembly version mismatch: ' + $file)
    }
}
Write-Host ('Build SDK: Rhino/Grasshopper ' + $version + ' (pinned; net48)')
[pscustomobject]@{
    Version = $version
    RhinoCommon = Join-Path $sdkRoot 'rhinocommon\RhinoCommon.dll'
    Grasshopper = Join-Path $sdkRoot 'grasshopper\Grasshopper.dll'
    GhIo = Join-Path $sdkRoot 'grasshopper\GH_IO.dll'
}
