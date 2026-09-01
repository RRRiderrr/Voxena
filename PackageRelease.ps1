param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SkipBuild) {
    & (Join-Path $root 'BuildRelease.bat')
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}
$src = Join-Path $root 'Voxena\bin\Release'
$dist = Join-Path $root 'dist'
$out = Join-Path $dist 'Voxena-win-x64'
$zip = Join-Path $dist 'Voxena-win-x64.zip'
if (-not (Test-Path (Join-Path $src 'Voxena.exe'))) { throw 'Voxena.exe is missing from bin\Release.' }
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null
Get-ChildItem $src -Force | Where-Object { $_.Name -notmatch '\.(pdb|xml)$' -and $_.Name -notin @('Logs','Cache','Output','Config','Voices','Models') } | ForEach-Object {
    Copy-Item $_.FullName -Destination $out -Recurse -Force
}
$required = @(
    'Voxena.exe',
    'Voxena.exe.config',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.WinForms.dll',
    'Web\index.html',
    'Web\app.js',
    'Runtime\Scripts\engine_host.py',
    'Runtime\Scripts\stress_gemma4.py'
)
foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $out $r))) { throw "Release is incomplete: $r" }
}
$loaderCandidates = @(
    'WebView2Loader.dll',
    'runtimes\win-x64\native\WebView2Loader.dll'
)
$hasLoader = $false
foreach ($candidate in $loaderCandidates) {
    if (Test-Path (Join-Path $out $candidate)) { $hasLoader = $true; break }
}
if (-not $hasLoader) { throw 'Release is incomplete: WebView2Loader.dll was not copied by the WebView2 SDK.' }
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path ($zip + '.sha256') -Value "$hash  Voxena-win-x64.zip" -Encoding Ascii
Write-Host "Release package: $zip"
Write-Host "SHA-256: $hash"
