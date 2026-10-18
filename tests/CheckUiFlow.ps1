param(
    [string]$AppDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'),
    [string]$Output = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.review\gogglecast\ui-flow')
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
$wpf = Join-Path $framework 'WPF'
New-Item -ItemType Directory -Path $Output -Force | Out-Null
foreach ($name in @('GoggleCast.exe', 'MainWindow.xaml')) {
    Copy-Item -LiteralPath (Join-Path $AppDirectory $name) -Destination (Join-Path $Output $name) -Force
}
Get-FileHash -LiteralPath (Join-Path $Output 'GoggleCast.exe'), (Join-Path $Output 'MainWindow.xaml') |
    Format-List Algorithm, Hash, Path | Out-String | Set-Content -LiteralPath (Join-Path $Output 'tested-build.txt') -Encoding UTF8
$references = @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
$references += '/reference:' + (Join-Path $Output 'GoggleCast.exe')
$executable = Join-Path $Output 'UiFlowCheck.exe'
& $compiler /nologo /langversion:5 /platform:x64 /target:exe /utf8output "/out:$executable" @references (Join-Path $PSScriptRoot 'CheckUiFlow.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI flow harness compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw "UI flow failed; inspect $Output\ui-flow-results.txt" }
Write-Output ("Evidence: " + (Join-Path $Output 'ui-flow-results.txt'))
