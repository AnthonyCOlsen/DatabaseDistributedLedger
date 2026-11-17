"""Independent QA-010 wire/identity oracle. No production modules or Developer fixtures.

Only public, deterministic NONPRODUCTION test seeds occur in generated fixtures.
Python struct/hashlib/lzma + cryptography Ed25519/AESGCM implement the specification.
"""
import argparse
import base64
import hashlib
import json
import lzma
import pathlib
import struct
import uuid
from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat

T = 1700000000000
DOMAIN = b"DDL-REVOKE-v3"
HEADER = struct.Struct(">H32s16sq32s64sQB")
FILTER = [{"id": lzma.FILTER_LZMA2, "dict_size": 4 << 20}]
KEYS = {x: hashlib.sha256(("QA010 NONPRODUCTION seed " + x).encode()).digest() for x in "ABCD"}


class Invalid(ValueError):
    def __init__(self, code):
        self.code = code
        super().__init__(code)


def require(condition, code):
    if not condition:
        raise Invalid(code)


def pub(seed):
    return Ed25519PrivateKey.from_private_bytes(seed).public_key().public_bytes(Encoding.Raw, PublicFormat.Raw)


PUBS = {k: pub(v) for k, v in KEYS.items()}
LEDGER = PUBS["A"]


def uid(name):
    return uuid.uuid5(uuid.NAMESPACE_URL, "https://userdimensions.example/QA010/" + name).bytes


def sha(raw):
    return hashlib.sha512(raw).hexdigest()


def var(value):
    require(0 <= value < 2**64, "BAD_EVENT")
    out = bytearray()
    while value > 127:
        out.append((value & 127) | 128)
        value >>= 7
    return bytes(out + bytes([value]))


def text(value):
    raw = value.encode("utf-8")
    return var(len(raw)) + raw


def integer(n):
    return b"\x01" + var(((n << 1) ^ (n >> 63)) & (2**64 - 1))


TABLE, ROW, COL1, COL2 = (uid(x) for x in ("table", "row", "column-1", "column-2"))
COL1, COL2 = sorted((COL1, COL2))


def row(kind, values):
    return bytes([kind]) + TABLE + ROW + var(len(values)) + b"".join(c + v for c, v in values)


def column(flags=1, default=None, declared="DECIMAL(10,2)"):
    return COL1 + text("Námé\u0000") + text(declared) + bytes([flags, default is not None]) + (default or b"")


def sign_block(body, name, signer="A", timestamp=None, ledger=LEDGER, compression=False, transmitted=None, version=0x0103):
    timestamp = T + 100 if timestamp is None else timestamp
    payload = body if body is not None else b""
    method = 1 if compression else 0
    if compression:
        payload = b"\x14" + lzma.compress(payload, format=lzma.FORMAT_RAW, filters=FILTER)
    if transmitted is not None:
        payload = transmitted
    header = HEADER.pack(version, ledger, uid(name), timestamp, PUBS[signer], hashlib.sha512(payload).digest(), len(payload), method)
    assert len(header) == 163
    message = header + payload
    return message + Ed25519PrivateKey.from_private_bytes(KEYS[signer]).sign(message)


def consent(name, time, outer="B", target="B", counter="A", ledger=LEDGER, domain=DOMAIN):
    msg = domain + ledger + uid(name) + struct.pack(">q", time) + PUBS[outer] + PUBS[target] + PUBS[counter]
    return msg + Ed25519PrivateKey.from_private_bytes(KEYS[counter]).sign(msg)


def revoke(name, consent_time, block_time, outer="A", counter="B", target="B"):
    package = consent(name, consent_time, outer, target, counter)
    event = b"\x21" + PUBS[target] + PUBS[counter] + struct.pack(">q", consent_time) + package[-64:]
    return sign_block(event, name, outer, block_time)


class Reader:
    def __init__(self, raw):
        self.raw, self.i = raw, 0

    def take(self, n):
        require(n >= 0 and n <= len(self.raw) - self.i, "BAD_EVENT")
        out = self.raw[self.i:self.i+n]
        self.i += n
        return out

    def byte(self):
        return self.take(1)[0]

    def guid(self):
        out = self.take(16)
        require(out != bytes(16), "BAD_EVENT")
        return out

    def number(self):
        value, start = 0, self.i
        for j in range(10):
            b = self.byte()
            require(j != 9 or b <= 1, "BAD_EVENT")
            value |= (b & 127) << (7*j)
            if b < 128:
                require(self.i-start == max(1, (value.bit_length()+6)//7), "BAD_EVENT")
                return value
        raise Invalid("BAD_EVENT")

    def text(self):
        try:
            return self.take(self.number()).decode("utf-8", "strict")
        except UnicodeError:
            raise Invalid("BAD_EVENT")

    def value(self):
        tag = self.byte()
        if tag == 0:
            pass
        elif tag == 1:
            self.number()
        elif tag == 2:
            bits = int.from_bytes(self.take(8), "big")
            require(not ((bits >> 52) & 2047 == 2047 and bits & ((1 << 52)-1)), "BAD_EVENT")
        elif tag == 3:
            self.text()
        elif tag == 4:
            self.take(self.number())
        else:
            raise Invalid("BAD_EVENT")
        return tag

    def column(self):
        ident = self.guid()
        self.text()
        self.text()
        require(self.byte() in (0, 1), "BAD_EVENT")
        presence = self.byte()
        require(presence in (0, 1), "BAD_EVENT")
        if presence:
            self.value()
        return ident


def check_event(body, info):
    r = Reader(body)
    kind = r.byte()
    if kind in (1, 2):
        r.guid(); r.guid()
        previous = None
        for _ in range(r.number()):
            col = r.guid()
            require(previous is None or previous < col, "BAD_EVENT")
            tag = r.value()
            require(kind != 1 or tag != 0, "BAD_EVENT")
            previous = col
    elif kind == 3:
        r.guid(); r.guid()
    elif kind in (0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16):
        r.guid()
        if kind == 0x10:
            r.text()
            ids = set()
            for _ in range(r.number()):
                ident = r.column()
                require(ident not in ids, "BAD_EVENT")
                ids.add(ident)
        elif kind == 0x12:
            r.text()
        elif kind in (0x13, 0x16):
            r.column()
        elif kind in (0x14, 0x15):
            r.guid()
            if kind == 0x15:
                r.text()
    elif kind == 0x20:
        r.take(32)
    elif kind == 0x21:
        target, counter, when, signature = r.take(32), r.take(32), struct.unpack(">q", r.take(8))[0], r.take(64)
        require(info["signer"] != counter and target in (info["signer"], counter), "BAD_EVENT")
        delta = info["time"] - when
        require(0 <= delta <= 900000, "CONSENT_EXPIRED")
        msg = DOMAIN + info["ledger"] + info["id"] + struct.pack(">q", when) + info["signer"] + target + counter
        try:
            Ed25519PublicKey.from_public_bytes(counter).verify(signature, msg)
        except (InvalidSignature, ValueError):
            raise Invalid("BAD_SIGNATURE")
        info.update(target=target, counter=counter, consent_time=when, consent=signature)
    else:
        raise Invalid("BAD_EVENT")
    require(r.i == len(body), "BAD_EVENT")
    return kind


def verify(raw, max_decoded=67108864):
    require(len(raw) >= 2, "BAD_FRAME")
    require(raw[:2] == b"\x01\x03", "UNSUPPORTED_VERSION")
    require(len(raw) >= 227, "BAD_FRAME")
    version, ledger, ident, when, signer, digest, size, compression = HEADER.unpack(raw[:163])
    require(size == len(raw)-227 and ident != bytes(16), "BAD_FRAME")
    transmitted = raw[163:-64]
    require(hashlib.sha512(transmitted).digest() == digest, "HASH_MISMATCH")
    try:
        Ed25519PublicKey.from_public_bytes(signer).verify(raw[-64:], raw[:-64])
    except (InvalidSignature, ValueError):
        raise Invalid("BAD_SIGNATURE")
    require(compression in (0, 1) and (size != 0 or compression == 0), "BAD_FRAME")
    body = transmitted
    if compression:
        require(len(body) >= 2 and body[0] == 20, "BAD_EVENT")
        try:
            decoder = lzma.LZMADecompressor(format=lzma.FORMAT_RAW, filters=FILTER)
            body = decoder.decompress(body[1:], max_length=max_decoded+1)
            require(len(body) <= max_decoded, "RESOURCE_LIMIT")
            require(decoder.eof and not decoder.unused_data, "BAD_EVENT")
        except lzma.LZMAError:
            raise Invalid("BAD_EVENT")
    require(len(body) <= max_decoded, "RESOURCE_LIMIT")
    info = dict(ledger=ledger, id=ident, time=when, signer=signer, body=body, hash=sha(raw), compression=compression)
    if size == 0:
        require(ledger == signer, "LEDGER_IDENTITY_MISMATCH")
        info["kind"] = None
    else:
        info["kind"] = check_event(body, info)
    return info


def order(block):
    return block["time"], block["id"], block["signer"]


def replay(raws):
    """Independent canonical authority oracle for one pinned, unambiguous ledger."""
    blocks = sorted((verify(x) for x in {bytes(x) for x in raws}), key=order)
    geneses = [x for x in blocks if x["kind"] is None]
    require(len(geneses) == 1, "GENESIS_CONFLICT")
    authorized, ever, states = set(), set(), {}
    for b in blocks:
        required = {b["signer"]}
        if b["kind"] == 0x21:
            required.add(b["counter"])
        good = b == geneses[0] or (b["kind"] is not None and required <= authorized)
        states[b["hash"]] = [good, required]
        if good:
            if b["kind"] is None:
                authorized.add(b["signer"]); ever.add(b["signer"])
            elif b["kind"] == 0x20:
                key = b["body"][1:33]; authorized.add(key); ever.add(key)
            elif b["kind"] == 0x21:
                authorized.discard(b["target"])
    for key, (good, required) in list(states.items()):
        states[key] = "retained" if good else "quarantined" if required <= ever else "pending"
    return states, authorized


def ddlb(raws):
    return b"DDLB\x00\x01" + struct.pack(">Q", len(raws)) + b"".join(struct.pack(">Q", len(x)) + x for x in raws)


def read_ddlb(raw):
    require(len(raw) >= 14 and raw[:6] == b"DDLB\x00\x01", "BAD_FRAME")
    pos, items = 14, []
    count = int.from_bytes(raw[6:14], "big")
    require(count <= len(raw)//8, "BAD_FRAME")
    for _ in range(count):
        require(pos+8 <= len(raw), "BAD_FRAME")
        n = int.from_bytes(raw[pos:pos+8], "big"); pos += 8
        require(n <= len(raw)-pos, "BAD_FRAME")
        items.append(raw[pos:pos+n]); pos += n
    require(pos == len(raw), "BAD_FRAME")
    return items


def aad(ident, public):
    return b"UDDL-ED25519-KEY-v1" + uuid.UUID(ident).bytes + public + b"\x00\x13application-wide-v1"


WRAP = hashlib.sha256(b"UserDimensions/SEC-002/application-wide-v1/public-obfuscation-key").digest()


def identity_files(label, binding=None):
    ident = str(uuid.UUID(bytes=uid("instance-"+label)))
    public, seed = PUBS[label], KEYS[label]
    nonce = hashlib.sha256(("QA010 nonce "+label).encode()).digest()[:12]
    encrypted = AESGCM(WRAP).encrypt(nonce, seed, aad(ident, public))
    key = dict(format="UDDL-ED25519-KEY", version=1, instance_guid=ident, public_key_hex=public.hex(), wrap_key_id="application-wide-v1",
               encryption="AES-256-GCM", nonce_b64=base64.b64encode(nonce).decode(), ciphertext_b64=base64.b64encode(encrypted[:-16]).decode(), tag_b64=base64.b64encode(encrypted[-16:]).decode())
    manifest = dict(format="UDDL-INSTANCE-IDENTITY", version=2, instance_guid=ident, public_key_hex=public.hex(), key_file="signing-key.v1.json", ledger_binding=binding)
    return key, manifest


def make_fixtures():
    vectors = []
    def add(name, body, expected="OK", **kwargs):
        raw = sign_block(body, name, **kwargs)
        vectors.append(dict(name=name, expected=expected, raw_b64=base64.b64encode(raw).decode(), sha512=sha(raw)))
        return raw
    add("genesis", None, timestamp=T)
    add("grant_B", b"\x20"+PUBS["B"], timestamp=T+100)
    add("grant_C_by_B", b"\x20"+PUBS["C"], signer="B", timestamp=T+150)
    add("B_data", b"\x11"+TABLE, signer="B", timestamp=T+300)
    add("C_data", b"\x11"+TABLE, signer="C", timestamp=T+350)
    add("regrant_B", b"\x20"+PUBS["B"], timestamp=T+250)
    for name, stamp in (("revoke_B", T+200), ("revoke_C", T+400)):
        raw = revoke(name, stamp, stamp, counter="B" if name=="revoke_B" else "C", target="B" if name=="revoke_B" else "C")
        vectors.append(dict(name=name, expected="OK", raw_b64=base64.b64encode(raw).decode(), sha512=sha(raw)))
    add("unknown_D", b"\x11"+TABLE, signer="D", timestamp=T+500)
    add("grant_D", b"\x20"+PUBS["D"], timestamp=T+450)
    for name, bits in dict(plus_zero=0, minus_zero=1<<63, plus_inf=0x7ff0000000000000, minus_inf=0xfff0000000000000,
                          subnormal=1, minus_subnormal=0x8000000000000001, max_finite=0x7fefffffffffffff,
                          quiet_nan=0x7ff8000000000000, signal_nan=0x7ff0000000000001, negative_nan=0xfff8000000000000).items():
        add("real_"+name, row(2, [(COL1, b"\x02"+struct.pack(">Q", bits))]), "BAD_EVENT" if "nan" in name else "OK")
    for name, value in dict(minimum=-(1<<63), maximum=(1<<63)-1, negative=-1, zero=0).items():
        add("integer_"+name, row(2, [(COL1, integer(value))]))
    add("text_utf8", row(2, [(COL1, b"\x03"+text("é\u0000Δ中文"))]))
    add("blob", row(2, [(COL1, b"\x04"+var(4)+b"\x00\xff\x80\x01")]))
    add("update_null", row(2, [(COL1, b"\x00")]))
    add("update_omitted", row(2, []))
    add("insert_omitted_null", row(1, []))
    add("insert_explicit_null", row(1, [(COL1, b"\x00")]), "BAD_EVENT")
    add("create_table", b"\x10"+TABLE+text("Téxt")+var(1)+column(default=b"\x00"))
    for kind, name, body in ((3,"delete",TABLE+ROW),(0x11,"drop_table",TABLE),(0x12,"rename_table",TABLE+text("new")),
                             (0x13,"add_column",TABLE+column(declared="")),(0x14,"drop_column",TABLE+COL1),
                             (0x15,"rename_column",TABLE+COL1+text("new")),(0x16,"alter_column",TABLE+column(flags=0,default=integer(5)))):
        add(name, bytes([kind])+body)
    compressed_body = row(2, [(COL1, b"\x03"+text("independent LZMA2 "*2000))])
    add("compressed_text", compressed_body, compression=True)
    add("compressed_grant_C", b"\x20"+PUBS["C"], compression=True, timestamp=T+50)
    for name, data in dict(unknown_kind=b"\xff", bad_value=row(2,[(COL1,b"\x05")]), nonminimal=row(2,[(COL1,b"\x01\x80\x00")]),
                           overflow_integer=row(2,[(COL1,b"\x01"+b"\xff"*9+b"\x02")]),
                           invalid_utf8=row(2,[(COL1,b"\x03\x01\xff")]), duplicate_columns=row(2,[(COL1,b"\x00"),(COL1,b"\x00")]),
                           unsorted_columns=row(2,[(COL2,b"\x00"),(COL1,b"\x00")]), bad_column_flags=b"\x13"+TABLE+column(flags=2),
                           event_trailing=b"\x11"+TABLE+b"\x00", truncated_real=row(2,[(COL1,b"\x02\x00")]),
                           zero_entity=b"\x11"+bytes(16), v2_revocation=b"\x21"+PUBS["B"]+PUBS["A"]+bytes(64)).items():
        add(name, data, "BAD_EVENT")
    packed = b"\x14" + lzma.compress(compressed_body,format=lzma.FORMAT_RAW,filters=FILTER)
    for name, transmitted in (("lzma_bad_property",b"\x13"+packed[1:]),("lzma_no_eos",packed[:-1]),("lzma_trailing",packed+b"\x00"),
                              ("lzma_malformed",b"\x14\xff\x00")):
        add(name, compressed_body, "BAD_EVENT", compression=True, transmitted=transmitted)
    for name, when in (("out_min",-(1<<63)),("out_max",(1<<63)-1)):
        add(name,b"\x11"+TABLE,timestamp=when)
    for delta in (0,900000,900001,-1):
        name="rev_delta_"+str(delta)
        raw=revoke(name,T+1000,T+1000+delta,outer="B",counter="A",target="B")
        vectors.append(dict(name=name,expected="OK" if 0<=delta<=900000 else "CONSENT_EXPIRED",raw_b64=base64.b64encode(raw).decode(),sha512=sha(raw)))
    for name,outer,counter,target,time,when,bad_sig in (
        ("rev_bad_countersignature","B","A","B",T+1000,T+1001,True),
        ("rev_same_signers","A","A","A",T+1000,T+1001,False),
        ("rev_third_target","B","A","C",T+1000,T+1001,False),
        ("rev_interval_overflow","B","A","B",-(1<<63),(1<<63)-1,False)):
        package=consent(name,time,outer=outer,counter=counter,target=target)
        signature=package[-64:]
        if bad_sig:signature=signature[:-1]+bytes([signature[-1]^1])
        body=b"\x21"+PUBS[target]+PUBS[counter]+struct.pack(">q",time)+signature
        add(name,body,"BAD_SIGNATURE" if bad_sig else "CONSENT_EXPIRED" if name=="rev_interval_overflow" else "BAD_EVENT",signer=outer,timestamp=when)
    raw=sign_block(None,"genesis-ledger-mismatch",ledger=PUBS["C"],timestamp=T)
    vectors.append(dict(name="genesis_ledger_mismatch",expected="LEDGER_IDENTITY_MISMATCH",exit=4,raw_b64=base64.b64encode(raw).decode(),sha512=sha(raw)))
    original = sign_block(b"\x11"+TABLE,"mutation-source")
    for name, index, expected in (("payload_hash",90,"HASH_MISMATCH"),("outer_signature",len(original)-1,"BAD_SIGNATURE"),("payload_length",154,"BAD_FRAME")):
        bad=bytearray(original);bad[index]^=1
        vectors.append(dict(name=name,expected=expected,raw_b64=base64.b64encode(bad).decode(),sha512=sha(bad)))
    for version in (1,0x0101,0x0102):
        if version==0x0102:
            raw=sign_block(None,"legacy-102",timestamp=T,version=version)
        else:
            payload=b"";msg=struct.pack(">H16s16sq32s64sQB",version,uid("legacy-ledger"),uid("legacy"+str(version)),T,PUBS["A"],hashlib.sha512(payload).digest(),0,0)
            raw=msg+Ed25519PrivateKey.from_private_bytes(KEYS["A"]).sign(msg)
        vectors.append(dict(name="legacy_"+format(version,"04x"),expected="UNSUPPORTED_VERSION",raw_b64=base64.b64encode(raw).decode(),sha512=sha(raw)))
    return dict(format="UDDL-QA010-INDEPENDENT-103",version=1,seed_warning="PUBLIC NONPRODUCTION TEST SEEDS ONLY; never use for live data",
                seeds_hex={k:v.hex() for k,v in KEYS.items()},public_keys_hex={k:v.hex() for k,v in PUBS.items()},ledger_id_hex=LEDGER.hex(),vectors=vectors)


def check_reference(doc):
    results=[]
    rfc_seed=bytes.fromhex("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60")
    assert pub(rfc_seed).hex()=="d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"
    assert Ed25519PrivateKey.from_private_bytes(rfc_seed).sign(b"").hex()=="e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"
    results.append(dict(id="REF-RFC8032",status="PASS"))
    for item in doc["vectors"]:
        raw=base64.b64decode(item["raw_b64"],validate=True)
        assert sha(raw)==item["sha512"]
        try:
            verify(raw); observed="OK"
        except Invalid as e:
            observed=e.code
        results.append(dict(id="REF-"+item["name"],status="PASS" if observed==item["expected"] else "FAIL",expected=item["expected"],observed=observed))
    byname={x["name"]:base64.b64decode(x["raw_b64"]) for x in doc["vectors"]}
    g=byname["genesis"];assert len(g)==227 and g[2:34]==LEDGER and g[58:90]==LEDGER and HEADER.size==163
    assert g[34:50]==uid("genesis") and struct.unpack(">q",g[50:58])[0]==T and g[90:154]==hashlib.sha512(b"").digest() and g[154:163]==bytes(9)
    assert read_ddlb(ddlb([g,byname["grant_B"]]))==[g,byname["grant_B"]]
    results.append(dict(id="REF-OFFSETS-DDLB",status="PASS"))
    for label in "ABCD":
        key,manifest=identity_files(label)
        associated=aad(key["instance_guid"],PUBS[label]);assert len(associated)==88
        seed=AESGCM(WRAP).decrypt(base64.b64decode(key["nonce_b64"]),base64.b64decode(key["ciphertext_b64"])+base64.b64decode(key["tag_b64"]),associated)
        assert seed==KEYS[label] and manifest["version"]==2
    results.append(dict(id="REF-AESGCM-AAD88",status="PASS"))
    return results


if __name__=="__main__":
    ap=argparse.ArgumentParser();ap.add_argument("--generate");ap.add_argument("--fixtures");ap.add_argument("--results")
    a=ap.parse_args()
    if a.generate:
        pathlib.Path(a.generate).write_text(json.dumps(make_fixtures(),indent=2)+"\n",encoding="utf-8")
    else:
        result=check_reference(json.loads(pathlib.Path(a.fixtures).read_text(encoding="utf-8")))
        if a.results:pathlib.Path(a.results).write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
        print(json.dumps(dict(suite="reference",counts={s:sum(x["status"]==s for x in result) for s in ("PASS","FAIL")},candidate_executed=False)))
        raise SystemExit(any(x["status"]=="FAIL" for x in result))
