param(
  [Parameter(Mandatory = $true)]
  [string]$ProjectPath,

  [string]$Title = 'Agent Bridge MVP Fresh',
  [string]$Ident = '',
  [string]$Org = 'local',
  [string]$TemplatePath = '',
  [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Get-SafeIdent([string]$Value) {
  if ([string]::IsNullOrWhiteSpace($Value)) {
    $Value = ''
  }

  $safe = $Value.ToLowerInvariant() -replace '[^a-z0-9_\.]+', '_'
  $safe = $safe.Trim([char[]]@(' ', '_', '.'))
  if ([string]::IsNullOrWhiteSpace($safe)) {
    return 'agent_bridge_mvp_fresh'
  }

  return $safe
}

if ([string]::IsNullOrWhiteSpace($Ident)) {
  $Ident = Get-SafeIdent ([System.IO.Path]::GetFileName($ProjectPath))
}

if ([string]::IsNullOrWhiteSpace($TemplatePath)) {
  $TemplatePath = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\sbox\templates\game.minimal'
}

$projectFullPath = [System.IO.Path]::GetFullPath($ProjectPath)
$templateFullPath = [System.IO.Path]::GetFullPath($TemplatePath)

if (-not (Test-Path -LiteralPath $templateFullPath -PathType Container)) {
  throw "Could not find s&box minimal game template: $templateFullPath"
}

if (Test-Path -LiteralPath $projectFullPath) {
  $existing = Get-ChildItem -LiteralPath $projectFullPath -Force
  if ($existing.Count -gt 0 -and -not $Force) {
    throw "ProjectPath already exists and is not empty: $projectFullPath. Pass -Force to overwrite template files."
  }
} else {
  New-Item -ItemType Directory -Force -Path $projectFullPath | Out-Null
}

Get-ChildItem -LiteralPath $templateFullPath -Force | ForEach-Object {
  Copy-Item -LiteralPath $_.FullName -Destination $projectFullPath -Recurse -Force
}

$templateProjectFile = Join-Path $projectFullPath '$ident.sbproj'
$projectFile = Join-Path $projectFullPath "$Ident.sbproj"
if (Test-Path -LiteralPath $templateProjectFile) {
  Move-Item -LiteralPath $templateProjectFile -Destination $projectFile -Force
}

if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
  throw "Could not find copied project file: $projectFile"
}

$projectJson = Get-Content -LiteralPath $projectFile -Raw | ConvertFrom-Json
$projectJson.Title = $Title
$projectJson.Org = $Org
$projectJson.Ident = $Ident
if ($projectJson.Metadata -and $projectJson.Metadata.ProjectTemplate) {
  $projectJson.Metadata.PSObject.Properties.Remove('ProjectTemplate')
}
$projectJson | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $projectFile -Encoding UTF8

$textExtensions = @('.cs', '.razor', '.scss', '.json', '.sbproj')
Get-ChildItem -LiteralPath $projectFullPath -Recurse -File | Where-Object {
  $textExtensions -contains $_.Extension.ToLowerInvariant()
} | ForEach-Object {
  $content = Get-Content -LiteralPath $_.FullName -Raw
  $content = $content.Replace('$title', $Title).Replace('$ident', $Ident)
  Set-Content -LiteralPath $_.FullName -Value $content -Encoding UTF8
}

[pscustomobject]@{
  ok = $true
  projectPath = $projectFullPath
  projectFile = $projectFile
  title = $Title
  ident = $Ident
  org = $Org
  templatePath = $templateFullPath
  nextStep = 'Install the bridge, launch the .sbproj with scripts/start-sbox-project.ps1 or open it in s&box, then run npm run smoke:mvp-suite from mcp-server.'
} | ConvertTo-Json -Depth 8
