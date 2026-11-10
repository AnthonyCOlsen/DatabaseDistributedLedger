#!/usr/bin/env python3
"""QA-owned independent OFFLINE verification of protocol vectors + Developer evidence.
NO product source or product binaries are imported. This is not DEV-003 acceptance.
Usage: python QA_DEV003_OFFLINE_INDEPENDENT_CHECK.py [--json-out PATH]
"""
import argparse, base64, hashlib, json, lzma, pathlib, struct, sys, zipfile
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

ROOT=pathlib.Path(__file__).parent
VEC=ROOT/'QA_PHASE1_REFERENCE_FIXTURES.json'
ZIP=ROOT/'DEV003-Native-Acceptance(1).zip'

def must(cond,msg):
    if not cond: raise AssertionError(msg)

def uleb(buf,i):
    total=0; shift=0; start=i
    while True:
        must(i<len(buf) and i-start<10,'truncated or oversized ULEB128')
        b=buf[i];i+=1
        total|=(b&127)<<shift
        if not b&128: break
        shift+=7
    must(total<2**64,'ULEB128 overflow')
    assert (i-start)==(1 if total==0 else (total.bit_length()+6)//7), 'non-minimal ULEB128'
    return total,i

def text(buf,i):
    n,i=uleb(buf,i); must(i+n<=len(buf),'text truncated')
    buf[i:i+n].decode('utf-8',errors='strict'); return i+n

def typed(buf,i):
    must(i<len(buf),'missing value tag')
    t=buf[i];i+=1
    must(t<=4,'unknown value tag')
    if t==0: return i
    if t==1:
        _,i=uleb(buf,i)
    elif t==2:
        must(i+8<=len(buf),'REAL truncated')
        bits=struct.unpack('>Q',buf[i:i+8])[0];i+=8
        if (bits>>52)&0x7ff==2047 and bits&((1<<52)-1):
            raise ValueError('REAL NaN forbidden')
    elif t==3: return text(buf,i)
    elif t==4:
        n,i=uleb(buf,i);must(i+n<=len(buf),'BLOB truncated');i+=n
    return i

def column(buf,i):
    must(i+16<=len(buf),'column GUID truncated');i+=16
    i=text(buf,i);i=text(buf,i)
    must(i+2<=len(buf),'column flags truncated')
    flags,presence=buf[i:i+2];i+=2
    must(flags in (0,1) and presence in (0,1),'column invalid flags')
    if presence: i=typed(buf,i)
    return i

def event_check(payload, signed, pubs):
    if not payload: return 'GENESIS'
    kind=payload[0];i=1
    if kind in (1,2):
        must(i+32<=len(payload),'DML GUIDs truncated');i+=32
        count,i=uleb(payload,i)
        last=b''
        for _ in range(count):
            must(i+16<=len(payload),'column GUID truncated')
            col=payload[i:i+16];i+=16
            must(not last or col>last,'column GUID not sorted');last=col
            i=typed(payload,i)
    elif kind==3 or kind==0x11 or kind==0x12 or kind==0x14 or kind==0x15 or kind==0x16 or kind==0x13 or kind==0x10:
        must(i+16<=len(payload),'schema/table GUID truncated');i+=16
        if kind==0x10:
            i=text(payload,i);count,i=uleb(payload,i)
            for _ in range(count): i=column(payload,i)
        elif kind==0x12:i=text(payload,i)
        elif kind in (0x14,0x15):
            must(i+16<=len(payload),'column GUID truncated');i+=16
            if kind==0x15:i=text(payload,i)
        elif kind in (0x13,0x16): i=column(payload,i)
        elif kind==3:must(i+16<=len(payload),'row GUID truncated');i+=16
    elif kind==0x20:
        must(len(payload)==33,'grant exact length');i=33
    elif kind==0x21:
        must(len(payload)==129,'revocation exact length')
        target=payload[1:33]; consent_key=payload[33:65]; consent=payload[65:129]
        signer=signed[42:74]
        must(signer!=consent_key and target in (signer,consent_key),'revocation signer role')
        msg=(b'DDL-REVOKE-v1'+signed[2:18]+signed[18:34]+signed[34:42]
             +signer+target+consent_key)
        must(len(msg)==149,'consent intent exact 149 bytes')
        Ed25519PublicKey.from_public_bytes(consent_key).verify(consent,msg)
        i=129
    else:
        raise ValueError('unsupported event kind')
    must(i==len(payload),f'event 0x{kind:02x} trailing bytes')
    return f'0x{kind:02x}'

def verify_block(item,pubs):
    raw=base64.b64decode(item['raw_b64'],validate=True)
    must(hashlib.sha512(raw).hexdigest()==item['full_sha512'],'block full SHA512')
    must(len(raw)>=211,'block short')
    ver=int.from_bytes(raw[:2],'big')
    n=int.from_bytes(raw[138:146],'big')
    must(len(raw)==211+n,'block length')
    must(hashlib.sha512(raw[147:-64]).digest()==raw[74:138],'payload SHA512')
    Ed25519PublicKey.from_public_bytes(raw[42:74]).verify(raw[-64:],raw[:-64])
    must(raw[42:74] in pubs.values(),'unexpected signer')
    must(raw[18:34].hex()==item['block_guid'].replace('-',''),'GUID bytes')
    must(struct.unpack('>q',raw[34:42])[0]==int(item['timestamp_utc_ms']),'timestamp bytes')
    if raw[146]==0: payload=raw[147:-64]
    elif raw[146]==1:
        must(raw[147]==20,'wrong LZMA property')
        payload=lzma.decompress(raw[148:-64],format=lzma.FORMAT_RAW,
                                filters=[{'id':lzma.FILTER_LZMA2,'dict_size':4<<20}])
    else:raise ValueError('unsupported compression')
    if ver!=0x0101:return 'UNSUPPORTED_VERSION'
    try: return event_check(payload,raw,pubs)
    except ValueError as e:
        if 'NaN' in str(e):return 'BAD_EVENT_NAN'
        raise

def run():
    doc=json.loads(VEC.read_text(encoding='utf-8'))
    pubs={name:bytes.fromhex(v) for name,v in doc['public_keys_hex'].items()}
    results={}; errors=[]
    for b in doc['blocks']:
        try: results[b['name']]=verify_block(b,pubs)
        except Exception as e:errors.append(f"{b['name']}: {type(e).__name__}: {e}")
    must(len(results)==26,'expected 26 signed vectors')
    must(results['genesis_A']=='GENESIS','empty genesis')
    must(results['unsupported_0001_signed']=='UNSUPPORTED_VERSION','historical version')
    must(results['real_qnan_invalid']=='BAD_EVENT_NAN','quiet NaN')
    must(results['real_snan_invalid']=='BAD_EVENT_NAN','signalling NaN')
    for x in ('real_plus_inf','real_minus_inf','real_plus_zero','real_minus_zero','compressed_grant_B_lzma2'):
        must(results[x]!='BAD_EVENT_NAN',f'{x} invalid')
    # Verify consent independently of the generator and the block's two-signature verifier.
    consent=doc['ed25519_consent'];msg=bytes.fromhex(consent['intent_hex']); sig=bytes.fromhex(consent['signature_hex'])
    must(len(msg)==149 and len(sig)==64 and msg.startswith(b'DDL-REVOKE-v1'),'revocation consent format')
    must(hashlib.sha512(msg).hexdigest()==consent['intent_sha512'],'intent SHA512')
    consent_pub=msg[-32:];Ed25519PublicKey.from_public_bytes(consent_pub).verify(sig,msg)
    # Independent AAD/authenticated key proof.
    k=doc['aes_gcm_key_file']; d=k['key_file']; aad=bytes.fromhex(k['aad_hex'])
    must(len(aad)==88 and len(bytes.fromhex(k['wrap_key_hex']))==32,'AAD length/wrap key')
    seed=AESGCM(bytes.fromhex(k['wrap_key_hex'])).decrypt(base64.b64decode(d['nonce_b64']),
           base64.b64decode(d['ciphertext_b64'])+base64.b64decode(d['tag_b64']),aad)
    must(seed.hex()==k['seed_hex'],'AES key bytes')
    # Unsigned transport. Exact framing and payloads not modified.
    batch=doc['ddlb_3_blocks']; raw=base64.b64decode(batch['raw_b64'],validate=True)
    must(hashlib.sha512(raw).hexdigest()==batch['sha512'],'DDLB digest')
    must(raw[:4]==b'DDLB' and int.from_bytes(raw[4:6],'big')==1,'DDLB header')
    total=int.from_bytes(raw[6:14],'big'); must(total==batch['block_count']==3,'DDLB count')
    off=14; embedded=[]
    for _ in range(total):
        length=int.from_bytes(raw[off:off+8],'big');off+=8
        embedded.append(raw[off:off+length]);off+=length
    must(off==len(raw),'DDLB trailing data')
    hashes={hashlib.sha512(base64.b64decode(b['raw_b64'])).hexdigest() for b in doc['blocks']}
    must(all(hashlib.sha512(b).hexdigest() in hashes for b in embedded),'DDLB embedded signatures')
    # Validate the ZIP's integrity, but never conflate developer-supplied evidence with QA execution.
    with zipfile.ZipFile(ZIP) as z:
        must(z.testzip() is None,'bad ZIP CRC')
        logname=next(n for n in z.namelist() if n.endswith('tests.log'))
        envname=next(n for n in z.namelist() if n.endswith('environment.txt'))
        exitname=next(n for n in z.namelist() if n.endswith('exit-code.txt'))
        log=z.read(logname).decode('utf-16').splitlines()
        passes=sum(line.startswith('PASS ') for line in log)
        must(passes==57,'developer log not 57 PASS')
        must('Developer regressions: 57 passed, 0 failed' in log,'developer summary differs')
        exitcode=int(z.read(exitname).decode('utf-8-sig').strip())
        environment=z.read(envname).decode('utf-8-sig')
    must(not errors,'; '.join(errors))
    return {
      'independent_offline_reference_checks':'PASS',
      'verified_signed_fixture_count':len(results),
      'verified_crypto_signatures':len(results),
      'semantic_negative_fixture_count':sum(v in ('BAD_EVENT_NAN','UNSUPPORTED_VERSION') for v in results.values()),
      'verified_revocation_consent_signature':True,
      'verified_aes256gcm_aad_88':True,
      'verified_ddlb_framing':True,
      'verified_lzma2_decoding':True,
      'developer_zip_crc':'PASS',
      'developer_log_pass_lines':passes,
      'developer_log_reported_failures':0,
      'developer_process_exit_code':exitcode,
      'candidate_binary_executed':False,
      'windows_fault_injection_executed':False,
      'acceptance_decision':'BLOCKED: candidate source/binary and native Windows 11 execution environment unavailable',
      'fixture_sha256':hashlib.sha256(VEC.read_bytes()).hexdigest(),
      'developer_evidence_zip_sha256':hashlib.sha256(ZIP.read_bytes()).hexdigest(),
      'recorded_environment':environment.strip(),
      'signature_and_event_classifications':results,
    }

def main():
    p=argparse.ArgumentParser();p.add_argument('--json-out')
    args=p.parse_args()
    result=run()
    if args.json_out:pathlib.Path(args.json_out).write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('PASS: independently parsed and cryptographically verified',result['verified_signed_fixture_count'],'signed fixture blocks')
    print('PASS: 149-byte Ed25519 consent, 88-byte AES-GCM AAD, DDLB, LZMA2, 1.01 semantic negative cases')
    print('PASS: developer-supplied ZIP integrity and reported 57/57 PASS entries (not QA execution)')
    print('BLOCKED: no candidate executable/.NET 10 runtime/Windows native platform available in this environment')

if __name__=='__main__':main()
