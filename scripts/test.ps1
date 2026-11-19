<# Run Developer regressions and current independent QA separately, with one entry point.
Fresh synthetic instances, crash workers and evidence stay in ignored local/qa.
OrdinaryWindowsPowerShell must be selected by an operator outside Codex.
No production instances, historical receipts or antivirus settings are changed. #>
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('CodexSandboxPreflight','OrdinaryWindowsPowerShell')][string]$RunContext,
 [Parameter(Mandatory=$true)][string]$PythonExe
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Repository.Paths.ps1')
if(!(Test-Path -LiteralPath $PythonExe -PathType Leaf)){throw 'Supply existing Python with cryptography/lzma; this script installs nothing.'}
$repo=Get-LedgerRepository
$root=Join-Path (Get-LedgerEvidenceRoot) ('layout-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$null=New-Item -ItemType Directory -Path $root
$begin=(Get-Date).ToUniversalTime().ToString('o')
& (Join-Path $PSScriptRoot 'build.ps1') -Test *> (Join-Path $root 'developer.log')
$developerExit=$LASTEXITCODE
Get-Content -LiteralPath (Join-Path $root 'developer.log') -Tail 3
if($developerExit -ne 0){Write-Output ('Developer regressions failed. Evidence: '+$root);exit $developerExit}
$qaRoot=Join-Path $root 'independent'
& $PythonExe -B (Get-LedgerFile 'run_current.py') --repo $repo --candidate (Get-LedgerCandidate) --dotnet (Get-Command dotnet -CommandType Application|Select-Object -First 1).Source --evidence $qaRoot --context $RunContext *> (Join-Path $root 'independent.log')
$qaExit=$LASTEXITCODE
Get-Content -LiteralPath (Join-Path $root 'independent.log') -Tail 5
$result=[ordered]@{context=$RunContext;utc_start=$begin;utc_finish=(Get-Date).ToUniversalTime().ToString('o');developer_exit=$developerExit;independent_exit=$qaExit;head=(& git -C $repo rev-parse HEAD);antivirus_changed=$false;historical_results_rewritten=$false}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding UTF8
Write-Output ('Combined evidence: '+$root)
exit $qaExit
