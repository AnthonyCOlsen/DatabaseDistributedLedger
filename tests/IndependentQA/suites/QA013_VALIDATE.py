"""Read-only QA-013 evidence review; writes a new validation receipt only.

Rechecks deterministic damage, original signatures, preserved diagnostic files,
snapshot/log hashes and READY PID receipts without executing the candidate again.
"""
import argparse
import collections
import hashlib
import json
import pathlib
import QA010_REFERENCE as ref
from QA013_MUTATIONS import mutate, rejection


def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def require(value,message):
    if not value:raise AssertionError(message)


def validate(root,expected_commit):
    run=read(root/'run-status.json');result=read(root/'candidate-results.json')
    for listing,directory in (('candidate-artifact-hashes.json','candidate-snapshot'),('qa-source-hashes.json','suite-snapshot')):
        for item in read(root/listing):
            require(digest(root/directory/item['name'])==item['sha256'],'Snapshot hash mismatch: '+item['name'])
    counts=collections.Counter(x['status'] for x in result['cases'])
    require(all(result['counts'][name]==counts[name] for name in ('PASS','FAIL','BLOCKED','NOT-RUN')),'Result counts differ from cases')
    require(run['actual_commit']==expected_commit and run['actual_commit']==run['expected_commit'],'Unexpected explicitly selected candidate commit')
    require(digest(root/'candidate-snapshot/ledgerctl.dll')==run['candidate_dll_sha256'],'CLI binary hash')
    require(digest(root/'candidate-snapshot/UserDimensions.Ledger.dll')==run['ledger_library_sha256'],'Library binary hash')
    commands=[];barriers=[]
    for p in sorted((root/'commands').glob('*.process.json')):
        item=read(p);stem=p.name.removesuffix('.process.json')
        stdout=root/'commands'/(stem+'.stdout.txt');stderr=root/'commands'/(stem+'.stderr.txt')
        require(stdout.is_file() and stderr.is_file(),'Missing command output logs')
        if 'stdout_sha256' in item:
            require(digest(stdout)==item['stdout_sha256'] and digest(stderr)==item['stderr_sha256'],'Command output hash mismatch')
        if 'pid' in item:
            ready=read(pathlib.Path(item['request']['marker']))
            require(item['barrier_reached'] and ready['pid']==item['pid'],'Positive READY PID mismatch')
            require(item['externally_terminated'] and item['exit_code']!=0 and not stdout.read_bytes(),'Temporary publisher must be externally terminated without acknowledgment')
            require(ready['checkpoint'].startswith('before-rename:') and ready['checkpoint'].endswith('.BLK'),'Existing pre-publication callback')
            barriers.append(dict(case=item['case'],pid=item['pid'],checkpoint=ready['checkpoint']))
        commands.append(dict(receipt=p.name,exit_code=item['exit_code']))
    mutations=[];behavior=[]
    for case in result['cases']:
        if not case['id'].startswith('CORRUPT-') or case['status']!='PASS':continue
        ident=case['id'];proof=read(root/(ident+'.mutation.json'))
        original=pathlib.Path(proof['original_fixture']).read_bytes();ref.verify(original)
        damaged=pathlib.Path(proof['damaged_fixture']).read_bytes();expected,action=mutate(original,proof['pattern'])
        require(damaged==expected,'Damaged fixture differs from deterministic mutation')
        require(all(proof[k]==v for k,v in action.items()),'Mutation offset/length/hash record mismatch')
        require(rejection(damaged)==proof['independent_damaged_rejection'],'Independent rejection mismatch')
        target=pathlib.Path(proof['target'])
        require(target.resolve().is_relative_to((root/'instances').resolve()),'Mutation escaped isolated instance space')
        require(target.name==proof['filename_unchanged'] and target.read_bytes()==damaged,'Diagnostic target changed or missing')
        before=read(root/(ident+'.before-corruption.json'));after=read(root/(ident+'.after-operations.json'))
        relative=next(name for name in before['files'] if str(target).replace('\\','/').endswith('/'+name))
        require(before['files'][relative]['sha512']==ref.sha(original),'Original on-disk hash evidence')
        for name,item in before['files'].items():
            if name!=relative:require(after['files'].get(name)==item,'Previously valid history/identity changed: '+name)
        require(after['files'][relative]['sha512']==ref.sha(damaged),'Final diagnostic inventory hash')
        require(pathlib.Path(proof['original_fixture']).read_bytes()==original,'Original byte fixture preserved')
        parsed=read(root/(ident+'.candidate-parse.json'))
        require(parsed['response']['accepted'] is False and parsed['exit_code']!=0,'Candidate parser accepted damaged bytes')
        observations=read(root/(ident+'.observations.json'));summaries=[]
        for item in observations:
            obj=item['response'];args=item.get('command');summary=dict(command=args or item.get('library'),exit_code=item['exit_code'])
            if 'ok' in obj:
                summary.update(ok=obj['ok'],code=obj['error']['code'] if obj['error'] else None)
                if obj['ok'] and obj['data'].get('operation')=='status':
                    summary.update(instance=obj['data']['instance'],blocks=obj['data']['blocks'],signing=obj['data']['signing'])
                output=item.get('output')
                if output and obj['ok']:
                    raw=pathlib.Path(output).read_bytes()
                    blocks=ref.read_ddlb(raw) if args[:2]==['block','export'] else [raw]
                    for block in blocks:ref.verify(block)
                    require([ref.sha(x) for x in blocks]==item['output_verified_blocks'],'Export/retrieval signed hashes differ')
                elif output:require(not pathlib.Path(output).exists(),'Failed output unexpectedly appeared')
            else:summary.update(completed=obj['completed'],code=obj.get('code'))
            summaries.append(summary)
        mutations.append(dict(case=ident,pattern=proof['pattern'],original_sha512=ref.sha(original),damaged_sha512=ref.sha(damaged),
                              independent_error=proof['independent_damaged_rejection'],candidate_parser_error=parsed['response']['code'],diagnostic_preserved=True))
        behavior.append(dict(case=ident,observations=summaries))
    require(len(barriers)==sum(x['status']=='PASS' and x['id'].startswith('CORRUPT-TEMP-') for x in result['cases']),'Missing successful temporary-publication barrier receipts')
    receipt=dict(format='UDDL-QA013-RECEIPT-VALIDATION',review_only=True,candidate_reexecuted=False,
                 context_operator_reported=run['context_operator_reported'],candidate_commit=run['actual_commit'],
                 candidate_sha256=run['candidate_dll_sha256'],ledger_sha256=run['ledger_library_sha256'],
                 counts=result['counts'],command_receipts_verified=len(commands),positive_barriers=barriers,
                 independently_reverified_mutations=mutations,observed_behavior=behavior,
                 physical_storage_tested=False,formal_step1_acceptance='NOT_DECLARED')
    path=root/'QA013-receipt-validation.json'
    require(not path.exists(),'Preserve existing validation receipt; do not overwrite')
    path.write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(receipt_validated=True,commands=len(commands),barriers=len(barriers),mutations=len(mutations),counts=result['counts'])))


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--evidence',required=True)
    ap.add_argument('--expected-commit',default='8bc96f0f513faac57c1db23a8189288479d8a992')
    args=ap.parse_args();validate(pathlib.Path(args.evidence),args.expected_commit)
