param(
    [switch]$Desktop,
    [ValidateSet('ts','mp4')][string]$Container = 'ts',
    [ValidateSet('auto','cpu')][string]$Encoder = 'auto',
    [string]$Output = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.review\gogglecast\desktop-audio-repeatable')
)
# The default test reads the default playback device into memory and discards it.
# -Desktop additionally captures the top-left 320x180 screen area for four seconds.
# Neither test records audio/video files, plays sounds, or contacts a receiver.
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$executable = Join-Path $Output 'DesktopAudioAcceptance.exe'
& $compiler /nologo /langversion:5 /platform:x64 /target:exe /utf8output "/out:$executable" "/reference:$(Join-Path $framework 'System.Web.Extensions.dll')" (Join-Path $project 'src\Media.cs') (Join-Path $project 'src\DesktopAudio.cs') (Join-Path $PSScriptRoot 'CheckDesktopAudio.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop audio acceptance compilation failed.' }
$mode = if ($Desktop) { 'desktop' } else { 'audio-only' }
& $executable $Output $mode $Container $Encoder
if ($LASTEXITCODE -ne 0) { throw "Desktop audio acceptance failed. See $Output\acceptance-results.txt" }
