<#
QA-owned DEV-003 independent Windows CLI candidate test harness.
Requires Windows 11, PowerShell 5.1+, .NET 10 runtime, candidate ledgerctl.dll.
Runs solely within a newly created, isolated QA test directory; never runs against live data.
No production secrets, no antivirus changes, and NO physical power-loss simulation.
Step 1 acceptance also requires the separate FI-01..FI-20 fault matrix on native Windows.
Example:
 powershell -NoProfile -ExecutionPolicy Bypass -File .\QA_DEV003_WINDOWS_INDEPENDENT_CLI.ps1 `
    -LedgerCtlDll 'C:\path\to\repo\src\ledgerctl\bin\Release\net10.0\ledgerctl.dll' `
    -FixturesJson 'C:\path\to\QA_PHASE1_REFERENCE_FIXTURES.json'
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$LedgerCtlDll,
  [Parameter(Mandatory=$true)][string]$FixturesJson,
  [string]$Dotnet = 'dotnet',
  [string]$EvidenceRoot = '',
  [string]$ExpectedCommit = '5c585cefa59509f9fad8cba55000e374768685b7'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$started = (Get-Date).ToUniversalTime().ToString('o')
if (!(Test-Path -LiteralPath $LedgerCtlDll -PathType Leaf)) { throw "Missing candidate: $LedgerCtlDll" }
if (!(Test-Path -LiteralPath $FixturesJson -PathType Leaf)) { throw "Missing independent QA fixtures: $FixturesJson" }
$LedgerCtlDll = (Resolve-Path -LiteralPath $LedgerCtlDll).Path
$FixturesJson = (Resolve-Path -LiteralPath $FixturesJson).Path
if (!(Get-Command $Dotnet -ErrorAction SilentlyContinue)) { throw "No dotnet executable: $Dotnet" }
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
  $EvidenceRoot = Join-Path $env:TEMP ('UDDL-DEV003-QA-' + [Guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $EvidenceRoot) { throw "Refusing to reuse existing evidence root: $EvidenceRoot" }
$null = New-Item -ItemType Directory -Path $EvidenceRoot
$EvidenceRoot = (Resolve-Path -LiteralPath $EvidenceRoot).Path
$Inputs = Join-Path $EvidenceRoot 'inputs'
$Outputs = Join-Path $EvidenceRoot 'outputs'
$null = New-Item -ItemType Directory -Path $Inputs
$null = New-Item -ItemType Directory -Path $Outputs
$InstanceRoot = Join-Path $EvidenceRoot 'ABC'
$null = New-Item -ItemType Directory -Path $InstanceRoot
$log = Join-Path $EvidenceRoot 'QA_CASE_RESULTS.jsonl'
$events = New-Object System.Collections.Generic.List[object]
$script:CommandIndex = 0
$fixtures = Get-Content -LiteralPath $FixturesJson -Raw -Encoding UTF8 | ConvertFrom-Json

function Fail([string]$msg) { throw "QA ASSERTION FAILED: $msg" }
function Assert-Equal($actual, $expected, [string]$what) {
  if ($null -eq $actual -or [string]$actual -cne [string]$expected) {
    Fail "$what — expected '$expected', observed '$actual'"
  }
}
function Assert-True([bool]$condition,[string]$what) { if (!$condition) { Fail $what } }
function Record([string]$name,[string]$result,[string]$detail) {
  $line = [pscustomobject]@{utc=(Get-Date).ToUniversalTime().ToString('o');test=$name;result=$result;detail=$detail}
  $line | ConvertTo-Json -Compress -Depth 12 | Add-Content -LiteralPath $log -Encoding UTF8
  Write-Host "$result  $name  $detail"
}
function Invoke-Ledger([string]$test,[string[]]$CommandArgs,[int]$expectExit=0,[string]$expectError='') {
  $script:CommandIndex++
  $id = '{0:D3}' -f $script:CommandIndex
  $stdout = Join-Path $Outputs "$id.stdout.json"
  $stderr = Join-Path $Outputs "$id.stderr.txt"
  # Quote arguments for Start-Process, including paths with spaces.
  $allArgs = @($LedgerCtlDll,'--instance-root',$InstanceRoot) + $CommandArgs
  foreach ($a in $allArgs) {
    if ($a.Contains('"')) { Fail "Unexpected double quote in test argument" }
  }
  $quoted = ($allArgs | ForEach-Object { '"' + $_ + '"' }) -join ' '
  $process = Start-Process -FilePath $Dotnet -ArgumentList $quoted -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr
  $exit = $process.ExitCode
  $content = [IO.File]::ReadAllText($stdout, [Text.Encoding]::UTF8).Trim()
  if (!$content) { Fail "${test}: no CLI stdout JSON (exit $exit); stderr $stderr" }
  try { $o = $content | ConvertFrom-Json -ErrorAction Stop }
  catch { Fail "${test}: invalid or multiple stdout JSON objects: $content" }
  Assert-True ($o -isnot [System.Array]) "${test}: stdout should be exactly one JSON object"
  Assert-Equal $exit $expectExit "$test exit"
  if ($expectExit -eq 0) {
    Assert-True ($o.ok -eq $true -and $null -ne $o.data -and $null -eq $o.error) "$test success envelope"
  } else {
    Assert-True ($o.ok -eq $false -and $null -eq $o.data -and $null -ne $o.error) "$test failure envelope"
    if ($expectError) { Assert-Equal $o.error.code $expectError "$test error code" }
  }
  Record $test 'PASS' "exit=$exit; args=$($CommandArgs -join ' '); stdout=$stdout"
  return $o
}
function Vector([string]$name) {
  $v = @($fixtures.blocks | Where-Object { $_.name -ceq $name })
  if ($v.Count -ne 1) { Fail "Fixture not unique/found: $name" }
  return $v[0]
}
function Import-Block([string]$name) {
  $v=Vector $name
  $path=Join-Path $Inputs "$name.BLK"
  $raw=[Convert]::FromBase64String($v.raw_b64)
  [IO.File]::WriteAllBytes($path,$raw)
  $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA512).Hash.ToLowerInvariant()
  Assert-Equal $hash $v.full_sha512 "$name fixture SHA512"
  return Invoke-Ledger "import-$name" @('block','import','--input',$path)
}
function Counts([int]$retained,[int]$pending,[int]$quarantined,[int]$disputed,[string]$name) {
  $r=Invoke-Ledger "status-$name" @('status')
  $b=$r.data.blocks
  Assert-True ($null -ne $b) "${name}: blocks should not be null"
  Assert-Equal $b.retained_count $retained "$name retained_count"
  Assert-Equal $b.pending_count $pending "$name pending_count"
  Assert-Equal $b.quarantined_count $quarantined "$name quarantined_count"
  Assert-Equal $b.disputed_count $disputed "$name disputed_count"
  Assert-Equal $b.opaque_count 0 "$name opaque_count"
  Assert-Equal $r.data.projection.state 'not_installed' "$name projection.state"
  Record "COUNTS-$name" 'PASS' "$retained retained / $pending pending / $quarantined quarantined / $disputed disputed"
}

try {
  Record 'QA_PRECHECK' 'PASS' "candidate=$LedgerCtlDll; sha256=$((Get-FileHash -LiteralPath $LedgerCtlDll -Algorithm SHA256).Hash); fixture_sha256=$((Get-FileHash -LiteralPath $FixturesJson -Algorithm SHA256).Hash); expected_commit=$ExpectedCommit"
  $about = & $Dotnet '--info' 2>&1 | Out-String
  Set-Content -LiteralPath (Join-Path $EvidenceRoot 'dotnet-info.txt') -Value $about -Encoding UTF8
  $null = Invoke-Ledger 'instance-init' @('instance','init')
  $v = Invoke-Ledger 'instance-status-unbound' @('instance','status')
  Assert-Equal $v.data.state 'unbound' 'fresh instance state'
  $null = Invoke-Ledger 'config-defaults' @('config','get')
  $g = Vector 'genesis_A'
  $genesisInput = Join-Path $Inputs 'genesis_A.BLK'
  [IO.File]::WriteAllBytes($genesisInput, [Convert]::FromBase64String($g.raw_b64))
  $null = Invoke-Ledger 'import-external-genesis' @('block','import','--input',$genesisInput)
  $null = Invoke-Ledger 'join-external-genesis' @('ledger','join','--ledger-guid',$fixtures.ledger_guid,'--genesis-sha512',$g.full_sha512)
  Counts 1 0 0 0 'genesis-only'

  $rawPath=Join-Path $Outputs 'QA-get-genesis.BLK'
  $null=Invoke-Ledger 'block-get-genesis' @('block','get','--id',$g.block_guid,'--output',$rawPath)
  Assert-Equal ((Get-FileHash -LiteralPath $rawPath -Algorithm SHA512).Hash.ToLowerInvariant()) $g.full_sha512 'raw get exact signed bytes'
  Record 'RAW_GET_BYTES' 'PASS' $rawPath
  $ddlb=Join-Path $Outputs 'QA-single-genesis.DDLB'
  $null=Invoke-Ledger 'block-export-one' @('block','export','--output',$ddlb)
  $bytes=[IO.File]::ReadAllBytes($ddlb)
  Assert-True ($bytes.Length -ge 22) 'DDLB header+record'
  Assert-Equal ([Text.Encoding]::ASCII.GetString($bytes,0,4)) 'DDLB' 'DDLB magic'
  Assert-True ($bytes[4] -eq 0 -and $bytes[5] -eq 1) 'DDLB version 0x0001'
  [long]$n=0;for($i=6;$i -le 13;$i++) { $n=$n*256+[int]$bytes[$i] }
  Assert-Equal $n 1 'one-block DDLB count'
  Record 'DDLB_ONE_BLOCK' 'PASS' $ddlb

  $null=Import-Block 'grant_B_at_100'
  $null=Import-Block 'B_event_at_300'
  Counts 3 0 0 0 'grant-B-and-event'
  $null=Import-Block 'revoke_B_at_200'
  Counts 4 0 1 0 'late-revocation'
  $null=Import-Block 'regrant_B_at_250'
  Counts 5 0 0 0 'late-regrant'
  $null=Import-Block 'disputed_identity_variant_A'
  $null=Import-Block 'disputed_identity_variant_B'
  Counts 7 0 2 2 'variant-dispute'
  $null=Import-Block 'grant_B_at_100'
  Counts 7 0 2 2 'duplicate-idempotence'
  $null=Invoke-Ledger 'disputed-get' @('block','get','--id','a0000000-0000-4000-8000-00000000000c','--output',(Join-Path $Outputs 'disputed.BLK')) 4 'BLOCK_ID_DISPUTED'

  foreach($bad in @($fixtures.negative_import_vectors)) {
    $p=Join-Path $Inputs ('negative-'+$bad.name+'.BLK')
    [IO.File]::WriteAllBytes($p,[Convert]::FromBase64String($bad.raw_b64))
    $null=Invoke-Ledger ('negative-'+$bad.name) @('block','import','--input',$p) 2 ([string]$bad.expected)
  }
  Counts 7 0 2 2 'after-bad-imports'

  $null=Invoke-Ledger 'config-set-one' @('config','set','--name','max_raw_block_bytes','--value','16777215')
  $changed=Invoke-Ledger 'config-read-persisted' @('config','get')
  Assert-Equal $changed.data.limits.max_raw_block_bytes 16777215 'config persisted across CLI processes'
  $config=Join-Path $InstanceRoot 'instance.config.json'
  $original=[IO.File]::ReadAllBytes($config)
  try {
    [IO.File]::WriteAllText($config,'{ not valid JSON', [Text.Encoding]::UTF8)
    $s=Invoke-Ledger 'config-corrupt-diagnostic' @('status')
    Assert-True (@($s.data.instance.recovery_codes) -contains 'CONFIG_CORRUPT') 'config corruption diagnosis'
    $null=Invoke-Ledger 'config-corrupt-get-fails' @('config','get') 4 'CONFIG_CORRUPT'
  } finally { [IO.File]::WriteAllBytes($config,$original) }
  $after=Invoke-Ledger 'config-restored' @('config','get')
  Assert-Equal $after.data.limits.max_raw_block_bytes 16777215 'config restored without silent reset'

  $key=Join-Path $InstanceRoot 'signing-key.v1.json'
  $keyAside=Join-Path $Outputs 'original-key.v1.json'
  Move-Item -LiteralPath $key -Destination $keyAside
  try {
    $s=Invoke-Ledger 'missing-key-diagnostic' @('status')
    Assert-Equal $s.data.instance.state 'recovery_required' 'missing key gate'
    $null=Invoke-Ledger 'missing-key-export-denied' @('block','export','--output',(Join-Path $Outputs 'should-not-exist.DDLB')) 4 'RECOVERY_REQUIRED'
  } finally { Move-Item -LiteralPath $keyAside -Destination $key }
  Counts 7 0 2 2 'after-original-key-restored'
  Record 'FINAL' 'PASS' "Candidate independent CLI test path completed; evidence: $EvidenceRoot"
  Write-Host "QA CANDIDATE CLI TESTS PASSED. Evidence preserved: $EvidenceRoot"
  exit 0
}
catch {
  $why=$_.Exception.ToString()
  Record 'FINAL' 'FAIL' $why
  Write-Error "Independent QA candidate test failed: $why`nEvidence preserved: $EvidenceRoot"
  exit 1
}
