param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    & "$PSScriptRoot\Test-Prerequisites.ps1" -Build
    dotnet build NexoDesk.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
    if (-not $SkipTests) {
        dotnet test NexoDesk.sln -c Release --no-build --nologo --logger 'trx;LogFileName=resultados.trx'
        if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes.' }
    }
    dotnet publish src/NexoDesk.App/NexoDesk.App.csproj -c Release -r win-x64 --self-contained true -o publish/app --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação da interface.' }
    dotnet publish src/NexoDesk.Service/NexoDesk.Service.csproj -c Release -r win-x64 --self-contained true -o publish/service --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação do serviço.' }
    [xml]$taskProps = Get-Content -LiteralPath 'Directory.Build.props'
    $taskVersion = $taskProps.Project.PropertyGroup.Version
    $taskInno = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Inno Setup 6\ISCC.exe'
    if (-not (Test-Path -LiteralPath $taskInno)) { throw 'Instale Inno Setup: https://jrsoftware.org/isdl.php' }
    & $taskInno "/DAppVersion=$taskVersion" installer/NexoDesk.iss
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar instalador.' }
    $taskSetup = Join-Path $taskRoot "artifacts\NexoDesk-New-Setup-$taskVersion.exe"
    $taskDigest = (Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$taskSetup.sha256", "$taskDigest  $([IO.Path]::GetFileName($taskSetup))" + [Environment]::NewLine)
    & "$PSScriptRoot\Verify-Artifacts.ps1"
    Get-Item -LiteralPath $taskSetup | Select-Object FullName,Length
} finally { Pop-Location }