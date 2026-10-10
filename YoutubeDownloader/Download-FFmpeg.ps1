param(
    [string]$Platform,
    [string]$OutputPath = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
if (-not $Platform) {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    if ($IsWindows) { $Platform = "windows-$architecture" }
    elseif ($IsLinux) { $Platform = "linux-$architecture" }
    elseif ($IsMacOS) { $Platform = "osx-$architecture" }
    else { throw 'Unsupported platform' }
}
$Platform = $Platform.ToLowerInvariant().Replace('win-', 'windows-')
if ($Platform -notmatch '^(windows|linux|osx)-(x64|x86|arm64|arm)$') { throw 'Unsupported platform identifier' }
$fileName = if ($Platform.StartsWith('windows-')) { 'ffmpeg.exe' } else { 'ffmpeg' }
if (Test-Path -LiteralPath $OutputPath -PathType Container) { $OutputPath = Join-Path $OutputPath $fileName }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$parentDirectory = [IO.Path]::GetDirectoryName($OutputPath)
[IO.Directory]::CreateDirectory($parentDirectory) | Out-Null
$archivePath = $OutputPath + '.' + [guid]::NewGuid().ToString('N') + '.zip'
$temporaryPath = $OutputPath + '.' + [guid]::NewGuid().ToString('N') + '.new'
Write-Output "Downloading FFmpeg for $Platform..."
try {
    Invoke-WebRequest -Uri "https://github.com/Tyrrrz/FFmpegBin/releases/download/7.1.2/ffmpeg-$Platform.zip" -OutFile $archivePath
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entry = $archive.GetEntry($fileName)
        if ($null -eq $entry -or $entry.Length -eq 0) { throw 'FFmpeg archive is invalid' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $temporaryPath)
    } finally { $archive.Dispose() }
    if (-not $IsWindows) {
        & chmod +x -- $temporaryPath
        if ($LASTEXITCODE -ne 0) { throw 'Could not set FFmpeg executable permissions' }
    }
    [IO.File]::Move($temporaryPath, $OutputPath, $true)
    Write-Output 'FFmpeg downloaded successfully.'
} finally {
    foreach ($temporaryFile in @($archivePath, $temporaryPath)) {
        if (Test-Path -LiteralPath $temporaryFile) { Remove-Item -LiteralPath $temporaryFile -Force }
    }
}
