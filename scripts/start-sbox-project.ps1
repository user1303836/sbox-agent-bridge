param(
  [Parameter(Mandatory = $true)]
  [string]$ProjectFile,

  [string]$SboxRoot = '',
  [string]$IpcRoot = '',
  [int]$WaitForBridgeSeconds = 0,
  [switch]$ClearIpc
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SboxRoot)) {
  $SboxRoot = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\sbox'
}

$sboxFullPath = [System.IO.Path]::GetFullPath($SboxRoot)
$projectFullPath = [System.IO.Path]::GetFullPath($ProjectFile)
$exe = Join-Path $sboxFullPath 'sbox-dev.exe'
$launcher = Join-Path $sboxFullPath 'sbox-launcher.dll'
$dev = Join-Path $sboxFullPath 'sbox-dev.dll'

if (-not (Test-Path -LiteralPath $projectFullPath -PathType Leaf)) {
  throw "ProjectFile does not exist: $projectFullPath"
}

foreach ($required in @($exe, $launcher, $dev)) {
  if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
    throw "Could not find required s&box launch file: $required"
  }
}

$resolvedIpcRoot = ''
if (-not [string]::IsNullOrWhiteSpace($IpcRoot)) {
  $resolvedIpcRoot = [System.IO.Path]::GetFullPath($IpcRoot)
  if ($ClearIpc -and (Test-Path -LiteralPath $resolvedIpcRoot)) {
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $trimmedIpcRoot = $resolvedIpcRoot.TrimEnd([char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar))
    $ipcLeaf = [System.IO.Path]::GetFileName($trimmedIpcRoot)
    if (-not $resolvedIpcRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or -not $ipcLeaf.StartsWith('sbox-agent-bridge', [System.StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing to clear IPC root outside the temp sbox-agent-bridge namespace: $resolvedIpcRoot"
    }

    Remove-Item -LiteralPath $resolvedIpcRoot -Recurse -Force
  }
}

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $exe
$psi.Arguments = '"' + $launcher + '" "' + $dev + '" -project "' + $projectFullPath + '"'
$psi.WorkingDirectory = $sboxFullPath
$psi.UseShellExecute = $false

if (-not [string]::IsNullOrWhiteSpace($resolvedIpcRoot)) {
  $psi.EnvironmentVariables['SBOX_AGENT_BRIDGE_IPC'] = $resolvedIpcRoot
}

$process = [System.Diagnostics.Process]::Start($psi)
if ($null -eq $process) {
  throw "Failed to start s&box project: $projectFullPath"
}

$bridgeReady = $null
if ($WaitForBridgeSeconds -gt 0) {
  $waitRoot = if (-not [string]::IsNullOrWhiteSpace($resolvedIpcRoot)) {
    $resolvedIpcRoot
  } else {
    Join-Path ([System.IO.Path]::GetTempPath()) 'sbox-agent-bridge'
  }

  $deadline = (Get-Date).AddSeconds($WaitForBridgeSeconds)
  $bridgeReady = $false
  while ((Get-Date) -lt $deadline) {
    $requests = Join-Path $waitRoot 'requests'
    $responses = Join-Path $waitRoot 'responses'
    if ((Test-Path -LiteralPath $requests -PathType Container) -and (Test-Path -LiteralPath $responses -PathType Container)) {
      $bridgeReady = $true
      break
    }

    Start-Sleep -Seconds 1
  }
}

[pscustomobject]@{
  ok = $true
  pid = $process.Id
  projectFile = $projectFullPath
  sboxRoot = $sboxFullPath
  ipcRoot = $resolvedIpcRoot
  bridgeReady = $bridgeReady
  commandLineArgs = $psi.Arguments
  nextStep = 'Set SBOX_AGENT_BRIDGE_IPC to ipcRoot when provided, then run bridge.doctor or npm run smoke:mvp-suite from mcp-server.'
} | ConvertTo-Json -Depth 8
