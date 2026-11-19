param([switch]$Test)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Paths.ps1')
$ledgerRoot = Get-LedgerRepository
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_HOME = Join-Path $ledgerRoot 'artifacts/cache/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $ledgerRoot 'artifacts/cache/packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $ledgerRoot 'artifacts/cache/http'
$env:NUGET_SCRATCH = Join-Path $ledgerRoot 'artifacts/cache/nuget-scratch'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $ledgerRoot 'artifacts/cache/nuget-plugins'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build (Join-Path $ledgerRoot 'UserDimensions.Ledger.slnx') -c Release --artifacts-path (Join-Path $ledgerRoot 'artifacts/build') --nologo -m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false /p:MSBuildEnableWorkloadResolver=false /p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Test) {
 dotnet (Join-Path $ledgerRoot 'artifacts/build/bin/UserDimensions.Ledger.Tests/release/UserDimensions.Ledger.Tests.dll')
 exit $LASTEXITCODE
}
