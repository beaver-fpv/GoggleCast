param(
    [string]$Version = '0.1.0',
    [string]$AppDirectory = (Join-Path $PSScriptRoot 'dist'),
    [string]$Output = (Join-Path $PSScriptRoot 'artifacts'),
    [string]$Iscc = '',
    [string]$RuntimeArchive = '',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+([.-][A-Za-z0-9.-]+)?$') { throw 'Version must be a safe semantic version such as 0.1.0.' }
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Output $AppDirectory }
$AppDirectory = (Resolve-Path -LiteralPath $AppDirectory).Path
foreach ($name in @('GoggleCast.exe', 'MainWindow.xaml', 'README.txt', 'THIRD-PARTY-NOTICES.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $AppDirectory $name) -PathType Leaf)) { throw ('Missing application file: ' + $name) }
}
if (!$Iscc) {
    $candidates = @()
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }
    if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
    $Iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$Iscc -or !(Test-Path -LiteralPath $Iscc -PathType Leaf)) { throw 'Inno Setup 6.7 or newer is required. Pass -Iscc with the full path to ISCC.exe.' }
$Iscc = (Resolve-Path -LiteralPath $Iscc).Path
# ISCC.exe itself can have file version 0.0.0.0. The .iss preprocessor checks VER.

$metadataPath = Join-Path $PSScriptRoot 'installer\runtime.json'
$runtime = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($runtime.archiveSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'A complete pinned SHA-256 is required for the runtime archive.' }
foreach ($uri in @($runtime.url, $runtime.mirrorUrl)) {
    if (![Uri]::IsWellFormedUriString($uri, [UriKind]::Absolute) -or ([Uri]$uri).Scheme -ne 'https') { throw 'Runtime download URLs must use HTTPS.' }
    if ($uri -match '(?i)(latest|nightly)') { throw 'Use an immutable, versioned runtime URL.' }
}
foreach ($relative in @($runtime.archiveRoot, $runtime.executablePath, $runtime.licensePath, $runtime.readmePath)) {
    if (!$relative -or [IO.Path]::IsPathRooted($relative) -or ($relative -split '[/\\]') -contains '..') { throw 'Runtime archive paths must stay inside the archive root.' }
}
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$Output = (Resolve-Path -LiteralPath $Output).Path
if (!$RuntimeArchive -or !(Test-Path -LiteralPath $RuntimeArchive -PathType Leaf)) { throw 'Pass -RuntimeArchive with the pinned FFmpeg archive. This build script does not download dependencies.' }
$RuntimeArchive = (Resolve-Path -LiteralPath $RuntimeArchive).Path
if ((Get-FileHash -LiteralPath $RuntimeArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $runtime.archiveSha256.ToLowerInvariant()) {
    throw 'The FFmpeg archive does not match the pinned SHA-256 in installer/runtime.json.'
}
$tar = Join-Path $env:WINDIR 'System32\tar.exe'
if (!(Test-Path -LiteralPath $tar)) { throw 'The Windows tar.exe archive tool is required to prepare the offline installer.' }
$stage = Join-Path $Output ('.installer-staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    $entries = @($runtime.executablePath, $runtime.licensePath, $runtime.readmePath) | ForEach-Object { $runtime.archiveRoot + '/' + $_ }
    & $tar -xf $RuntimeArchive -C $stage @entries
    if ($LASTEXITCODE -ne 0) { throw 'The Windows archive tool could not extract the pinned FFmpeg archive.' }
    $runtimeSource = Join-Path $stage $runtime.archiveRoot
    foreach ($relative in @($runtime.executablePath, $runtime.licensePath, $runtime.readmePath)) {
        if (!(Test-Path -LiteralPath (Join-Path $runtimeSource $relative) -PathType Leaf)) { throw ('Missing required FFmpeg archive entry: ' + $relative) }
    }
$arguments = @(
    ('/DAppVersion=' + $Version),
    ('/DAppSource=' + $AppDirectory),
    ('/DInstallerOutput=' + $Output),
    ('/DRuntimeMetadata=' + $metadataPath),
    ('/DRuntimeSource=' + $runtimeSource),
    ('/DFFmpegVersion=' + $runtime.version),
    ('/DFFmpegUrl=' + $runtime.url),
    ('/DFFmpegMirrorUrl=' + $runtime.mirrorUrl),
    ('/DFFmpegArchive=' + $runtime.archiveName),
    ('/DFFmpegSha256=' + $runtime.archiveSha256),
    ('/DFFmpegRoot=' + $runtime.archiveRoot),
    ('/DFFmpegExecutable=' + ($runtime.executablePath -replace '/', '\')),
    ('/DFFmpegLicense=' + ($runtime.licensePath -replace '/', '\')),
    ('/DFFmpegReadme=' + ($runtime.readmePath -replace '/', '\'))
)
foreach ($argument in $arguments) {
    if ($argument -match '["\r\n]') { throw 'Installer definitions cannot contain quotes or newlines.' }
}
$arguments += Join-Path $PSScriptRoot 'installer\GoggleCast.iss'
& $Iscc @arguments
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$setup = Join-Path $Output ('GoggleCast-' + $Version + '-Setup.exe')
if (!(Test-Path -LiteralPath $setup)) { throw 'The expected installer was not produced.' }
$hash = Get-FileHash -LiteralPath $setup -Algorithm SHA256
($hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($setup)) |
    Set-Content -LiteralPath ($setup + '.sha256') -Encoding ASCII
Get-Item -LiteralPath $setup | Select-Object FullName, Length, LastWriteTime
Write-Output ('SHA256: ' + $hash.Hash.ToLowerInvariant())
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $expectedPrefix = [IO.Path]::GetFullPath($Output).TrimEnd('\') + '\.installer-staging-'
    if (!$resolvedStage.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean an installer staging directory outside the selected output directory.' }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}
