"""Independent, read-only review of QA-015 saved receipts; never reruns candidates."""
import argparse
import collections
import hashlib
import json
import pathlib
import subprocess
import QA010_REFERENCE as ref
from QA010_RUN import inventory, block_path
from QA014_RUN import EXISTING, NEW
from QA015_RUN import EXTRA

PRODUCTION='c001fc4a0d50067e2b8c9a2685625bf43aeea464'
CLI='7ad53dd36931831df14bc8ecb82f9a53576d71c2fff28a0d22f085191c9c504b'
LIBRARY='9cbc07e403386ec87ed9a576666fc1c0a3bb3a1a40814e5789ae925477a50941'


def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def require(value,message):
    if not value:raise AssertionError(message)


def validate(root):
    status=read(root/'run-status.json');result=read(root/'candidate-results.json')
    require(status['actual_commit']==status['expected_commit'],'Selected repository revision')
    require(status['production_revision']==PRODUCTION and status['candidate_dll_sha256']==CLI and status['ledger_library_sha256']==LIBRARY,'Exact reviewed production and binary identities')
    require(not (root/'production-source-diff.stdout.txt').read_text().strip() and not (root/'production-status.stdout.txt').read_text().strip(),'Production baseline equivalence and clean Developer source')
    for listing,folder in (('candidate-artifact-hashes.json','candidate-snapshot'),('qa-source-hashes.json','suite-snapshot'),('design-document-hashes.json','design-document-snapshot')):
        for entry in read(root/listing):require(sha(root/folder/entry['name'])==entry['sha256'],'Frozen evidence hash mismatch: '+entry['name'])
    repository=root.parents[2]
    original=subprocess.check_output(['git','-C',str(repository),'show','5bbcd2d:tests/IndependentQA/QA014_RUN.py'])
    require((root/'suite-snapshot/QA014_RUN.py').read_bytes()==original,'Original 38-case assertion runner changed')
    require(len(result['cases'])==49 and len({x['id'] for x in result['cases']})==49,'All 49 unique named cases accounted for')
    require(set(x['id'] for x in result['cases'])==set(EXISTING+NEW+EXTRA),'Unexpected or omitted case selection')
    counts=collections.Counter(x['status'] for x in result['cases'])
    require(all(counts[x]==result['counts'][x] for x in ('PASS','FAIL','BLOCKED','NOT-RUN')),'Truthful case totals')
    commands=0
    for child in ('qa014','additional'):
        for path in (root/child/'commands').glob('*.process.json'):
            receipt=read(path);prefix=path.name.removesuffix('.process.json')
            for stream in ('stdout','stderr'):
                require(sha(path.with_name(prefix+'.'+stream+'.txt'))==receipt[stream+'_sha256'],'Command stream integrity')
            commands+=1
    reference=read(root/'reference-results.json')
    require(len(reference)==76 and all(x['status']=='PASS' for x in reference),'76 independent reference results')
    archived=[]
    for child,duplicate in (('qa014',False),('additional',True)):
        for kind in ('quarantined','pending','revocation'):
            proof=read(root/child/('ARCH-'+kind+'.pre-retry-proof.json'));raw=pathlib.Path(proof['original_fixture']).read_bytes();ref.verify(raw)
            require(ref.sha(raw)==proof['original_sha512'],'Original archived signature/hash')
            if not duplicate:
                recovery=read(root/child/('ARCH-RESTORE-'+kind+'.recovery.json'))
                require(recovery['original_sha512']==recovery['restored_sha512']==ref.sha(raw) and recovery['original_response']==recovery['retry_response'],'Original restored publication exact')
                before=read(root/child/('ARCH-RETRY-'+kind+'.before.json'));after=read(root/child/('ARCH-RETRY-'+kind+'.after.json'))
                contract=read(root/child/('ARCH-RETRY-'+kind+'.contract.json'))
                require(not contract['mismatches'] and contract['actual_response']['error']['code']=='BLOCK_ARCHIVED','Formerly failing archived retry assertion')
                current=pathlib.Path(proof['historical_active_path'])
            else:
                name='ARCHIVED-DUPLICATE-'+kind;before=read(root/child/(name+'.before.json'));after=read(root/child/(name+'.after.json'))
                contract=read(root/child/(name+'.contract.json'));current=pathlib.Path(contract['archive_path'])
                require(current.read_bytes()==raw and not pathlib.Path(proof['historical_active_path']).exists(),'Duplicate import cannot restore or replace archived signed bytes')
                for response in contract['imports']:
                    item=response['results'][0]
                    require(response['durable_count']==0 and response['duplicate_count']==1 and item['duplicate'] is True and item['durable'] is True and item['retryable'] is False,'Archived duplicate counts/flags')
                    require(item['status']==proof['expected_status'] and item['authorization']==contract['prior_classification']['authorization'] and item['reason']==contract['prior_classification']['reason'],'Duplicate preserves prior classification')
            require(before==after and current.read_bytes()==raw,'Exact original bytes and inventories preserved')
            instance=next(p for p in current.parents if p.parent==root/child/'instances')
            all_raw=[p.read_bytes() for p in instance.rglob('*') if p.suffix in ('.BLK','.XBLK')]
            states,_=ref.replay(all_raw)
            for p in instance.rglob('*'):
                if p.suffix in ('.BLK','.XBLK'):
                    b=p.read_bytes();require(p==block_path(instance,b,states[ref.sha(b)]!='retained'),'Physical classification agrees with independent canonical authority')
            archived.append(dict(child=child,kind=kind,sha512=ref.sha(raw),original_signature_and_classification_verified=True))
    missing=[]
    for name in ('MISSING-GENESIS-FILE','MISSING-BLK-DIRECTORY'):
        contract=read(root/'additional'/(name+'.contract.json'));impaired=read(root/'additional'/(name+'.impaired.json'));after=read(root/'additional'/(name+'.after.json'))
        instance=root/'additional/instances'/name
        require(impaired==after==inventory(instance),'Incomplete-history diagnostics changed surviving files')
        original=read(root/'additional'/(name+'.before.json'))
        for k,v in impaired['files'].items():require(original['files'][k]==v,'Unaffected identity or signed block changed')
        missing.append(dict(case=name,defect=contract['defect'],mismatches=contract['mismatches'],signing=contract['status']['signing'],
            recovery_codes=contract['status']['instance']['recovery_codes'],ordinary_errors=[dict(command=x['command'][:2],exit_code=x['exit_code'],error=x['response'].get('error')) for x in contract['ordinary_operations']],
            identity_and_unaffected_files_preserved=True))
    context=status['context_operator_reported'];native=context=='OrdinaryWindowsPowerShell'
    audit=dict(format='QA015-INDEPENDENT-RECEIPT-VALIDATION',candidate_reexecuted=False,context_operator_reported=context,
        repository_commit=status['actual_commit'],production_revision=PRODUCTION,cli_sha256=CLI,library_sha256=LIBRARY,
        counts=result['counts'],reference_pass=76,command_receipts_verified=commands,original_qa014_assertions_verified=True,
        archived_signatures_and_classification=archived,missing_genesis_findings=missing,
        native_execution_counts=result['counts'] if native else {'PASS':0,'FAIL':0,'BLOCKED':0,'NOT-RUN':49},
        readiness='NOT_READY' if not native or counts['FAIL'] or counts['BLOCKED'] or counts['NOT-RUN'] else 'QA_REVIEW_REQUIRED',
        production_modified=False,formal_step1_acceptance='NOT_DECLARED')
    target=root/'QA015-receipt-validation.json';require(not target.exists(),'Preserve original validation receipt')
    target.write_text(json.dumps(audit,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(counts=audit['counts'],reference_pass=76,commands_verified=commands,native_execution_counts=audit['native_execution_counts'],readiness=audit['readiness'])))


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--evidence',required=True)
    validate(pathlib.Path(ap.parse_args().evidence))
