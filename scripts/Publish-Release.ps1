param(
    [string]$Repository = 'gustavotoigo-max/NexoDesk-New',
    [string]$Commit = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Repositório inválido.' }
[xml]$taskProps = Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props')
$taskVersion = [string]$taskProps.Project.PropertyGroup.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Versão inválida: use X.Y.Z.' }
$taskTag = "v$taskVersion"
if (-not $Commit) {
    $Commit = & git -c "safe.directory=$($taskRoot.Replace('\','/'))" -C $taskRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Crie o commit desta versão antes de publicar.' }
}
if ($Commit -notmatch '^[0-9a-fA-F]{40}$') { throw 'Commit inválido.' }
$taskSetup = Join-Path $taskRoot "artifacts\NexoDesk-New-Setup-$taskVersion.exe"
$taskChecksum = "$taskSetup.sha256"
foreach ($taskFile in @($taskSetup,$taskChecksum)) {
    if (-not (Test-Path -LiteralPath $taskFile)) { throw "Artefato ausente: $taskFile" }
}
$taskHash = (Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-Content -LiteralPath $taskChecksum).Split(' ')[0] -ne $taskHash) { throw 'SHA-256 divergente.' }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI ausente: https://cli.github.com/' }
$taskExistingJson = & gh api "repos/$Repository/releases/tags/$taskTag" 2>$null
if ($LASTEXITCODE -eq 0) {
    $taskExisting = $taskExistingJson | ConvertFrom-Json
    $taskNames = @($taskExisting.assets.name)
    if ($taskExisting.draft -or
        $taskNames -notcontains [IO.Path]::GetFileName($taskSetup) -or
        $taskNames -notcontains [IO.Path]::GetFileName($taskChecksum)) {
        throw "O release $taskTag existe, mas está incompleto. Não será sobrescrito."
    }
    Write-Host "Release $taskTag já publicado; arquivos preservados."
    Write-Host $taskExisting.html_url
    exit 0
}
$taskRefJson = & gh api "repos/$Repository/git/ref/tags/$taskTag" 2>$null
if ($LASTEXITCODE -eq 0) {
    $taskRef = $taskRefJson | ConvertFrom-Json
    $taskTarget = $taskRef.object.sha
    if ($taskRef.object.type -eq 'tag') {
        $taskTagJson = & gh api "repos/$Repository/git/tags/$taskTarget"
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao conferir tag existente.' }
        $taskTarget = ($taskTagJson | ConvertFrom-Json).object.sha
    }
    if ($taskTarget -ne $Commit) { throw "A tag $taskTag aponta para outro commit. Use uma nova versão." }
}
$taskLatest = '--latest'
$taskLatestJson = & gh api "repos/$Repository/releases/latest" 2>$null
if ($LASTEXITCODE -eq 0) {
    $taskLatestVersion = [version]::new()
    if ([version]::TryParse(($taskLatestJson | ConvertFrom-Json).tag_name.TrimStart('v'), [ref]$taskLatestVersion) -and
        $taskLatestVersion -gt [version]$taskVersion) { $taskLatest = '--latest=false' }
}
$taskNotes = Join-Path $taskRoot "docs\releases\$taskVersion.md"
$taskArguments = @('release','create',$taskTag,'--repo',$Repository,'--target',$Commit,
    '--title',"NexoDesk New $taskVersion",$taskLatest,$taskSetup,$taskChecksum)
if (Test-Path -LiteralPath $taskNotes) { $taskArguments += @('--notes-file',$taskNotes) }
else { $taskArguments += '--generate-notes' }
& gh @taskArguments
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar $taskTag." }