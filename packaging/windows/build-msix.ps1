<#
.SYNOPSIS
  Builds Claudette as an MSIX package (DESIGN.md §2, "Packaging"), signed when a certificate is given.

.DESCRIPTION
  Publishes the app self-contained for one architecture, lays it out with the manifest and tile images, builds the
  resource index for the scaled icons and packs it with makeappx. The Windows SDK supplies makeappx, makepri and
  signtool (GitHub's Windows runners have it).

  Releases are signed afterwards, by the package workflow with Azure Artifact Signing. Windows only installs signed
  packages, so for a local test sign with a self-signed certificate whose subject matches -Publisher, given as
  -CertificatePath, and trust that certificate.

  With -StorePublisher, the same layout is packed a second time for the Microsoft Store, into a store folder in the
  output: the Store wants its own publisher in the manifest, and signs the package itself, so that one stays unsigned.

.EXAMPLE
  ./packaging/windows/build-msix.ps1 -Version 1.2.3 -Architecture x64
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [ValidateSet('x64', 'arm64')] [string] $Architecture = 'x64',
    # Must match the signing certificate's subject exactly. Releases use the Artifact Signing certificate profile's,
    # "CN=dotBunny, O=dotBunny, L=Peterborough, S=Ontario, C=CA", which the package workflow passes.
    [string] $Publisher = $(if ($env:MSIX_PUBLISHER) { $env:MSIX_PUBLISHER } else { 'CN=Claudette Development' }),
    # Partner Center's Package/Identity/Publisher for Claudette (Product management, Product identity).
    [string] $StorePublisher = $env:MSIX_STORE_PUBLISHER,
    [string] $CertificatePath = $env:WINDOWS_CERTIFICATE_PATH,
    [string] $CertificatePassword = $env:WINDOWS_CERTIFICATE_PASSWORD,
    [string] $TimestampUrl = 'http://timestamp.digicert.com',
    [string] $Output = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$work = Join-Path $root "obj/package/win-$Architecture"
$layout = Join-Path $work 'layout'
New-Item -ItemType Directory -Force -Path $work, (Join-Path $root $Output) | Out-Null
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }

# MSIX versions have four parts, the last one 0.
$parts = $Version.Split('-')[0].Split('.')
while ($parts.Count -lt 3) { $parts += '0' }
$packageVersion = ($parts[0..2] + '0') -join '.'

Write-Host "Publishing Claudette $Version for win-$Architecture"
dotnet publish (Join-Path $root 'src/Claudette.App/Claudette.App.csproj') -c Release -r "win-$Architecture" --self-contained `
    -p:Version=$Version -p:DebugType=portable -o $layout
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# The symbols stay out of the package, kept beside it to read crash reports' stack traces with.
$symbols = Join-Path $root "$Output/symbols-win-$Architecture"
if (Test-Path $symbols) { Remove-Item -Recurse -Force $symbols }
New-Item -ItemType Directory -Force -Path $symbols | Out-Null
Get-ChildItem $layout -Filter *.pdb | Move-Item -Destination $symbols -Force

# Into the app's own Assets folder, which the publish already made: copying the folder itself would nest it, as
# Assets\Assets, where Windows and the Store don't find the tiles.
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
Copy-Item -Recurse -Force (Join-Path $PSScriptRoot 'Assets/*') $assets
$manifest = Join-Path $layout 'AppxManifest.xml'
function Write-Manifest([string] $publisher) {
    (Get-Content -Raw -Encoding utf8 (Join-Path $PSScriptRoot 'Package.appxmanifest')) `
        -replace '\$VERSION\$', $packageVersion `
        -replace '\$PUBLISHER\$', [System.Security.SecurityElement]::Escape($publisher) `
        -replace '\$ARCHITECTURE\$', $Architecture |
        Set-Content -Encoding utf8 $manifest
}
Write-Manifest $Publisher

# Every image the manifest names must be where it says, or the Store refuses the package.
$missing = [regex]::Matches((Get-Content -Raw $manifest), '[">](Assets\\[^"<]+\.png)["<]') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique | Where-Object { -not (Test-Path (Join-Path $layout $_)) }
if ($missing) { throw "The manifest names images the package doesn't have: $($missing -join ', ')" }

function Find-SdkTool([string] $name) {
    $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Directory.Parent.Name) } | Select-Object -Last 1
    if (-not $tool) { throw "$name wasn't found. Install the Windows SDK." }
    return $tool.FullName
}

# The resource index lets the taskbar pick the scaled, unplated icons.
$makepri = Find-SdkTool 'makepri.exe'
$priConfig = Join-Path $work 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /o | Out-Host
& $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'makepri failed' }

$msix = Join-Path $root "$Output/Claudette-$Version-$Architecture.msix"
& (Find-SdkTool 'makeappx.exe') pack /d $layout /p $msix /o | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }

if ($CertificatePath) {
    Write-Host 'Signing'
    & (Find-SdkTool 'signtool.exe') sign /fd SHA256 /td SHA256 /tr $TimestampUrl /f $CertificatePath /p $CertificatePassword $msix | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }
}
else {
    Write-Warning 'No certificate: the package is unsigned, so Windows will not install it as is.'
}
Write-Host "Built $msix"

# The Store's copy: the same files, the Store's publisher. The resource index only names the package, which is the same.
if ($StorePublisher) {
    $store = Join-Path $root "$Output/store"
    New-Item -ItemType Directory -Force -Path $store | Out-Null
    Write-Manifest $StorePublisher
    $storeMsix = Join-Path $store "Claudette-$Version-$Architecture.msix"
    & (Find-SdkTool 'makeappx.exe') pack /d $layout /p $storeMsix /o | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'makeappx failed for the Store package' }
    Write-Host "Built $storeMsix for the Store"
}
