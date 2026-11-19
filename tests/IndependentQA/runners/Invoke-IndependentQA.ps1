<# Integration-owned launcher. The eight QA originals and their assertions remain unchanged. #>
[CmdletBinding()]
param(
 [ValidateSet('All','Reference','Candidate')][string]$Suite='All',
 [ValidateSet('Unspecified','CodexSandboxPreflight','OrdinaryWindowsPowerShell')][string]$RunContext='Unspecified',
 [string]$PythonExe='',
 [string]$LedgerCtlDll='',
 [string]$PowerShellExe='',
 [string]$EvidenceRoot='',
 [string]$AntivirusState='Not recorded by operator; launcher does not change antivirus settings',
 [string]$ExpectedCommit='5c585cefa59509f9fad8cba55000e374768685b7'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$qaSource=$PSScriptRoot
$qaRepo=Split-Path (Split-Path $qaSource -Parent) -Parent
$qaWork=Join-Path $qaRepo '.git\dev-003-work'
$qaRuntimeHint=Join-Path $qaWork 'independent-qa-runtimes.json'
if(Test-Path -LiteralPath $qaRuntimeHint){
 $qaHints=Get-Content -LiteralPath $qaRuntimeHint -Raw -Encoding UTF8|ConvertFrom-Json
 if(!$PythonExe -and $null -ne $qaHints.PSObject.Properties['python'] -and (Test-Path -LiteralPath $qaHints.python -PathType Leaf)){$PythonExe=$qaHints.python}
 if(!$PowerShellExe -and $null -ne $qaHints.PSObject.Properties['powershell'] -and (Test-Path -LiteralPath $qaHints.powershell -PathType Leaf)){$PowerShellExe=$qaHints.powershell}
}
if(!$LedgerCtlDll){$LedgerCtlDll=Join-Path $qaRepo 'src\ledgerctl\bin\Release\net10.0\ledgerctl.dll'}
if(!$PowerShellExe){
 $qaPwsh=Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue|Select-Object -First 1
 if($null -ne $qaPwsh){$PowerShellExe=$qaPwsh.Source}
 elseif($PythonExe -and (Test-Path -LiteralPath (Join-Path (Split-Path (Split-Path $PythonExe -Parent) -Parent) 'native\powershell\pwsh.exe'))){$PowerShellExe=Join-Path (Split-Path (Split-Path $PythonExe -Parent) -Parent) 'native\powershell\pwsh.exe'}
 else{$PowerShellExe=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'}
}
if(!$EvidenceRoot){$EvidenceRoot=Join-Path $qaWork ('independent-qa-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$EvidenceRoot=[IO.Path]::GetFullPath($EvidenceRoot)
$qaAllowed=[IO.Path]::GetFullPath($qaWork).TrimEnd('\')+'\'
if(!$EvidenceRoot.StartsWith($qaAllowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence must be a fresh subdirectory of the approved .git/dev-003-work folder.'}
if(Test-Path -LiteralPath $EvidenceRoot){throw 'Refusing to reuse or overwrite an existing QA evidence directory.'}
$null=New-Item -ItemType Directory -Path $EvidenceRoot

function Write-QaJson([string]$Path,$Value){
 [IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20)+"`n",[Text.UTF8Encoding]::new($false))
}
function Invoke-QaProcess([string]$Name,[string]$File,[string[]]$Arguments){
 $stdout=Join-Path $EvidenceRoot ($Name+'.stdout.txt')
 $stderr=Join-Path $EvidenceRoot ($Name+'.stderr.txt')
 foreach($arg in $Arguments){if($arg.Contains('"') -or $arg.EndsWith('\')){throw 'Unsupported quoted/trailing-separator process argument.'}}
 $start=[Diagnostics.ProcessStartInfo]::new()
 $start.FileName=$File
 $start.Arguments=($Arguments|ForEach-Object {'"'+$_+'"'}) -join ' '
 $start.WorkingDirectory=$EvidenceRoot
 $start.UseShellExecute=$false
 $start.CreateNoWindow=$true
 $start.RedirectStandardOutput=$true
 $start.RedirectStandardError=$true
 $begin=(Get-Date).ToUniversalTime().ToString('o')
 $p=[Diagnostics.Process]::Start($start)
 try{
  $outTask=$p.StandardOutput.ReadToEndAsync();$errTask=$p.StandardError.ReadToEndAsync()
  $p.WaitForExit()
  [IO.File]::WriteAllText($stdout,$outTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText($stderr,$errTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
  $result=[ordered]@{name=$Name;executable=$File;arguments=$Arguments;pid=$p.Id;utc_start=$begin;utc_finish=(Get-Date).ToUniversalTime().ToString('o');exit_code=$p.ExitCode;stdout=$stdout;stderr=$stderr}
  Write-QaJson (Join-Path $EvidenceRoot ($Name+'.process.json')) $result
  return [pscustomobject]$result
 }finally{$p.Dispose()}
}

$qaSummary=[ordered]@{
 format='UDDL-INDEPENDENT-QA-RUN';version=1;utc_start=(Get-Date).ToUniversalTime().ToString('o');
 run_context_operator_reported=$RunContext;antivirus_operator_reported=$AntivirusState;antivirus_changed=$false;
 expected_candidate_commit=$ExpectedCommit;actual_head=$null;git_status=$null;git_status_exit=$null;
 windows_version=[Environment]::OSVersion.VersionString;powershell_version=$PSVersionTable.PSVersion.ToString();
 candidate_binary=$LedgerCtlDll;candidate_binary_sha256=$null;original_hashes_verified=$false;
 reference=[ordered]@{verdict='NOT_RUN';exit_code=$null;candidate_executed=$false};
 candidate=[ordered]@{verdict='NOT_RUN';exit_code=$null;final_event=$null;pass_events=0;fail_events=0;candidate_commands_with_stdout=0;native_execution_verified=$false};
 fault_matrix=[ordered]@{executed=$false;reason='Review/coordination only; no new production hooks authorized'};
 formal_step1_acceptance='NOT_DECLARED';evidence_root=$EvidenceRoot
}
$qaExit=1
try{
 $qaManifest=Get-Content -LiteralPath (Join-Path $qaSource 'ORIGINALS.manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
 if($qaManifest.original_count -ne 8 -or @($qaManifest.files).Count -ne 8){throw 'Invalid original-file manifest.'}
 foreach($file in $qaManifest.files){
  if([IO.Path]::GetFileName($file.name) -cne $file.name){throw 'Invalid manifest filename.'}
  $path=Join-Path $qaSource $file.name
  if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256){throw ('QA original hash mismatch: '+$file.name)}
 }
 $qaSummary.original_hashes_verified=$true
 Copy-Item -LiteralPath (Join-Path $qaSource 'ORIGINALS.manifest.json') -Destination (Join-Path $EvidenceRoot 'originals.manifest.json')
 $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($EvidenceRoot))
 $qaSummary['filesystem']=[ordered]@{format=$drive.DriveFormat;drive_type=$drive.DriveType.ToString()}
 $qaGit=(Get-Command git -CommandType Application -ErrorAction Stop|Select-Object -First 1).Source
 $gitHead=Invoke-QaProcess 'git-head' $qaGit @('--git-dir', (Join-Path $qaRepo '.git'),'rev-parse','HEAD')
 if($gitHead.exit_code -ne 0){throw 'Cannot record candidate Git HEAD.'}
 $qaSummary.actual_head=[IO.File]::ReadAllText($gitHead.stdout).Trim()
 $gitStatus=Invoke-QaProcess 'git-status' $qaGit @('--no-optional-locks','-C',$qaRepo,'status','--short','--branch')
 $qaSummary.git_status=[IO.File]::ReadAllText($gitStatus.stdout).Trim()
 $qaSummary.git_status_exit=$gitStatus.exit_code
 if($qaSummary.actual_head -cne $ExpectedCommit){throw 'Candidate HEAD differs from expected commit; select the intended candidate explicitly.'}
 if(!(Test-Path -LiteralPath $LedgerCtlDll -PathType Leaf)){throw 'Candidate ledgerctl.dll is missing. Build the unchanged candidate before execution.'}
 $qaSummary.candidate_binary_sha256=(Get-FileHash -LiteralPath $LedgerCtlDll -Algorithm SHA256).Hash.ToLowerInvariant()
 $artifacts=@(Get-ChildItem -LiteralPath (Split-Path $LedgerCtlDll -Parent) -File|ForEach-Object {[pscustomobject]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
 Write-QaJson (Join-Path $EvidenceRoot 'candidate-artifact-hashes.json') $artifacts
 $qaDotnet=(Get-Command dotnet -CommandType Application -ErrorAction Stop|Select-Object -First 1).Source
 $null=Invoke-QaProcess 'dotnet-info' $qaDotnet @('--info')
 if($Suite -ne 'Candidate'){
  if(!$PythonExe){
   $found=@(Get-Command python,python3 -CommandType Application -ErrorAction SilentlyContinue|Where-Object {$_.Source -notlike '*\Microsoft\WindowsApps\python*'})
   if($found.Count -gt 0){$PythonExe=$found[0].Source}
  }
  if(!$PythonExe -or !(Test-Path -LiteralPath $PythonExe -PathType Leaf)){
   $qaSummary.reference.verdict='BLOCKED';$qaSummary.reference['reason']='Supply an existing Python 3 executable with cryptography and lzma; no installation is performed.'
  }else{
   $ref=Invoke-QaProcess 'reference' $PythonExe @('-B',(Join-Path $qaSource 'QA_DEV003_OFFLINE_INDEPENDENT_CHECK.py'),'--json-out',(Join-Path $EvidenceRoot 'reference-results.json'))
   $qaSummary.reference.exit_code=$ref.exit_code
   $qaSummary.reference.verdict=if($ref.exit_code -eq 0){'PASS'}else{'FAIL'}
   $qaSummary.reference['historical_message_note']='Original verifier prints a fixed historical environment/acceptance message. Its output is preserved; it does not execute the candidate.'
  }
 }
 if($Suite -ne 'Reference'){
  if($RunContext -eq 'Unspecified'){throw 'Explicitly select OrdinaryWindowsPowerShell or CodexSandboxPreflight; do not implicitly label a sandbox run native.'}
  if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or $drive.DriveFormat -ne 'NTFS'){throw 'Candidate suite requires Windows and an isolated local NTFS evidence location.'}
  $candidateRoot=Join-Path $EvidenceRoot 'candidate'
  # Matches the QA runner's supplied example. Applies only to this child process; no Set-ExecutionPolicy is called.
  $run=Invoke-QaProcess 'candidate-runner' $PowerShellExe @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $qaSource 'QA_DEV003_WINDOWS_INDEPENDENT_CLI.ps1'),'-LedgerCtlDll',$LedgerCtlDll,'-FixturesJson',(Join-Path $qaSource 'QA_PHASE1_REFERENCE_FIXTURES.json'),'-Dotnet',$qaDotnet,'-EvidenceRoot',$candidateRoot,'-ExpectedCommit',$ExpectedCommit)
  $qaSummary.candidate.exit_code=$run.exit_code
  $events=@();$caseLog=Join-Path $candidateRoot 'QA_CASE_RESULTS.jsonl'
  if(Test-Path -LiteralPath $caseLog){$events=@(Get-Content -LiteralPath $caseLog -Encoding UTF8|Where-Object {$_.Trim()}|ForEach-Object {$_|ConvertFrom-Json})}
  $final=@($events|Where-Object {$_.test -eq 'FINAL'}|Select-Object -Last 1)
  $qaSummary.candidate.pass_events=@($events|Where-Object {$_.result -eq 'PASS' -and $_.test -ne 'FINAL'}).Count
  $qaSummary.candidate.fail_events=@($events|Where-Object {$_.result -eq 'FAIL'}).Count
  if($final.Count -eq 1){$qaSummary.candidate.final_event=$final[0]}
  $qaSummary.candidate.candidate_commands_with_stdout=if(Test-Path -LiteralPath (Join-Path $candidateRoot 'outputs')){@(Get-ChildItem -LiteralPath (Join-Path $candidateRoot 'outputs') -Filter '*.stdout.json' -File).Count}else{0}
  $qaSummary.candidate.verdict=if($run.exit_code -eq 0 -and $final.Count -eq 1 -and $final[0].result -eq 'PASS'){'PASS'}else{'FAIL_REQUIRES_TRIAGE'}
  $qaSummary.candidate['acceptance_scope']=if($RunContext -eq 'OrdinaryWindowsPowerShell'){'Operator-reported ordinary terminal; independent QA acceptance still requires QA/PM/Owner review and fault coverage'}else{'Sandbox diagnostic only; not native filesystem/locking acceptance'}
 }
 $qaExit=0
 if($Suite -ne 'Candidate' -and $qaSummary.reference.verdict -ne 'PASS'){$qaExit=1}
 if($Suite -ne 'Reference' -and $qaSummary.candidate.verdict -ne 'PASS'){$qaExit=1}
}catch{
 $qaSummary['launcher_error']=$_.Exception.ToString()
 [IO.File]::WriteAllText((Join-Path $EvidenceRoot 'launcher-error.txt'),$_.Exception.ToString(),[Text.UTF8Encoding]::new($false))
 $qaExit=1
}finally{
 $qaSummary['utc_finish']=(Get-Date).ToUniversalTime().ToString('o')
 $qaSummary['launcher_exit_code']=$qaExit
 Write-QaJson (Join-Path $EvidenceRoot 'run-status.json') $qaSummary
 [IO.File]::WriteAllText((Join-Path $EvidenceRoot 'exit-code.txt'),[string]$qaExit+"`n",[Text.UTF8Encoding]::new($false))
 Write-Host ('Independent reference: '+$qaSummary.reference.verdict)
 Write-Host ('Candidate suite: '+$qaSummary.candidate.verdict+'; context='+$RunContext)
 Write-Host ('Evidence: '+$EvidenceRoot)
 Write-Host 'Step 1 acceptance is not declared by this integration launcher.'
}
exit $qaExit
