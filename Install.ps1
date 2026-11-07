param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$ConfigPath = 'C:\Users\Andrei\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml'
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name RimWorldWin64,RimWorld -ErrorAction SilentlyContinue) { throw 'Close RimWorld normally before installation. No files changed.' }
$packageId = 'andrei.simplepawndiary'
$destination = Join-Path $GameDir 'Mods\SimplePawnDiary'
$originalText = [IO.File]::ReadAllText($ConfigPath)
$originalHash = (Get-FileHash -LiteralPath $ConfigPath).Hash
[xml]$original = $originalText
$active = @($original.ModsConfigData.activeMods.li | ForEach-Object { [string]$_ })
if ($active -notcontains 'brrainz.harmony') { throw 'Harmony must be enabled.' }
$updatedText = $originalText
if ($active -notcontains $packageId) {
    $updatedText = [regex]::Replace($originalText, '(?m)^([\t ]*)</activeMods>', "    <li>$packageId</li>`r`n`$1</activeMods>")
    if ($updatedText -eq $originalText) { throw 'Cannot locate the active mod list.' }
}
[xml]$updated = $updatedText
$expected = @($updated.ModsConfigData.activeMods.li | ForEach-Object { [string]$_ })
if (@($expected | Where-Object { $_ -eq $packageId }).Count -ne 1) { throw 'Diary must be enabled exactly once.' }
if ((@($expected | Where-Object { $_ -ne $packageId }) -join "`n") -cne (@($active | Where-Object { $_ -ne $packageId }) -join "`n")) { throw 'Unexpected changes to other mods or load order.' }
if ([array]::IndexOf($expected,$packageId) -le [array]::IndexOf($expected,'brrainz.harmony')) { throw 'Diary must load after Harmony.' }
$parts = @('About','Defs','Assemblies')
foreach ($part in $parts) { if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $part))) { throw ('Missing build part: ' + $part) } }
foreach ($file in @('About\About.xml','Defs\MainButton.xml')) { [xml]$validated = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $file)) }
$backup = Join-Path $PSScriptRoot ('Backups\install-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $backup 'ModsConfig.xml')
if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination (Join-Path $backup 'PreviousMod') -Recurse }
$stagedConfig = Join-Path $backup 'ModsConfig.enabled.xml'
[IO.File]::WriteAllText($stagedConfig, $updatedText, [Text.UTF8Encoding]::new($false))
if (Get-Process -Name RimWorldWin64,RimWorld -ErrorAction SilentlyContinue) { throw 'RimWorld started during preparation.' }
if ((Get-FileHash -LiteralPath $ConfigPath).Hash -ne $originalHash) { throw 'Mod list changed during preparation.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$verified = @()
foreach ($part in $parts) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $part) -Destination $destination -Recurse -Force
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $part) -Recurse -File) {
        $relative = $file.FullName.Substring($PSScriptRoot.Length + 1)
        $hash = (Get-FileHash -LiteralPath $file.FullName).Hash
        if ((Get-FileHash -LiteralPath (Join-Path $destination $relative)).Hash -ne $hash) { throw ('Installed file differs: ' + $relative) }
        $verified += [pscustomobject]@{file=$relative;sha256=$hash}
    }
}
Copy-Item -LiteralPath $stagedConfig -Destination $ConfigPath -Force
if ((Get-FileHash -LiteralPath $ConfigPath).Hash -ne (Get-FileHash -LiteralPath $stagedConfig).Hash) {
    Copy-Item -LiteralPath (Join-Path $backup 'ModsConfig.xml') -Destination $ConfigPath -Force
    throw 'Config verification failed; original restored.'
}
$report = [ordered]@{packageId=$packageId;version='0.1.0';destination=$destination;backup=$backup;enabledExactlyOnce=$true;otherModsAndOrderPreserved=$true;files=$verified}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Installation.json') -Encoding utf8
$report | ConvertTo-Json -Depth 5
