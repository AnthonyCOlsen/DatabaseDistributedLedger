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
if(!$WorkRoot){$WorkRoot=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent}
if(!(Test-Path -LiteralPath $PythonExe -PathType Leaf)){throw 'Supply an existing Python with cryptography/lzma; nothing is installed.'}
$arguments=@('-B',(Join-Path $PSScriptRoot 'QA017_EXECUTE.py'),'--work-root',$WorkRoot,'--context',$RunContext)
if($EvidenceRoot){$arguments+=@('--evidence',$EvidenceRoot)}
& $PythonExe @arguments
exit $LASTEXITCODE
