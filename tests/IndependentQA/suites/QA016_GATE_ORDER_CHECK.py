"""Portable advisory checks supporting manual review of this exact C# call graph.
An unfamiliar helper/layout is REVIEW REQUIRED, never an inferred production defect.
This is source-order evidence, not runtime cryptographic instrumentation.
"""
import argparse
import hashlib
import json
import pathlib
import re


def clean(text):
    # Verbatim C# path strings do not treat a trailing backslash as a quote escape.
    return re.sub(r"""//[^\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'""",
        lambda m:''.join('\n' if c=='\n' else ' ' for c in m[0]),text,flags=re.S)


def method(text,marker):
    start=text.index(marker);opening=text.index('{',start);depth=1;end=opening+1
    while depth:
        depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[opening:end]


def verify(root,output):
    paths=['src/UserDimensions.Ledger/'+x+'.cs' for x in ('LedgerInstance','Management','Persistence','Protocol')]
    raw={p:(root/p).read_text(encoding='utf-8-sig') for p in paths};code={p:clean(t) for p,t in raw.items()}
    ledger=code[paths[0]];management=code[paths[1]];persistence=code[paths[2]];protocol=code[paths[3]]
    core=method(ledger,'private BlockDisposition PublishCore(');retry=method(ledger,'private BlockDisposition? ExistingPublication(')
    consent=method(ledger,'private BlockDisposition PublishConsent(');prepare=method(ledger,'private RevocationConsent CreateConsent(')
    genesis=method(ledger,'public SignedBlock InitializeLedger(');create=method(protocol,'internal static byte[] Create(')
    cases=[]
    def check(name,description,test):
        try:
            if not test():raise ValueError('Expected reviewed call/statement order not found')
            cases.append(dict(id=name,status='PASS',evidence_kind='source inspection',finding=description))
        except Exception as e:cases.append(dict(id=name,status='BLOCKED',evidence_kind='advisory source checker',finding='Independent manual re-review required: '+str(e)))
    check('SOURCE-FRESH-NO-GUID','PublishCore executes unconditional Inventory before optional retry branch, timestamp callback and CreateBlock.',
        lambda:core.index('var inventory=Inventory();')<core.index('if(id!=null')<core.index('identity.CreateBlock(') and
        core.index('var inventory=Inventory();')<core.index('utc()') and 'PublishCore(' in method(ledger,'public BlockDisposition Publish('))
    check('SOURCE-EXPLICIT-RETRY','Retry lookup consumes already verified inventory; default-null helper independently calls Inventory.',
        lambda:'ExistingPublication(id.Value,body,inventory)' in core and 'inventory ??= Inventory();' in retry and
        not re.search(r'identity\.(CreateBlock|Sign)\(',retry))
    check('SOURCE-REVOCATION-PUBLISH','Consent publication verifies retained history through default Inventory in ExistingPublication before fresh UTC and PublishCore.',
        lambda:consent.index('ExistingPublication(consent.BlockId,body)')<consent.index('var time=utc();')<consent.index('return PublishCore('))
    check('SOURCE-CONSENT-SIGN','CreateConsent validates inventory and canonical authority before identity.Sign.',
        lambda:prepare.index('Inventory()')<prepare.index('AuthorizationReplay.Evaluate(')<prepare.index('identity.Sign('))
    check('SOURCE-GENESIS-SIGN','InitializeLedger validates inventory and prior genesis evidence before signing genesis.',
        lambda:genesis.index('var inventory=Inventory();')<genesis.index('identity.CreateBlock('))
    check('SOURCE-SIGNING-CALLGRAPH','CLI calls gated LedgerInstance authoring APIs; exactly two CreateBlock sites and one consent Sign site. Primitive chain reaches Ed25519.Sign.',
        lambda:ledger.count('identity.CreateBlock(')==2 and ledger.count('identity.Sign(')==1 and
        '=> BlockCodec.Create(' in persistence and '=> Signing.Sign(seed, message)' in persistence and
        'Signing.Sign(seed, message)' in create and 'Ed25519.Sign(' in protocol and
        'instance.Publish(eventBytes,' in management and 'instance.PublishRevocationConsent(eventBytes)' in management and
        'instance.CreateRevocationConsent(' in management)
    counts={s:sum(x['status']==s for x in cases) for s in ('PASS','FAIL','BLOCKED','NOT-RUN')}
    result=dict(scope='QA-016 manual call-graph review supported by portable advisory checker',counts=counts,cases=cases,
        source_hashes=[dict(path=p,sha256=hashlib.sha256((root/p).read_bytes()).hexdigest()) for p in paths],
        source_only=True,runtime_signing_observed=False,unknown_layout_requires_manual_review=True,
        reviewed_graph=['Management -> Publish -> PublishCore -> Inventory -> SigningIdentity.CreateBlock -> BlockCodec.Create -> Signing.Sign -> Ed25519.Sign',
            'Management -> PublishRevocationConsent -> PublishConsent -> ExistingPublication -> Inventory; then PublishCore gate before CreateBlock',
            'Management -> CreateRevocationConsent -> CreateConsent -> Inventory -> AuthorizationReplay -> SigningIdentity.Sign -> Signing.Sign',
            'Management -> InitializeLedger -> Inventory -> SigningIdentity.CreateBlock'],
        limitations='Source checks describe this reviewed source snapshot, not arbitrary future helpers. Public seed-based cryptographic utilities are not ledger publication APIs.')
    output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print(json.dumps(counts))
    return 2 if counts['BLOCKED'] else 0


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--source-root',required=True);ap.add_argument('--output',required=True);a=ap.parse_args()
    raise SystemExit(verify(pathlib.Path(a.source_root),pathlib.Path(a.output)))
