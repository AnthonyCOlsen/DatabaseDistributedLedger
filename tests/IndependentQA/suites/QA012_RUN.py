"""QA-012 observable software qualification; original OS-internal points are NOT EXECUTED.
No QA-011 test is rerun. Its completed Owner evidence is validated and carried forward.
All candidate filesystem cases are Windows-backend qualification, not portable-core tests.
"""
import argparse
import hashlib
import json
import pathlib
import uuid
import QA010_REFERENCE as ref
from QA010_RUN import Suite, check, write_json, inventory, block_path


def configuration(raw, manifest, expected_value):
    check(not raw.startswith(b'\xef\xbb\xbf'),'Config must be UTF-8 without BOM')
    def pairs(items):
        obj={}
        for k,v in items:
            check(k not in obj,'Duplicate configuration property');obj[k]=v
        return obj
    value=json.loads(raw.decode('utf-8','strict'),object_pairs_hook=pairs)
    check(set(value)=={'format','version','instance_guid','limits'},'Complete config has exact top-level fields')
    check(value['format']=='UDDL-INSTANCE-CONFIG' and type(value['version']) is int and value['version']==1,'Config v1 format')
    check(value['instance_guid']==manifest['instance_guid'],'Config retains original instance GUID')
    defaults=dict(max_raw_block_bytes=expected_value,max_decompressed_event_bytes=67108864,max_inflight_import_bytes=134217728,per_block_timeout_seconds=60)
    check(value['limits']==defaults and all(type(x) is int for x in value['limits'].values()),'Complete unmixed old or new configuration')
    return value


class Qualification(Suite):
    def carried(self):
        previous=pathlib.Path(self.a.qa011_evidence)
        run=json.loads((previous/'run-status.json').read_text());cases=json.loads((previous/'candidate-results.json').read_text())
        check(run['context_operator_reported']=='OrdinaryWindowsPowerShell' and run['actual_commit']=='8bc96f0f513faac57c1db23a8189288479d8a992','Carry forward completed Owner-run QA-011, same candidate')
        check(hashlib.sha256(pathlib.Path(self.a.candidate).read_bytes()).hexdigest()==run['candidate_dll_sha256'],'Current frozen candidate equals QA-011 executed binary')
        library=pathlib.Path(self.a.candidate).parent/'UserDimensions.Ledger.dll'
        check(hashlib.sha256(library.read_bytes()).hexdigest()==run['ledger_library_sha256'],'Current frozen library equals QA-011 executed library')
        selected={c['id']:c['status'] for c in cases['cases'] if c['id'] in ('JOIN-01','JOIN-NEG-ddlb','FI-09','FI-09-GRANT','FI09-READY-RELEASE-RESULT')}
        check(len(selected)==5 and set(selected.values())=={'PASS'},'Requested completed focused checks remain evidenced without duplicate execution')
        evidence=dict(evidence_root=str(previous),candidate_commit=run['actual_commit'],candidate_sha256=run['candidate_dll_sha256'],ledger_sha256=run['ledger_library_sha256'],case_results=selected,
                      reexecuted=False,live_result_limitation='QA-011 paired completed result evidence remains separate from unavailable live-result object')
        write_json(self.root/'QA011-carried-forward.json',evidence)

    def managed(self, root, command, **extras):
        return dict(root=str(root),operation='management',command=[str(x) for x in command],
                    trace=str(self.outputs/(self.active+'-'+uuid.uuid4().hex+'.trace.json')),**extras)

    def response(self, held, expected_exit=0, code=None):
        p,out,err,prefix,request,marker=held
        check(p.returncode==expected_exit,f'Expected command exit {expected_exit}, observed {p.returncode}')
        raw=prefix.with_suffix('.stdout.txt').read_bytes()
        check(raw.endswith(b'\n') and raw.count(b'\n')==1,'One truthful final JSON response')
        obj=json.loads(raw)
        check(obj['ok']==(expected_exit==0),'No false success acknowledgment')
        if code:check(obj['error']['code']==code,f'Expected {code}, got {obj["error"]["code"]}')
        return obj

    def adapter(self, request, expected_exit=0, code=None):
        path=self.store_input(self.active+'-request',json.dumps(request).encode())
        p=self.process([self.a.dotnet,self.a.driver,path])
        check(p.returncode==expected_exit,f'Expected {expected_exit}, observed {p.returncode}: {p.stdout.decode(errors="replace")}')
        obj=json.loads(p.stdout);check(obj['ok']==(expected_exit==0),'No false success on failed/uncertain operation')
        if code:check(obj['error']['code']==code,f'Expected {code}, got {obj["error"]["code"]}')
        return obj

    def no_ack(self, held):
        check(not held[3].with_suffix('.stdout.txt').read_bytes(),'No success/durable acknowledgment while operation is held at READY')

    def preserved(self, root, before):
        after=inventory(root)
        for name,record in before['files'].items():
            if name=='instance.config.json':continue
            check(after['files'].get(name)==record,'Previously accepted signed bytes and original identity/binding remain unchanged: '+name)

    def block_setup(self, name):
        root=self.base(name,label='A');ident=str(uuid.uuid4());body=self.store_input(name+'-body',b'\x11'+ref.TABLE)
        command=['block','publish','--event-file',body,'--block-guid',ident]
        return root,ident,body,command

    def publication_at_ready(self, held, root, ident, before_operation):
        self.no_ack(held)
        temps=list((root/'BLK').rglob('.uddl-*.tmp'))
        if before_operation:
            check(len(temps)==1,'One complete flushed temporary block before final publication')
            raw=temps[0].read_bytes();info=ref.verify(raw);destination=block_path(root,raw)
            check(not destination.exists(),'New block is not visible at final BLK before rename')
        else:
            blocks=[p for p in root.rglob('*.BLK') if ref.verify(p.read_bytes())['id']==uuid.UUID(ident).bytes]
            check(len(blocks)==1 and not temps,'After successful rename exactly one complete final block and no new temp remain')
            destination=blocks[0];raw=destination.read_bytes();info=ref.verify(raw)
            check(destination==block_path(root,raw),'Final path matches independently verified timestamp/ID/hash')
        check(info['id']==uuid.UUID(ident).bytes and info['ledger']==ref.LEDGER and info['signer']==ref.PUBS['A'] and info['body']==b'\x11'+ref.TABLE,'Independent signed-block fields and exact payload')
        states,_=ref.replay([self.raw['genesis'],raw]);check(set(states.values())=={'retained'},'Independent historical authority of new block')
        write_json(self.root/(self.active+'.independent-ready.json'),dict(checkpoint=json.loads(held[5].read_text())['checkpoint'],temporary_file=str(temps[0]) if before_operation else None,
            final_path=str(destination),sha512=ref.sha(raw),signature_verified=True,authority_verified=True,acknowledgment_observed=False,
            observation='before native filesystem call' if before_operation else 'after native filesystem call returned; not an internal OS checkpoint'))
        return raw,destination,temps[0] if before_operation else None

    def config_setup(self, name):
        root=self.base(name);self.cli(root,'config','set','--name','max_raw_block_bytes','--value','6000')
        old=(root/'instance.config.json').read_bytes();manifest=json.loads((root/'instance.identity.json').read_bytes())
        configuration(old,manifest,6000)
        return root,old,manifest,['config','set','--name','max_raw_block_bytes','--value','8000']

    def config_ready(self, held, root, manifest, before_operation):
        self.no_ack(held);canonical=root/'instance.config.json';temps=list(root.glob('.uddl-*.tmp'))
        if before_operation:
            configuration(canonical.read_bytes(),manifest,6000);check(len(temps)==1,'One complete replacement config temp')
            raw=temps[0].read_bytes();configuration(raw,manifest,8000)
        else:
            raw=canonical.read_bytes();configuration(raw,manifest,8000);check(not temps,'Atomic replacement has consumed its temporary file')
        write_json(self.root/(self.active+'.config-ready.json'),dict(old_or_new_complete=True,expected_active_value=6000 if before_operation else 8000,
            replacement_sha512=ref.sha(raw),instance_guid=manifest['instance_guid'],binding=manifest['ledger_binding'],acknowledgment_observed=False,
            observation='before native replacement call' if before_operation else 'after successful replacement call return; not inside OS'))
        return raw

    def qualification(self):
        self.carried()
        def block_before():
            root,ident,body,command=self.block_setup('fi07-before');before=inventory(root)
            held=self.driver(self.managed(root,command,barrier='before-block-rename'),kill=False)
            try:raw,destination,temp=self.publication_at_ready(held,root,ident,True)
            finally:self.release_driver(held,terminate=True)
            check(held[0].returncode!=0 and not held[3].with_suffix('.stdout.txt').read_bytes(),'Pre-operation termination produces no success acknowledgment')
            check(temp.read_bytes()==raw and not destination.exists(),'Complete authentic temporary bytes survive without a false final block')
            self.preserved(root,before);self.counts(root,1)
            gate_before=inventory(root);self.cli(root,*command,exit=6,code='DURABILITY_UNCERTAIN');check(inventory(root)==gate_before,'Uncertain retry cannot restamp/overwrite original temp evidence')
            result=self.cli(root,'block','import','--input',temp);check(result['durable_count']==1,'Explicit exact-byte import recovers complete original temporary block')
            check(destination.read_bytes()==raw and temp.read_bytes()==raw,'Recovery preserves original signed bytes and surviving diagnostic temp')
            retried=self.cli(root,*command);check(retried['sha512']==ref.sha(raw),'Idempotent retry returns original block after explicit recovery')
            self.counts(root,2);self.preserved(root,before)
        self.case('FI07-SW-BEFORE-KILL',['FI-07-alternative','complete-authentic-temp','no-premature-ack','safe-retry'],block_before,fault=True)
        for terminate in (True,False):
            def block_after(terminate=terminate):
                root,ident,body,command=self.block_setup('fi07-after-'+str(terminate));before=inventory(root)
                held=self.driver(self.managed(root,command,barrier='after-block-rename'),kill=False)
                try:raw,destination,_=self.publication_at_ready(held,root,ident,False)
                finally:self.release_driver(held,terminate=terminate)
                if terminate:check(held[0].returncode!=0 and not held[3].with_suffix('.stdout.txt').read_bytes(),'Post-operation termination before normal acknowledgment')
                else:
                    response=self.response(held)['data'];check(response['durable'] is True and response['sha512']==ref.sha(raw) and pathlib.Path(response['path'])==destination,'Successful durable response only after RELEASE/completion')
                snapshot=inventory(root);retry=self.cli(root,*command)
                check(retry['sha512']==ref.sha(raw) and destination.read_bytes()==raw and inventory(root)==snapshot,'Restart/retry preserves exact original accepted bytes without restamping or overwrite')
                self.counts(root,2);self.preserved(root,before)
            self.case('FI07-SW-AFTER-'+('KILL' if terminate else 'RELEASE'),['FI-07-alternative','after-call-return','exact-bytes','truthful-ack'],block_after,fault=True)
        def block_held():
            root,ident,body,command=self.block_setup('fi07-held-source');before=inventory(root)
            publisher=self.driver(self.managed(root,command,barrier='before-block-rename'),kill=False)
            try:
                raw,destination,temp=self.publication_at_ready(publisher,root,ident,True)
                case=self.active;self.active=case+'-holder'
                try:holder=self.driver(dict(root=str(root),operation='hold-file',input=str(temp)),kill=False)
                finally:self.active=case
            except BaseException:
                self.release_driver(publisher,terminate=True);raise
            try:
                self.release_driver(publisher);self.response(publisher,6,'IO_ERROR')
            finally:
                case=self.active;self.active=case+'-holder'
                try:self.release_driver(holder,terminate=True)
                finally:self.active=case
            check(temp.read_bytes()==raw and not destination.exists(),'Real exclusive source handle causes truthful failure and preserves original complete bytes')
            self.preserved(root,before);self.counts(root,1)
            self.cli(root,'block','import','--input',temp);check(destination.read_bytes()==raw,'Recovery succeeds after held handle is released')
            self.counts(root,2)
        self.case('FI07-SW-HELD-SOURCE',['FI-07-alternative','Windows-sharing-error','no-false-success'],block_held,fault=True)
        def no_overwrite():
            root=self.base('fi07-no-overwrite');before=inventory(root)
            result=self.import_named(root,'genesis');check(result['duplicate_count']==1 and result['durable_count']==0 and inventory(root)==before,'Previously accepted original genesis is not overwritten on duplicate receipt')
            target=self.outputs/'existing-output.bin';original=b'QA012 existing output';target.write_bytes(original)
            self.cli(root,'block','get','--id',str(uuid.UUID(bytes=ref.uid('genesis'))),'--output',target,exit=4,code='OUTPUT_EXISTS')
            check(target.read_bytes()==original and inventory(root)==before,'Existing output and signed history remain unchanged on no-overwrite failure')
        self.case('FI07-SW-NO-OVERWRITE',['FI-07-alternative','basic-no-overwrite'],no_overwrite,fault=True)
        def block_uncertain():
            root,ident,body,command=self.block_setup('fi07-uncertain');before=inventory(root)
            # Independently authored fixture has a known destination, allowing a precise
            # post-rename injected error through the real Management import command.
            raw=self.raw['drop_table'];destination=block_path(root,raw)
            request=self.managed(root,['block','import','--input',self.paths['drop_table']],throw_checkpoint='after-rename:'+destination.name)
            self.adapter(request,6,'IO_ERROR')
            check(destination.read_bytes()==raw,'Unacknowledged post-call observer failure preserves already published signed bytes')
            result=self.import_named(root,'drop_table');check(result['duplicate_count']==1 and result['durable_count']==0,'Retry diagnoses existing publication without double-counting')
            self.counts(root,2);self.preserved(root,before)
        self.case('FI07-SW-POST-CALL-ERROR',['FI-07-alternative','injected-observer-error','uncertain-ack','no-data-loss'],block_uncertain,fault=True)
        for before_operation,terminate in ((True,True),(False,True),(False,False)):
            def config_crash(before_operation=before_operation,terminate=terminate):
                name='fi15-'+str(before_operation)+'-'+str(terminate);root,old,manifest,command=self.config_setup(name);before=inventory(root)
                checkpoint=('before-rename:' if before_operation else 'after-rename:')+'instance.config.json'
                held=self.driver(self.managed(root,command,barrier=checkpoint),kill=False)
                try:new=self.config_ready(held,root,manifest,before_operation)
                finally:self.release_driver(held,terminate=terminate)
                if terminate:check(held[0].returncode!=0 and not held[3].with_suffix('.stdout.txt').read_bytes(),'Interrupted replacement never acknowledges success')
                else:check(self.response(held)['data']['limits']['max_raw_block_bytes']==8000,'Config success only after post-call RELEASE')
                actual=(root/'instance.config.json').read_bytes();expected=6000 if before_operation else 8000
                configuration(actual,manifest,expected);check(actual==(old if before_operation else new),'Complete old or complete new image survives the named boundary')
                check(self.cli(root,'config','get')['limits']['max_raw_block_bytes']==expected,'Restart reads actual complete configuration, not fabricated defaults')
                self.preserved(root,before);self.cli(root,*command)
                check((root/'instance.config.json').read_bytes()==new,'Retry installs exactly the complete valid replacement image')
                configuration((root/'instance.config.json').read_bytes(),manifest,8000);self.preserved(root,before)
            label='BEFORE-KILL' if before_operation else 'AFTER-KILL' if terminate else 'AFTER-RELEASE'
            self.case('FI15-SW-'+label,['FI-15-alternative','complete-old-or-new','identity-trust-preserved','truthful-ack'],config_crash,fault=True)
        def config_held():
            root,old,manifest,command=self.config_setup('fi15-held-target');before=inventory(root)
            publisher=self.driver(self.managed(root,command,barrier='before-rename:instance.config.json'),kill=False)
            try:
                new=self.config_ready(publisher,root,manifest,True);case=self.active;self.active=case+'-holder'
                try:holder=self.driver(dict(root=str(root),operation='hold-file',input=str(root/'instance.config.json')),kill=False)
                finally:self.active=case
            except BaseException:self.release_driver(publisher,terminate=True);raise
            try:self.release_driver(publisher);self.response(publisher,6,'IO_ERROR')
            finally:
                case=self.active;self.active=case+'-holder'
                try:self.release_driver(holder,terminate=True)
                finally:self.active=case
            check((root/'instance.config.json').read_bytes()==old,'Real held-target replacement failure preserves complete old image')
            configuration(old,manifest,6000);self.preserved(root,before)
            self.cli(root,*command);check((root/'instance.config.json').read_bytes()==new,'Released handle permits complete replacement retry')
            self.preserved(root,before)
        self.case('FI15-SW-HELD-TARGET',['FI-15-alternative','Windows-sharing-error','complete-old-image','no-false-success'],config_held,fault=True)
        def config_uncertain():
            root,old,manifest,command=self.config_setup('fi15-post-error');before=inventory(root)
            self.adapter(self.managed(root,command,throw_checkpoint='after-rename:instance.config.json'),6,'IO_ERROR')
            new=(root/'instance.config.json').read_bytes();configuration(new,manifest,8000)
            check(new!=old and self.cli(root,'config','get')['limits']['max_raw_block_bytes']==8000,'No false acknowledgment; restart correctly reads already completed new image')
            self.preserved(root,before)
        self.case('FI15-SW-POST-CALL-ERROR',['FI-15-alternative','injected-observer-error','complete-new-image','no-false-success'],config_uncertain,fault=True)

    def finish(self):
        code=super().finish();path=self.root/'candidate-results.json';obj=json.loads(path.read_text())
        obj.update(qa_assignment='QA-012',scope='New observable Windows software qualification only; QA-011 not reexecuted',
                   original_os_internal_checkpoints=[dict(id=x,status='NOT_EXECUTED',reason='Replaced for this assignment by approved observable software qualification; no callback inserted inside OS') for x in ('FI-07','FI-15')],
                   platforms_not_tested=['Linux','macOS','other operating systems'],physical_power_loss_proven=False)
        write_json(path,obj)
        return code


if __name__=='__main__':
    ap=argparse.ArgumentParser()
    for name in ('candidate','dotnet','fixtures','evidence','context','qa011-evidence'):ap.add_argument('--'+name,required=True)
    ap.add_argument('--driver',default='');args=ap.parse_args();suite=Qualification(args)
    try:suite.qualification()
    except BaseException:
        import traceback
        (suite.root/'harness-fatal.txt').write_text(traceback.format_exc())
        suite.events.append(dict(id='QA012-HARNESS-FATAL',requirements=['harness'],status='FAIL',execution_context=args.context,detail=traceback.format_exc()))
    raise SystemExit(suite.finish())
