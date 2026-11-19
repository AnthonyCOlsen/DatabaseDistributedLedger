<# QA-012 observable software qualification; portable reference and Windows backend scopes are separate.
Exit 0: requested suites passed with no blocked cases; 1: failures; 2: blocked coverage.
No exit code declares formal Step 1 acceptance. Never changes antivirus configuration.
#>
[CmdletBinding()]
param(
 [ValidateSet('All','Reference','Candidate')][string]$Suite='Candidate',
 [Parameter(Mandatory=$true)][ValidateSet('CodexSandboxPreflight','OrdinaryWindowsPowerShell','PortableReference')][string]$RunContext,
 [string]$ExpectedCommit='8bc96f0f513faac57c1db23a8189288479d8a992',
 [string]$PythonExe='', [string]$LedgerCtlDll='', [string]$EvidenceRoot='',
 [string]$AntivirusState='Not recorded by operator; settings unchanged'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'QA-010 requires PowerShell 7. Invoke this script with pwsh.exe; do not use Windows PowerShell 5.1.'}
if($Suite -ne 'Reference' -and $RunContext -eq 'PortableReference'){throw 'PortableReference selects reference-only execution; Windows candidate qualification needs its actual context.'}
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$work=Join-Path $repo '.git/dev-003-work'
$git=(Get-Command git -CommandType Application|Select-Object -First 1).Source
$dotnet='';if($Suite -ne 'Reference'){$dotnet=(Get-Command dotnet -CommandType Application|Select-Object -First 1).Source}
if(!$PythonExe){
 # Ordinary terminals need not have Codex's bundled Git on PATH. When this
 # PowerShell itself is bundled, discover its sibling Python directly.
 $hostDependencies=Split-Path (Split-Path $PSHOME -Parent) -Parent
 $hostPython=Join-Path $hostDependencies 'python/python.exe'
 if(Test-Path -LiteralPath $hostPython -PathType Leaf){$PythonExe=$hostPython}
}
if(!$PythonExe){
 $runtimeGit=Get-Command git -All -CommandType Application|Where-Object Source -Like '*WindowsApps*'|Select-Object -First 1
 if($null -ne $runtimeGit){
  $deps=Split-Path (Split-Path (Split-Path (Split-Path $runtimeGit.Source -Parent) -Parent) -Parent) -Parent
  $candidatePython=Join-Path $deps 'python/python.exe'
  if(Test-Path -LiteralPath $candidatePython){$PythonExe=$candidatePython}
 }
 if(!$PythonExe){
  $found=Get-Command python,python3 -CommandType Application -ErrorAction SilentlyContinue|Where-Object Source -NotLike '*Microsoft\WindowsApps\python*'|Select-Object -First 1
  if($null -ne $found){$PythonExe=$found.Source}
 }
}
if(!$PythonExe -or !(Test-Path -LiteralPath $PythonExe -PathType Leaf)){throw 'Supply -PythonExe with existing Python 3 + cryptography/lzma; this launcher installs nothing.'}
if(!$LedgerCtlDll){$LedgerCtlDll=Join-Path $repo 'src/ledgerctl/bin/Release/net10.0/ledgerctl.dll'}
if(!$EvidenceRoot){$EvidenceRoot=Join-Path $work ('qa012-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))}
$EvidenceRoot=[IO.Path]::GetFullPath($EvidenceRoot)
$allowed=[IO.Path]::GetFullPath($work).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
if(!$EvidenceRoot.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $EvidenceRoot)){throw 'Evidence must be a fresh subdirectory under .git/dev-003-work; reuse and other locations are refused.'}
$null=New-Item -ItemType Directory -Path $EvidenceRoot
function Write-QaJson([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 30)+"`n",[Text.UTF8Encoding]::new($false))}
function Run-Process([string]$Name,[string]$Executable,[string[]]$ProcessArguments){
 $start=[Diagnostics.ProcessStartInfo]::new($Executable);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $start.WorkingDirectory=$EvidenceRoot;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 foreach($arg in $ProcessArguments){$start.ArgumentList.Add($arg)}
 $begin=(Get-Date).ToUniversalTime().ToString('o');$p=[Diagnostics.Process]::Start($start)
 try{
  $out=$p.StandardOutput.ReadToEndAsync();$err=$p.StandardError.ReadToEndAsync();$p.WaitForExit()
  [IO.File]::WriteAllText((Join-Path $EvidenceRoot "$Name.stdout.txt"),$out.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $EvidenceRoot "$Name.stderr.txt"),$err.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
  Write-QaJson (Join-Path $EvidenceRoot "$Name.process.json") ([ordered]@{executable=$Executable;arguments=$ProcessArguments;pid=$p.Id;utc_start=$begin;utc_finish=(Get-Date).ToUniversalTime().ToString('o');exit_code=$p.ExitCode})
  return $p.ExitCode
 }finally{$p.Dispose()}
}
$status=[ordered]@{format='UDDL-QA012-RUN';version=1;utc_start=(Get-Date).ToUniversalTime().ToString('o');context_operator_reported=$RunContext;
 os=[Environment]::OSVersion.VersionString;powershell=$PSVersionTable.PSVersion.ToString();antivirus_operator_reported=$AntivirusState;antivirus_changed=$false;
 candidate_scope='QA-012 new observable software cases only; completed QA-011 is carried forward';original_internal_FI07='NOT_EXECUTED';original_internal_FI15='NOT_EXECUTED';
 expected_commit=$ExpectedCommit;actual_commit=$null;reference='NOT-RUN';candidate='NOT-RUN';adapter='NOT-RUN';formal_step1_acceptance='NOT_DECLARED';physical_power_loss_proven=$false}
$exitCode=1
try{
 $headExit=Run-Process 'git-head' $git @('-C',$repo,'rev-parse','HEAD')
 if($headExit -ne 0){throw 'Cannot record Git HEAD'}
 $status.actual_commit=[IO.File]::ReadAllText((Join-Path $EvidenceRoot 'git-head.stdout.txt')).Trim()
 if($status.actual_commit -cne $ExpectedCommit){throw 'HEAD differs from the explicitly selected candidate commit'}
 $null=Run-Process 'git-status-before' $git @('--no-optional-locks','-C',$repo,'status','--short','--branch')
 $productionExit=Run-Process 'production-status' $git @('--no-optional-locks','-C',$repo,'status','--porcelain','--','src','tests/UserDimensions.Ledger.Tests','third_party/sqlite','Directory.Build.props')
 if($productionExit -ne 0 -or [IO.File]::ReadAllText((Join-Path $EvidenceRoot 'production-status.stdout.txt')).Trim()){throw 'Developer/production work is dirty; do not snapshot or execute a moving candidate'}
 $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($EvidenceRoot))
 $status['filesystem']=[ordered]@{format=$drive.DriveFormat;drive_type=$drive.DriveType.ToString()}
 if($Suite -ne 'Reference' -and (![OperatingSystem]::IsWindows() -or $drive.DriveType -ne 'Fixed' -or $drive.DriveFormat -ne 'NTFS')){throw 'Initial candidate durability suite requires local Windows fixed NTFS'}
 $history=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ORIGINALS.manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
 foreach($file in $history.files){if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $file.name) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256){throw ('Historical QA original changed: '+$file.name)}}
 $status['historical_originals_verified']=$true
 $suiteSnapshot=Join-Path $EvidenceRoot 'suite-snapshot';$null=New-Item -ItemType Directory -Path $suiteSnapshot
 $inputs=@()
 foreach($file in Get-ChildItem -LiteralPath $PSScriptRoot -File|Where-Object {$_.Name -like 'QA010*' -or $_.Name -like 'QA011*' -or $_.Name -like 'QA012*' -or $_.Name -eq 'Invoke-QA012.ps1'}){
  $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $suiteSnapshot $file.Name)
  if((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash -or (Get-FileHash -LiteralPath (Join-Path $suiteSnapshot $file.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash){throw 'QA source changed while creating an immutable run snapshot'}
  $inputs += [ordered]@{name=$file.Name;sha256=$hash}
 }
 Write-QaJson (Join-Path $EvidenceRoot 'qa-source-hashes.json') @($inputs)
 $docHashes=@('README.md','Database_Distributed_Ledger_Technical_Design.md','Distributed_Ledger_Management_API_Specification.md','SQLite_Architecture_and_Implementation_Plan.md')|ForEach-Object {[ordered]@{name=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_) -Algorithm SHA256).Hash.ToLowerInvariant()}}
 Write-QaJson (Join-Path $EvidenceRoot 'design-document-hashes.json') @($docHashes)
 if($dotnet){$null=Run-Process 'dotnet-info' $dotnet @('--info')}
 $null=Run-Process 'python-info' $PythonExe @('-B','-c','import sys,cryptography,lzma;print(sys.version);print(cryptography.__version__)')
 $fixtures=Join-Path $suiteSnapshot 'QA010_FIXTURES_103.json'
 $exitCode=0
 if($Suite -ne 'Candidate'){
  $refExit=Run-Process 'reference' $PythonExe @('-B',(Join-Path $suiteSnapshot 'QA010_REFERENCE.py'),'--fixtures',$fixtures,'--results',(Join-Path $EvidenceRoot 'reference-results.json'))
  $status.reference=if($refExit -eq 0){'PASS'}else{'FAIL'}
  if($refExit -ne 0){throw 'Independent reference validation failed; candidate suite was not executed'}
 }
 if($Suite -ne 'Reference'){
  if(!(Test-Path -LiteralPath $LedgerCtlDll -PathType Leaf)){throw 'Candidate DLL missing; ask Developer to supply the selected build. No production build is overwritten.'}
  $snapshot=Join-Path $EvidenceRoot 'candidate-snapshot';$null=New-Item -ItemType Directory -Path $snapshot
  $source=Split-Path $LedgerCtlDll -Parent
  $artifactHashes=@()
  foreach($file in Get-ChildItem -LiteralPath $source -File){
   $before=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
   Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $snapshot $file.Name)
   $after=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
   $copied=(Get-FileHash -LiteralPath (Join-Path $snapshot $file.Name) -Algorithm SHA256).Hash
   if($before -cne $after -or $before -cne $copied){throw 'Candidate binary changed while snapshotting; no execution permitted'}
   $artifactHashes += [ordered]@{name=$file.Name;sha256=$before.ToLowerInvariant()}
  }
  Write-QaJson (Join-Path $EvidenceRoot 'candidate-artifact-hashes.json') $artifactHashes
  $candidate=Join-Path $snapshot ([IO.Path]::GetFileName($LedgerCtlDll));$status['candidate_dll_sha256']=(Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
  $status['ledger_library_sha256']=(Get-FileHash -LiteralPath (Join-Path $snapshot 'UserDimensions.Ledger.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
  $driverRoot=Join-Path $EvidenceRoot 'driver';$null=New-Item -ItemType Directory -Path $driverRoot
  foreach($name in @('QA010_FaultDriver.cs','QA010_FaultDriver.csproj')){Copy-Item -LiteralPath (Join-Path $suiteSnapshot $name) -Destination (Join-Path $driverRoot $name)}
  [IO.File]::WriteAllText((Join-Path $driverRoot 'NuGet.Config'),'<configuration><packageSources><clear/></packageSources></configuration>',[Text.UTF8Encoding]::new($false))
  $env:DOTNET_CLI_HOME=Join-Path $EvidenceRoot 'dotnet-home';$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1';$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
  $env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false';$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
  $env:MSBuildEnableWorkloadResolver='false';$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
  $driverExit=Run-Process 'adapter-build' $dotnet @('build',(Join-Path $driverRoot 'QA010_FaultDriver.csproj'),'-c','Release','--nologo','-m:1','/p:BuildInParallel=false','/p:UseSharedCompilation=false','/p:MSBuildEnableWorkloadResolver=false',('/p:CandidateDirectory='+$snapshot))
  $driver=if($driverExit -eq 0){Join-Path $driverRoot 'bin/Release/net10.0/QA010_FaultDriver.dll'}else{''}
  $status.adapter=if($driverExit -eq 0){'PASS'}else{'BLOCKED'}
  $arguments=@('-B',(Join-Path $suiteSnapshot 'QA012_RUN.py'),'--candidate',$candidate,'--dotnet',$dotnet,'--fixtures',$fixtures,'--evidence',$EvidenceRoot,'--context',$RunContext,'--qa011-evidence',(Join-Path $work 'qa011-20261117-073333-c359b240'))
  if($driver){$arguments+=@('--driver',$driver)}
  $candidateExit=Run-Process 'candidate' $PythonExe $arguments
  $status.candidate=if($candidateExit -eq 0){'PASS'}elseif($candidateExit -eq 2){'BLOCKED'}else{'FAIL'}
  if($candidateExit -eq 1){$exitCode=1}elseif($candidateExit -ne 0 -and $exitCode -eq 0){$exitCode=2}
 }
}catch{
 $status['launcher_error']=$_.Exception.ToString();[IO.File]::WriteAllText((Join-Path $EvidenceRoot 'launcher-error.txt'),$_.Exception.ToString());$exitCode=1
}finally{
 $null=Run-Process 'git-status-after' $git @('--no-optional-locks','-C',$repo,'status','--short','--branch')
 $status['utc_finish']=(Get-Date).ToUniversalTime().ToString('o');$status['exit_code']=$exitCode
 Write-QaJson (Join-Path $EvidenceRoot 'run-status.json') $status
 [IO.File]::WriteAllText((Join-Path $EvidenceRoot 'exit-code.txt'),[string]$exitCode+"`n")
 Write-Host ('Reference: '+$status.reference+'; candidate: '+$status.candidate+'; context: '+$RunContext)
 Write-Host ('Evidence: '+$EvidenceRoot)
 Write-Host 'Observed software behavior only; original OS-internal FI-07/FI-15 checkpoints NOT EXECUTED. Step 1 acceptance is not declared.'
}
exit $exitCode
