"""Portable deterministic corruption fixtures; independent oracle, no candidate code.

All seeds in QA010_REFERENCE are public NONPRODUCTION test identities. Mutations
describe damaged bytes, not a normative on-disk recovery policy or OS simulation.
"""
import argparse
import base64
import collections
import json
import pathlib
import QA010_REFERENCE as ref

PATTERNS = ('truncate-1', 'truncate-7', 'truncate-64', 'truncate-into-header',
            'zero-length', 'flip-header', 'flip-payload', 'flip-hash',
            'flip-signature', 'append-1', 'append-8')
FOCUSED = ('truncate-1', 'flip-payload', 'append-8')


def mutate(raw, name):
    if len(raw) < 244:
        raise ValueError('Use a complete signed block with at least 17 payload bytes')
    action = dict(pattern=name, original_length=len(raw), offset_basis='zero-based bytes')
    if name.startswith('truncate-'):
        removed = len(raw)-162 if name=='truncate-into-header' else int(name.split('-')[1])
        damaged = raw[:-removed]
        action.update(operation='truncate', offset=len(damaged), removed_length=removed)
    elif name=='zero-length':
        damaged=b''; action.update(operation='truncate', offset=0, removed_length=len(raw))
    elif name.startswith('flip-'):
        offset,mask={'flip-header':(50,1), 'flip-payload':(171,4),
                     'flip-hash':(90,128), 'flip-signature':(len(raw)-17,32)}[name]
        data=bytearray(raw);data[offset]^=mask;damaged=bytes(data)
        action.update(operation='xor', offset=offset, length=1, xor_mask=mask,
                      original_byte=raw[offset], damaged_byte=damaged[offset])
    elif name.startswith('append-'):
        suffix=b'\x00' if name=='append-1' else b'QA013\x00\xff\x7f'
        damaged=raw+suffix
        action.update(operation='append', offset=len(raw), appended_hex=suffix.hex(), appended_length=len(suffix))
    else:
        raise ValueError('Unknown corruption pattern: '+name)
    action.update(damaged_length=len(damaged), original_sha512=ref.sha(raw), damaged_sha512=ref.sha(damaged))
    return damaged,action


def rejection(raw):
    try:
        ref.verify(raw)
    except ref.Invalid as e:
        return e.code
    raise AssertionError('Independent verifier authenticated damaged bytes')


def fixtures():
    original=ref.sign_block(b'\x11'+ref.TABLE,'QA013 corruption reference',timestamp=ref.T+200)
    vectors=[]
    for pattern in PATTERNS:
        raw,action=mutate(original,pattern)
        vectors.append(dict(**action, raw_b64=base64.b64encode(raw).decode(), independent_error=rejection(raw)))
    return dict(format='UDDL-QA013-CORRUPTION-FIXTURES',version=1,protocol='1.03',
                nonproduction=True, seed_description='Public deterministic QA010 NONPRODUCTION seed A; UUIDv5 QA013 corruption reference',
                original_b64=base64.b64encode(original).decode(),original_sha512=ref.sha(original),vectors=vectors)


def validate(path, output):
    actual=json.loads(pathlib.Path(path).read_text(encoding='utf-8'))
    if actual!=fixtures():
        raise AssertionError('Fixture bytes/offsets/expected independent rejections differ from deterministic definition')
    original=base64.b64decode(actual['original_b64'],validate=True);ref.verify(original)
    cases=[dict(id='REF013-ORIGINAL',status='PASS',sha512=ref.sha(original))]
    for vector in actual['vectors']:
        raw=base64.b64decode(vector['raw_b64'],validate=True)
        observed=rejection(raw)
        if observed!=vector['independent_error']:
            raise AssertionError('Independent rejection mismatch')
        cases.append(dict(id='REF013-'+vector['pattern'],status='PASS',independent_error=observed,
                          damaged_sha512=ref.sha(raw),mutation={k:v for k,v in vector.items() if k!='raw_b64'}))
    result=dict(suite='QA013-independent-reference',candidate_executed=False,
                counts=dict(PASS=len(cases),FAIL=0,BLOCKED=0,NOT_RUN=0),cases=cases,
                physical_storage_tested=False,formal_step1_acceptance='NOT_DECLARED')
    pathlib.Path(output).write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result['counts']))


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--write-fixtures');ap.add_argument('--fixtures');ap.add_argument('--results')
    args=ap.parse_args()
    if args.write_fixtures:
        target=pathlib.Path(args.write_fixtures)
        if target.exists():raise SystemExit('Refusing to overwrite fixtures')
        target.write_text(json.dumps(fixtures(),indent=2)+'\n',encoding='utf-8')
    elif args.fixtures and args.results:
        validate(args.fixtures,args.results)
    else:
        ap.error('Choose --write-fixtures PATH or --fixtures PATH --results PATH')
