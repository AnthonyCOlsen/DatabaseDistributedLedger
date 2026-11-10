param([switch]$Test)
$ErrorActionPreference = 'Stop'
$ledgerRoot = Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_HOME = Join-Path $ledgerRoot '.git/dev-003-work/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $ledgerRoot '.git/dev-003-work/packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $ledgerRoot '.git/dev-003-work/http-cache'
$env:NUGET_SCRATCH = Join-Path $ledgerRoot '.git/dev-003-work/nuget-scratch'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $ledgerRoot '.git/dev-003-work/plugin-cache'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build (Join-Path $ledgerRoot 'UserDimensions.Ledger.slnx') -c Release --nologo -m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false /p:MSBuildEnableWorkloadResolver=false /p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Test) {
 dotnet run --project (Join-Path $ledgerRoot 'tests/UserDimensions.Ledger.Tests') -c Release --no-build
 exit $LASTEXITCODE
}
