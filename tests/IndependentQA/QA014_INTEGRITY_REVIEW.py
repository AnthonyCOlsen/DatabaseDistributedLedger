"""Reference-backed closure review: supplied bytes versus corrupted retained history.
Seven focused candidate cases; no production modifications or repair operations.
"""
import argparse
import base64
import collections
import json
import os
import pathlib
import traceback
import uuid
import QA010_REFERENCE as ref
from QA010_RUN import Suite, check, inventory, block_path, write_json
from QA013_MUTATIONS import mutate, rejection

PATTERNS={'truncate-1':'BAD_FRAME','flip-payload':'HASH_MISMATCH','flip-signature':'BAD_SIGNATURE'}


def reference(fixtures,output):
    doc=json.loads(pathlib.Path(fixtures).read_text(encoding='utf-8'))
    vector=next(x for x in doc['vectors'] if x['name']=='drop_table')
    raw=base64.b64decode(vector['raw_b64'],validate=True);ref.verify(raw)
    check(ref.sha(raw)==vector['sha512'],'Exact original independent fixture identity')
    cases=[dict(id='REF-INTEGRITY-ORIGINAL',status='PASS',sha512=ref.sha(raw))]
    for pattern,expected in PATTERNS.items():
        bad,action=mutate(raw,pattern);actual=rejection(bad);check(actual==expected,'Independent corruption classification')
        cases.append(dict(id='REF-INTEGRITY-'+pattern,status='PASS',independent_error=actual,mutation=action))
    write_json(pathlib.Path(output),dict(suite='independent-integrity-reference',candidate_executed=False,
        counts=dict(PASS=4,FAIL=0,BLOCKED=0,NOT_RUN=0),cases=cases))
    print(json.dumps(dict(PASS=4,FAIL=0)))


class IntegrityReview(Suite):
    def direct(self,path,expected):
        p=self.process([self.a.dotnet,self.a.verifier,path]);obj=json.loads(p.stdout)
        check(p.returncode==2 and obj.get('accepted') is False and obj.get('code')==expected,'Direct candidate byte validation retains its wire diagnostic')
        return dict(exit_code=p.returncode,response=obj)

    def supplied(self,pattern,expected):
        root=self.base('supplied-'+pattern);before=inventory(root)
        bad,action=mutate(self.raw['drop_table'],pattern);check(rejection(bad)==expected,'Independent invalid supplied-byte oracle')
        path=self.store_input(self.active+'-supplied',bad);parsed=self.direct(path,expected)
        error=self.cli(root,'block','import','--input',path,exit=2,code=expected)
        status=self.cli(root,'status')
        check(status['instance']['state']=='bound' and not status['instance']['recovery_codes'],'Rejected new input does not put healthy history into recovery')
        check(status['signing']['available'] is True and status['blocks']['retained_count']==1,'Healthy identity and genesis remain available')
        check(inventory(root)==before,'Invalid supplied input does not mutate history, identity or directories')
        write_json(self.root/(self.active+'.contexts.json'),dict(context='new supplied input',mutation=action,
            independent_error=expected,direct_parser=parsed,incoming_import=error,status=status,instance_unchanged=True))

    def retained(self,pattern,expected):
        root=self.base('retained-'+pattern,label='A');self.import_named(root,'drop_table')
        ident=str(uuid.UUID(bytes=ref.uid('integrity-review-'+pattern)))
        event=self.store_input(self.active+'-event',b'\x11'+ref.TABLE)
        publication=['block','publish','--event-file',str(event),'--block-guid',ident]
        receipt=self.cli(root,*publication);target=pathlib.Path(receipt['path']);original=target.read_bytes()
        info=ref.verify(original);states,_=ref.replay([self.raw['genesis'],self.raw['drop_table'],original])
        check(receipt['durable'] is True and receipt['sha512']==ref.sha(original),'Accepted publication acknowledged with exact signed bytes')
        check(info['id']==uuid.UUID(ident).bytes and info['signer']==ref.PUBS['A'] and set(states.values())=={'retained'},'Independent signature and historical authority')
        self.store_input(self.active+'-ORIGINAL',original);before=inventory(root)
        damaged,action=mutate(original,pattern);check(rejection(damaged)==expected,'Independent damaged-history proof')
        check(target.resolve().is_relative_to(self.instances.resolve()),'Corruption target confined to fresh QA instance')
        with target.open('wb') as f:f.write(damaged);f.flush();os.fsync(f.fileno())
        corrupted=inventory(root);copy=self.store_input(self.active+'-DAMAGED',damaged)
        direct=self.direct(copy,expected)
        observations=[]
        commands=[publication,
            ['block','get','--id',ident,'--output',str(self.outputs/(self.active+'-get.BLK'))],
            ['block','export','--output',str(self.outputs/(self.active+'-export.DDLB'))],
            ['block','import','--input',str(self.paths['rename_table'])]]
        for command in commands:
            error=self.cli(root,*command,exit=4,code='RECOVERY_REQUIRED')
            check(error['details']['reason']=='LEDGER_HISTORY_CORRUPT','Stored-history failure carries approved recovery reason')
            observations.append(dict(command=command,exit_code=4,error=error))
            check(inventory(root)==corrupted,'Restricted normal operation does not alter surviving history or evidence')
        # Invalid new bytes are still rejected as input even while the store is restricted.
        incoming=self.cli(root,'block','import','--input',copy,exit=2,code=expected)
        status=self.cli(root,'status');identity=self.cli(root,'instance','status')
        check(status['instance']['state'] in ('bound','recovery_required'),'Allowed diagnostic state, not a fabricated universal state rule')
        check(status['instance']['recovery_codes'] and not {'IDENTITY_MISMATCH','SIGNING_IDENTITY_UNAVAILABLE'} & set(status['instance']['recovery_codes']),'History diagnosis is distinct from signing-key corruption')
        check(status['signing']['authorized'] is False and status['blocks'] is None,'Unavailable authority/counts never reported as healthy or zero')
        check(status['signing']['available'] is True and identity['signing_available'] is True,'Observed healthy signing identity remains available diagnostically')
        check(status['binding']['ledger_id_hex']==ref.LEDGER.hex() and status['binding']['genesis_sha512']==ref.sha(self.raw['genesis']),'Trusted ledger/genesis binding preserved')
        check(status['instance']['instance_guid']==identity['instance_guid'] and status['instance']['public_key_hex']==ref.PUBS['A'].hex(),'Original identity remains exact')
        after=inventory(root);relative=target.relative_to(root).as_posix()
        for name,item in before['files'].items():
            if name!=relative:check(after['files'].get(name)==item,'Unaffected original file preserved: '+name)
        check(after==corrupted and target.read_bytes()==damaged,'Diagnostic corrupt bytes preserved; no automatic repair/deletion/re-signing')
        check(not list(self.outputs.glob(self.active+'-*.BLK')) and not list(self.outputs.glob(self.active+'-*.DDLB')),'No corrupt history output published')
        write_json(self.root/(self.active+'.before.json'),before);write_json(self.root/(self.active+'.corrupted.json'),corrupted)
        write_json(self.root/(self.active+'.after.json'),after)
        write_json(self.root/(self.active+'.contexts.json'),dict(context='previously accepted durable history',target=str(target),mutation=action,
            independently_verified_original_sha512=ref.sha(original),independent_corruption_error=expected,
            direct_parser_on_same_damaged_bytes=direct,ordinary_operations=observations,malformed_input_while_restricted=incoming,
            status=status,instance_status=identity,identity_and_unaffected_history_preserved=True,automatic_repair=False))

    def key_control(self):
        root=self.base('signing-key-control',label='A');key=root/'signing-key.v1.json'
        self.store_input('KEY-CONTROL-ORIGINAL',key.read_bytes());key.write_bytes(b'{}')
        before=inventory(root);status=self.cli(root,'status');identity=self.cli(root,'instance','status')
        check(status['signing']['available'] is False and identity['signing_available'] is False,'Signing-key corruption is unavailable, unlike the healthy-key history cases')
        check('IDENTITY_MISMATCH' in status['instance']['recovery_codes'] and 'LEDGER_HISTORY_CORRUPT' not in status['instance']['recovery_codes'],'Distinct key-specific diagnostic')
        error=self.cli(root,'block','export','--output',self.outputs/'key-control.DDLB',exit=4,code='RECOVERY_REQUIRED')
        check(error['details']['reason']=='IDENTITY_MISMATCH','Ordinary-operation key gate remains distinct')
        check(inventory(root)==before,'Diagnostics and failed export never silently regenerate the damaged test key')
        write_json(self.root/(self.active+'.contexts.json'),dict(context='test-only signing-key corruption control',status=status,instance_status=identity,error=error,instance_unchanged=True))

    def focused(self):
        for pattern,expected in PATTERNS.items():
            self.case('SUPPLIED-'+pattern,['direct-byte-validation','incoming-wire-errors','no-mutation'],lambda pattern=pattern,expected=expected:self.supplied(pattern,expected))
            self.case('RETAINED-'+pattern,['accepted-history-recovery','diagnostics','safe-operational-gate','identity-preservation'],lambda pattern=pattern,expected=expected:self.retained(pattern,expected))
        self.case('KEY-CORRUPTION-CONTROL',['distinct-key-history-diagnostics','no-key-regeneration'],self.key_control)

    def finish(self):
        code=super().finish();path=self.root/'candidate-results.json';obj=json.loads(path.read_text())
        counts=collections.Counter(x['status'] for x in self.events)
        obj.update(scope='QA-014 corrupted BLK diagnostic conformance follow-up',counts={k:counts[k] for k in ('PASS','FAIL','BLOCKED','NOT-RUN')},
            planned_cases=7,production_modified=False,historical_evidence_preserved=True,physical_power_loss_proven=False,formal_step1_acceptance='NOT_DECLARED')
        write_json(path,obj);return code


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--fixtures',required=True);ap.add_argument('--reference-only',action='store_true');ap.add_argument('--results')
    for arg in ('candidate','dotnet','verifier','evidence','context'):ap.add_argument('--'+arg)
    args=ap.parse_args()
    if args.reference_only:reference(args.fixtures,args.results)
    else:
        args.driver='';suite=IntegrityReview(args)
        try:suite.focused()
        except BaseException:
            detail=traceback.format_exc();(suite.root/'harness-fatal.txt').write_text(detail)
            suite.events.append(dict(id='INTEGRITY-REVIEW-HARNESS-FATAL',status='FAIL',requirements=['harness'],detail=detail,execution_context=args.context))
        raise SystemExit(suite.finish())
