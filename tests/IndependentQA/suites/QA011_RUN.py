"""Focused independent QA-011. Production and Developer regressions are never edited.

Reuse the unchanged QA-010 JOIN-01/JOIN-NEG-ddlb assertions. New fixtures and
expected hashes/order use only the independent Python reference implementation.
"""
import argparse
import ast
import hashlib
import json
import pathlib
import uuid
import QA010_REFERENCE as ref
import QA010_RUN as old
from QA010_RUN import check, write_json, inventory, block_path, Blocked


class Focused(old.Suite):
    def setup_batch(self, name):
        root=self.base(name)
        raws=[self.raw['B_data'],self.raw['grant_B']]
        path=self.store_input(name+'-inverted',ref.ddlb(raws))
        return root,path

    def request(self, root, path, **extras):
        return dict(root=str(root),operation='batch',input=str(path),
                    trace=str(self.outputs/(self.active+'-'+uuid.uuid4().hex+'.trace.json')),**extras)

    def run_adapter(self, request, exit=0):
        if not self.a.driver:raise Blocked('QA adapter unavailable')
        p=self.process([self.a.dotnet,self.a.driver,self.store_input(self.active+'-request-'+uuid.uuid4().hex,json.dumps(request).encode())])
        check(p.returncode==exit,f'Adapter expected exit {exit}, observed {p.returncode}: {p.stdout.decode(errors="replace")}')
        check(p.stdout.endswith(b'\n') and p.stdout.count(b'\n')==1,'Actual Management.Run emits exactly one JSON object')
        response=json.loads(p.stdout)
        check(response['ok']==(exit==0),'Actual Management.Run success/failure envelope')
        trace=json.loads(pathlib.Path(request['trace']).read_text())
        return response,trace

    def names(self, trace):
        return [x['checkpoint'] for x in trace['batch_events']]

    def expected_sequence(self):
        return ['batch-grant-durable:1:'+ref.sha(self.raw['grant_B']),
                'batch-item-completed:1:'+ref.sha(self.raw['grant_B']),
                'batch-item-completed:0:'+ref.sha(self.raw['B_data'])]

    def barrier_check(self, held, root, checkpoint):
        p,out,err,prefix,request,marker=held
        receipt=json.loads(marker.read_text());write_json(self.root/(self.active+'.ready-receipt.json'),receipt)
        check(receipt['pid']==p.pid and receipt['checkpoint']==checkpoint,'Exact positive READY/PID/checkpoint acknowledgment')
        event=receipt['batch_events'][-1]
        grant=self.raw['grant_B'];dependent=self.raw['B_data'];expected=block_path(root,grant)
        check(event['input_index']==1 and event['canonical_ordinal']==0,'Transport index 1 is separately recorded as canonical execution ordinal 0')
        check(event['sha512']==ref.sha(grant) and event['matching_final_blk_files']==[str(expected)],'Exact callback hash and final signed BLK location')
        check(expected.read_bytes()==grant,'Exact independently authored grant bytes at READY')
        verified=ref.verify(expected.read_bytes());check(verified['kind']==0x20 and verified['body'][1:]==ref.PUBS['B'],'Independent signature, hash, grant grammar and grantee verification')
        states,_=ref.replay([self.raw['genesis'],grant]);check(set(states.values())=={'retained'},'Independent canonical authority of the surviving grant')
        check(not block_path(root,dependent).exists() and not block_path(root,dependent,True).exists(),'Dependent has not been published or archived')
        check(not list(root.rglob('.uddl-*')),'No temporary incoming block or directory at READY')
        check(len(list(root.rglob('*.BLK')))==2,'Only genesis and selected grant exist at READY')
        expected_prefix=self.expected_sequence()[:1 if checkpoint.startswith('batch-grant-durable:') else 2]
        check([x['checkpoint'] for x in receipt['batch_events']]==expected_prefix,'Correct original-index callback sequence before pausing')
        # The callback contract exposes no result object. Do not invent durable/duplicate
        # fields here. The separate released trial inspects actual returned fields.
        check(receipt['current_import_result_exposed'] is False,'Receipt must disclose the lack of a live result object')
        write_json(self.root/(self.active+'.independent-ready-check.json'),dict(
            original_input_index=1,canonical_ordinal=0,grant_sha512=verified['hash'],genesis_sha512=ref.sha(self.raw['genesis']),
            grant_final_path=str(expected),grant_signature_verified=True,dependent_absent=True,incoming_temp_absent=True,
            live_import_result_available=False,returned_result_evidence='Separate completed READY/RELEASE trial'))

    def finished_new_results(self, response):
        check(response['ok'] is True and response['error'] is None,'Completed import success')
        data=response['data'];check((data['durable_count'],data['duplicate_count'],data['rejected_count'],data['total_count'])==(2,0,0,2),'Two newly durable accepted items, no duplicates/rejections')
        for i,name in enumerate(('B_data','grant_B')):
            result=data['results'][i]
            check(result['index']==i and result['sha512']==ref.sha(self.raw[name]),'Returned item index/hash independent of canonical execution order')
            check(result['durable'] is True and result['duplicate'] is False and result['status']=='retained' and result['authorization']=='authorized','Actual completed import result confirms newly durable acceptance')

    def focused(self):
        # Selection changes dispatch only; original join test bodies/assertions are reused.
        self.selected={'JOIN-01','JOIN-NEG-ddlb'}
        self.functional()
        self.selected=None
        def released():
            root,path=self.setup_batch('fi09-release');checkpoint=self.expected_sequence()[1]
            request=self.request(root,path,barrier=checkpoint)
            held=self.driver(request,kill=False)
            try:self.barrier_check(held,root,checkpoint)
            except BaseException:
                self.release_driver(held,terminate=True);raise
            self.release_driver(held)
            response=json.loads(held[3].with_suffix('.stdout.txt').read_bytes())
            self.finished_new_results(response)
            trace=json.loads(pathlib.Path(request['trace']).read_text())
            check(self.names(trace)==self.expected_sequence(),'Complete grant/item/dependent callback ordering')
            check([x['input_index'] for x in trace['batch_events']]==[1,1,0] and [x['canonical_ordinal'] for x in trace['batch_events']]==[0,0,1],'Original input indices and canonical ordinals kept separate')
            self.counts(root,3)
            write_json(self.root/'FI09-completed-result-proof.json',dict(actual_completed_response=response,trace=trace,
                scope='Result flags observed AFTER release/completion, not exposed inside READY callback'))
        self.case('FI09-READY-RELEASE-RESULT',['FI-09','newly-durable-result','callback-order','positive-ready-release'],released,fault=True)
        for label,checkpoint in (('FI-09',self.expected_sequence()[1]),('FI-09-GRANT',self.expected_sequence()[0])):
            def killed(label=label,checkpoint=checkpoint):
                root,path=self.setup_batch(label.lower()+'-kill');identity=inventory(root)
                request=self.request(root,path,barrier=checkpoint);held=self.driver(request,kill=False)
                try:self.barrier_check(held,root,checkpoint)
                finally:self.release_driver(held,terminate=True)
                check(held[0].returncode!=0,'Externally terminated worker cannot report successful command completion')
                check(not held[3].with_suffix('.stdout.txt').read_bytes(),'No fabricated pre-termination CLI result')
                self.counts(root,2)
                grant=self.raw['grant_B'];dep=self.raw['B_data']
                check(block_path(root,grant).read_bytes()==grant and not block_path(root,dep).exists(),'Restart preserves exact grant, dependent still absent')
                for name in ('signing-key.v1.json','instance.identity.json'):
                    check(inventory(root)['files'][name]==identity['files'][name],'Process interruption preserves original identity and binding')
                retry=self.cli(root,'block','import','--input',path)
                check((retry['durable_count'],retry['duplicate_count'],retry['rejected_count'],retry['total_count'])==(1,1,0,2),'Restart/reimport reports one duplicate grant and one new dependent')
                items=retry['results'];check(items[1]['duplicate'] is True and items[1]['durable'] is True and items[0]['duplicate'] is False and items[0]['durable'] is True,'Correct per-input recovered accounting')
                self.counts(root,3);check(block_path(root,grant).read_bytes()==grant and block_path(root,dep).read_bytes()==dep,'Original/recovered signed hashes and bytes unchanged')
                again=self.cli(root,'block','import','--input',path);check(again['durable_count']==0 and again['duplicate_count']==2,'Repeated recovery never duplicates grant or dependent')
                write_json(self.root/(label+'.recovery-hashes.json'),dict(original_grant_sha512=ref.sha(grant),recovered_grant_sha512=ref.sha(block_path(root,grant).read_bytes()),dependent_sha512=ref.sha(block_path(root,dep).read_bytes()),retry_results=retry,second_retry_results=again))
            self.case(label,['actual-DDLB-path','external-termination','durable-prefix','grant-before-dependent'],killed,fault=True)
        def duplicates():
            root,path=self.setup_batch('duplicate-callback');self.cli(root,'block','import','--input',path);before=inventory(root)
            response,trace=self.run_adapter(self.request(root,path))
            data=response['data'];check(data['durable_count']==0 and data['duplicate_count']==2,'Actual duplicate import has no newly durable items')
            check(all(x['duplicate'] is True and x['durable'] is True for x in data['results']),'Durable duplicate is distinguished from new publication')
            check(self.names(trace)==self.expected_sequence()[1:],'Duplicate grants produce item-completed but no grant-durable callback')
            check(inventory(root)==before,'Duplicate callbacks cannot change instance filesystem')
        self.case('CALLBACK-DUPLICATE',['callback-not-proof-of-new-durability','duplicate-grants'],duplicates)
        def rejected():
            root=self.base('rejected-callback');self.import_named(root,'grant_B');self.import_named(root,'revoke_B')
            names=['unknown_D','B_data','grant_B','drop_table','real_quiet_nan']
            path=self.store_input('mixed-callback',ref.ddlb([self.raw[x] for x in names]))
            response,trace=self.run_adapter(self.request(root,path),exit=3)
            check(response['error']['code']=='PARTIAL_IMPORT','Mixed batch truthfully reports partial completion')
            data=response['error']['details'];check((data['durable_count'],data['duplicate_count'],data['rejected_count'],data['total_count'])==(1,1,3,5),'Mixed new/duplicate/rejected truthful counts')
            results=data['results']
            check(results[0]['reason']=='AUTHORITY_UNRESOLVED' and results[0]['retryable'] is True and results[0]['durable'] is False,'Unresolved candidate rejected without durability')
            check(results[1]['reason']=='NOT_AUTHORIZED' and results[1]['retryable'] is False and results[1]['durable'] is False,'Known unauthorized candidate rejected without durability')
            check(results[2]['duplicate'] is True and results[2]['durable'] is True and results[3]['duplicate'] is False and results[3]['durable'] is True,'Duplicate versus newly accepted result flags')
            check(results[4]['reason']=='BAD_EVENT' and results[4]['durable'] is False,'Malformed event rejected during parsing')
            canonical=sorted(range(4),key=lambda i:ref.order(ref.verify(self.raw[names[i]])))
            expected=['batch-item-completed:'+str(i)+':'+ref.sha(self.raw[names[i]]) for i in canonical]
            check(self.names(trace)==expected,'Completed callbacks include parsed rejects/duplicates, exclude malformed noncandidate, and do not imply durability')
            for event in trace['batch_events']:
                i=event['input_index'];check(event['sha512']==ref.sha(self.raw[names[i]]),'Exact callback identity/hash')
                if i in (0,1):check(event['matching_final_blk_files']==[],'Rejected item-completed has no final BLK')
            for name in ('unknown_D','B_data'):
                check(not block_path(root,self.raw[name]).exists() and not block_path(root,self.raw[name],True).exists(),'Rejected batch inputs never persist')
            check(not list(root.rglob('.uddl-*')),'Rejected inputs do not leave temporary incoming files')
            self.counts(root,4)
        self.case('CALLBACK-REJECTED',['callback-not-proof-of-durability','partial-import','rejected-candidates'],rejected)
        for prefix in ('batch-grant-durable','batch-item-completed'):
            def interrupted(prefix=prefix):
                root,path=self.setup_batch('throw-'+prefix);checkpoint=prefix+':1:'+ref.sha(self.raw['grant_B'])
                request=self.request(root,path,throw_checkpoint=checkpoint)
                response,trace=self.run_adapter(request,exit=3)
                check(response['error']['code']=='PARTIAL_IMPORT','Injected observer exception must preserve truthful partial outcome')
                data=response['error']['details'];check((data['durable_count'],data['duplicate_count'],data['rejected_count'],data['total_count'])==(1,0,1,2),'Completed durable grant receipt is retained on observer failure')
                check(data['results'][1]['durable'] is True and data['results'][1]['duplicate'] is False and data['results'][0]['durable'] is False,'No false dependent success or lost grant receipt')
                expected=self.expected_sequence()[:1 if prefix=='batch-grant-durable' else 2]
                check(self.names(trace)==expected,'No next candidate checkpoint after observer interruption')
                check(block_path(root,self.raw['grant_B']).read_bytes()==self.raw['grant_B'] and not block_path(root,self.raw['B_data']).exists(),'Grant survives unchanged and dependent remains absent')
                self.counts(root,2);retry=self.cli(root,'block','import','--input',path)
                check(retry['durable_count']==1 and retry['duplicate_count']==1,'Recovery from observer exception has honest duplicate/new counts')
                self.counts(root,3)
            self.case('CALLBACK-THROW-'+prefix,['callback-interruption','honest-durable-counts','dependent-not-persisted'],interrupted,fault=True)
        def normal():
            root,path=self.setup_batch('normal-cli');normal=self.cli(root,'block','import','--input',path)
            other,other_path=self.setup_batch('normal-api');request=self.request(other,other_path);request['operation']='batch-normal'
            response,trace=self.run_adapter(request)
            check(trace==dict(observed_checkpoints=[],batch_events=[]),'Normal three-argument entry point cannot activate unprovided test callbacks')
            check(response['data']==normal,'Normal CLI and normal API preserve identical import contract')
            before=inventory(root);self.cli(root,'block','import','--input',path,'--checkpoint','batch-item-completed',exit=2,code='INVALID_ARGUMENT')
            check(inventory(root)==before,'Normal CLI exposes no fault-control flag and cannot mutate on invalid arguments')
        self.case('CALLBACK-NORMAL-RUNTIME',['production-disabled-default','normal-CLI-contract'],normal)
        for name,reason in (('FI-07','No safe observable rename/pre-directory-confirmation callback inside MoveFileExW WRITE_THROUGH'),
                            ('FI-15','No safe observable config-replacement/pre-directory-confirmation callback inside MoveFileExW WRITE_THROUGH')):
            def blocked(reason=reason):raise Blocked(reason+'; existing before/after tests retained, no artificial intermediate callback')
            self.case(name,['qualification-required'],blocked,fault=True)
        def live_result():
            raise Blocked('Callback exposes index/hash only, not live import-result fields. Paired completed READY/RELEASE trial verifies actual result flags separately; terminated-worker READY cannot expose those fields without an additional interface.')
        self.case('FI09-LIVE-RESULT-SNAPSHOT',['literal-live-result-at-READY'],live_result,fault=True)

    def finish(self):
        code=super().finish()
        path=self.root/'candidate-results.json';obj=json.loads(path.read_text())
        obj.update(qa_assignment='QA-011',scope='Focused: unchanged join assertions and implemented batch callbacks',
                   broader_independent_regression='NOT-RUN; QA decides follow-up scope',
                   live_result_limitation='No per-item result object exposed at READY; paired completed release result is separate evidence')
        write_json(path,obj)
        return code


if __name__=='__main__':
    ap=argparse.ArgumentParser()
    for name in ('candidate','dotnet','fixtures','evidence','context'):ap.add_argument('--'+name,required=True)
    ap.add_argument('--driver',default='');args=ap.parse_args();s=Focused(args)
    try:s.focused()
    except BaseException:
        import traceback
        (s.root/'harness-fatal.txt').write_text(traceback.format_exc())
        s.events.append(dict(id='QA011-HARNESS-FATAL',requirements=['harness'],status='FAIL',execution_context=args.context,detail=traceback.format_exc()))
    raise SystemExit(s.finish())
