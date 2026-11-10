param([string]$AntivirusState = 'Not recorded by operator', [string]$LedgerRunContext = 'Ordinary Windows terminal (operator should verify)')
$ErrorActionPreference = 'Stop'
$ledgerRoot = Split-Path $PSScriptRoot -Parent
$ledgerEvidence = Join-Path $ledgerRoot ('.git/dev-003-work/native-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $ledgerEvidence -Force | Out-Null
$ledgerReceipt = Join-Path $ledgerEvidence 'environment.txt'
@(
 'DEV-003 Developer regressions; not independent QA certification',
 ('Timestamp: ' + (Get-Date -Format o)),
 ('OS: ' + [Environment]::OSVersion.VersionString),
 ('Antivirus state (operator reported): ' + $AntivirusState),
 ('Execution context (operator reported): ' + $LedgerRunContext),
 ('HEAD: ' + (git --no-optional-locks -C $ledgerRoot rev-parse HEAD)),
 ('Working tree: ' + ((git --no-optional-locks -C $ledgerRoot status --short) -join '; '))
) | Set-Content -LiteralPath $ledgerReceipt -Encoding utf8
& (Join-Path $PSScriptRoot 'build.ps1') -Test 2>&1 | Tee-Object -FilePath (Join-Path $ledgerEvidence 'tests.log')
$ledgerExit = $LASTEXITCODE
Set-Content -LiteralPath (Join-Path $ledgerEvidence 'exit-code.txt') -Value $ledgerExit -Encoding ascii
Write-Output ('Evidence: ' + $ledgerEvidence)
exit $ledgerExit
