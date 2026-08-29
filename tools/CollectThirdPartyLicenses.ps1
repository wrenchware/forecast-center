param(
    [Parameter(Mandatory = $true)] [string] $AssetsFile,
    [Parameter(Mandatory = $true)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$assetsPath = (Resolve-Path -LiteralPath $AssetsFile).Path
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
if (-not $packageRoot) { throw 'The NuGet assets file has no package folder.' }

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$inventory = foreach ($library in $assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | Sort-Object Name) {
    $parts = $library.Name -split '/', 2
    $id = $parts[0]
    $version = $parts[1]
    $packagePath = Join-Path $packageRoot $library.Value.path
    $nuspecPath = Join-Path $packagePath ($id.ToLowerInvariant() + '.nuspec')
    $license = 'See bundled package files or package metadata.'
    $projectUrl = ''
    if (Test-Path -LiteralPath $nuspecPath) {
        [xml] $nuspec = Get-Content -LiteralPath $nuspecPath
        $metadata = $nuspec.package.metadata
        if ($metadata.license) {
            $license = $metadata.license.InnerText
            if (-not $license) { $license = [string] $metadata.license }
        } elseif ($metadata.licenseUrl) { $license = [string] $metadata.licenseUrl }
        $projectUrl = [string] $metadata.projectUrl
    }

    $safeName = ($id + '-' + $version) -replace '[^A-Za-z0-9._-]', '_'
    $licenseFiles = Get-ChildItem -LiteralPath $packagePath -File -Recurse |
        Where-Object { $_.Name -match '^(license|licence|notice|copying|thirdpartynotices)(\..*)?$' } |
        Sort-Object FullName -Unique
    $copied = @()
    $index = 0
    foreach ($file in $licenseFiles) {
        $index++
        $destinationName = $safeName + '-' + $index + '-' + $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputDirectory $destinationName) -Force
        $copied += $destinationName
    }

    [pscustomobject]@{
        Package = $id
        Version = $version
        License = $license
        ProjectUrl = $projectUrl
        BundledFiles = $copied -join '; '
    }
}

$inventory | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'NuGet-Package-Inventory.csv') -NoTypeInformation -Encoding utf8
Write-Output ("Collected notices for {0} resolved NuGet packages." -f $inventory.Count)
