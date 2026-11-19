<# Runs unchanged independent assertions against the separately built maintenance candidate.
Creates only QA adapters, disposable instances and evidence. Does not build production,
change antivirus, merge, commit or replace the accepted baseline. Ordinary context must
be selected by an operator executing outside Codex. #>
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('OrdinaryWindowsPowerShell','CodexSandboxPreflight')][string]$RunContext,
 [Parameter(Mandatory=$true)][string]$PythonExe,
 [string]$WorkRoot='', [string]$EvidenceRoot=''
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../../scripts/Repository.Paths.ps1')
if(!$WorkRoot){$WorkRoot=Join-Path (Get-LedgerGitDirectory) 'dev-003-work/qa017-20261118-111036-e94e79c9'}
if(!(Test-Path -LiteralPath $PythonExe -PathType Leaf)){throw 'Supply an existing Python with cryptography/lzma; nothing is installed.'}
$arguments=@('-B',(Get-LedgerFile 'QA017_EXECUTE.py'),'--work-root',$WorkRoot,'--context',$RunContext)
if($EvidenceRoot){$arguments+=@('--evidence',$EvidenceRoot)}
& $PythonExe @arguments
exit $LASTEXITCODE
