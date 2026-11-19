"""Independent simulated-byte-corruption tests, not disk/power-loss certification.

Candidate errors/states are observations unless strict integrity establishes an
invariant. Accepted-file restoration/state policy remains SPEC-PENDING. Mutations
are confined to fresh instances below this run's evidence directory.
"""
import argparse
import collections
import json
import os
import pathlib
import traceback
import uuid
import QA010_REFERENCE as ref
from QA010_RUN import Suite, Blocked, check, write_json, inventory, block_path
from QA013_MUTATIONS import PATTERNS, FOCUSED, mutate, rejection


class Corruption(Suite):
    def __init__(self,args):
        super().__init__(args)
        self.observations=[]
        self.patterns=FOCUSED if args.selection=='Focused' else PATTERNS

    def observe(self,root,args,output=None,output_kind=None,known=None):
        before=inventory(root)
        p=self.process([self.a.dotnet,self.a.candidate,'--instance-root',root,*args])
        check(p.stdout.endswith(b'\n') and p.stdout.count(b'\n')==1 and not p.stdout.startswith(b'\xef\xbb\xbf'),'One complete UTF-8 CLI JSON envelope')
        obj=json.loads(p.stdout.decode('utf-8','strict'))
        check(set(obj)=={'ok','data','error'} and type(obj['ok']) is bool,'CLI envelope schema')
        check((p.returncode==0)==obj['ok'],'Exit and success envelope agree')
        if obj['ok']:
            check(obj['error'] is None and isinstance(obj['data'],dict),'Success data/error schema')
        else:
            check(obj['data'] is None and isinstance(obj['error'],dict) and bool(p.stderr),'Failure data/error/diagnostic schema')
            check(set(obj['error'])=={'code','message','retryable','details'},'Error schema')
        record=dict(command=list(map(str,args)),exit_code=p.returncode,response=obj,
                    inventory_before=before,inventory_after=inventory(root),output=str(output) if output else None)
        self.observations.append(record)
        write_json(self.root/(self.active+'.observations.json'),self.observations)
        if output:
            if obj['ok']:
                check(output.is_file(),'Successful retrieval/export has an output')
                raw=output.read_bytes()
                blocks=ref.read_ddlb(raw) if output_kind=='ddlb' else [raw]
                for block in blocks:
                    ref.verify(block)
                    check(block in known,'Only previously authenticated original signed bytes may be exported as history')
                record['output_verified_blocks']=[ref.sha(x) for x in blocks]
                write_json(self.root/(self.active+'.observations.json'),self.observations)
            else:
                check(not output.exists(),'Failed retrieval/export cannot publish a final output containing corrupt history')
        return obj

    def verify_candidate(self,path,accepted):
        p=self.process([self.a.dotnet,self.a.verifier,path]);obj=json.loads(p.stdout)
        check('probe_error' not in obj,'Candidate verification adapter executed without harness exception')
        check(obj['accepted'] is accepted and (p.returncode==0)==accepted,'Candidate parser must independently reject damaged signed bytes')
        if accepted:check(obj['sha512']==ref.sha(path.read_bytes()),'Candidate parses original exact signed bytes')
        else:check(bool(obj.get('code')) and bool(p.stderr),'Actual parser rejection supplies diagnostic evidence')
        return dict(exit_code=p.returncode,response=obj)

    def preserve(self,root,before,target,damaged):
        after=inventory(root);relative=target.relative_to(root).as_posix()
        for name,record in before['files'].items():
            if name==relative:continue
            check(after['files'].get(name)==record,'Unrelated valid history, original identity, key and trust binding unchanged: '+name)
        check(target.exists() and target.read_bytes()==damaged,'Corruption diagnostic bytes are not silently deleted, rewritten or re-signed')
        old={n for n in before['files'] if n.endswith(('.BLK','.XBLK'))}
        new={n for n in after['files'] if n.endswith(('.BLK','.XBLK'))}
        check(old==new,'Corrupt evidence never silently becomes new authenticated BLK/XBLK history')
        write_json(self.root/(self.active+'.after-operations.json'),after)

    def setup(self,kind,pattern):
        root=self.base('qa013-'+kind+'-'+pattern,label='A')
        result=self.import_named(root,'drop_table');check(result['durable_count']==1,'Unrelated valid signed history durably accepted')
        self.counts(root,2)
        ident=str(uuid.UUID(bytes=ref.uid('QA013-'+kind+'-'+pattern)))
        body=self.store_input(self.active+'-event',b'\x11'+ref.TABLE)
        command=['block','publish','--event-file',str(body),'--block-guid',ident]
        return root,ident,command

    def damage(self,root,target,pattern,original):
        # Never mutate a shared fixture, candidate snapshot, repository source or real instance.
        check(root.resolve().is_relative_to(self.instances.resolve()),'Mutation root is isolated test evidence')
        check(target.resolve().is_relative_to(root.resolve()) and target.is_file() and not target.is_symlink(),'Mutation target is an existing isolated regular file')
        damaged,action=mutate(original,pattern);independent_error=rejection(damaged)
        original_path=self.inputs/(self.active+'-ORIGINAL.BLK')
        check(not original_path.exists(),'Preserve one original byte reference without overwrite')
        original_path.write_bytes(original)
        damaged_path=self.inputs/(self.active+'-DAMAGED.bin');damaged_path.write_bytes(damaged)
        before=inventory(root);write_json(self.root/(self.active+'.before-corruption.json'),before)
        with target.open('wb') as stream:
            stream.write(damaged);stream.flush();os.fsync(stream.fileno())
        check(target.read_bytes()==damaged,'Exact deterministic mutation installed at original path')
        after=inventory(root);write_json(self.root/(self.active+'.after-corruption.json'),after)
        proof=dict(**action,target=str(target),original_fixture=str(original_path),damaged_fixture=str(damaged_path),
                   original_signature_verified=True,independent_damaged_rejection=independent_error,
                   filename_unchanged=target.name,simulated_bytes_only=True,physical_storage_tested=False)
        write_json(self.root/(self.active+'.mutation.json'),proof)
        parser=self.verify_candidate(damaged_path,False)
        write_json(self.root/(self.active+'.candidate-parse.json'),parser)
        return damaged,damaged_path,before

    def incoming_rejection(self,damaged_path):
        # A healthy separate receiver ensures corruption rejection is not merely
        # caused by the already damaged local inventory or an orphan-retry fence.
        receiver=self.base(self.active+'-healthy-receiver')
        before=inventory(receiver)
        obj=self.observe(receiver,['block','import','--input',str(damaged_path)])
        check(obj['ok'] is False,'Malformed signed bytes are not accepted on a healthy receiver')
        check(inventory(receiver)==before,'Corrupt input leaves healthy receiver entirely unchanged')

    def probes(self,root,ident,command,damaged_path,original,temporary):
        known=[self.raw['genesis'],self.raw['drop_table']]+([] if temporary else [original])
        self.observe(root,['status'])
        self.observe(root,['instance','status'])
        for suffix,extra in (('id',[]),('id-and-original-sha',['--sha512',ref.sha(original)])):
            output=self.outputs/(self.active+'-'+suffix+'.BLK')
            self.observe(root,['block','get','--id',ident,*extra,'--output',str(output)],output,'raw',known)
        for suffix,extra in (('all',[]),('selected',['--id',ident,'--sha512',ref.sha(original)])):
            output=self.outputs/(self.active+'-'+suffix+'.DDLB')
            self.observe(root,['block','export',*extra,'--output',str(output)],output,'ddlb',known)
        output=self.outputs/(self.active+'-unrelated.BLK')
        self.observe(root,['block','get','--id',str(uuid.UUID(bytes=ref.verify(self.raw['drop_table'])['id'])),'--output',str(output)],output,'raw',known)
        request=self.store_input(self.active+'-snapshot-request',json.dumps(dict(root=str(root),operation='snapshot')).encode())
        p=self.process([self.a.dotnet,self.a.driver,request]);snapshot=json.loads(p.stdout)
        check(type(snapshot['completed']) is bool and (p.returncode==0)==snapshot['completed'],'Library snapshot probe supplies truthful completion')
        self.observations.append(dict(library='LedgerInstance.Snapshot',exit_code=p.returncode,response=snapshot))
        self.observe(root,['ledger','recover-genesis','--ledger-id',ref.LEDGER.hex(),'--genesis-sha512',ref.sha(self.raw['genesis'])])
        self.observe(root,command)
        self.observe(root,['block','import','--input',str(damaged_path)])
        write_json(self.root/(self.active+'.observations.json'),self.observations)

    def interrupted(self,pattern):
        root,ident,command=self.setup('temporary',pattern)
        held=self.driver(dict(root=str(root),operation='management',command=command,barrier='before-block-rename'),kill=False)
        try:
            temps=list((root/'BLK').rglob('.uddl-*.tmp'))
            check(len(temps)==1,'One completely flushed block temp at positive pre-publication READY')
            target=temps[0];original=target.read_bytes();info=ref.verify(original)
            check(info['id']==uuid.UUID(ident).bytes and info['signer']==ref.PUBS['A'],'Independent original temp signature and intended identity')
            check(not block_path(root,original).exists(),'No final publication before rename')
            check(not held[3].with_suffix('.stdout.txt').read_bytes(),'No success acknowledgment at READY')
        finally:
            self.release_driver(held,terminate=True)
        check(held[0].returncode!=0 and target.read_bytes()==original,'Terminated worker left exact original complete temp')
        original_check=self.store_input(self.active+'-valid-control',original)
        self.verify_candidate(original_check,True)
        damaged,damaged_path,before=self.damage(root,target,pattern,original)
        self.probes(root,ident,command,damaged_path,original,True)
        self.preserve(root,before,target,damaged)
        self.incoming_rejection(damaged_path)

    def accepted(self,pattern):
        root,ident,command=self.setup('accepted',pattern)
        receipt=self.cli(root,*command)
        check(receipt['durable'] is True,'Successful durable publication acknowledged before corruption')
        target=pathlib.Path(receipt['path']);original=target.read_bytes();info=ref.verify(original)
        states,_=ref.replay([self.raw['genesis'],self.raw['drop_table'],original])
        check(info['id']==uuid.UUID(ident).bytes and states[ref.sha(original)]=='retained','Independent signature and historical authority of accepted block')
        check(ref.sha(original)==receipt['sha512'] and target==block_path(root,original),'Original signed SHA-512 and canonical original filename')
        self.verify_candidate(target,True)
        # Every CLI command has exited; no instance process/lease remains during mutation.
        damaged,damaged_path,before=self.damage(root,target,pattern,original)
        self.probes(root,ident,command,damaged_path,original,False)
        self.preserve(root,before,target,damaged)
        self.incoming_rejection(damaged_path)

    def corruption(self):
        for kind,method in (('TEMP',self.interrupted),('ACCEPTED',self.accepted)):
            for pattern in PATTERNS:
                if pattern not in self.patterns:
                    self.events.append(dict(id='CORRUPT-'+kind+'-'+pattern,status='NOT-RUN',requirements=['QA-013'],
                                            detail='Focused selection; no execution',execution_context=self.a.context,category='selection'))
                    continue
                def execute(pattern=pattern,method=method):
                    self.observations=[]
                    method(pattern)
                self.case('CORRUPT-'+kind+'-'+pattern,['QA-013',kind,'independent-integrity','diagnostic-preservation'],execute,fault=(kind=='TEMP'))
        def pending(message):raise Blocked('SPEC-PENDING: '+message)
        self.case('POLICY-ACCEPTED-STATE',['QA-013','Architect-confirmation'],lambda:pending('Required state, diagnostic precedence, counters and gating/serving unrelated valid history after accepted-file damage are not fully specified. Observed behavior is recorded, not invented as an acceptance contract.'))
        self.case('POLICY-ACCEPTED-REPAIR',['QA-013','Architect-confirmation'],lambda:pending('Approved explicit repair/re-fetch/restore procedure, preservation/quarantine policy and conditions for resuming operations are not specified. No restoration/deletion/re-signing performed.'))

    def finish(self):
        code=super().finish();path=self.root/'candidate-results.json';result=json.loads(path.read_text())
        counts=collections.Counter(x['status'] for x in self.events)
        result.update(qa_assignment='QA-013-CORRUPTION',selection=self.a.selection,
                      counts={k:counts[k] for k in ('PASS','FAIL','BLOCKED','NOT-RUN')},
                      independent_reference_results_separate=True,simulated_file_byte_damage=True,
                      os_or_physical_storage_certified=False,production_modified=False,
                      recovery_policy='SPEC-PENDING',untested_platforms=[dict(platform=p,status='NOT-RUN') for p in ('Linux','macOS')],
                      formal_step1_acceptance='NOT_DECLARED')
        write_json(path,result)
        return code


if __name__=='__main__':
    ap=argparse.ArgumentParser()
    for arg in ('candidate','dotnet','fixtures','evidence','context','driver','verifier'):ap.add_argument('--'+arg,required=True)
    ap.add_argument('--selection',choices=('Focused','All'),default='Focused')
    args=ap.parse_args();suite=Corruption(args)
    try:suite.corruption()
    except BaseException:
        detail=traceback.format_exc();(suite.root/'harness-fatal.txt').write_text(detail)
        suite.events.append(dict(id='QA013-HARNESS-FATAL',status='FAIL',requirements=['harness'],detail=detail,execution_context=args.context))
    raise SystemExit(suite.finish())
