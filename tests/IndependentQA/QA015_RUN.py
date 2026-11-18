"""QA-015: unchanged QA-014 regression plus isolated, independent closure checks.
Never builds or changes production. Each child keeps its own receipts and instances.
"""
import argparse
import collections
import json
import pathlib
import shutil
import subprocess
import sys
import traceback
import uuid
import QA010_REFERENCE as ref
from QA010_RUN import Suite, check, inventory, block_path, write_json
from QA014_RUN import FinalRegression, EXISTING, NEW
from QA014_INTEGRITY_REVIEW import IntegrityReview, PATTERNS
from QA013_MUTATIONS import mutate

EXTRA = tuple('ARCHIVED-DUPLICATE-'+x for x in ('quarantined','pending','revocation')) + (
    'MISSING-GENESIS-FILE','MISSING-BLK-DIRECTORY') + tuple(
    context+'-'+pattern for pattern in PATTERNS for context in ('SUPPLIED','RETAINED'))


class Additional(FinalRegression, IntegrityReview):
    def __init__(self,args):
        FinalRegression.__init__(self,args)
        self.selected=None

    def archived_duplicate(self,kind):
        c=self.prepare_archive(kind);root=c['root'];raw=c['original'];info=ref.verify(raw)
        original_error=self.cli(root,*c['command'],exit=4,code='BLOCK_ARCHIVED')
        classification=original_error['details']['current']
        before=inventory(root);responses=[]
        for attempt in range(2):
            result=self.cli(root,'block','import','--input',c['original_fixture'])
            responses.append(result);item=result['results'][0]
            check(len(result['results'])==1 and result['durable_count']==0 and result['duplicate_count']==1 and
                  result['rejected_count']==0 and result['total_count']==1,'Archived duplicate counts no new durable publication')
            check(item['index']==0 and item['sha512']==ref.sha(raw) and item['block_guid']==str(uuid.UUID(bytes=info['id'])) and
                  item['ledger_id_hex']==ref.LEDGER.hex(),'Exact original signed duplicate identity')
            check(item['duplicate'] is True and item['durable'] is True and item['retryable'] is False,'Approved archived duplicate flags')
            check(item['status']==c['status']==classification['status'] and item['authorization']==classification['authorization'] and
                  item['reason']==classification['reason'],'Current archive classification, authorization and reason retained')
            check(inventory(root)==before,'Duplicate import has no filesystem mutation')
        status=self.cli(root,'status');check(status['blocks']==c['counts']['blocks'],'No counters changed')
        check(c['archived'].read_bytes()==raw and not block_path(root,raw).exists(),'No new signature or archive restoration')
        out=self.outputs/(self.active+'-get.BLK')
        self.cli(root,'block','get','--id',str(uuid.UUID(bytes=info['id'])),'--sha512',ref.sha(raw),'--output',out,exit=4,code='BLOCK_NOT_FOUND')
        export=self.outputs/(self.active+'-export.DDLB');self.cli(root,'block','export','--output',export)
        check(not out.exists() and raw not in ref.read_ddlb(export.read_bytes()),'Duplicate never enables unauthorized retrieval/export')
        check(inventory(root)==before,'Duplicate, status and export preserve original history')
        write_json(self.root/(self.active+'.contract.json'),dict(authority='Management API 13.4',
            original_fixture=str(c['original_fixture']),original_sha512=ref.sha(raw),archive_path=str(c['archived']),
            independent_authority_status=c['status'],prior_classification=classification,imports=responses,status=status,
            no_restoration=True,no_new_signature=True,new_durable_count=0))
        write_json(self.root/(self.active+'.before.json'),before);write_json(self.root/(self.active+'.after.json'),inventory(root))

    def missing_genesis(self,whole_directory):
        root=self.base(self.active,label='A');self.import_named(root,'drop_table')
        identity_before=self.cli(root,'instance','status');before=inventory(root)
        source=root/'BLK' if whole_directory else block_path(root,self.raw['genesis'])
        saved=self.inputs/(self.active+'-WITHDRAWN')
        check(source.resolve().is_relative_to(self.instances.resolve()) and saved.resolve().is_relative_to(self.inputs.resolve()) and
              source.exists() and not saved.exists(),'Move confined to fresh isolated QA evidence, never real instances')
        shutil.move(str(source),str(saved));impaired=inventory(root)
        write_json(self.root/(self.active+'.before.json'),before);write_json(self.root/(self.active+'.impaired.json'),impaired)
        mismatches=[];status=self.cli(root,'status');identity=self.cli(root,'instance','status')
        def expect(value,message):
            if not value:mismatches.append(message)
        expect(status['signing']==dict(available=True,authorized=False),'Healthy original signing key must remain available, authorization unavailable')
        expect(identity['signing_available'] is True,'instance status incorrectly reports healthy key unavailable')
        expect('LEDGER_HISTORY_INCOMPLETE' in status['instance']['recovery_codes'] and
               'LEDGER_HISTORY_INCOMPLETE' in identity['recovery_codes'],'Missing-history diagnostic code required')
        expect(not {'IDENTITY_MISMATCH','SIGNING_IDENTITY_UNAVAILABLE'} & set(status['instance']['recovery_codes']),'Missing history is not key corruption')
        expect(status['blocks'] is None,'Unavailable aggregate counts must be null')
        expect(status['binding']['ledger_id_hex']==identity_before['ledger_id_hex'] and
               status['binding']['genesis_sha512']==identity_before['genesis_sha512'],'Original trusted ledger/genesis binding preserved')
        expect(identity['instance_guid']==identity_before['instance_guid'] and identity['public_key_hex']==identity_before['public_key_hex'] and
               identity['ledger_id_hex']==identity_before['ledger_id_hex'] and identity['genesis_sha512']==identity_before['genesis_sha512'],'Original instance identity and pin remain exact')
        event=self.store_input(self.active+'-event',b'\x11'+ref.TABLE);observations=[]
        commands=[['block','publish','--event-file',str(event)],
            ['block','get','--id',str(uuid.UUID(bytes=ref.verify(self.raw['drop_table'])['id'])),'--output',str(self.outputs/(self.active+'-get.BLK'))],
            ['block','export','--output',str(self.outputs/(self.active+'-export.DDLB'))],
            ['block','import','--input',str(self.paths['rename_table'])],
            ['master','countersign-revocation','--outer-signer-public-key',ref.PUBS['B'].hex(),'--revoked-public-key',ref.PUBS['A'].hex(),'--output',str(self.outputs/(self.active+'-consent.bin'))]]
        for command in commands:
            code,obj=self.raw_response(root,command);error=obj.get('error') or {};details=error.get('details') or {}
            observations.append(dict(command=command,exit_code=code,response=obj))
            expect(code==4 and error.get('code')=='RECOVERY_REQUIRED' and details.get('reason')=='LEDGER_HISTORY_INCOMPLETE',
                   ' '.join(command[:2])+': expected RECOVERY_REQUIRED/4, details.reason LEDGER_HISTORY_INCOMPLETE')
            expect(inventory(root)==impaired,'Ordinary operation mutated incomplete-history instance')
        incoming=[]
        for pattern,expected in PATTERNS.items():
            bad,_=mutate(self.raw['drop_table'],pattern);path=self.store_input(self.active+'-new-'+pattern,bad)
            error=self.cli(root,'block','import','--input',path,exit=2,code=expected)
            incoming.append(dict(pattern=pattern,exit_code=2,error=error))
        after=inventory(root)
        check(after==impaired,'Identity, unaffected blocks and directory inventory unchanged after all diagnostics/rejections')
        for name,item in impaired['files'].items():check(before['files'][name]==item,'Unaffected file preserved byte for byte')
        check(not list(self.outputs.glob(self.active+'-*')),'No output or success acknowledgment from restricted history operations')
        if whole_directory:
            check(inventory(saved)==inventory_from_subtree(before,'BLK'),'Entire withdrawn test BLK history remains intact as evidence')
        else:check(saved.read_bytes()==self.raw['genesis'],'Exact original pinned genesis preserved outside instance')
        write_json(self.root/(self.active+'.after.json'),after)
        write_json(self.root/(self.active+'.contract.json'),dict(authority='Technical Design accepted-history integrity; QA-015 section 3',
            defect='QA015-D01' if mismatches else None,scenario='entire BLK directory absent' if whole_directory else 'pinned genesis absent, valid other ledger file retained',
            withdrawn_original=str(saved),status=status,instance_status=identity,original_instance_status=identity_before,
            ordinary_operations=observations,invalid_new_inputs=incoming,mismatches=mismatches,
            original_identity_and_unaffected_files_preserved=True,repair_attempted=False))
        check(not mismatches,'; '.join(mismatches))

    def focused(self):
        for kind in ('quarantined','pending','revocation'):
            self.case('ARCHIVED-DUPLICATE-'+kind,['Management-13.4','archived-duplicate','no-mutation'],lambda kind=kind:self.archived_duplicate(kind))
        self.case('MISSING-GENESIS-FILE',['QA015-D01','missing-pinned-genesis','diagnostic-precedence'],lambda:self.missing_genesis(False))
        self.case('MISSING-BLK-DIRECTORY',['QA015-D01','missing-pinned-genesis','diagnostic-precedence'],lambda:self.missing_genesis(True))
        for pattern,expected in PATTERNS.items():
            self.case('SUPPLIED-'+pattern,['new-input-wire-errors'],lambda pattern=pattern,expected=expected:self.supplied(pattern,expected))
            self.case('RETAINED-'+pattern,['accepted-history-corruption','distinct-key-diagnostics','preservation'],lambda pattern=pattern,expected=expected:self.retained(pattern,expected))


def inventory_from_subtree(before,directory):
    prefix=directory+'/'
    return dict(exists=True,directories=[x[len(prefix):] for x in before['directories'] if x.startswith(prefix)],
        files={k[len(prefix):]:v for k,v in before['files'].items() if k.startswith(prefix)})


def main(args):
    evidence=pathlib.Path(args.evidence);old=evidence/'qa014';old.mkdir();extra=evidence/'additional';extra.mkdir()
    command=[sys.executable,'-B',str(pathlib.Path(__file__).with_name('QA014_RUN.py')),
        '--candidate',args.candidate,'--dotnet',args.dotnet,'--fixtures',args.fixtures,'--driver',args.driver,
        '--evidence',str(old),'--context',args.context,'--selection','Targeted']
    p=subprocess.run(command,capture_output=True,cwd=evidence)
    (evidence/'qa014.stdout.txt').write_bytes(p.stdout);(evidence/'qa014.stderr.txt').write_bytes(p.stderr)
    write_json(evidence/'qa014.process.json',dict(arguments=command,exit_code=p.returncode))
    print(p.stdout.decode('utf-8',errors='replace'),end='',flush=True)
    args.evidence=str(extra);args.selection='Targeted';suite=Additional(args)
    try:suite.focused()
    except BaseException:
        detail=traceback.format_exc();(extra/'harness-fatal.txt').write_text(detail)
        suite.events.append(dict(id='QA015-HARNESS-FATAL',status='FAIL',detail=detail,requirements=['harness'],execution_context=args.context))
    Suite.finish(suite)
    additional=json.loads((extra/'candidate-results.json').read_text());prior=json.loads((old/'candidate-results.json').read_text())
    cases=[]
    for name,result in (('qa014',prior),('additional',additional)):
        for case in result['cases']:cases.append(dict(case,evidence_child=name))
    observed={x['id'] for x in cases}
    for name in EXISTING+NEW+EXTRA:
        if name not in observed:cases.append(dict(id=name,status='NOT-RUN',detail='No completed result receipt',evidence_child='unknown'))
    counts=collections.Counter(x['status'] for x in cases)
    result=dict(suite='QA-015',context_operator_reported=args.context,counts={k:counts[k] for k in ('PASS','FAIL','BLOCKED','NOT-RUN')},
        planned_cases=49,cases=cases,qa014_original_assertions_unchanged=True,production_modified=False,
        readiness='NOT_READY' if counts['FAIL'] or counts['BLOCKED'] or counts['NOT-RUN'] or args.context!='OrdinaryWindowsPowerShell' else 'QA_REVIEW_REQUIRED',
        formal_step1_acceptance='NOT_DECLARED',physical_power_loss_proven=False)
    write_json(evidence/'candidate-results.json',result)
    print(json.dumps(dict(qa015_counts=result['counts'],readiness=result['readiness'])),flush=True)
    return 1 if counts['FAIL'] else 2 if counts['BLOCKED'] or counts['NOT-RUN'] else 0


if __name__=='__main__':
    ap=argparse.ArgumentParser()
    for name in ('candidate','dotnet','fixtures','driver','verifier','evidence','context'):ap.add_argument('--'+name,required=True)
    raise SystemExit(main(ap.parse_args()))
