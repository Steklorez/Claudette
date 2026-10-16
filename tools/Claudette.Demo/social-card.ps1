<#
.SYNOPSIS
Draws the site's social card: the picture a link to claudette.dotbunny.com shows where it's shared.

.DESCRIPTION
GUIDE.md ("The site") says what it is. social-card.html lays it out at 1280 by 640, with the README's main window
(docs/screenshots/standard-dark.png) in it, and a headless Edge or Chrome draws it to docs/assets/social-card.png,
which docs/_config.yml gives every page as its image. screenshots.ps1 runs this last, so the card keeps the window's
latest picture; run it by itself after changing social-card.html.

.EXAMPLE
tools\Claudette.Demo\social-card.ps1
#>
param(
    [string] $Out = (Join-Path $PSScriptRoot '..\..\docs\assets\social-card.png')
)
$ErrorActionPreference = 'Stop'

$browser = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'
    '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge'
    (Get-Command google-chrome, chromium, microsoft-edge -ErrorAction SilentlyContinue | Select-Object -First 1).Source
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $browser) { throw 'Drawing the social card needs Microsoft Edge or Google Chrome.' }

$card = [Uri]::new((Resolve-Path (Join-Path $PSScriptRoot 'social-card.html')).Path).AbsoluteUri
$Out = [IO.Path]::GetFullPath($Out)
# A profile of its own, so the browser doesn't hand the page to one already open.
$browserProfile = Join-Path ([IO.Path]::GetTempPath()) "claudette-social-card-$PID"
Remove-Item $Out -ErrorAction SilentlyContinue
try {
    # Start-Process, since on Windows the browser is a windowed program, which calling it alone wouldn't wait for.
    Start-Process $browser -Wait -ArgumentList ('--headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 ' +
        "--window-size=1280,640 `"--user-data-dir=$browserProfile`" `"--screenshot=$Out`" $card")
}
finally {
    Remove-Item $browserProfile -Recurse -Force -ErrorAction SilentlyContinue
}
if (-not (Test-Path $Out)) { throw "The browser didn't draw the social card ($browser)." }

# A PNG's width and height are the first two numbers of its header, big-endian, 16 bytes in.
$header = [IO.File]::ReadAllBytes($Out)[16..23]
$width = ([int] $header[0] -shl 24) -bor ([int] $header[1] -shl 16) -bor ([int] $header[2] -shl 8) -bor $header[3]
$height = ([int] $header[4] -shl 24) -bor ([int] $header[5] -shl 16) -bor ([int] $header[6] -shl 8) -bor $header[7]
if ($width -ne 1280 -or $height -ne 640) { throw "The social card came out $width by $height rather than 1280 by 640." }
Write-Host "Saved the social card to $Out"
