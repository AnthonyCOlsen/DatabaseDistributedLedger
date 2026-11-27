param([Parameter(Mandatory)][string]$Batch,[Parameter(Mandatory)][string]$App)
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
function Run([string]$program,[string[]]$arguments,[string]$name,[int]$expected){
  $pi=[Diagnostics.ProcessStartInfo]::new($program)
  $pi.UseShellExecute=$false
  $pi.CreateNoWindow=$true
  $pi.WorkingDirectory=$b
  $pi.RedirectStandardOutput=$true
  $pi.RedirectStandardError=$true
  foreach($a in $arguments){$pi.ArgumentList.Add($a)}
  $p=[Diagnostics.Process]::Start($pi)
  try{
    $out=$p.StandardOutput.ReadToEndAsync()
    $err=$p.StandardError.ReadToEndAsync()
    if(!$p.WaitForExit(55000)){$p.Kill($true);$null=$p.WaitForExit(5000);throw "$name timeout"}
    $rc=$p.ExitCode
    [IO.File]::WriteAllText("$b\$name.out",$out.GetAwaiter().GetResult())
    [IO.File]::WriteAllText("$b\$name.err",$err.GetAwaiter().GetResult())
    $receipt="$name exit=$rc command=$program $($arguments -join ' ')"
    Add-Content "$b\commands.txt" $receipt
    $receipt
    if($rc -ne $expected){Get-Content "$b\$name.out","$b\$name.err";throw "$name failed"}
  }finally{$p.Dispose()}
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
    $openssl=Join-Path $env:ProgramFiles 'Git\usr\bin\openssl.exe'
    if(Test-Path -LiteralPath $openssl){
      $key=[byte[]]([Convert]::FromHexString('302a300506032b6570032100') + $raw[58..89])
      [IO.File]::WriteAllBytes("$b\$case.pub.der",$key)
      [IO.File]::WriteAllBytes("$b\$case.message",[byte[]]$raw[0..196])
      [IO.File]::WriteAllBytes("$b\$case.sig",[byte[]]$raw[197..260])
      Run $openssl @('pkeyutl','-verify','-pubin','-keyform','DER','-inkey',"$b\$case.pub.der",'-rawin','-in',"$b\$case.message",'-sigfile',"$b\$case.sig") "$case-ed25519" 0
    }else{Write-Output 'Independent OpenSSL signature check NOT RUN'}
  }
}
if(!$log.Contains('LOST_REPLY original=IOERR further_dml=AUTH further_commit=AUTH') -or !$log.Contains('publication rejected') -or !$log.Contains('publication outcome unknown after submission') -or
   [regex]::Matches($log,'PUBLISH_RETURNED').Count -ne 2 -or [regex]::Matches($log,'PRODUCER_EXIT').Count -ne 4){throw 'Missing native outcome'}
"Projection before=$before after=$after; outer RELEASE success, denial and lost reply, both inner savepoint scenarios, full rollback, positive COMMIT, effective UPDATE/DELETE, unchanged, missing, DML rollback and four Step 2a scenarios checked."
