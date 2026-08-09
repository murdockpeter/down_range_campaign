param(
  [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.2.12f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$unityProject = Join-Path $projectRoot 'unity-tactical'
$buildLog = Join-Path $unityProject 'unity-build.log'
$player = Join-Path $unityProject 'Build\DownRangeTactical.exe'

if (-not (Test-Path -LiteralPath $UnityEditor)) { throw "Unity Editor was not found at $UnityEditor" }

$arguments = @(
  '-batchmode', '-quit',
  '-projectPath', ('"' + $unityProject + '"'),
  '-executeMethod', 'DownRange.Editor.BuildGame.PerformBuild',
  '-logFile', ('"' + $buildLog + '"')
)
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
  Get-Content -LiteralPath $buildLog -Tail 120
  throw "Unity build failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath $player)) { throw 'Unity reported success but the tactical player was not generated.' }
Write-Output "Unity tactical player built: $player"
