param([string]$Output = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$wpf = Join-Path $framework 'WPF'
if (!(Test-Path -LiteralPath $compiler)) { throw 'This build requires the Windows .NET Framework C# compiler.' }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Net.Http.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/codepage:65001','/langversion:5',('/out:' + (Join-Path $Output 'GoggleCast.exe')),('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest'))) + $references + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\MainWindow.xaml') -Destination (Join-Path $Output 'MainWindow.xaml') -Force
foreach ($name in @('README.txt','THIRD-PARTY-NOTICES.txt','LICENSE')) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $name)) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $Output -Force }
}
Get-Item -LiteralPath (Join-Path $Output 'GoggleCast.exe') | Select-Object FullName,Length,LastWriteTime
