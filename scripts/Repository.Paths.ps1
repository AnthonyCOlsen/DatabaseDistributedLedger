# Shared repository/layout discovery. Supports normal checkouts and Git worktrees.
function Get-LedgerRepository([string]$StartPath=$PSScriptRoot) {
 $directory=[IO.DirectoryInfo]::new([IO.Path]::GetFullPath($StartPath))
 while($null -ne $directory){
  if(Test-Path -LiteralPath (Join-Path $directory.FullName 'UserDimensions.Ledger.slnx') -PathType Leaf){return $directory.FullName}
  $directory=$directory.Parent
 }
 throw 'Cannot locate UserDimensions.Ledger.slnx above the supplied path.'
}
function Get-LedgerEvidenceRoot { Join-Path (Get-LedgerRepository) 'local/qa' }
function Get-LedgerGitDirectory {
 $path=& git -C (Get-LedgerRepository) rev-parse --path-format=absolute --git-common-dir
 if($LASTEXITCODE -ne 0){throw 'Cannot locate Git common directory.'}
 return $path.Trim()
}
function Get-LedgerFile([string]$Name) {
 $repo=Get-LedgerRepository
 if($Name -eq 'README.md'){return (Join-Path $repo $Name)}
 $direct=Join-Path $repo $Name
 if(Test-Path -LiteralPath $direct -PathType Leaf){return $direct}
 $roots=@('docs/architecture','docs/qa','docs/archive/qa','tests/IndependentQA')
 $located=@(foreach($root in $roots){
  Get-ChildItem -LiteralPath (Join-Path $repo $root) -Recurse -File -Filter $Name |
   Where-Object {$_.FullName -notmatch '[\\/](bin|obj|__pycache__)[\\/]'}
 })
 if($located.Count -ne 1){throw ('Expected one layout file named '+$Name+'; found '+$located.Count)}
 return $located[0].FullName
}
function Get-QAFiles {
 $repo=Get-LedgerRepository
 foreach($root in @('tests/IndependentQA','docs/qa/reports')){
  Get-ChildItem -LiteralPath (Join-Path $repo $root) -Recurse -File |
   Where-Object {$_.FullName -notmatch '[\\/](bin|obj|__pycache__)[\\/]'}
 }
}
function Get-LedgerCandidate {
 $repo=Get-LedgerRepository
 $current=Join-Path $repo 'artifacts/build/bin/ledgerctl/release/ledgerctl.dll'
 if(Test-Path -LiteralPath $current -PathType Leaf){return $current}
 return (Join-Path $repo 'src/ledgerctl/bin/Release/net10.0/ledgerctl.dll')
}
