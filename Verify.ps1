$ErrorActionPreference = 'Stop'
$testDir = Join-Path $PSScriptRoot 'Tests'
$compiler = 'C:\Program Files\dotnet\sdk\10.0.303\Roslyn\bincore\csc.dll'
$references = Get-ChildItem -LiteralPath 'C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.11\ref\net10.0' -Filter '*.dll' | ForEach-Object { '/r:' + $_.FullName }
$output = Join-Path $testDir 'Verify.dll'
& dotnet $compiler /nologo /target:exe /nowarn:SYSLIB0014 ('/out:' + $output) $references (Join-Path $testDir 'Verify.cs') (Join-Path $PSScriptRoot 'Source\DiaryPolicy.cs') (Join-Path $PSScriptRoot 'Source\ApiClient.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
'{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.11"}}}' | Set-Content -LiteralPath (Join-Path $testDir 'Verify.runtimeconfig.json') -Encoding utf8
& dotnet $output 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll' (Join-Path $PSScriptRoot 'Assemblies\SimplePawnDiary.dll') | Tee-Object -FilePath (Join-Path $testDir 'Verification.txt')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
foreach ($path in @('About\About.xml','Defs\MainButton.xml')) { [xml]$xml = Get-Content -LiteralPath (Join-Path $PSScriptRoot $path) -Raw }
Write-Output 'Mod XML parsed successfully.'
