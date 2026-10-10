param(
    [Parameter(Mandatory=$true)][string]$PublishDir,
    [ValidateSet('true','false')][string]$ExpectedFFmpeg = 'true'
)
$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $PublishDir).Path
$binaryRoot = $packageRoot
$macBinaryRoot = Join-Path $packageRoot 'YoutubeDownloader.app/Contents/MacOS'
if (Test-Path -LiteralPath $macBinaryRoot -PathType Container) { $binaryRoot = $macBinaryRoot }
foreach ($required in @('YoutubeDownloader.dll','YoutubeDownloader.runtimeconfig.json','License.txt','CHANGELOG.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $binaryRoot $required) -PathType Leaf)) { throw "Missing package file: $required" }
}
$runtime = Get-Content -LiteralPath (Join-Path $binaryRoot 'YoutubeDownloader.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks) { throw 'The package must be self-contained.' }
$ffmpegPresent = (Test-Path -LiteralPath (Join-Path $binaryRoot 'ffmpeg.exe')) -or (Test-Path -LiteralPath (Join-Path $binaryRoot 'ffmpeg'))
if ($ExpectedFFmpeg -eq 'true' -and -not $ffmpegPresent) { throw 'Bundled FFmpeg is missing.' }
if ($ExpectedFFmpeg -eq 'false' -and $ffmpegPresent) { throw 'A Bare package unexpectedly includes FFmpeg.' }
if (-not ((Test-Path -LiteralPath (Join-Path $binaryRoot 'YoutubeDownloader.exe')) -or (Test-Path -LiteralPath (Join-Path $binaryRoot 'YoutubeDownloader')))) { throw 'Application launcher is missing.' }
if ((Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object { $_.Name -in @('Settings.dat','auth.protected','queue.json','diagnostics.log') }).Count -gt 0) { throw 'Package contains private user data.' }
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $binaryRoot 'YoutubeDownloader.dll')).Version.ToString(3)
@{ version=$version; repository='MrHakan/YoutubeDownloader'; bundledFFmpeg=$ffmpegPresent } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'VERSION.json') -Encoding utf8
$hashes = foreach ($file in (Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Sort-Object FullName)) {
    if ($file.Name -eq 'SHA256SUMS.txt') { continue }
    $relative = [IO.Path]::GetRelativePath($packageRoot, $file.FullName).Replace('\','/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
$hashes | Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Encoding utf8
Write-Output "Verified package $version ($ExpectedFFmpeg FFmpeg): $packageRoot"
