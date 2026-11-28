param([Parameter(Mandatory)][string]$Batch,[Parameter(Mandatory)][string]$App,[string]$StoredCommitHost,[string]$NodeVerifier)
$ErrorActionPreference='Stop'
$b=[IO.Path]::GetFullPath($Batch)
if([IO.Directory]::Exists($b)){throw 'Expected fresh batch'}
[void][IO.Directory]::CreateDirectory($b)
$env:TEMP=$b
$env:TMP=$b
$d=[IO.Directory]::CreateDirectory((Join-Path $b 'projection')).FullName
$db=Join-Path $d 'items.DLDB'
$l=Join-Path $d 'items'
$exe=Join-Path $App 'ledgerctl.exe'
$client=Join-Path $App 'publication-client.exe'
if($NodeVerifier){$NodeVerifier=(Resolve-Path -LiteralPath $NodeVerifier).Path}
function Run([string]$program,[string[]]$arguments,[string]$name,[int]$expected,[byte[]]$serviceKey){
  $pi=[Diagnostics.ProcessStartInfo]::new($program)
  $pi.UseShellExecute=$false
  $pi.CreateNoWindow=$true
  $pi.WorkingDirectory=$b
  $pi.RedirectStandardOutput=$true
  $pi.RedirectStandardError=$true
  $pi.RedirectStandardInput=($null -ne $serviceKey)
  foreach($a in $arguments){$pi.ArgumentList.Add($a)}
  $p=[Diagnostics.Process]::Start($pi)
  try{
    if($serviceKey){
      $p.StandardInput.BaseStream.Write($serviceKey,0,$serviceKey.Length)
      $p.StandardInput.Close()
    }
    $out=$p.StandardOutput.ReadToEndAsync()
    $err=$p.StandardError.ReadToEndAsync()
    if(!$p.WaitForExit(55000)){$p.Kill($true);$null=$p.WaitForExit(5000);throw "$name timeout"}
    $rc=$p.ExitCode
    [IO.File]::WriteAllText("$b\$name.out",$out.GetAwaiter().GetResult())
    [IO.File]::WriteAllText("$b\$name.err",$err.GetAwaiter().GetResult())
    $receipt="$name exit=$rc pid=$($p.Id) command=$program $($arguments -join ' ')"
    Add-Content "$b\commands.txt" $receipt
    $receipt
    if($rc -ne $expected){Get-Content "$b\$name.out","$b\$name.err";throw "$name failed"}
  }finally{$p.Dispose()}
}
function Confirm-BlockSignature([string]$case,[byte[]]$raw,[switch]$AllowMissing){
  $openssl=Join-Path $env:ProgramFiles 'Git\usr\bin\openssl.exe'
  if(!$NodeVerifier -and $AllowMissing -and !(Test-Path -LiteralPath $openssl)){
    Write-Output 'Independent OpenSSL signature check NOT RUN'
    return
  }
  $keyPath="$b\$case.pub.der"
  $messagePath="$b\$case.message"
  $signaturePath="$b\$case.sig"
  [IO.File]::WriteAllBytes($keyPath,[byte[]]([Convert]::FromHexString('302a300506032b6570032100') + $raw[58..89]))
  [IO.File]::WriteAllBytes($messagePath,[byte[]]($raw[0..($raw.Length-65)]))
  [IO.File]::WriteAllBytes($signaturePath,[byte[]]($raw[($raw.Length-64)..($raw.Length-1)]))
  if($NodeVerifier){
    $verify='
      const { readFileSync } = require("node:fs");
      const { verify } = require("node:crypto");
      const [key, message, signature] = process.argv.slice(1).map(path => readFileSync(path));
      if (!verify(null, message, { key, format: "der", type: "spki" }, signature)) process.exit(1);
      console.log("Signature Verified Successfully");
    '
    Run $NodeVerifier @('-e',$verify,$keyPath,$messagePath,$signaturePath) "$case-ed25519" 0
  }else{
    Run $openssl @('pkeyutl','-verify','-pubin','-keyform','DER','-inkey',$keyPath,'-rawin','-in',$messagePath,'-sigfile',$signaturePath) "$case-ed25519" 0
  }
}
function Stop-F02Process([Diagnostics.Process]$process,[string]$role,[bool]$stopTree){
  # The witness retains these exact process handles through the bounded wait.
  $wasAlive=!$process.HasExited
  if($wasAlive){
    try{$process.Kill($stopTree)}
    catch [InvalidOperationException]{if(!$process.HasExited){throw}}
  }
  if(!$process.WaitForExit(5000) -or !$process.HasExited){
    throw "F02 cleanup could not verify $role PID $($process.Id) exited"
  }
  "F02 cleanup role=$role pid=$($process.Id) aliveBeforeStop=$wasAlive exited=True exit=$($process.ExitCode)"
}

function Invoke-F02Cut([string]$projection,[string]$scratch,[byte[]]$key){
  $start=[Diagnostics.ProcessStartInfo]::new($StoredCommitHost)
  $start.UseShellExecute=$false
  $start.CreateNoWindow=$true
  $start.WorkingDirectory=$b
  $start.RedirectStandardInput=$true
  $start.RedirectStandardOutput=$true
  $start.RedirectStandardError=$true
  foreach($arg in '--cut-before-progress',$projection,$scratch){$start.ArgumentList.Add($arg)}
  $hostProcess=[Diagnostics.Process]::Start($start)
  $native=$null
  $nativeVerified=$false
  $failure=$null
  $cleanupErrors=[Collections.Generic.List[Exception]]::new()
  $cleanupReceipts=[Collections.Generic.List[string]]::new()
  $trace=[Collections.Generic.List[string]]::new()
  try{
    $stderr=$hostProcess.StandardError.ReadToEndAsync()
    $inputPipe=$hostProcess.StandardInput.BaseStream
    $inputPipe.Write($key,0,$key.Length)
    $inputPipe.Flush()
    do{
      $line=$hostProcess.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(15)).GetAwaiter().GetResult()
      if($null -eq $line){throw 'Service exited before F02 cut'}
      $trace.Add($line)
      $cut=[regex]::Match($line,'^F02_CUT service=(\d+) client=(\d+) block=([0-9a-f-]{36}) hash=([0-9a-fA-F]{128})$')
    }until($cut.Success)
    if([int]$cut.Groups[1].Value -ne $hostProcess.Id){throw 'F02 service PID mismatch'}
    $native=[Diagnostics.Process]::GetProcessById([int]$cut.Groups[2].Value)
    $null=$native.SafeHandle
    if($native.HasExited -or $native.MainModule.FileName -ne $client){throw 'F02 native process mismatch'}
    $nativeVerified=$true
    $inputPipe.WriteByte(1)
    $hostProcess.StandardInput.Close()
    if(!$hostProcess.WaitForExit(10000) -or $hostProcess.ExitCode -ne 82){throw 'F02 service exit not verified'}
    if(!$native.WaitForExit(10000)){throw 'F02 native process did not exit'}
    $trace.Add($hostProcess.StandardOutput.ReadToEnd())
    [IO.File]::WriteAllText("$b\F02-host.err",$stderr.GetAwaiter().GetResult())
    $receipt="F02 service=$($hostProcess.Id) exit=$($hostProcess.ExitCode) native=$($native.Id) exit=$($native.ExitCode)"
    Add-Content "$b\commands.txt" "$receipt command=$StoredCommitHost --cut-before-progress $projection $scratch"
    # publication-client returns 7 only for SQLITE_IOERR, after sqlite3_close succeeds.
    if($native.ExitCode -ne 7){throw "F02 native COMMIT outcome: $receipt"}
    Write-Output $receipt
  }catch{
    $failure=$_
  }finally{
    try{$cleanupReceipts.Add((Stop-F02Process $hostProcess 'service' $true))}
    catch{$cleanupErrors.Add($_.Exception)}
    if($nativeVerified){
      try{$cleanupReceipts.Add((Stop-F02Process $native 'native' $false))}
      catch{$cleanupErrors.Add($_.Exception)}
    }
    if($native){$native.Dispose()}
    $hostProcess.Dispose()

    # A failed trace or receipt must neither skip a process wait nor mask its outcome.
    try{[IO.File]::WriteAllLines("$b\F02-host.out",$trace)}
    catch{$cleanupErrors.Add($_.Exception)}
    if($failure){$cleanupReceipts.Add("F02 failure=$($failure.Exception.Message)")}
    foreach($errorDetail in $cleanupErrors){$cleanupReceipts.Add("F02 cleanup error=$($errorDetail.Message)")}
    try{[IO.File]::AppendAllLines("$b\commands.txt",$cleanupReceipts)}
    catch{$cleanupErrors.Add($_.Exception)}
  }
  if($cleanupErrors.Count){
    if($failure){$cleanupErrors.Insert(0,$failure.Exception)}
    throw [AggregateException]::new('F02 verification failed',$cleanupErrors)
  }
  if($failure){throw $failure}
}

function Get-F02HeadHash([string]$journal){
  $entries=@(Get-ChildItem -LiteralPath $journal -Force)
  if($entries.Count -ne 1 -or $entries[0].Name -cne 'head' -or $entries[0].Length -ne 108){
    throw 'F02 journal contains unexpected files or head size'
  }
  return (Get-FileHash -LiteralPath $entries[0].FullName).Hash
}

function Test-F02Observation([string]$projectionHash){
  $root=Join-Path $d 'F02'
  $scratch=[IO.Directory]::CreateDirectory((Join-Path $b 'F02-storage')).FullName
  $serviceKey=[Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
  Run $exe @('--instance-root',$root,'block','export','--output',"$b\F02-genesis.ddlb") 'F02-before-cut' 0
  if((Get-Content "$b\F02-before-cut.out" -Raw | ConvertFrom-Json).data.block_count -ne 1){throw 'F02 initial count'}
  Invoke-F02Cut "$root.DLDB" $scratch $serviceKey
  $trace=Get-Content "$b\F02-host.out" -Raw
  $launch=[regex]::Match($trace,'LAUNCH_EXPECTED scenario=StoredCommit instance=\S+ enrolled=True block=(\S+) table=(\S+) row=(\S+)')
  if(!$launch.Success -or $launch.Groups[2].Value.Replace('-','') -ne '11111111111111111111111111111111'){throw 'F02 launch'}
  $id=$launch.Groups[1].Value
  $row=$launch.Groups[3].Value
  $body='01'+$launch.Groups[2].Value.Replace('-','')+$row.Replace('-','')+'00'
  $cut=[regex]::Match($trace,"F02_CUT service=(\d+) client=(\d+) block=$id hash=([0-9a-fA-F]{128})")
  if(!$cut.Success){throw 'F02 cut marker missing'}
  $hash=$cut.Groups[3].Value
  $journal=Join-Path $scratch ('journal\'+$id.Replace('-',''))
  $head=Get-F02HeadHash $journal
  $gate=$trace.IndexOf("STORED_GATE block=$id sequence=0 headSha256=$head",[StringComparison]::OrdinalIgnoreCase)
  $published=$trace.IndexOf("block=$id event=$body hash=$hash",[StringComparison]::OrdinalIgnoreCase)
  if($gate -lt 0 -or $published -le $gate -or $cut.Index -le $published -or
     [regex]::Matches($trace,"SUBMISSION_RECEIVED scenario=StoredCommit block=$id").Count -ne 1 -or
     [regex]::Matches($trace,'PUBLISH_RETURNED').Count -ne 1 -or $trace -match 'STORED_PROGRESS|REPLY pid='){
    throw 'F02 submission, retained readback or cut ordering mismatch'
  }
  Run $exe @('--instance-root',$root,'block','export','--output',"$b\F02-cut.ddlb") 'F02-cut-inventory' 0
  if((Get-Content "$b\F02-cut-inventory.out" -Raw | ConvertFrom-Json).data.block_count -ne 2){throw 'F02 inventory count'}
  $inventory=(Get-FileHash "$b\F02-cut.ddlb").Hash
  Run $exe @('--instance-root',$root,'block','get','--id',$id,'--output',"$b\F02.raw") 'F02-retained' 0
  $raw=[IO.File]::ReadAllBytes("$b\F02.raw")
  $signer=(Get-Content "$b\F02-init.out" -Raw | ConvertFrom-Json).data.public_key_hex
  if($raw.Length -ne 261 -or [Convert]::ToHexString($raw,34,16) -ne $id.Replace('-','') -or
     [Convert]::ToHexString($raw,58,32) -ne $signer -or [Convert]::ToHexString($raw,163,34) -ne $body -or
     (Get-FileHash "$b\F02.raw" -Algorithm SHA512).Hash -ne $hash){throw 'F02 exact signed bytes differ'}
  Confirm-BlockSignature 'F02' $raw
  foreach($mode in 'valid','wrong-key','wrong-hash'){
    $key=if($mode -eq 'wrong-key'){[Security.Cryptography.RandomNumberGenerator]::GetBytes(32)}else{$serviceKey}
    $expectedHash=if($mode -eq 'wrong-hash'){'0'*128}else{$hash}
    $exit=if($mode -eq 'valid'){3}else{4}
    Run $StoredCommitHost @('--observe-before-progress',$root,$scratch,$row,$id,$body,$expectedHash) "F02-$mode" $exit $key
    $pattern=if($mode -eq 'valid'){
      "^F02_OBSERVED pid=(\d+) block=$id hash=$hash sequence=0 records=0 tail=none checkpoint=unconfirmed publicationCalls=0$"
    }else{'^F02_REFUSED pid=(\d+) publicationCalls=0$'}
    $receipt=[regex]::Match((Get-Content "$b\F02-$mode.out" -Raw).Trim(),$pattern)
    if(!$receipt.Success -or $receipt.Groups[1].Value -eq $cut.Groups[1].Value){throw "F02 $mode observation receipt"}
    Run $exe @('--instance-root',$root,'block','export','--output',"$b\F02-$mode.ddlb") "F02-$mode-inventory" 0
    if((Get-F02HeadHash $journal) -ne $head -or (Get-FileHash "$b\F02-$mode.ddlb").Hash -ne $inventory){
      throw "F02 $mode observation changed journal or inventory"
    }
  }
  if((Get-FileHash "$root.DLDB").Hash -ne $projectionHash){throw 'F02 projection changed'}
  foreach($suffix in '-journal','-wal','-shm'){if(Test-Path -LiteralPath "$root.DLDB$suffix"){throw "F02 sidecar $suffix"}}
  "F02 PASS block=$id hash=$hash journalSha256=$head inventorySha256=$inventory checkpoint=unconfirmed"
}

function Test-StoredPublication([string]$case,[string]$option,[string]$projectionHash){
  $root=Join-Path $d $case
  $projection="$root.DLDB"
  # Keep test receipts out of the directory monitored for projection writes.
  $scratch=[IO.Directory]::CreateDirectory((Join-Path $b "$case-storage")).FullName
  Run $exe @('--instance-root',$root,'block','export','--output',"$b\$case-before.ddlb") "$case-before" 0
  if($case -eq 'StoredCommit'){
    $serviceKey=[Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    Run $StoredCommitHost @('--start-restart-fixture',$projection,$scratch) "$case-host" 0 $serviceKey
  }else{
    Run $StoredCommitHost @('--stored-commit-fixture',$projection,$option,$scratch) "$case-host" 0
  }
  Run $exe @('--instance-root',$root,'block','export','--output',"$b\$case-after.ddlb") "$case-after" 0

  $trace=Get-Content "$b\$case-host.out" -Raw
  $launch=[regex]::Match($trace,"LAUNCH_EXPECTED scenario=$case instance=(\S+) enrolled=True block=(\S+) table=(\S+) row=(\S+)")
  if(!$launch.Success -or [regex]::Matches($trace,'SUBMISSION_RECEIVED').Count -ne 1 -or
     !$trace.Contains('SQL [BEGIN] rc=101') -or
     !$trace.Contains('SQL [INSERT INTO items(row_guid) VALUES(?1)] rc=101')){throw "$case native transaction evidence incomplete"}
  if((Get-FileHash -LiteralPath $projection).Hash -ne $projectionHash){throw "$case projection changed"}
  foreach($suffix in '-journal','-wal','-shm'){
    if(Test-Path -LiteralPath "$projection$suffix"){throw "$case unexpected sidecar"}
  }

  $id=$launch.Groups[2].Value
  $idHex=$id.Replace('-','')
  $intent=Join-Path $scratch "intents\$idHex"
  $journal=Join-Path $scratch "journal\$idHex"
  $body='01'+$launch.Groups[3].Value.Replace('-','')+$launch.Groups[4].Value.Replace('-','')+'00'
  $payload=[IO.File]::ReadAllBytes("$intent\payload")
  if([Convert]::ToHexString($payload) -ne $body){throw "$case stored native event differs"}
  $digest=(Get-FileHash -LiteralPath "$intent\payload" -Algorithm SHA256).Hash
  $instance=(Get-Content "$b\$case-init.out" -Raw | ConvertFrom-Json).data
  $genesis=(Get-Content "$b\$case-genesis.out" -Raw | ConvertFrom-Json).data
  $intentBytes=[IO.File]::ReadAllBytes("$intent\manifest")
  if($intentBytes.Length -ne 248 -or [Text.Encoding]::ASCII.GetString($intentBytes,0,8) -ne 'UDDLFIM1' -or
     [Convert]::ToHexString($intentBytes,8,16) -ne $idHex -or
     [Convert]::ToHexString($intentBytes,24,16) -ne $launch.Groups[4].Value.Replace('-','') -or
     $launch.Groups[1].Value -ne $instance.instance_guid -or
     [Convert]::ToHexString($intentBytes,40,16) -ne $instance.instance_guid.Replace('-','') -or
     [Convert]::ToHexString($intentBytes,56,32) -ne $instance.public_key_hex -or
     [Convert]::ToHexString($intentBytes,88,32) -ne $genesis.ledger_id_hex -or
     [Convert]::ToHexString($intentBytes,120,64) -ne $genesis.genesis_sha512 -or
     [BitConverter]::ToInt32($intentBytes,192) -ne 1 -or
     [Convert]::ToHexString($intentBytes,196,16) -ne $idHex -or
     [BitConverter]::ToInt32($intentBytes,212) -ne $payload.Length -or
     [Convert]::ToHexString($intentBytes,216,32) -ne $digest){throw "$case stored intent binding differs"}
  $countBefore=(Get-Content "$b\$case-before.out" -Raw | ConvertFrom-Json).data.block_count
  $countAfter=(Get-Content "$b\$case-after.out" -Raw | ConvertFrom-Json).data.block_count
  if($countBefore -ne 1){throw "$case fixture is not genesis-only"}
  $rawPath="$b\$case.raw"
  if($case -eq 'StoredRefused'){
    Run $exe @('--instance-root',$root,'block','get','--id',$id,'--output',$rawPath) "$case-absent" 4
    if($countAfter -ne 1 -or (Get-FileHash "$b\$case-before.ddlb").Hash -ne (Get-FileHash "$b\$case-after.ddlb").Hash -or
       (Get-Content "$b\$case-absent.out" -Raw | ConvertFrom-Json).error.code -ne 'BLOCK_NOT_FOUND' -or
       (Test-Path -LiteralPath "$intent\ready") -or (Test-Path -LiteralPath $journal) -or
       !$trace.Contains("STORED_READY_FAULT block=$id") -or !$trace.Contains("STORED_INTENT_REFUSED request=$id readiness=Restricted") -or
       !$trace.Contains("enrolled=True sameLaunch=True block=$id") -or !$trace.Contains('status=2') -or
       !$trace.Contains('SQL [COMMIT] rc=23') -or $trace.Contains('STORED_GATE') -or $trace.Contains('PUBLISH_RETURNED')){
      throw "$case pre-publication refusal evidence incomplete"
    }
    Write-Output "$case PASS: native refusal, no publication and identical retained inventory"
    return
  }

  $gate=[regex]::Match($trace,"STORED_GATE block=$id sequence=0 headSha256=[0-9A-F]{64}")
  $publication=[regex]::Match($trace,"PUBLISH_RETURNED pid=\d+ instance=$($launch.Groups[1].Value) block=$id event=$body hash=(\S+)",[Text.RegularExpressions.RegexOptions]::IgnoreCase)
  $progress=[regex]::Match($trace,"STORED_PROGRESS block=$id record=(\S+) sequence=1 headSha256=([0-9A-F]{64})")
  $transport=if($case -eq 'StoredLost'){"STORED_REPLY_DROPPED block=$id"}else{"enrolled=True sameLaunch=True block=$id event=" + $body.ToUpperInvariant() + ' status=1'}
  $nativeResult=if($case -eq 'StoredLost'){'SQL [COMMIT] rc=10'}else{'SQL [COMMIT] rc=101'}
  if(!$gate.Success -or !$publication.Success -or !$progress.Success -or $countAfter -ne 2 -or
     [regex]::Matches($trace,'PUBLISH_RETURNED').Count -ne 1){throw "$case expected one retained publication and verified journal progression"}
  $last=-1
  foreach($marker in "STORED_INTENT_READY request=$id bodySha256=$digest",$gate.Value,$publication.Value,$progress.Value,$transport,$nativeResult){
    $at=$trace.IndexOf($marker,[StringComparison]::Ordinal)
    if($at -le $last){throw "$case trace missing or out of order: $marker"}
    $last=$at
  }
  if($case -eq 'StoredLost' -and (!$trace.Contains('STORED_NO_RESUBMISSION connections=1 requests=1') -or
     !$trace.Contains('LOST_REPLY original=IOERR further_dml=AUTH further_commit=AUTH') -or
     $trace.Contains('SQL [COMMIT] rc=101') -or $trace.Contains('REPLY pid='))){throw "$case uncertainty or no-resend evidence incomplete"}

  Run $exe @('--instance-root',$root,'block','get','--id',$id,'--output',$rawPath) "$case-retained" 0
  $raw=[IO.File]::ReadAllBytes($rawPath)
  if($raw.Length -ne 261 -or [Convert]::ToHexString($raw,34,16) -ne $idHex -or
     [Convert]::ToHexString($raw,58,32) -ne $instance.public_key_hex -or
     [Convert]::ToHexString($raw,163,34) -ne $body -or
     (Get-FileHash -LiteralPath $rawPath -Algorithm SHA512).Hash -ne $publication.Groups[1].Value){throw "$case signed block differs from stored payload"}
  Confirm-BlockSignature $case $raw

  $manifest=(Get-FileHash -LiteralPath "$intent\manifest" -Algorithm SHA256).Hash
  $ready=[IO.File]::ReadAllBytes("$intent\ready")
  $head=[IO.File]::ReadAllBytes("$journal\head")
  $record=[IO.File]::ReadAllBytes("$journal\0001.rec")
  if($ready.Length -ne 40 -or [Text.Encoding]::ASCII.GetString($ready,0,8) -ne 'UDDLFIR1' -or
     [Convert]::ToHexString($ready,8,32) -ne $manifest -or $head.Length -ne 108 -or $record.Length -ne 177 -or
     [Text.Encoding]::ASCII.GetString($head,0,8) -ne 'UDDLJHD1' -or [Text.Encoding]::ASCII.GetString($record,0,8) -ne 'UDDLJRC1' -or
     [Convert]::ToHexString($head,8,32) -ne $manifest -or [Convert]::ToHexString($record,8,32) -ne $manifest -or
     [BitConverter]::ToInt32($head,40) -ne 1 -or [BitConverter]::ToInt32($record,40) -ne 1 -or
     [Convert]::ToHexString($head,44,32) -ne (Get-FileHash -LiteralPath "$journal\0001.rec" -Algorithm SHA256).Hash -or
     [Convert]::ToHexString($record,77,16) -ne $progress.Groups[1].Value.Replace('-','') -or
     [BitConverter]::ToInt32($record,93) -ne 0 -or [Convert]::ToHexString($record,97,16) -ne $idHex -or
     [Convert]::ToHexString($record,113,32) -ne $digest -or
     (Get-FileHash -LiteralPath "$journal\head" -Algorithm SHA256).Hash -ne $progress.Groups[2].Value){
    throw "$case journal or readiness files disagree with the fixture receipts"
  }
  if($case -eq 'StoredCommit'){
    $original=[regex]::Match($trace,'SERVICE_START pid=(\d+)')
    if(!$original.Success){throw 'Missing original service PID'}
    $recover=@('--recover-stored-fixture',$root,$scratch,$launch.Groups[4].Value,$id,$body)
    foreach($mode in 'valid','wrong-key','unreadable'){
      $key=if($mode -eq 'wrong-key'){[Security.Cryptography.RandomNumberGenerator]::GetBytes(32)}else{$serviceKey}
      $locked=$null
      try{
        if($mode -eq 'unreadable'){
          $locked=[IO.File]::Open("$journal\head",'Open','Read','None')
        }
        $expected=if($mode -eq 'valid'){0}else{4}
        Run $StoredCommitHost $recover "$case-restart-$mode" $expected $key
      }finally{if($locked){$locked.Dispose()}}
      $recognized=($mode -eq 'valid').ToString()
      $receipt=Get-Content "$b\$case-restart-$mode.out" -Raw
      $result=[regex]::Match($receipt,"(?m)^RECOVERY pid=(\d+) block=$id recognized=$recognized publicationCalls=0\r?$")
      if(!$result.Success -or $result.Groups[1].Value -eq $original.Groups[1].Value){throw "$mode recovery outcome or PID"}
      Run $exe @('--instance-root',$root,'block','export','--output',"$b\$case-$mode.ddlb") "$case-$mode-inventory" 0
      if((Get-FileHash "$b\$case-$mode.ddlb").Hash -ne (Get-FileHash "$b\$case-after.ddlb").Hash){
        throw "$mode restart changed the signed inventory"
      }
    }
    if((Get-FileHash -LiteralPath $projection).Hash -ne $projectionHash){throw 'Restart changed projection'}
    foreach($suffix in '-journal','-wal','-shm'){
      if(Test-Path -LiteralPath "$projection$suffix"){throw "Restart left $suffix"}
    }
  }
  Write-Output "$case PASS: exact stored event, independently verified signature, one durable block, $nativeResult"
}
$ordinary=Join-Path $b 'ordinary.db'
Run $client @('--create-seed',$ordinary) 'create' 0
$image=[IO.File]::ReadAllBytes($ordinary)
Run $client @($ordinary,'22222222222222222222222222222222') 'ordinary' 0
Run $client @($ordinary,'22222222222222222222222222222222') 'ordinary-reopen' 5
# Convert only this disposable seed image. Never hand an official file to the
# setup path. Managed target has the accepted 16-byte signature and sibling root.
$signature=[Text.Encoding]::ASCII.GetBytes("UDDL database 1"+[char]0)
$signature.CopyTo($image,0)
Run $exe @('--instance-root',$l,'instance','init') 'instance' 0
Run $exe @('--instance-root',$l,'ledger','init') 'genesis' 0
[IO.File]::WriteAllBytes($db,$image)
$before=(Get-FileHash -LiteralPath $db).Hash
$r=Join-Path $d 'replica'
$rdb=Join-Path $d 'replica.DLDB'
$gen=(Get-Content "$b\genesis.out" -Raw | ConvertFrom-Json).data
Run $exe @('--instance-root',$l,'block','get','--id',$gen.genesis_block_guid,'--output',"$b\genesis.raw") 'genesis-read' 0
if((Get-FileHash "$b\genesis.raw" -Algorithm SHA512).Hash -ne $gen.genesis_sha512){throw 'Genesis pin mismatch'}
Run $exe @('--instance-root',$r,'instance','init') 'replica-init' 0
Run $exe @('--instance-root',$r,'ledger','join','--ledger-id',$gen.ledger_id_hex,'--genesis-sha512',$gen.genesis_sha512,'--genesis-file',"$b\genesis.raw") 'replica-join' 0
Run $exe @('--instance-root',$r,'status') 'replica-status' 0
Run $exe @('--instance-root',$l,'status') 'master-status' 0
$rs=(Get-Content "$b\replica-status.out" -Raw | ConvertFrom-Json).data
$ms=(Get-Content "$b\master-status.out" -Raw | ConvertFrom-Json).data
foreach($s in $rs,$ms){
  if($s.instance.state -ne 'bound' -or $s.instance.recovery_codes.Count -ne 0 -or
     $s.signing.available -ne $true -or $s.binding.ledger_id_hex -ne $gen.ledger_id_hex -or
     $s.binding.genesis_sha512 -ne $gen.genesis_sha512 -or $s.blocks.retained_count -ne 1){throw 'Initial binding or inventory'}
}
if($rs.instance.instance_guid -eq $ms.instance.instance_guid -or $rs.instance.public_key_hex -eq $ms.instance.public_key_hex -or
   $rs.signing.authorized -ne $false -or $ms.signing.authorized -ne $true){throw 'Local roles or identities differ from fixture expectations'}
[IO.File]::WriteAllBytes($rdb,$image)
$replicaHash=(Get-FileHash $rdb).Hash
# Three independent ledger roots and frozen input images. The ADD/DROP fixtures
# have a signed notes definition before the native transaction starts.
$notes='33'*16
$createBody='10'+$notes+'056E6F74657301'+('44'*16)+'017604424C4F420100'
$addBody='13'+$notes+('55'*16)+'0374616704544558540100'
$dropBody='11'+$notes
[IO.File]::WriteAllBytes("$b\notes-definition.event",[Convert]::FromHexString($createBody))
Run $client @('--notes-seed',"$b\ordinary-notes.db") 'ordinary-notes' 0
$notesImage=[IO.File]::ReadAllBytes("$b\ordinary-notes.db")
$signature.CopyTo($notesImage,0)
$schemaHashes=@{}
foreach($case in 'SchemaCreate','SchemaAdd','SchemaDrop'){
  $root=Join-Path $d $case
  Run $exe @('--instance-root',$root,'instance','init') "$case-init" 0
  Run $exe @('--instance-root',$root,'ledger','init') "$case-genesis" 0
  if($case -eq 'SchemaCreate'){[IO.File]::WriteAllBytes("$root.DLDB",$image)}
  else{
    Run $exe @('--instance-root',$root,'block','publish','--event-file',"$b\notes-definition.event") "$case-definition" 0
    [IO.File]::WriteAllBytes("$root.DLDB",$notesImage)
  }
  $schemaHashes[$case]=(Get-FileHash "$root.DLDB").Hash
}
$storedCases=[ordered]@{StoredCommit='--stored-commit';StoredRefused='--stored-refused';StoredLost='--stored-lost'}
$storedHashes=@{}
if($StoredCommitHost){
  $StoredCommitHost=(Resolve-Path -LiteralPath $StoredCommitHost).Path
  foreach($case in (@($storedCases.Keys) + 'F02')){
    $root=Join-Path $d $case
    Run $exe @('--instance-root',$root,'instance','init') "$case-init" 0
    Run $exe @('--instance-root',$root,'ledger','init') "$case-genesis" 0
    [IO.File]::WriteAllBytes("$root.DLDB",$image)
    $storedHashes[$case]=(Get-FileHash -LiteralPath "$root.DLDB").Hash
  }
}
$watch=[IO.FileSystemWatcher]::new($d)
$watch.NotifyFilter=[IO.NotifyFilters]'FileName,LastWrite,Size'
foreach($e in 'Created','Changed','Deleted','Renamed','Error'){Register-ObjectEvent $watch $e -SourceIdentifier "projection-$e" | Out-Null}
$watch.EnableRaisingEvents=$true
try{
  [IO.File]::WriteAllText("$d\control-before",'before')
  Run $exe @('--instance-root',$r,'block','export','--output',"$b\replica-before.ddlb") 'replica-before' 0
  Run $exe @('serve-projection-write',$rdb,'--commit-control') 'replica-write' 0
  $trace=Get-Content "$b\replica-write.out" -Raw
  $attempt=[regex]::Match($trace,'LAUNCH_EXPECTED scenario=Enrolled instance=(\S+) enrolled=True block=(\S+)')
  $id=$attempt.Groups[2].Value
  if(!$attempt.Success -or $attempt.Groups[1].Value -ne $rs.instance.instance_guid -or
     !$trace.Contains("INITIAL_REPLICA_REFUSED instance=$($rs.instance.instance_guid) block=$id genesis=$($gen.genesis_sha512)") -or
     !$trace.Contains("enrolled=True sameLaunch=True block=$id") -or !$trace.Contains('status=2') -or
     [regex]::Matches($trace,'SUBMISSION_RECEIVED').Count -ne 1 -or $trace.Contains('PUBLISH_RETURNED') -or
     !$trace.Contains('SQL [BEGIN] rc=101') -or !$trace.Contains('SQL [INSERT INTO items(row_guid) VALUES(?1)] rc=101') -or
     !$trace.Contains('SQL [COMMIT] rc=23') -or !$trace.Contains('publication rejected')){throw 'Replica rejection evidence incomplete'}
  Run $exe @('--instance-root',$r,'block','export','--output',"$b\replica-after.ddlb") 'replica-after' 0
  if((Get-FileHash "$b\replica-before.ddlb").Hash -ne (Get-FileHash "$b\replica-after.ddlb").Hash -or
     (Get-Content "$b\replica-after.out" -Raw | ConvertFrom-Json).data.block_count -ne 1){throw 'Replica inventory changed'}
  Run $exe @('--instance-root',$r,'block','get','--id',$id,'--output',"$b\unexpected-replica.raw") 'replica-absent' 4
  if((Get-Content "$b\replica-absent.out" -Raw | ConvertFrom-Json).error.code -ne 'BLOCK_NOT_FOUND'){throw 'Unexpected replica lookup'}
  if((Get-FileHash $rdb).Hash -ne $replicaHash){throw 'Replica projection changed'}
  foreach($s in '-journal','-wal','-shm'){if(Test-Path -LiteralPath "$rdb$s"){throw "Unexpected replica $s"}}
  Run $exe @('--instance-root',$l,'block','export','--output',"$b\before.ddlb") 'inventory-before' 0
  Run $exe @('serve-projection-write',$db,'--rollback') 'rollback' 0
  $rollbackLog=Get-Content "$b\rollback.out" -Raw
  $match=[regex]::Match($rollbackLog,'LAUNCH_EXPECTED scenario=Rollback instance=\S+ enrolled=True block=(\S+) table=(\S+) row=(\S+)')
  if(!$match.Success -or !$rollbackLog.Contains('SQL [BEGIN] rc=101') -or
     !$rollbackLog.Contains('SQL [INSERT INTO items(row_guid) VALUES(?1)] rc=101') -or
     !$rollbackLog.Contains('SQL [ROLLBACK] rc=101') -or !$rollbackLog.Contains('CLOSE rc=0') -or
     !$rollbackLog.Contains('connections=0 requests=0') -or $rollbackLog.Contains('SUBMISSION_RECEIVED')){throw 'Rollback evidence incomplete'}
  Run $exe @('--instance-root',$l,'block','export','--output',"$b\after.ddlb") 'inventory-after' 0
  if((Get-FileHash "$b\before.ddlb").Hash -ne (Get-FileHash "$b\after.ddlb").Hash -or
    (Get-Content "$b\inventory-after.out" -Raw | ConvertFrom-Json).data.block_count -ne 1){throw 'Rollback changed inventory'}
  Run $exe @('--instance-root',$l,'block','get','--id',$match.Groups[1].Value,'--output',"$b\unexpected.raw") 'rollback-read' 4
  if((Get-Content "$b\rollback-read.out" -Raw | ConvertFrom-Json).error.code -ne 'BLOCK_NOT_FOUND'){throw 'Unexpected rollback lookup'}
  Run $exe @('serve-projection-write',$db,'--commit-control') 'positive' 0
  $positive=Get-Content "$b\positive.out" -Raw
  $commit=[regex]::Match($positive,'LAUNCH_EXPECTED scenario=Enrolled instance=\S+ enrolled=True block=(\S+) table=(\S+) row=(\S+)')
  if(!$commit.Success -or $commit.Groups[1].Value -eq $match.Groups[1].Value -or
     $commit.Groups[3].Value -eq $match.Groups[3].Value -or [regex]::Matches($positive,'SUBMISSION_RECEIVED').Count -ne 1 -or
     !$positive.Contains('SQL [COMMIT] rc=101') -or !$positive.Contains('status=1')){throw 'Commit control evidence incomplete'}
  Run $exe @('--instance-root',$l,'block','export','--output',"$b\control.ddlb") 'inventory-control' 0
  if((Get-Content "$b\inventory-control.out" -Raw | ConvertFrom-Json).data.block_count -ne 2){throw 'Expected exactly one new block'}
  Run $exe @('--instance-root',$l,'block','get','--id',$commit.Groups[1].Value,'--output',"$b\control.raw") 'control-read' 0
  $control=[IO.File]::ReadAllBytes("$b\control.raw")
  $event='01'+$commit.Groups[2].Value.Replace('-','')+$commit.Groups[3].Value.Replace('-','')+'00'
  if($control.Length -ne 261 -or [Convert]::ToHexString($control,34,16) -ne $commit.Groups[1].Value.Replace('-','') -or
     [Convert]::ToHexString($control,163,34) -ne $event){throw 'Positive control signed content differs'}
  $expectedCount=2
  foreach($case in 'Discard','Retain'){
    $option=if($case -eq 'Discard'){'--discard-at-savepoint'}else{'--retain-at-savepoint'}
    Run $exe @('serve-projection-write',$db,$option) $case 0
    $trace=Get-Content "$b\$case.out" -Raw
    $launch=[regex]::Match($trace,"LAUNCH_EXPECTED scenario=$case instance=\S+ enrolled=True block=(\S+) table=(\S+) row=(\S+)")
    $expectedInserts=if($case -eq 'Discard'){2}else{1}
    if(!$launch.Success -or !$trace.Contains('SQL [SAVEPOINT staged] rc=101') -or
       [regex]::Matches($trace,'SQL \[INSERT INTO items\(row_guid\) VALUES\(\?1\)\] rc=101').Count -ne $expectedInserts -or
       ($case -eq 'Discard' -and !$trace.Contains('SQL [ROLLBACK TO staged] rc=101')) -or
       !$trace.Contains('SQL [RELEASE staged] rc=101') -or !$trace.Contains('BEFORE_OUTER_COMMIT connections=0 requests=0') -or
       [regex]::Matches($trace,'SUBMISSION_RECEIVED').Count -ne 1 -or !$trace.Contains('SQL [COMMIT] rc=101') -or
       !$trace.Contains('status=1')){throw "$case transaction or IPC evidence incomplete"}
    $id=$launch.Groups[1].Value
    $row=$launch.Groups[3].Value.Replace('-','')
    $event='01'+$launch.Groups[2].Value.Replace('-','')+$row+'00'
    Run $exe @('--instance-root',$l,'block','get','--id',$id,'--output',"$b\$case.raw") "$case-read" 0
    $raw=[IO.File]::ReadAllBytes("$b\$case.raw")
    if($raw.Length -ne 261 -or [Convert]::ToHexString($raw,34,16) -ne $id.Replace('-','') -or
       [Convert]::ToHexString($raw,163,34) -ne $event){throw "$case signed event mismatch"}
    Run $exe @('--instance-root',$l,'block','export','--output',"$b\$case.ddlb") "$case-export" 0
    $expectedCount++
    if((Get-Content "$b\$case-export.out" -Raw | ConvertFrom-Json).data.block_count -ne $expectedCount){throw "$case inventory count"}
    if($case -eq 'Discard'){
      $discarded=[Convert]::FromHexString($row)
      $discarded[0]=$discarded[0] -bxor 128
      $inventory=[IO.File]::ReadAllBytes("$b\$case.ddlb")
      $offset=14
      for($i=0;$i -lt $expectedCount;$i++){
        $length=[System.Buffers.Binary.BinaryPrimitives]::ReadUInt64BigEndian([byte[]]$inventory[$offset..($offset+7)])
        $offset+=8
        if($length -eq 261 -and $inventory[$offset+163] -eq 1 -and
           [Convert]::ToHexString($inventory,$offset+180,16) -eq [Convert]::ToHexString($discarded)){throw 'Discarded row retained'}
        $offset+=[int]$length
      }
      if($offset -ne $inventory.Length){throw 'Incomplete inventory inspection'}
    }
  }
  foreach($case in 'Outer','OuterDenied','OuterLost'){
    $option=@{Outer='--outer-release';OuterDenied='--outer-denied';OuterLost='--outer-lost'}[$case]
    Run $exe @('serve-projection-write',$db,$option) $case 0
    $trace=Get-Content "$b\$case.out" -Raw
    $launch=[regex]::Match($trace,"LAUNCH_EXPECTED scenario=$case instance=\S+ enrolled=\w+ block=(\S+) table=(\S+) row=(\S+)")
    $expected=@{Outer='rc=101';OuterDenied='rc=23 message=publication rejected';OuterLost='rc=10 message=publication outcome unknown after submission'}[$case]
    if(!$launch.Success -or !$trace.Contains('SQL [SAVEPOINT staged] rc=101') -or
       !$trace.Contains('SQL [INSERT INTO items(row_guid) VALUES(?1)] rc=101') -or
       !$trace.Contains('BEFORE_OUTER_RELEASE connections=0 requests=0 block=absent') -or
       [regex]::Matches($trace,'SUBMISSION_RECEIVED').Count -ne 1 -or !$trace.Contains("SQL [RELEASE staged] $expected") -or
       ($case -ne 'Outer' -and !$trace.Contains('rc=23 message=unsupported managed transaction'))){throw "$case SQL or service evidence incomplete"}
    $id=$launch.Groups[1].Value
    $published=$case -ne 'OuterDenied'
    Run $exe @('--instance-root',$l,'block','get','--id',$id,'--output',"$b\$case.raw") "$case-read" $(if($published){0}else{4})
    if($published){
      $expectedCount++
      $raw=[IO.File]::ReadAllBytes("$b\$case.raw")
      $body='01'+$launch.Groups[2].Value.Replace('-','')+$launch.Groups[3].Value.Replace('-','')+'00'
      if($raw.Length -ne 261 -or [Convert]::ToHexString($raw,34,16) -ne $id.Replace('-','') -or
         [Convert]::ToHexString($raw,163,34) -ne $body){throw "$case signed content mismatch"}
    }elseif((Get-Content "$b\$case-read.out" -Raw | ConvertFrom-Json).error.code -ne 'BLOCK_NOT_FOUND'){throw 'Denial lookup mismatch'}
    Run $exe @('--instance-root',$l,'block','export','--output',"$b\$case.ddlb") "$case-export" 0
    if((Get-Content "$b\$case-export.out" -Raw | ConvertFrom-Json).data.block_count -ne $expectedCount){throw "$case inventory count"}
  }
  Run $exe @('serve-projection-write',$db) 'host' 0
  Run $exe @('--instance-root',$l,'block','export','--output',"$b\dml-before.ddlb") 'dml-before' 0
  Run $exe @('serve-projection-write',$db,'--effective-dml') 'effective-dml' 0
  $trace=Get-Content "$b\effective-dml.out" -Raw
  $runs=@($trace -split '(?=LAUNCH_EXPECTED )' | Where-Object {$_ -match '^LAUNCH_EXPECTED '})
  if($runs.Count -ne 5){throw 'Missing effective-DML launch'}
  foreach($case in 'Unchanged','Missing','UpdateRollback','Update','Delete'){
    $part=@($runs | Where-Object {$_.StartsWith("LAUNCH_EXPECTED scenario=$case ")})
    if($part.Count -ne 1){throw "$case launch count"}
    $part=$part[0]
    $launch=[regex]::Match($part,'enrolled=True block=(\S+) table=(\S+) row=(\S+)')
    if(!$launch.Success -or !$part.Contains('SQL [BEGIN] rc=101')){throw "$case transaction"}
    $id=$launch.Groups[1].Value
    $update=$case -in 'Update','Unchanged','UpdateRollback'
    $statement=if($update){'UPDATE items SET value=?2 WHERE row_guid=?1 AND value IS NOT ?2'}else{'DELETE FROM items WHERE row_guid=?1'}
    if(!$part.Contains("SQL [$statement] rc=101")){throw "$case native statement"}
    $boundary=if($case -eq 'UpdateRollback'){'ROLLBACK'}else{'COMMIT'}
    if(!$part.Contains("SQL [$boundary] rc=101")){throw "$case native boundary"}
    if($case -notin 'Update','Delete'){
      if(!$part.Contains('connections=0 requests=0') -or $part.Contains('SUBMISSION_RECEIVED')){throw "$case IPC"}
      Run $exe @('--instance-root',$l,'block','get','--id',$id,'--output',"$b\$case.raw") "$case-read" 4
      if((Get-Content "$b\$case-read.out" -Raw | ConvertFrom-Json).error.code -ne 'BLOCK_NOT_FOUND'){throw "$case absence"}
      continue
    }
    $table='11111111111111111111111111111111'
    $expected=if($update){'02'+$table+'01000000000000000000000000000000'+'01'+'22222222222222222222222222222222'+'040200FF'}else{'03'+$table+'02000000000000000000000000000000'}
    if([regex]::Matches($part,'SUBMISSION_RECEIVED').Count -ne 1 -or
       !$part.Contains("enrolled=True sameLaunch=True block=$id event=$expected status=1") -or
       !$part.Contains("block=$id event=$expected hash=")){throw "$case service correspondence"}
    Run $exe @('--instance-root',$l,'block','get','--id',$id,'--output',"$b\$case.raw") "$case-read" 0
    $raw=[IO.File]::ReadAllBytes("$b\$case.raw")
    $length=[int]($expected.Length/2)
    if($raw.Length -ne 163+$length+64 -or [Convert]::ToHexString($raw,34,16) -ne $id.Replace('-','') -or
       [Convert]::ToHexString($raw,163,$length) -ne $expected){throw "$case reopened signed content"}
  }
  Run $exe @('--instance-root',$l,'block','export','--output',"$b\dml-after.ddlb") 'dml-after' 0
  $old=(Get-Content "$b\dml-before.out" -Raw | ConvertFrom-Json).data.block_count
  if((Get-Content "$b\dml-after.out" -Raw | ConvertFrom-Json).data.block_count -ne $old+2){throw 'Unexpected effective-DML inventory'}
  foreach($case in 'SchemaCreate','SchemaAdd','SchemaDrop'){
    $root=Join-Path $d $case
    $option,$body,$sql=switch($case){
      'SchemaCreate' {'--create-notes';$createBody;'CREATE TABLE notes(v BLOB)'}
      'SchemaAdd' {'--add-tag';$addBody;'ALTER TABLE notes ADD COLUMN tag TEXT'}
      'SchemaDrop' {'--drop-notes';$dropBody;'DROP TABLE notes'}
    }
    Run $exe @('serve-projection-write',"$root.DLDB",$option) "$case-host" 0
    $trace=Get-Content "$b\$case-host.out" -Raw
    $launch=[regex]::Match($trace,"LAUNCH_EXPECTED scenario=$case instance=\S+ enrolled=True block=(\S+) table=(\S+)")
    $id=$launch.Groups[1].Value
    if(!$launch.Success -or $launch.Groups[2].Value.Replace('-','') -ne $notes -or
       !$trace.Contains("SQL [$sql] rc=101") -or !$trace.Contains('BEFORE_OUTER_COMMIT connections=0 requests=0') -or
       [regex]::Matches($trace,'SUBMISSION_RECEIVED').Count -ne 1 -or [regex]::Matches($trace,'PUBLISH_RETURNED').Count -ne 1 -or
       !$trace.Contains("sameLaunch=True block=$id event=$body status=1") -or !$trace.Contains('SQL [COMMIT] rc=101')){
      throw "$case transaction/ack"
    }
    foreach($silent in $(if($case -eq 'SchemaCreate'){'SchemaRollback';'SchemaUnsupported';'SchemaMixed'}elseif($case -eq 'SchemaAdd'){'SchemaDuplicate'})){
      $attempt=[regex]::Match($trace,"LAUNCH_EXPECTED scenario=$silent instance=\S+ enrolled=True block=(\S+)")
      if(!$attempt.Success -or !$trace.Contains("NO_SUBMISSION scenario=$silent")){throw "$silent observation"}
      Run $exe @('--instance-root',$root,'block','get','--id',$attempt.Groups[1].Value,'--output',"$b\$silent-unexpected.raw") "$silent-absent" 4
      if((Get-Content "$b\$silent-absent.out" -Raw | ConvertFrom-Json).error.code -ne 'BLOCK_NOT_FOUND'){throw "$silent absence"}
    }
    Run $exe @('--instance-root',$root,'block','export','--output',"$b\$case.ddlb") "$case-export" 0
    $expectedCount=if($case -eq 'SchemaCreate'){2}else{3}
    if((Get-Content "$b\$case-export.out" -Raw | ConvertFrom-Json).data.block_count -ne $expectedCount){throw "$case inventory"}
    Run $exe @('--instance-root',$root,'block','get','--id',$id,'--output',"$b\$case.raw") "$case-read" 0
    $raw=[IO.File]::ReadAllBytes("$b\$case.raw")
    $n=[int]($body.Length/2)
    if($raw.Length -ne 163+$n+64 -or [Convert]::ToHexString($raw,34,16) -ne $id.Replace('-','') -or
       [Convert]::ToHexString($raw,163,$n) -ne $body){throw "$case signed schema content"}
    $publicKey=(Get-Content "$b\$case-init.out" -Raw | ConvertFrom-Json).data.public_key_hex
    if([Convert]::ToHexString($raw,58,32) -ne $publicKey){throw "$case signer"}
    Confirm-BlockSignature $case $raw
    if((Get-FileHash "$root.DLDB").Hash -ne $schemaHashes[$case]){throw "$case projection changed"}
    foreach($s in '-journal','-wal','-shm'){if(Test-Path "$root.DLDB$s"){throw "$case unexpected sidecar"}}
  }
  if($StoredCommitHost){
    foreach($case in $storedCases.Keys){Test-StoredPublication $case $storedCases[$case] $storedHashes[$case]}
    Test-F02Observation $storedHashes['F02']
  }
  [IO.File]::WriteAllText("$d\control-after",'after')
  $clock=[Diagnostics.Stopwatch]::StartNew()
  do{
    $events=@(Get-Event | Where-Object SourceIdentifier -like 'projection-*')
    if(@($events | Where-Object {$_.SourceEventArgs.Name -eq 'control-after'}).Count){break}
    Start-Sleep -Milliseconds 40
  }while($clock.ElapsedMilliseconds -lt 2500)
  $seen=@($events | ForEach-Object {"$($_.SourceIdentifier) $($_.SourceEventArgs.Name)"})
  $seen | Set-Content "$b\notifications.txt"
  if(!($seen -contains 'projection-Created control-before') -or !($seen -contains 'projection-Created control-after') -or
     @($events | Where-Object {$_.SourceIdentifier -eq 'projection-Error' -or $_.SourceEventArgs.Name -notin 'control-before','control-after'}).Count){throw 'Observer gap or unexpected change'}
}finally{$watch.Dispose();foreach($e in 'Created','Changed','Deleted','Renamed','Error'){Unregister-Event "projection-$e"}}
$after=(Get-FileHash -LiteralPath $db).Hash
if($before -ne $after){throw 'Projection changed'}
foreach($s in '-journal','-wal','-shm'){if(Test-Path -LiteralPath "$db$s"){throw "Unexpected $s"}}
$log=[IO.File]::ReadAllText("$b\host.out")
$launch=[regex]::Matches($log,'LAUNCH_EXPECTED scenario=(\w+) instance=\S+ enrolled=\w+ block=(\S+) table=(\S+) row=(\S+)')
if($launch.Count -ne 4){throw 'Incomplete launches'}
foreach($m in $launch){
  $case=$m.Groups[1].Value
  $id=$m.Groups[2].Value
  $retained=$case -in 'Enrolled','LostReply'
  $rawPath="$b\$case.raw"
  Run $exe @('--instance-root',$l,'block','get','--id',$id,'--output',$rawPath) $case $(if($retained){0}else{4})
  if(!$retained){
    if((Get-Content "$b\$case.out" -Raw | ConvertFrom-Json).error.code -ne 'BLOCK_NOT_FOUND'){throw "$case unexpected read result"}
  }
  if($retained){
    $raw=[IO.File]::ReadAllBytes($rawPath)
    $body='01'+$m.Groups[3].Value.Replace('-','')+$m.Groups[4].Value.Replace('-','')+'00'
    if($raw.Length -ne 261 -or [Convert]::ToHexString($raw,34,16) -ne $id.Replace('-','') -or
       [Convert]::ToHexString($raw,163,34) -ne $body){throw "$case retained event mismatch"}
    Confirm-BlockSignature $case $raw -AllowMissing
  }
}
if(!$log.Contains('LOST_REPLY original=IOERR further_dml=AUTH further_commit=AUTH') -or !$log.Contains('publication rejected') -or !$log.Contains('publication outcome unknown after submission') -or
   [regex]::Matches($log,'PUBLISH_RETURNED').Count -ne 2 -or [regex]::Matches($log,'PRODUCER_EXIT').Count -ne 4){throw 'Missing native outcome'}
"Projection before=$before after=$after; outer RELEASE success, denial and lost reply, both inner savepoint scenarios, full rollback, positive COMMIT, representable CREATE/ADD/DROP, DDL rejection and rollback, effective UPDATE/DELETE, unchanged, missing, DML rollback and four Step 2a scenarios checked."
