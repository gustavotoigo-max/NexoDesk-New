$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
[xml]$taskProps = Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props')
$taskVersion = $taskProps.Project.PropertyGroup.Version
if (-not ('NexoDeskArtifactNative' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NexoDeskArtifactNative {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    public static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")]
    public static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    public static extern bool FreeLibrary(IntPtr module);
}
"@
}
foreach ($taskRelative in @('publish\app\NexoDesk New.exe','publish\service\NexoDesk.Service.exe')) {
    $taskExecutable = Join-Path $taskRoot $taskRelative
    if (-not (Test-Path -LiteralPath $taskExecutable)) { throw "Artefato ausente: $taskExecutable" }
    $taskModule = [NexoDeskArtifactNative]::LoadLibraryEx($taskExecutable,[IntPtr]::Zero,2)
    if ($taskModule -eq [IntPtr]::Zero) { throw "Falha ao ler os recursos de $taskExecutable" }
    try {
        $taskResource = [NexoDeskArtifactNative]::FindResource($taskModule,[IntPtr]1,[IntPtr]24)
        if ($taskResource -eq [IntPtr]::Zero) { throw "Manifesto ausente: $taskExecutable" }
        $taskSize = [NexoDeskArtifactNative]::SizeofResource($taskModule,$taskResource)
        $taskHandle = [NexoDeskArtifactNative]::LoadResource($taskModule,$taskResource)
        $taskPointer = [NexoDeskArtifactNative]::LockResource($taskHandle)
        $taskBytes = [byte[]]::new($taskSize)
        [Runtime.InteropServices.Marshal]::Copy($taskPointer,$taskBytes,0,$taskSize)
        $taskManifest = [Text.Encoding]::UTF8.GetString($taskBytes)
        if ($taskManifest -notmatch 'requestedExecutionLevel\s+level="requireAdministrator"') {
            throw "O executável não exige administrador: $taskExecutable"
        }
        Write-Host "Manifesto administrativo confirmado: $taskRelative"
    } finally { [NexoDeskArtifactNative]::FreeLibrary($taskModule) | Out-Null }
}
# O bootstrap do Inno usa asInvoker; a elevação ocorre por PrivilegesRequired.
# https://jrsoftware.org/is6help/topic_securitymeasures.htm
$taskInstallerSource = Get-Content -LiteralPath (Join-Path $taskRoot 'installer\NexoDesk.iss') -Raw
if ($taskInstallerSource -notmatch '(?m)^PrivilegesRequired=admin\s*$' -or
    $taskInstallerSource -match '(?m)^PrivilegesRequiredOverridesAllowed\s*=') {
    throw 'O instalador deve exigir administrador sem permitir redução de privilégios.'
}
if ($taskInstallerSource -notmatch '(?m)^Filename: "\{app\}\\\{#AppExe\}"; Description:.*Flags:.*postinstall.*runascurrentuser') {
    throw 'A primeira abertura pelo instalador deve herdar a elevação administrativa.'
}
if ($taskInstallerSource -match '(?im)taskkill.*?/IM') { throw 'Limpeza por nome global não é permitida: conferir caminho e PID.' }
Write-Host 'Instalador configurado para exigir elevação e abrir a interface elevada.'
foreach ($taskRole in @('app','service')) {
    foreach ($taskDependency in @('coreclr.dll','System.Private.CoreLib.dll','NexoDesk.Shared.dll','NexoDesk.Desktop.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $taskRoot "publish\$taskRole\$taskDependency"))) {
            throw "Publicação incompleta: $taskRole / $taskDependency"
        }
    }
}
Write-Host 'Runtime .NET incorporado nas duas publicações.'
foreach ($taskDependency in @('Vortice.Direct2D1.dll','Vortice.Direct3D11.dll','Vortice.Direct3D9.dll',
    'Vortice.DirectX.dll','Vortice.DXGI.dll','Vortice.Mathematics.dll','SharpGen.Runtime.dll',
    'SharpGen.Runtime.COM.dll','ThirdPartyNotices.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskRoot "publish/app/$taskDependency"))) {
        throw "Dependência do ajuste de imagem ausente: $taskDependency"
    }
}
Write-Host 'Dependências e licenças do ajuste de imagem incorporadas.'
$taskSetup = Join-Path $taskRoot "artifacts\NexoDesk-New-Setup-$taskVersion.exe"
$taskExpected = (Get-Content -LiteralPath "$taskSetup.sha256").Split(' ')[0]
$taskActual = (Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant()
if ($taskActual -ne $taskExpected) { throw 'SHA-256 do instalador diverge.' }
Write-Host 'SHA-256 do instalador confirmado.'