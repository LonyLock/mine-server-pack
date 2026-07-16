# Собирает MineServerPack-Setup.exe встроенным в Windows компилятором C#.
$root = Split-Path -Parent $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /optimize+ /target:exe `
    /out:"$root\MineServerPack-Setup.exe" `
    /r:System.Web.Extensions.dll /r:System.IO.Compression.dll `
    "$root\src\MineServerPackSetup.cs"
if ($LASTEXITCODE -eq 0) { Write-Host "OK: $root\MineServerPack-Setup.exe" }
