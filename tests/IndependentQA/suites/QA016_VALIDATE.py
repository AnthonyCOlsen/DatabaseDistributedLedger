"""Validate saved QA-016 evidence without rerunning candidate binaries or tests."""
import argparse
import collections
import json
import pathlib
import subprocess
import QA010_REFERENCE as ref
from QA010_RUN import inventory,block_path
from QA015_VALIDATE import validate as validate_qa015,read,sha,require


def validate(root,source_review=None):
    # Original 49-case evidence assertions are reused unchanged; only its reviewed
    # production/hash constants were updated for the explicitly selected candidate.
    validate_qa015(root)
    status=read(root/'run-status.json');all_results=read(root/'qa016-results.json')
    prior=read(root/'qa015-candidate-results.json');require(prior==read(root/'candidate-results.json'),'49-case original result preserved')
    original_audit=read(root/'QA015-receipt-validation.json')
    require(original_audit['counts']==dict(PASS=49,FAIL=0,BLOCKED=0,**{'NOT-RUN':0}),'All original regression assertions pass')
    original_source=read(root/'source-order-results.json')
    source=read(source_review) if source_review else original_source
    require(source['counts']==dict(PASS=6,FAIL=0,BLOCKED=0,**{'NOT-RUN':0}),'Six completed source-order reviews')
    repository=root.parents[2]
    for entry in read(root/'production-source-hashes.json'):
        frozen=root/'source-snapshot'/entry['path'];require(sha(frozen)==entry['sha256'],'Frozen production source changed')
        committed=subprocess.check_output(['git','-C',str(repository),'show',status['production_revision']+':'+entry['path']])
        require(frozen.read_bytes().replace(b'\r\n',b'\n')==committed.replace(b'\r\n',b'\n'),'Reviewed source differs from production Git blob')
    for entry in source['source_hashes']:require(sha(root/'source-snapshot'/entry['path'])==entry['sha256'],'Source-order checker used another source revision')
    runtime=root/'gate-runtime';result=read(runtime/'candidate-results.json')
    require(len(result['cases'])==3 and all(c['status']=='PASS' for c in result['cases']),'Three fresh-publication runtime checks pass')
    runtime_receipts=0
    for path in (runtime/'commands').glob('*.process.json'):
        receipt=read(path);prefix=path.name.removesuffix('.process.json')
        for stream in ('stdout','stderr'):require(sha(path.with_name(prefix+'.'+stream+'.txt'))==receipt[stream+'_sha256'],'Runtime command stream hash')
        runtime_receipts+=1
    originals=[]
    for condition in ('CORRUPT','MISSING','HEALTHY'):
        name='FRESH-NO-GUID-'+condition;proof=read(runtime/(name+'.contract.json'));before=read(runtime/(name+'.before.json'));after=read(runtime/(name+'.after.json'))
        instance=pathlib.Path(proof['instance_root']);require(inventory(instance)==after,'Final runtime filesystem disagrees with recorded inventory')
        require('--block-guid' not in proof['command'] and proof['runtime_signing_observed'] is False,'No-GUID path and truthful observation boundary')
        for field in ('instance_guid','public_key_hex','ledger_id_hex','genesis_sha512'):
            require(proof['identity_before'][field]==proof['identity_after'][field],'Original identity/trusted binding changed')
        if condition=='HEALTHY':
            receipt=proof['response'];p=pathlib.Path(receipt['path']);raw=p.read_bytes();ref.verify(raw)
            require(receipt['sha512']==ref.sha(raw) and p==block_path(instance,raw),'Authentic exact healthy publication')
            require(set(after['files'])-set(before['files'])=={p.relative_to(instance).as_posix()},'Healthy call must create exactly one block')
            require(all(after['files'].get(k)==v for k,v in before['files'].items()),'Healthy call changed original identity/history')
            states,_=ref.replay([p.read_bytes() for p in instance.rglob('*.BLK')]);require(len(states)==3 and set(states.values())=={'retained'},'Independent canonical acceptance of exactly three retained blocks')
            originals.append(dict(case=name,new_block_sha512=ref.sha(raw),independent_signature_and_authority_verified=True))
        else:
            reason='LEDGER_HISTORY_CORRUPT' if condition=='CORRUPT' else 'LEDGER_HISTORY_INCOMPLETE'
            require(before==after and proof['error']['code']=='RECOVERY_REQUIRED' and proof['error']['details']['reason']==reason,'Impaired-history error or preservation mismatch')
        require(not list((runtime/'outputs').iterdir()),'No external publication output expected')
    candidate_counts=collections.Counter(c['status'] for c in prior['cases']+result['cases'])
    require(all(all_results['candidate_counts'][s]==candidate_counts[s] for s in ('PASS','FAIL','BLOCKED','NOT-RUN')),'Truthful combined candidate totals')
    require(len(all_results['candidate_cases'])==52 and len({c['id'] for c in all_results['candidate_cases']})==52,'52 unique candidate cases')
    native=status['context_operator_reported']=='OrdinaryWindowsPowerShell'
    audit=dict(format='QA016-INDEPENDENT-EVIDENCE-VALIDATION',context_operator_reported=status['context_operator_reported'],
        repository_commit=status['actual_commit'],production_revision=status['production_revision'],candidate_sha256=status['candidate_dll_sha256'],ledger_sha256=status['ledger_library_sha256'],
        candidate_counts=all_results['candidate_counts'],source_counts=source['counts'],reference_counts=all_results['reference_counts'],
        original_source_counts=original_source['counts'],source_review_followup=str(source_review) if source_review else None,
        command_receipts_verified=original_audit['command_receipts_verified']+runtime_receipts,
        runtime_command_receipts=runtime_receipts,original_assertions_and_source_provenance_verified=True,
        source_vs_runtime_evidence_separate=True,runtime_signing_observed=False,healthy_publication_independently_verified=originals,
        native_candidate_counts=all_results['candidate_counts'] if native else dict(PASS=0,FAIL=0,BLOCKED=0,**{'NOT-RUN':52}),
        independent_disposition='QA READY' if native else 'QA NOT READY — ordinary Windows confirmation NOT RUN',formal_owner_acceptance='NOT_DECLARED')
    target=root/'QA016-receipt-validation.json';require(not target.exists(),'Preserve prior validation receipt')
    target.write_text(json.dumps(audit,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(candidate=audit['candidate_counts'],source=audit['source_counts'],reference=audit['reference_counts'],
        receipts_verified=audit['command_receipts_verified'],disposition=audit['independent_disposition'])))


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--evidence',required=True);ap.add_argument('--source-review')
    args=ap.parse_args();validate(pathlib.Path(args.evidence),pathlib.Path(args.source_review) if args.source_review else None)
