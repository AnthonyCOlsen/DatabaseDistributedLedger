"""Execute unchanged QA-015 assertions, plus three fresh no-GUID runtime checks."""
import argparse
import collections
import json
import os
import pathlib
import QA010_REFERENCE as ref
from QA010_RUN import Suite,check,inventory,block_path,write_json
from QA013_MUTATIONS import mutate
from QA015_RUN import main as qa015_main


class GateRuntime(Suite):
    def publication(self,condition):
        root=self.base('fresh-'+condition,label='A');self.import_named(root,'drop_table')
        identity=self.cli(root,'instance','status');original_inventory=inventory(root)
        damaged_path=None
        if condition=='corrupt':
            damaged_path=block_path(root,self.raw['drop_table']);raw=damaged_path.read_bytes()
            self.store_input(self.active+'-ORIGINAL',raw);bad,action=mutate(raw,'flip-payload')
            check(damaged_path.resolve().is_relative_to(self.instances.resolve()),'Damage limited to isolated test instance')
            with damaged_path.open('wb') as f:f.write(bad);f.flush();os.fsync(f.fileno())
        elif condition=='missing':
            path=block_path(root,self.raw['genesis']);saved=self.inputs/(self.active+'-ORIGINAL-GENESIS.BLK')
            check(path.resolve().is_relative_to(self.instances.resolve()) and saved.resolve().is_relative_to(self.inputs.resolve()),'Only fresh test genesis moved to preserved evidence')
            path.rename(saved);check(saved.read_bytes()==self.raw['genesis'],'Withdrawn exact genesis retained')
        before=inventory(root);event=self.store_input(self.active+'-event',b'\x11'+ref.TABLE)
        command=['block','publish','--event-file',str(event)]
        if condition=='healthy':
            response=self.cli(root,*command);path=pathlib.Path(response['path']);raw=path.read_bytes();info=ref.verify(raw)
            states,_=ref.replay([self.raw['genesis'],self.raw['drop_table'],raw])
            after=inventory(root);new=set(after['files'])-set(before['files'])
            check(new=={path.relative_to(root).as_posix()} and path==block_path(root,raw),'Exactly one new correctly named block')
            check(response['durable'] is True and response['authorization']=='authorized' and response['sha512']==ref.sha(raw) and info['signer']==ref.PUBS['A'],'Independent signature and truthful durable result')
            check(response['timestamp_utc_ms']==str(info['time']) and set(states.values())=={'retained'},'Signed timestamp and independent canonical authority')
            check(all(after['files'].get(k)==v for k,v in before['files'].items()),'Original identity and signed history unchanged')
            self.counts(root,3);error=None
        else:
            reason='LEDGER_HISTORY_CORRUPT' if condition=='corrupt' else 'LEDGER_HISTORY_INCOMPLETE'
            error=self.cli(root,*command,exit=4,code='RECOVERY_REQUIRED');response=None
            check(error['details']['reason']==reason,'Correct recovery-gate reason')
            status=self.cli(root,'status');check(status['signing']==dict(available=True,authorized=False) and status['blocks'] is None,'Healthy key diagnostics remain distinct')
            after=inventory(root);check(after==before,'No failed publication, new file, archive transition or identity mutation')
            check(not list(root.rglob('.uddl-*.tmp')),'No incoming publication temp remains')
        current=self.cli(root,'instance','status')
        for key in ('instance_guid','public_key_hex','ledger_id_hex','genesis_sha512'):
            check(current[key]==identity[key],'Original identity/trust binding preserved: '+key)
        check(inventory(root)==after,'Diagnostic access does not mutate instance files')
        check(not list(self.outputs.iterdir()),'CLI created no external output')
        write_json(self.root/(self.active+'.original.json'),original_inventory)
        write_json(self.root/(self.active+'.before.json'),before);write_json(self.root/(self.active+'.after.json'),after)
        write_json(self.root/(self.active+'.contract.json'),dict(condition=condition,command=command,error=error,response=response,
            instance_root=str(root),identity_before=identity,identity_after=current,runtime_signing_observed=False,
            outcome='Exactly one independently authenticated durable publication' if condition=='healthy' else 'Recovery rejection without publication or filesystem mutation'))


def main(args):
    root=pathlib.Path(args.evidence);code=qa015_main(args)
    prior=json.loads((root/'candidate-results.json').read_text());write_json(root/'qa015-candidate-results.json',prior)
    args.evidence=str(root/'gate-runtime');pathlib.Path(args.evidence).mkdir();suite=GateRuntime(args)
    for condition in ('corrupt','missing','healthy'):
        suite.case('FRESH-NO-GUID-'+condition.upper(),['pre-signing-runtime','no-explicit-guid','identity-and-byte-preservation'],lambda condition=condition:suite.publication(condition))
    Suite.finish(suite);extra=json.loads((pathlib.Path(args.evidence)/'candidate-results.json').read_text())
    cases=[dict(c,evidence_group='qa015-regression') for c in prior['cases']]+[dict(c,evidence_group='gate-runtime') for c in extra['cases']]
    counts=collections.Counter(c['status'] for c in cases);source=json.loads((root/'source-order-results.json').read_text())
    result=dict(assignment='QA-016',context_operator_reported=args.context,candidate_counts={s:counts[s] for s in ('PASS','FAIL','BLOCKED','NOT-RUN')},
        candidate_cases=cases,source_counts=source['counts'],reference_counts=dict(PASS=76,FAIL=0,BLOCKED=0,**{'NOT-RUN':0}),
        runtime_signing_observed=False,source_order_evidence_separate=True,historical_qa015_results_rewritten=False,
        independent_disposition='QA READY' if args.context=='OrdinaryWindowsPowerShell' and counts['PASS']==52 and source['counts']['PASS']==6 else 'QA NOT READY',
        formal_owner_step1_acceptance='NOT_DECLARED')
    write_json(root/'qa016-results.json',result);print(json.dumps(dict(candidate=result['candidate_counts'],source=result['source_counts'],disposition=result['independent_disposition'])),flush=True)
    return 1 if counts['FAIL'] or source['counts']['FAIL'] else 2 if counts['BLOCKED'] or counts['NOT-RUN'] or source['counts']['BLOCKED'] else 0


if __name__=='__main__':
    ap=argparse.ArgumentParser()
    for name in ('candidate','dotnet','fixtures','driver','verifier','evidence','context'):ap.add_argument('--'+name,required=True)
    raise SystemExit(main(ap.parse_args()))
