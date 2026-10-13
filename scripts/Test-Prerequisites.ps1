param([switch]$Build)
$ErrorActionPreference = 'Stop'
$taskDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$taskSdk = if ($taskDotnet) { (& dotnet --list-sdks) -match '^10\.' } else { @() }
$taskTailscale = Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'
$taskInno = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Inno Setup 6\ISCC.exe'
[pscustomobject]@{
    Windows64Bits = [Environment]::Is64BitOperatingSystem
    DotNet10SDK = [bool]$taskSdk
    Tailscale = Test-Path -LiteralPath $taskTailscale
    InnoSetup = Test-Path -LiteralPath $taskInno
    Administrador = ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
if ($Build -and -not $taskSdk) {
    throw '.NET 10 SDK ausente. Baixe em https://dotnet.microsoft.com/pt-br/download/dotnet/10.0'
}
if ($Build -and -not (Test-Path -LiteralPath $taskInno)) {
    Write-Warning 'Inno Setup ausente. Baixe em https://jrsoftware.org/isdl.php'
}
if (-not (Test-Path -LiteralPath $taskTailscale)) {
    Write-Host 'Para acesso pela internet: https://tailscale.com/download/windows. Na rede local, Tailscale não é necessário.'
}