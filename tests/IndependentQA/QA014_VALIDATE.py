"""Read-only review of QA-014 receipts; no candidate re-execution or result rewriting."""
import argparse
import collections
import hashlib
import json
import pathlib
import uuid
import QA010_REFERENCE as ref
from QA010_RUN import block_path
from QA013_MUTATIONS import mutate, rejection


def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha256(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def require(value,message):
    if not value:raise AssertionError(message)


def validate(root,expected):
    status=read(root/'run-status.json');result=read(root/'candidate-results.json')
    require(status['actual_commit']==status['expected_commit']==expected,'Explicit candidate commit mismatch')
    require(status['production_revision']=='8bc96f0f513faac57c1db23a8189288479d8a992','Reviewed production baseline')
    for listing,folder in (('candidate-artifact-hashes.json','candidate-snapshot'),('qa-source-hashes.json','suite-snapshot'),('design-document-hashes.json','design-document-snapshot')):
        for item in read(root/listing):require(sha256(root/folder/item['name'])==item['sha256'],'Frozen source/artifact hash mismatch: '+item['name'])
    counts=collections.Counter(x['status'] for x in result['cases'])
    require(all(counts[k]==result['counts'][k] for k in ('PASS','FAIL','BLOCKED','NOT-RUN')),'Counts do not match named cases')
    require(len(result['cases'])==38,'Expected 30 reused + eight new selected cases')
    require(not (root/'production-source-diff.stdout.txt').read_text().strip(),'Production source baseline changed')
    commands=0
    for path in (root/'commands').glob('*.process.json'):
        receipt=read(path);stem=path.name.removesuffix('.process.json')
        out=root/'commands'/(stem+'.stdout.txt');err=root/'commands'/(stem+'.stderr.txt')
        require(out.is_file() and err.is_file(),'Missing command stdout/stderr')
        require(sha256(out)==receipt['stdout_sha256'] and sha256(err)==receipt['stderr_sha256'],'Command output hash mismatch')
        commands+=1
    archives=[]
    for kind in ('quarantined','pending','revocation'):
        proof=read(root/('ARCH-'+kind+'.pre-retry-proof.json'));original=pathlib.Path(proof['original_fixture']).read_bytes();info=ref.verify(original)
        require(ref.sha(original)==proof['original_sha512'],'Original signed hash mismatch')
        instance=next(p for p in pathlib.Path(proof['current_archive_path']).parents if p.parent==root/'instances')
        require(pathlib.Path(proof['historical_active_path'])==block_path(instance,original),'Historical active path mismatch')
        before=read(root/('ARCH-RETRY-'+kind+'.before.json'));after=read(root/('ARCH-RETRY-'+kind+'.after.json'))
        require(before==after,'Archived retry mutated filesystem')
        relative=pathlib.Path(proof['current_archive_path']).relative_to(instance).as_posix()
        require(before['files'][relative]['sha512']==ref.sha(original),'Original authentic XBLK inventory proof')
        contract=read(root/('ARCH-RETRY-'+kind+'.contract.json'))
        require(contract['expected_original_publication']['sha512']==ref.sha(original) and contract['expected_original_publication']['timestamp_utc_ms']==str(info['time']),'Independent expected original metadata')
        recovery=read(root/('ARCH-RESTORE-'+kind+'.recovery.json'))
        require(recovery['original_sha512']==recovery['restored_sha512']==ref.sha(original),'Restoration changed original signed bytes')
        require(recovery['original_response']==recovery['retry_response'],'Active restored retry changed metadata')
        require(block_path(instance,original).read_bytes()==original and not pathlib.Path(proof['current_archive_path']).exists(),'Final original signed block not restored intact')
        final_raws=[p.read_bytes() for p in instance.rglob('*') if p.suffix in ('.BLK','.XBLK')]
        states,_=ref.replay(final_raws)
        for p in instance.rglob('*'):
            if p.suffix in ('.BLK','.XBLK'):
                raw=p.read_bytes();require(p==block_path(instance,raw,states[ref.sha(raw)]!='retained'),'Final physical classification differs from independent canonical replay')
        archives.append(dict(kind=kind,original_sha512=ref.sha(original),actual_exit=contract['actual_exit'],actual_code=contract['actual_response']['error']['code'],mismatches=contract['mismatches'],no_retry_mutation=True,restored_signature_and_hash_reverified=True))
    integrity=read(root/'INTEGRITY-STATE-CONTRACT.contract.json')
    instance=root/'instances/QA014-integrity-state';original=(root/'inputs/QA014-integrity-ORIGINAL.bin').read_bytes();ref.verify(original)
    damaged,action=mutate(original,'flip-payload')
    require(action==integrity['mutation'] and rejection(damaged)==integrity['independent_rejection'],'Independent corruption proof mismatch')
    before=read(root/'INTEGRITY-STATE-CONTRACT.before.json');after=read(root/'INTEGRITY-STATE-CONTRACT.after.json')
    for name,entry in before['files'].items():
        current=(instance/name).read_bytes()
        require(ref.sha(current)==(ref.sha(damaged) if entry['sha512']==ref.sha(original) else entry['sha512']),'Original identity/valid bytes or corrupted diagnostic target changed')
        require(after['files'][name]['sha512']==ref.sha(current),'Final inventory inconsistent')
    reference=read(root/'reference-results.json');require(len(reference)==76 and all(x['status']=='PASS' for x in reference),'Independent reference result mismatch')
    receipt=dict(format='UDDL-QA014-RECEIPT-VALIDATION',candidate_reexecuted=False,context_operator_reported=status['context_operator_reported'],
        repository_commit=status['actual_commit'],production_revision=status['production_revision'],
        candidate_sha256=status['candidate_dll_sha256'],ledger_sha256=status['ledger_library_sha256'],
        source_design_artifact_hashes_verified=True,command_receipts_verified=commands,counts=result['counts'],reference_pass=76,
        archived_retry_and_restoration=archives,integrity_diagnostic_actual=[dict(command=x['command'][:2],exit_code=x['exit_code'],error=x['response']['error']) for x in integrity['commands']],
        all_independently_verified_originals_preserved=True,production_modified=False,formal_step1_acceptance='NOT_DECLARED')
    target=root/'QA014-receipt-validation.json';require(not target.exists(),'Preserve previous validation receipt')
    target.write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(receipts_verified=commands,reference_pass=76,counts=result['counts'],restored_originals_reverified=3,physical_power_loss_proven=False)))


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--evidence',required=True);ap.add_argument('--expected-commit',default='00b54475aef34181ce9f9290c0080aa9974d45de')
    args=ap.parse_args();validate(pathlib.Path(args.evidence),args.expected_commit)
