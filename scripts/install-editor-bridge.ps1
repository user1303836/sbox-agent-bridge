param(
  [Parameter(Mandatory = $true)]
  [string]$ProjectPath,

  [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
  $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

$projectFullPath = [System.IO.Path]::GetFullPath($ProjectPath)
$repoFullPath = [System.IO.Path]::GetFullPath($RepoRoot)
$sourcePath = Join-Path $repoFullPath 'editor'
$librariesPath = Join-Path $projectFullPath 'Libraries'
$targetPath = Join-Path $librariesPath 'sbox_agent_bridge'

if (-not (Test-Path -LiteralPath $projectFullPath -PathType Container)) {
  throw "ProjectPath does not exist: $projectFullPath"
}

if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
  throw "Could not find editor bridge source: $sourcePath"
}

New-Item -ItemType Directory -Force -Path $targetPath | Out-Null
Get-ChildItem -LiteralPath $sourcePath -Force | Copy-Item -Destination $targetPath -Recurse -Force

[pscustomobject]@{
  ok = $true
  projectPath = $projectFullPath
  sourcePath = $sourcePath
  installedPath = $targetPath
  nextStep = 'Launch the project with scripts/start-sbox-project.ps1 or open it in s&box, wait for compile, then run bridge.doctor or npm run smoke:mvp-suite from mcp-server.'
} | ConvertTo-Json -Depth 8
