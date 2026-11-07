param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameDir 'RimWorldWin64_Data\Managed'
$compiler = 'C:\Program Files\dotnet\sdk\10.0.303\Roslyn\bincore\csc.dll'
$outDir = Join-Path $PSScriptRoot 'Assemblies'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$references = @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll','System.Runtime.Serialization.dll','netstandard.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
$references += '/r:C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.6\Assemblies\0Harmony.dll'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Source') -Filter '*.cs' | ForEach-Object FullName
& dotnet $compiler /nologo /target:library /optimize+ /nostdlib+ /langversion:latest ('/out:' + (Join-Path $outDir 'SimplePawnDiary.dll')) $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output (Join-Path $outDir 'SimplePawnDiary.dll')
