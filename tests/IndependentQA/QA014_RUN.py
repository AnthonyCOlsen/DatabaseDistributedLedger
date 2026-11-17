"""Final Step 1 targeted QA. Reuses existing assertions, adds approved contract checks.
No production/Developer/Architect edits or later-phase implementation. Historical
results are retained separately; this run does not declare Step 1 acceptance.
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
from QA010_RUN import Suite, Blocked, check, inventory, block_path, write_json
from QA013_MUTATIONS import mutate, rejection

EXISTING = (
    'JOIN-01','JOIN-SUBSTITUTION','JOIN-NEG-wrong-id','JOIN-NEG-wrong-pin',
    'JOIN-NEG-bad-signature','JOIN-NEG-truncated','JOIN-NEG-ddlb',
    'WIRE-NEG-legacy_0001','WIRE-NEG-legacy_0101','WIRE-NEG-legacy_0102',
    'ORDER-01','AUTH-NO-WRITE-CALLBACKS','AUTH-01','AUTH-02','AUTH-03',
    'PUB-01','PUB-02','REV-EXPIRED-RETRY','BATCH-ORDER-False','BATCH-ORDER-True',
    'BATCH-FAILED-GRANT','BATCH-MIXED','DDLB-truncated','DDLB-trailing','DDLB-overflow',
    'DDLB-zero','EXPORT-01','EXPORT-02','IDENTITY-missing','IDENTITY-corrupt')
NEW = tuple('ARCH-'+stage+'-'+kind for kind in ('quarantined','pending','revocation')
            for stage in ('RETRY','RESTORE'))+('JOIN-LEGACY-GENESIS','INTEGRITY-STATE-CONTRACT')


class FinalRegression(Suite):
    def __init__(self,args):
        super().__init__(args);self.archives={}
        self.selected=set(NEW if args.selection=='NewOnly' else EXISTING+NEW)

    def raw_response(self,root,command):
        p=self.process([self.a.dotnet,self.a.candidate,'--instance-root',root,*command])
        check(p.stdout.endswith(b'\n') and p.stdout.count(b'\n')==1,'Exactly one final JSON response')
        obj=json.loads(p.stdout.decode('utf-8','strict'))
        check(set(obj)=={'ok','data','error'} and (p.returncode==0)==obj['ok'],'Truthful CLI envelope/exit')
        if obj['ok']:check(obj['error'] is None and isinstance(obj['data'],dict),'Success schema')
        else:check(obj['data'] is None and isinstance(obj['error'],dict) and bool(p.stderr),'Failure schema/diagnostic')
        return p.returncode,obj

    def prepare_archive(self,kind):
        label='C' if kind=='pending' else 'B'
        root=self.base('qa014-archive-'+kind,label=label);self.import_named(root,'grant_B')
        if kind=='pending':self.import_named(root,'grant_C_by_B')
        if kind=='revocation':
            name='QA014-old-revocation'
            package=ref.consent(name,ref.T+1000,outer='B',counter='A',target='B')
            original=ref.revoke(name,ref.T+1000,ref.T+1001,outer='B',counter='A',target='B')
            self.import_raw(root,original)
            command=['block','publish','--revocation-consent-file',str(self.store_input(name,package))]
            receipt=self.cli(root,*command)
        else:
            body=b'\x11'+ref.TABLE;ident=str(uuid.UUID(bytes=ref.uid('QA014-publish-'+kind)))
            command=['block','publish','--event-file',str(self.store_input('QA014-body-'+kind,body)),'--block-guid',ident]
            receipt=self.cli(root,*command);original=pathlib.Path(receipt['path']).read_bytes()
        info=ref.verify(original)
        check(info['signer']==ref.PUBS[label] and info['ledger']==ref.LEDGER,'Independently verified local signer and ledger')
        check(receipt['sha512']==ref.sha(original) and receipt['timestamp_utc_ms']==str(info['time']) and receipt['durable'] is True,'Original successful metadata matches exact authenticated bytes')
        check(pathlib.Path(receipt['path'])==block_path(root,original),'Historical active path independently reconstructed')
        original_fixture=self.store_input('QA014-ORIGINAL-'+kind,original)
        if kind=='pending':
            late=ref.revoke('QA014-before-B-grant-C',ref.T+125,ref.T+125)
        else:late=self.raw['revoke_B']
        late_path=self.store_input('QA014-late-authority-'+kind,late);self.cli(root,'block','import','--input',late_path)
        accepted=[self.raw['genesis'],self.raw['grant_B'],original,late]
        if kind=='pending':accepted.append(self.raw['grant_C_by_B'])
        states,_=ref.replay(accepted);status='pending' if kind=='pending' else 'quarantined'
        check(states[ref.sha(original)]==status,'Independent canonical authority explains archived original')
        archived=block_path(root,original,True)
        check(archived.read_bytes()==original and not block_path(root,original).exists(),'Exact original bytes moved to XBLK, not rewritten')
        counts=self.counts(root,len(accepted),list(states.values()).count('pending'),list(states.values()).count('quarantined'))
        out=self.outputs/('QA014-archived-'+kind+'.BLK')
        e=self.cli(root,'block','get','--id',str(uuid.UUID(bytes=info['id'])),'--sha512',ref.sha(original),'--output',out,exit=4,code='BLOCK_NOT_FOUND')
        check(e['details']['reason']=='ARCHIVED_NOT_EXPORTABLE' and not out.exists(),'Exact digest cannot bypass archived exclusion')
        export=self.outputs/('QA014-archived-'+kind+'.DDLB');self.cli(root,'block','export','--output',export)
        exported=ref.read_ddlb(export.read_bytes())
        check(original not in exported and all(ref.sha(x) in states and states[ref.sha(x)]=='retained' for x in exported),'Only independently active signed history exported')
        context=dict(root=root,command=command,original=original,receipt=receipt,archived=archived,
                     states=states,accepted=accepted,counts=counts,status=status,original_fixture=original_fixture)
        self.archives[kind]=context
        write_json(self.root/('ARCH-'+kind+'.pre-retry-proof.json'),dict(original_fixture=str(original_fixture),
            original_sha512=ref.sha(original),historical_active_path=receipt['path'],current_archive_path=str(archived),
            expected_status=status,signature_verified=True,independent_authority_verified=True,
            original_publication=receipt,retained_count=len(accepted),exported_sha512=[ref.sha(x) for x in exported]))
        return context

    def archived_retry(self,kind):
        context=self.prepare_archive(kind);root=context['root'];before=inventory(root)
        code,obj=self.raw_response(root,context['command']);after=inventory(root)
        current_status=self.cli(root,'status')
        check(before==after and inventory(root)==after,'Archived retry and diagnostic status do not mutate files')
        check(current_status['blocks']==context['counts']['blocks'],'Archived retry does not change counters')
        check(context['archived'].read_bytes()==context['original'],'Original archived signed bytes unchanged')
        error=obj.get('error') or {};details=error.get('details') or {}
        expected_current=dict(state='archived',status=context['status'],
            authorization='pending' if kind=='pending' else 'unauthorized',
            path=str(context['archived']),retained=True)
        mismatches=[]
        if code!=4 or obj['ok'] is not False or error.get('code')!='BLOCK_ARCHIVED' or error.get('retryable') is not False:
            mismatches.append('Expected BLOCK_ARCHIVED, exit 4, retryable:false')
        if set(details)!={'original_publication','current','new_publication','mutated'}:mismatches.append('Required error.details schema absent/different')
        if details.get('original_publication')!=context['receipt']:mismatches.append('Original historical publication metadata absent/different')
        current=details.get('current') or {}
        if set(current)!={'state','status','authorization','reason','path','retained'}:mismatches.append('Required current classification schema absent/different')
        if any(current.get(k)!=v for k,v in expected_current.items()) or not isinstance(current.get('reason'),str) or not current.get('reason'):
            mismatches.append('Current archived classification/path/retention reason absent/different')
        if details.get('new_publication') is not False or details.get('mutated') is not False:mismatches.append('Required no-new-publication/no-mutation flags absent/different')
        write_json(self.root/(self.active+'.contract.json'),dict(authority='Management API 13.3.1',actual_exit=code,actual_response=obj,
            expected_original_publication=context['receipt'],expected_current=expected_current,mismatches=mismatches,
            no_filesystem_mutation=True,no_counter_change=True,original_sha512=ref.sha(context['original'])))
        write_json(self.root/(self.active+'.before.json'),before);write_json(self.root/(self.active+'.after.json'),after)
        check(not mismatches,'; '.join(mismatches))

    def restore(self,kind):
        if kind not in self.archives:raise Blocked('Archived scenario preparation did not complete; inspect prerequisite evidence')
        context=self.archives[kind];root=context['root']
        key='C' if kind=='pending' else 'B';when=ref.T+(500 if kind=='revocation' else 250)
        grant=ref.sign_block(b'\x20'+ref.PUBS[key],'QA014-restore-'+kind,timestamp=when)
        self.import_raw(root,grant);states,_=ref.replay(context['accepted']+[grant])
        check(states[ref.sha(context['original'])]=='retained','Independent regrant restores historical authorization')
        final=block_path(root,context['original'])
        check(final.read_bytes()==context['original'] and not context['archived'].exists(),'XBLK to BLK preserves original signed bytes')
        self.counts(root,len(context['accepted'])+1,list(states.values()).count('pending'),list(states.values()).count('quarantined'))
        before=inventory(root);retry=self.cli(root,*context['command'])
        check(retry==context['receipt'] and inventory(root)==before,'Restored active retry returns original metadata without fresh timestamp/signature or mutation')
        write_json(self.root/(self.active+'.recovery.json'),dict(original_sha512=ref.sha(context['original']),
            restored_sha512=ref.sha(final.read_bytes()),original_response=context['receipt'],retry_response=retry,
            filesystem_unchanged_on_retry=True))

    def incompatible_join(self):
        for name in ('legacy_0001','legacy_0101','legacy_0102'):
            root=self.fresh('QA014-join-'+name)
            self.reject_unchanged(root,['ledger','join','--ledger-id',ref.LEDGER.hex(),
                '--genesis-sha512',ref.sha(self.raw[name]),'--genesis-file',self.paths[name]],2,'UNSUPPORTED_VERSION')

    def history_integrity_contract(self):
        root=self.base('QA014-integrity-state',label='A');ident=str(uuid.UUID(bytes=ref.uid('QA014-integrity-state')))
        event=self.store_input('QA014-integrity-body',b'\x11'+ref.TABLE)
        publish=['block','publish','--event-file',str(event),'--block-guid',ident]
        receipt=self.cli(root,*publish);target=pathlib.Path(receipt['path']);original=target.read_bytes();ref.verify(original)
        before=inventory(root);self.store_input('QA014-integrity-ORIGINAL',original)
        damaged,action=mutate(original,'flip-payload');independent=rejection(damaged)
        check(target.resolve().is_relative_to(self.instances.resolve()),'Corruption confined to isolated QA instance')
        with target.open('wb') as f:f.write(damaged);f.flush();os.fsync(f.fileno())
        corrupted=inventory(root);write_json(self.root/(self.active+'.before.json'),before)
        write_json(self.root/(self.active+'.corrupted.json'),corrupted)
        status=self.cli(root,'status');check(status['instance']['recovery_codes'] and status['blocks'] is None and status['signing']['authorized'] is False,'Detected bad history cannot authorize or fabricate aggregate counts')
        observations=[];mismatches=[]
        for command in (['block','get','--id',ident,'--output',str(self.outputs/'QA014-corrupt-get.BLK')],
                        ['block','export','--output',str(self.outputs/'QA014-corrupt-export.DDLB')],
                        publish,['block','import','--input',str(self.paths['drop_table'])]):
            code,obj=self.raw_response(root,command);error=obj.get('error') or {};details=error.get('details') or {}
            observations.append(dict(command=command,exit_code=code,response=obj))
            if code!=4 or obj['ok'] is not False or error.get('code')!='RECOVERY_REQUIRED' or details.get('reason')!='LEDGER_HISTORY_CORRUPT':
                mismatches.append(' '.join(command[:2])+': expected RECOVERY_REQUIRED/4, details.reason LEDGER_HISTORY_CORRUPT')
        check(inventory(root)==corrupted and target.read_bytes()==damaged,'Original identity, valid history and corrupt diagnostic bytes preserved')
        check(not (self.outputs/'QA014-corrupt-get.BLK').exists() and not (self.outputs/'QA014-corrupt-export.DDLB').exists(),'No output authenticates corrupted history')
        write_json(self.root/(self.active+'.contract.json'),dict(authority='Management API 13.5 / QA-013 Step 1 clarification',
            mutation=action,independent_rejection=independent,status=status,commands=observations,mismatches=mismatches,
            original_identity_unchanged=True,unaffected_history_unchanged=True,automatic_repair_attempted=False))
        write_json(self.root/(self.active+'.after.json'),inventory(root))
        check(not mismatches,'; '.join(mismatches))

    def targeted(self):
        self.functional()
        for kind in ('quarantined','pending','revocation'):
            self.case('ARCH-RETRY-'+kind,['Management-13.3.1','BLOCK_ARCHIVED','exact-original-metadata','no-mutation'],lambda kind=kind:self.archived_retry(kind))
            self.case('ARCH-RESTORE-'+kind,['XBLK-BLK','late-authority','active-idempotency','exact-original-bytes'],lambda kind=kind:self.restore(kind))
        self.case('JOIN-LEGACY-GENESIS',['join-validation','version-precedence','no-mutation'],self.incompatible_join)
        self.case('INTEGRITY-STATE-CONTRACT',['Management-13.5','Step-1-integrity-diagnostics','no-auto-repair'],self.history_integrity_contract)

    def finish(self):
        actual={x['id'] for x in self.events}
        for name in EXISTING+NEW:
            if name not in actual:self.events.append(dict(id=name,status='NOT-RUN',requirements=['targeted-selection'],
                detail='Not selected in NewOnly preflight',execution_context=self.a.context))
        code=super().finish();path=self.root/'candidate-results.json';result=json.loads(path.read_text())
        counts=collections.Counter(x['status'] for x in self.events)
        result.update(qa_assignment='QA-014',selection=self.a.selection,counts={k:counts[k] for k in ('PASS','FAIL','BLOCKED','NOT-RUN')},
            planned_targeted_cases=len(EXISTING)+len(NEW),reused_existing_cases=len(EXISTING),new_contract_cases=len(NEW),
            historical_results_recounted_as_new=False,unrelated_completed_cases_reexecuted=False,
            production_modified=False,formal_step1_acceptance='NOT_DECLARED')
        write_json(path,result);return code


if __name__=='__main__':
    ap=argparse.ArgumentParser()
    for name in ('candidate','dotnet','fixtures','driver','evidence','context'):ap.add_argument('--'+name,required=True)
    ap.add_argument('--selection',choices=('Targeted','NewOnly'),default='Targeted')
    args=ap.parse_args();suite=FinalRegression(args)
    try:suite.targeted()
    except BaseException:
        detail=traceback.format_exc();(suite.root/'harness-fatal.txt').write_text(detail)
        suite.events.append(dict(id='QA014-HARNESS-FATAL',status='FAIL',requirements=['harness'],detail=detail,execution_context=args.context))
    raise SystemExit(suite.finish())
