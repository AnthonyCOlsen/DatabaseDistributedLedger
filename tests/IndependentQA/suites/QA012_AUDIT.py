"""Read-only source portability inventory, not a platform-support or certification test."""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess


def audit(repo):
    commands=[]
    def git(*args):
        p=subprocess.run(['git','--no-optional-locks','-C',str(repo),*args],capture_output=True,text=True,encoding='utf-8')
        commands.append(dict(arguments=list(args),exit_code=p.returncode,stdout=p.stdout,stderr=p.stderr))
        if p.returncode:raise RuntimeError('Cannot audit Git source: '+p.stderr)
        return p.stdout.strip()
    current=git('rev-parse','HEAD');baseline=git('rev-parse','sqlite/upstream-3.54.1:third_party/sqlite')
    sqlite=git('rev-parse','HEAD:third_party/sqlite')
    diff=git('diff','--name-only','sqlite/upstream-3.54.1','HEAD','--','third_party/sqlite')
    working=git('status','--porcelain','--','src','third_party/sqlite','Directory.Build.props','scripts')
    sources={};matches=[]
    pattern=re.compile(r'OperatingSystem|NTFS|DriveInfo|DllImport|kernel32|MoveFileEx|Global\\|Mutex|32000|255|OrdinalIgnoreCase|ToUpperInvariant|SetUnixFileMode|RuntimeIdentifier|net10\.0-windows|WindowsDesktop')
    selected=list((repo/'src').rglob('*.cs'))+list((repo/'src').rglob('*.csproj'))+[repo/'Directory.Build.props',repo/'scripts/build.ps1']
    for path in selected:
        if {'bin','obj'} & set(path.relative_to(repo).parts):continue
        raw=path.read_bytes();name=path.relative_to(repo).as_posix();sources[name]=hashlib.sha256(raw).hexdigest()
        for line,text in enumerate(raw.decode('utf-8-sig').splitlines(),1):
            if pattern.search(text):matches.append(dict(path=name,line=line,text=text))
    sqlite_sources={}
    for name in ('os.c','os_setup.h','os_unix.c','os_win.c'):
        path=repo/'third_party/sqlite/src'/name;sqlite_sources[name]=hashlib.sha256(path.read_bytes()).hexdigest()
    return dict(format='UDDL-QA012-PORTABILITY-SOURCE-REVIEW',candidate_commit=current,review_only=True,
                sqlite_baseline_tree=baseline,sqlite_current_tree=sqlite,sqlite_committed_changes=diff,
                sqlite_pristine_against_preserved_tag=(baseline==sqlite and not diff),protected_working_status=working,
                source_sha256=sources,os_related_source_locations=matches,sqlite_os_source_sha256=sqlite_sources,
                commands=commands,platforms_not_qualified=['Linux','macOS','other operating systems'],
                production_modified=False,notes='Pristine vendor source preserves upstream portability code; future native ledger/.DLDB modifications are not present to assess.')


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--repo',required=True);parser.add_argument('--output',required=True);a=parser.parse_args()
    result=audit(pathlib.Path(a.repo));pathlib.Path(a.output).write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(review_only=True,candidate_commit=result['candidate_commit'],sqlite_pristine=result['sqlite_pristine_against_preserved_tag'],production_modified=False)))
