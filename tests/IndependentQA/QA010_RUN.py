"""Independent QA candidate execution. Never edits candidate code, builds, or QA history."""
import argparse
import base64
import collections
import datetime as dt
import hashlib
import json
import pathlib
import shutil
import struct
import subprocess
import sys
import time
import traceback
import uuid
from cryptography.exceptions import InvalidTag, InvalidSignature
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
import QA010_REFERENCE as ref


class CandidateFailure(AssertionError):
    pass


class Blocked(Exception):
    pass


def check(value, message):
    if not value:
        raise CandidateFailure(message)


def write_json(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")


def inventory(root):
    if not root.exists():
        return {"exists": False, "directories": [], "files": {}}
    files={}
    for p in sorted(root.rglob("*")):
        if not p.is_file():continue
        try:entry=dict(bytes=p.stat().st_size,sha512=ref.sha(p.read_bytes()))
        except PermissionError as e:entry=dict(read_error="exclusive lock/access denied",errno=e.errno,sha512=None)
        files[p.relative_to(root).as_posix()]=entry
    return dict(exists=True,directories=sorted(p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_dir()),files=files)


def block_path(root, raw, archive=False):
    b=ref.verify(raw)
    try:
        date=dt.datetime(1970,1,1,tzinfo=dt.timezone.utc)+dt.timedelta(milliseconds=b["time"])
        directory=root/"BLK"/date.strftime("%Y")/date.strftime("%Y%m")/date.strftime("%Y%m%d")
        stamp=date.strftime("%Y%m%d-%H%M%S")+f"{date.microsecond//1000:03d}0"
    except (OverflowError,ValueError):
        directory=root/"BLK"/"OUTOFRANGE"
        stamp="T"+format((b["time"] & (2**64-1)) ^ (1<<63),"016x")
    return directory/(stamp+"-"+str(uuid.UUID(bytes=b["id"]))+"-"+(b["signer"].hex() if archive else b["hash"])+(".XBLK" if archive else ".BLK"))


class Suite:
    def __init__(self, args):
        self.a=args; self.root=pathlib.Path(args.evidence); self.commands=self.root/"commands"; self.commands.mkdir()
        self.instances=self.root/"instances"; self.instances.mkdir();self.inputs=self.root/"inputs";self.inputs.mkdir()
        self.outputs=self.root/"outputs";self.outputs.mkdir();self.events=[];self.command_no=0;self.active="PRECHECK"
        self.selected=getattr(args,"case_ids",None)
        self.fixture=json.loads(pathlib.Path(args.fixtures).read_text(encoding="utf-8"))
        self.vectors={x["name"]:x for x in self.fixture["vectors"]}
        self.raw={k:base64.b64decode(v["raw_b64"],validate=True) for k,v in self.vectors.items()}
        self.paths={}
        for name,raw in self.raw.items():
            p=self.inputs/(name+".BLK");p.write_bytes(raw);self.paths[name]=p

    def case(self, ident, requirements, function, *, fault=False):
        if self.selected is not None and ident not in self.selected:return
        self.active=ident; start=dt.datetime.now(dt.timezone.utc).isoformat(); first=self.command_no+1
        status="PASS";detail="Assertions satisfied";category="candidate"
        try:
            function()
        except Blocked as e:
            status="BLOCKED";detail=str(e);category="missing_interface_or_prerequisite"
        except CandidateFailure as e:
            status="FAIL";detail=str(e);category="candidate_or_specification"
            (self.root/(ident+".failure.txt")).write_text(traceback.format_exc(),encoding="utf-8")
        except Exception as e:
            status="FAIL";detail=f"{type(e).__name__}: {e}";category="harness_or_environment"
            (self.root/(ident+".failure.txt")).write_text(traceback.format_exc(),encoding="utf-8")
        event=dict(id=ident,requirements=requirements,status=status,detail=detail,category=category,
                   utc_start=start,utc_finish=dt.datetime.now(dt.timezone.utc).isoformat(),commands=list(range(first,self.command_no+1)),
                   execution_context=self.a.context,filesystem_acceptance="operator-reported native Windows" if fault and self.a.context=="OrdinaryWindowsPowerShell" else "not native acceptance",fault_case=fault)
        self.events.append(event)
        with (self.root/"cases.jsonl").open("a",encoding="utf-8") as f:f.write(json.dumps(event,ensure_ascii=False)+"\n")
        print(status,ident,detail,flush=True)

    def prerequisite(self, ident):
        if not any(x["id"]==ident and x["status"]=="PASS" for x in self.events):
            raise Blocked("Prerequisite did not pass: "+ident)

    def process(self, args, timeout=180):
        self.command_no+=1; prefix=self.commands/f"{self.command_no:04d}"
        started=dt.datetime.now(dt.timezone.utc).isoformat()
        p=subprocess.run([str(x) for x in args],cwd=self.root,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=timeout)
        prefix.with_suffix(".stdout.txt").write_bytes(p.stdout);prefix.with_suffix(".stderr.txt").write_bytes(p.stderr)
        write_json(prefix.with_suffix(".process.json"),dict(case=self.active,arguments=[str(x) for x in args],utc_start=started,
                   utc_finish=dt.datetime.now(dt.timezone.utc).isoformat(),exit_code=p.returncode,stdout_sha256=hashlib.sha256(p.stdout).hexdigest(),stderr_sha256=hashlib.sha256(p.stderr).hexdigest()))
        return p

    def cli(self, root, *args, exit=0, code=None):
        p=self.process([self.a.dotnet,self.a.candidate,"--instance-root",root,*args])
        check(p.returncode==exit,f"Expected exit {exit}, observed {p.returncode}; argv={args}; stdout={p.stdout.decode('utf-8',errors='replace')}")
        check(not p.stdout.startswith(b"\xef\xbb\xbf") and p.stdout.endswith(b"\n") and p.stdout.count(b"\n")==1,"CLI requires one UTF-8 JSON object plus final LF")
        obj=json.loads(p.stdout.decode("utf-8",errors="strict"));check(set(obj)=={"ok","data","error"},"Exact JSON envelope fields")
        if exit==0:
            check(obj["ok"] is True and isinstance(obj["data"],dict) and obj["error"] is None,"Success envelope")
            expected=args[0] if args[0]=="status" else ".".join(args[:2])
            check(obj["data"]["operation"]==expected,"Qualified operation field")
            return obj["data"]
        check(obj["ok"] is False and obj["data"] is None and isinstance(obj["error"],dict),"Failure envelope")
        check(set(obj["error"])=={"code","message","retryable","details"} and type(obj["error"]["retryable"]) is bool,"Error schema")
        check(bool(p.stderr),"Failure diagnostic must be present on stderr")
        if code is not None:check(obj["error"]["code"]==code,f"Expected {code}, got {obj['error']['code']}")
        return obj["error"]

    def fresh(self, name, label=None):
        root=self.instances/name
        if label:
            root.mkdir();key,manifest=ref.identity_files(label)
            write_json(root/"signing-key.v1.json",key);write_json(root/"instance.identity.json",manifest)
        else:self.cli(root,"instance","init")
        return root

    def base(self, name, label=None, genesis=None):
        root=self.fresh(name,label);raw=genesis or self.raw["genesis"];b=ref.verify(raw)
        path=self.inputs/(name+"-genesis.BLK");path.write_bytes(raw)
        self.cli(root,"ledger","join","--ledger-id",b["ledger"].hex(),"--genesis-sha512",b["hash"],"--genesis-file",path)
        check(block_path(root,raw).read_bytes()==raw,"Join preserves exact independently signed genesis")
        return root

    def import_raw(self, root, raw, **expect):
        path=self.inputs/(self.active+"-"+uuid.uuid4().hex+".BLK");path.write_bytes(raw)
        return self.cli(root,"block","import","--input",path,**expect)

    def import_named(self, root, name, **expect):
        return self.cli(root,"block","import","--input",self.paths[name],**expect)

    def reject_unchanged(self, root, command, exit, code):
        before=inventory(root);write_json(self.root/(self.active+".before.json"),before)
        try:result=self.cli(root,*command,exit=exit,code=code)
        finally:
            after=inventory(root);write_json(self.root/(self.active+".after.json"),after)
            check(before==after,"Rejected input changed instance file/directory inventory or hashes (including temp files)")
        return result

    def counts(self, root, retained, pending=0, quarantined=0):
        status=self.cli(root,"status");b=status["blocks"]
        check(b==dict(retained_count=retained,pending_count=pending,quarantined_count=quarantined,disputed_count=0,opaque_count=0),f"Unexpected inclusive/disjoint counts: {b}")
        check(status["projection"]==dict(state="not_installed",applied_count=None),"Step 1 must not claim projection")
        active=sum(1 for p in (root/"BLK").rglob("*.BLK") if p.is_file())
        check(retained==active+pending+quarantined,"retained = active + pending + quarantined")
        return status

    def driver(self, request, kill=True):
        if not self.a.driver or not pathlib.Path(self.a.driver).is_file():raise Blocked("QA adapter build/runtime unavailable")
        marker=self.outputs/(self.active+"-"+uuid.uuid4().hex+".ready.json")
        request={**request,"marker":str(marker)};path=self.inputs/(marker.stem+".request.json");write_json(path,request)
        write_json(self.root/(self.active+".before-driver.json"),inventory(pathlib.Path(request["root"])))
        self.command_no+=1;prefix=self.commands/f"{self.command_no:04d}"
        out=prefix.with_suffix(".stdout.txt").open("wb");err=prefix.with_suffix(".stderr.txt").open("wb")
        p=subprocess.Popen([self.a.dotnet,self.a.driver,str(path)],cwd=self.root,stdout=out,stderr=err)
        deadline=time.monotonic()+90
        while not marker.exists() and p.poll() is None and time.monotonic()<deadline:time.sleep(.02)
        if not marker.exists():
            if p.poll() is None:p.kill()
            p.wait();out.close();err.close()
            write_json(prefix.with_suffix(".process.json"),dict(case=self.active,pid=p.pid,exit_code=p.returncode,request=request,barrier_reached=False))
            raise Blocked("Requested existing checkpoint was not reached; inspect "+str(prefix)+".stderr.txt")
        receipt=json.loads(marker.read_text(encoding="utf-8"));check(receipt["pid"]==p.pid,"Barrier PID handshake mismatch")
        write_json(self.root/(self.active+".at-barrier.json"),inventory(pathlib.Path(request["root"])))
        if not kill:return p,out,err,prefix,request,marker
        p.kill();p.wait(timeout=30);out.close();err.close()
        check(p.returncode!=0,"Terminated worker must not report normal success")
        write_json(prefix.with_suffix(".process.json"),dict(case=self.active,pid=p.pid,exit_code=p.returncode,request=request,barrier_reached=True,externally_terminated=True))
        write_json(self.root/(self.active+".after-kill.json"),inventory(pathlib.Path(request["root"])))
        return receipt

    def release_driver(self, held, terminate=False):
        p,out,err,prefix,request,marker=held
        if terminate:p.kill()
        else:pathlib.Path(str(marker)+".release").write_text("release",encoding="ascii")
        p.wait(timeout=30);out.close();err.close()
        write_json(prefix.with_suffix(".process.json"),dict(case=self.active,pid=p.pid,exit_code=p.returncode,request=request,barrier_reached=True,externally_terminated=terminate))
        write_json(self.root/(self.active+".after-driver.json"),inventory(pathlib.Path(request["root"])))

    def functional(self):
        def lifecycle():
            root=self.fresh("new-ledger");key0=(root/"signing-key.v1.json").read_bytes();m0=json.loads((root/"instance.identity.json").read_bytes())
            check(m0["version"]==2 and m0["ledger_binding"] is None,"New manifest v2 unbound")
            key=json.loads(key0);aad=ref.aad(key["instance_guid"],bytes.fromhex(key["public_key_hex"]));check(len(aad)==88,"88-byte independent AAD")
            seed=AESGCM(ref.WRAP).decrypt(base64.b64decode(key["nonce_b64"]),base64.b64decode(key["ciphertext_b64"])+base64.b64decode(key["tag_b64"]),aad)
            check(ref.pub(seed).hex()==m0["public_key_hex"],"Independent AES/Ed25519 derivation matches saved identity")
            start=int(time.time()*1000);g=self.cli(root,"ledger","init");finish=int(time.time()*1000)
            raws=[p.read_bytes() for p in root.rglob("*.BLK")];check(len(raws)==1,"Exactly one genesis")
            b=ref.verify(raws[0]);check(len(raws[0])==227 and b["kind"] is None,"Independent genesis validation")
            check(b["signer"].hex()==m0["public_key_hex"]==g["ledger_id_hex"],"Genesis uses original local identity")
            check(start<=b["time"]<=finish and g["genesis_sha512"]==ref.sha(raws[0]),"Internally current UTC and exact whole genesis SHA-512")
            self.reject_unchanged(root,["instance","init"],4,"RECOVERY_REQUIRED")
            self.reject_unchanged(root,["ledger","init"],4,"ALREADY_BOUND")
            check((root/"signing-key.v1.json").read_bytes()==key0,"No replacement identity")
            self.counts(root,1)
        self.case("LIFE-01",["initialization","key-AAD88","manifest-v2","current-UTC"],lifecycle)
        def joined():
            root=self.fresh("replica");original=inventory(root);m0=json.loads((root/"instance.identity.json").read_bytes())
            g=self.vectors["genesis"]
            check(m0["public_key_hex"]!=ref.LEDGER.hex(),"Replica must have an independent key")
            self.cli(root,"ledger","join","--ledger-id",ref.LEDGER.hex(),"--genesis-sha512",g["sha512"],"--genesis-file",self.paths["genesis"])
            status=self.cli(root,"instance","status");check(status["state"]=="bound" and status["public_key_hex"]==m0["public_key_hex"],"Restart preserves replica identity")
            check(inventory(root)["files"]["signing-key.v1.json"]==original["files"]["signing-key.v1.json"],"Join must not rewrite replica key")
            before=inventory(root);self.cli(root,"ledger","recover-genesis","--ledger-id",ref.LEDGER.hex(),"--genesis-sha512",g["sha512"])
            check(before==inventory(root),"Already-bound recovery is byte-idempotent")
        self.case("JOIN-01",["join","replica-identity","recovery-idempotence"],joined)
        def substitution():
            root=self.base("genesis-substitution");replacement=ref.sign_block(None,"another-genesis-same-key",timestamp=ref.T+1)
            path=self.store_input("another-genesis",replacement)
            self.reject_unchanged(root,["ledger","join","--ledger-id",ref.LEDGER.hex(),"--genesis-sha512",ref.sha(replacement),"--genesis-file",path],4,"GENESIS_CONFLICT")
            self.reject_unchanged(root,["block","import","--input",path],4,"GENESIS_CONFLICT")
            check(block_path(root,self.raw["genesis"]).read_bytes()==self.raw["genesis"],"Same-key substitute cannot alter original pinned genesis")
        self.case("JOIN-SUBSTITUTION",["genesis-trust-pin","same-key-genesis-substitution"],substitution)
        for variant in ("wrong-id","wrong-pin","bad-signature","truncated","ddlb"):
            def negative_join(variant=variant):
                root=self.fresh("join-"+variant);raw=self.raw["genesis"];expected="GENESIS_CONFLICT"
                ledger=ref.LEDGER.hex();pin=ref.sha(raw)
                if variant=="wrong-id":ledger="1"*64;expected="LEDGER_IDENTITY_MISMATCH"
                if variant=="wrong-pin":pin="0"*128
                if variant=="bad-signature":raw=raw[:-1]+bytes([raw[-1]^1]);expected="BAD_SIGNATURE"
                if variant=="truncated":raw=raw[:100];expected="BAD_FRAME"
                if variant=="ddlb":raw=ref.ddlb([raw]);expected="BAD_FRAME"
                path=self.inputs/(variant+"-join.bin");path.write_bytes(raw)
                self.reject_unchanged(root,["ledger","join","--ledger-id",ledger,"--genesis-sha512",pin,"--genesis-file",path],2 if expected in ("BAD_SIGNATURE","BAD_FRAME") else 4,expected)
            self.case("JOIN-NEG-"+variant,["join-in-memory-validation","no-write-on-reject"],negative_join)
        if self.selected is None or any(x.startswith(("WIRE-","LIMIT-","LZMA2-")) for x in self.selected):
            self.good=self.base("wire-positives")
        excluded={"genesis","grant_B","grant_C_by_B","B_data","C_data","regrant_B","revoke_B","revoke_C","unknown_D","grant_D","out_min","out_max"}
        positives=[name for name,v in self.vectors.items() if v["expected"]=="OK" and name not in excluded and not name.startswith("rev_delta_")]
        for name in positives:
            def positive(name=name):
                result=self.import_named(self.good,name);item=result["results"][0]
                check(item["status"]=="retained" and item["durable"] is True,"Independent positive fixture must execute successfully on candidate")
                check(block_path(self.good,self.raw[name]).read_bytes()==self.raw[name],"Candidate retains exact signed input")
            self.case("WIRE-POS-"+name,["wire-103","values-schema","LZMA2","actual-candidate-fixtures"],positive)
        for name,v in self.vectors.items():
            if v["expected"]=="OK":continue
            def negative(name=name,v=v):
                self.reject_unchanged(self.good,["block","import","--input",self.paths[name]],v.get("exit",2),v["expected"])
            self.case("WIRE-NEG-"+name,["wire-103","negative-fixtures","no-write-on-reject"],negative)
        def compression_limit():
            root=self.base("decoded-limit")
            self.cli(root,"config","set","--name","max_decompressed_event_bytes","--value","100")
            self.reject_unchanged(root,["block","import","--input",self.paths["compressed_text"]],5,"RESOURCE_LIMIT")
        self.case("LIMIT-DECODED",["LZMA2","resource-limits","no-write-on-reject"],compression_limit)
        def encoder_interop():
            if not self.a.driver:raise Blocked("QA adapter required for public compression API")
            root=self.base("candidate-compression",label="A");body=ref.verify(self.raw["compressed_text"])["body"]
            request=dict(root=str(root),operation="publish",input=str(self.store_input("compress-body",body)),compress=True)
            p=self.process([self.a.dotnet,self.a.driver,self.store_input("compress-request",json.dumps(request).encode())])
            check(p.returncode==0,"Candidate public compression API must complete")
            blocks=[p.read_bytes() for p in root.rglob("*.BLK") if ref.verify(p.read_bytes())["kind"] is not None]
            check(len(blocks)==1,"One candidate-authored compressed block")
            b=ref.verify(blocks[0]);check(b["compression"]==1 and b["body"]==body and len(blocks[0])<len(body),"Python independently decodes candidate raw LZMA2 with exact event bytes")
        self.case("LZMA2-OUTBOUND",["LZMA2","independent-encoder-interoperability"],encoder_interop)
        def canonical():
            root=self.base("canonical-guid")
            self.import_named(root,"grant_B")
            grant=ref.sign_block(b"\x20"+ref.PUBS["C"],"same-time-grant",signer="B",timestamp=ref.T+200)
            event=ref.sign_block(b"\x11"+ref.TABLE,"same-time-event",signer="C",timestamp=ref.T+200)
            # Select distinct deterministic names until GUID order is grant < event; this tests
            # canonical ordering, not accidental collision or competing-GUID arbitration.
            index=0
            while ref.verify(grant)["id"]>=ref.verify(event)["id"]:
                index+=1;event=ref.sign_block(b"\x11"+ref.TABLE,"same-time-event"+str(index),signer="C",timestamp=ref.T+200)
            check(ref.PUBS["B"]>ref.PUBS["C"],"Chosen keys oppose signer-first order; test discriminates the required GUID-before-signer rule")
            payload=ref.ddlb([event,grant]);path=self.inputs/"canonical-guid.DDLB";path.write_bytes(payload)
            self.cli(root,"block","import","--input",path);self.counts(root,4)
            states,_=ref.replay([self.raw["genesis"],self.raw["grant_B"],grant,event]);check(set(states.values())=={"retained"},"Independent canonical authority oracle")
        self.case("ORDER-01",["canonical-order","batch-inverted-order"],canonical)
        self.authorization();self.publication();self.revocations();self.batches();self.recovery_config()

    def authorization(self):
        def no_write_callbacks():
            if not self.a.driver:raise Blocked("Existing write-checkpoint observation requires QA adapter")
            root=self.base("observed-no-write")
            for name,expected,exit in (("unknown_D","AUTHORITY_UNRESOLVED",4),("outer_signature","BAD_SIGNATURE",2),("real_quiet_nan","BAD_EVENT",2)):
                before=inventory(root);request=dict(root=str(root),operation="import",input=str(self.paths[name]))
                p=self.process([self.a.dotnet,self.a.driver,self.store_input("write-gate-"+name,json.dumps(request).encode())])
                result=json.loads(p.stdout)
                check(p.returncode==exit and result["code"]==expected and result["observed_checkpoints"]==[],"Rejected import must not reach any injected DurableFiles write/rename checkpoint")
                check(inventory(root)==before,"Rejected callback-observed import leaves no files, dirs or hash changes")
            self.import_named(root,"grant_B");self.import_named(root,"revoke_B")
            request=dict(root=str(root),operation="import",input=str(self.paths["B_data"]))
            before=inventory(root);p=self.process([self.a.dotnet,self.a.driver,self.store_input("write-gate-unauthorized",json.dumps(request).encode())]);result=json.loads(p.stdout)
            check(p.returncode==4 and result["code"]=="NOT_AUTHORIZED" and result["observed_checkpoints"]==[] and inventory(root)==before,"Known-unauthorized input cannot reach a write checkpoint or alter retained history")
        self.case("AUTH-NO-WRITE-CALLBACKS",["memory-authorization","zero-write-checkpoints","no-write-on-reject"],no_write_callbacks)
        def unresolved():
            root=self.base("authority-missing");before=self.counts(root,1)
            e=self.reject_unchanged(root,["block","import","--input",self.paths["unknown_D"]],4,"AUTHORITY_UNRESOLVED")
            check(e["retryable"] is True and e["details"]["results"][0]["authorization"]=="pending","Unresolved reason/authorization/retryable")
            self.counts(root,1);self.import_named(root,"grant_D");self.import_named(root,"unknown_D");self.counts(root,3)
            check(block_path(root,self.raw["unknown_D"]).read_bytes()==self.raw["unknown_D"],"Original unchanged block succeeds after missing grant")
        self.case("AUTH-01",["memory-authorization","retryable","no-projector-dependency"],unresolved)
        def history():
            root=self.base("history")
            for name in ("grant_B","B_data"):self.import_named(root,name)
            self.counts(root,3);self.import_named(root,"revoke_B");self.counts(root,4,0,1)
            archived=block_path(root,self.raw["B_data"],True);check(archived.read_bytes()==self.raw["B_data"],"Archive preserves signed bytes")
            self.cli(root,"block","get","--id",str(uuid.UUID(bytes=ref.verify(self.raw["B_data"])["id"])),"--sha512",ref.sha(self.raw["B_data"]),"--output",self.outputs/"archived-get.BLK",exit=4,code="BLOCK_NOT_FOUND")
            out=self.outputs/"history-before-regrant.DDLB";self.cli(root,"block","export","--output",out)
            check(self.raw["B_data"] not in ref.read_ddlb(out.read_bytes()),"Ordinary export excludes XBLK")
            never=ref.sign_block(b"\x11"+ref.TABLE,"new-revoked-B",signer="B",timestamp=ref.T+450)
            e=self.reject_unchanged(root,["block","import","--input",self.store_input("known-unauthorized",never)],4,"NOT_AUTHORIZED")
            check(e["retryable"] is False,"Known unauthorized is not immediately retryable")
            self.import_named(root,"regrant_B");self.counts(root,5)
            check(not archived.exists() and block_path(root,self.raw["B_data"]).read_bytes()==self.raw["B_data"],"Canonical regrant restores unchanged BLK")
            states,_=ref.replay([self.raw[x] for x in ("genesis","grant_B","B_data","revoke_B","regrant_B")])
            check(set(states.values())=={"retained"},"Independent canonical oracle after late regrant")
        self.case("AUTH-02",["historical-authorization","XBLK","counts","known-unauthorized"],history)
        def pending_history():
            root=self.base("pending-history")
            for name in ("grant_B","grant_C_by_B","C_data","revoke_B"):self.import_named(root,name)
            states,_=ref.replay([self.raw[x] for x in ("genesis","grant_B","grant_C_by_B","C_data","revoke_B")])
            # B grant C at 150 precedes revocation 200 and therefore remains valid.
            check(set(states.values())=={"retained"},"Baseline canonical positions")
            earlier=ref.revoke("earlier-revoke-B",ref.T+125,ref.T+125)
            self.import_raw(root,earlier)
            expected,_=ref.replay([self.raw[x] for x in ("genesis","grant_B","grant_C_by_B","C_data","revoke_B")]+[earlier])
            self.counts(root,6,list(expected.values()).count("pending"),list(expected.values()).count("quarantined"))
            check(expected[ref.sha(self.raw["C_data"])]=="pending","Formerly accepted descendant becomes unresolved")
            check(block_path(root,self.raw["C_data"],True).read_bytes()==self.raw["C_data"],"Pending archive exact bytes")
        self.case("AUTH-03",["historical-authorization","pending-archive","counts"],pending_history)
        def extremes():
            genesis=ref.sign_block(None,"out-genesis",timestamp=-(1<<63));root=self.base("out-of-range",genesis=genesis)
            self.import_named(root,"out_max")
            check(block_path(root,genesis).name.startswith("T0000000000000000-"),"Minimum signed int64 sign-biased filename")
            check(block_path(root,self.raw["out_max"]).name.startswith("Tffffffffffffffff-"),"Maximum signed int64 sign-biased filename")
            check(block_path(root,genesis).read_bytes()==genesis,"OUTOFRANGE genesis exact bytes")
        self.case("PATH-OUTOFRANGE",["out-of-range","canonical-order"],extremes)

    def store_input(self, name, raw):
        path=self.inputs/(name+".bin");path.write_bytes(raw);return path

    def publication(self):
        def publish_retry():
            root=self.base("publication",label="A");body=b"\x11"+ref.TABLE;path=self.store_input("publish-body",body);ident=str(uuid.UUID(bytes=ref.uid("ordinary-retry")))
            start=int(time.time()*1000);first=self.cli(root,"block","publish","--event-file",path,"--block-guid",ident);finish=int(time.time()*1000)
            raw=pathlib.Path(first["path"]).read_bytes();b=ref.verify(raw)
            check(start<=b["time"]<=finish and b["body"]==body and b["ledger"]==ref.LEDGER,"Current UTC local authoring independently decoded")
            before=inventory(root);retry=self.cli(root,"block","publish","--event-file",path,"--block-guid",ident)
            check(first==retry and inventory(root)==before,"Identical retry must preserve original JSON and entire filesystem")
            second=self.cli(root,"block","publish","--event-file",path);check(second["block_guid"]!=ident,"Omitted GUID starts a new publication")
            check(ref.verify(pathlib.Path(second["path"]).read_bytes())["body"]==body,"New publication independently verified")
            self.counts(root,3)
        self.case("PUB-01",["current-UTC","idempotent-publication","exact-byte-preservation"],publish_retry)
        def flags():
            root=self.instances/"invalid-api-flags";body=self.paths["drop_table"]
            self.cli(root,"block","publish","--event-file",body,"--timestamp-utc-ms","0",exit=2,code="INVALID_ARGUMENT")
            self.cli(root,"master","countersign-revocation","--block-guid",str(uuid.uuid4()),exit=2,code="INVALID_ARGUMENT")
            self.cli(root,"ledger","join","--ledger-guid",str(uuid.uuid4()),exit=2,code="INVALID_ARGUMENT")
            check(not root.exists(),"Invalid public arguments cannot create instance files")
            if not self.a.driver:raise Blocked("Public API reflection requires QA adapter")
            request=self.store_input("api-surface",json.dumps(dict(operation="api-surface")).encode())
            p=self.process([self.a.dotnet,self.a.driver,request]);check(p.returncode==0,"Public API reflection process")
            methods=json.loads(p.stdout)["methods"];check({m["name"] for m in methods}=={"Publish","CreateRevocationConsent","PublishRevocationConsent"},"Expected public signing API families")
            for method in methods:
                check(not any(p["name"].lower() in ("time","timestamp","clock","utc") or "DateTime" in p["type"] or "Func" in p["type"] for p in method["parameters"]),"Public authoring API cannot override time")
        self.case("PUB-02",["no-public-time-override","CLI-contract"],flags)

    def revocations(self):
        for variant in ("same-master","third-target","ungranted-B","ungranted-A"):
            def prepare_invalid(variant=variant):
                root=self.base("prepare-negative-"+variant,label="D" if variant=="ungranted-A" else "A")
                local=ref.PUBS["D"] if variant=="ungranted-A" else ref.PUBS["A"]
                outer=local if variant=="same-master" else ref.PUBS["B"]
                target=ref.PUBS["C"] if variant=="third-target" else local
                if variant in ("same-master","third-target"):self.import_named(root,"grant_B")
                out=self.outputs/("prepare-negative-"+variant+".bin")
                self.reject_unchanged(root,["master","countersign-revocation","--outer-signer-public-key",outer.hex(),"--revoked-public-key",target.hex(),"--output",out],
                                      2 if variant in ("same-master","third-target") else 4,"BAD_EVENT" if variant in ("same-master","third-target") else "NOT_AUTHORIZED")
                check(not out.exists(),"Rejected consent preparation cannot emit a signed package")
            self.case("REV-A-NEG-"+variant,["A-consent","distinct-authorized-masters","role-authority-validation"],prepare_invalid)
        for delta in (0,900000,900001,-1):
            def boundary(delta=delta):
                root=self.base("consent-boundary-"+str(delta));self.import_named(root,"grant_B");name="rev_delta_"+str(delta)
                if delta in (0,900000):self.import_named(root,name);self.counts(root,3)
                else:self.reject_unchanged(root,["block","import","--input",self.paths[name]],2,"CONSENT_EXPIRED");self.counts(root,2)
            self.case("REV-DELTA-"+str(delta),["revocation-v3","signed-time-boundaries","actual-candidate-fixtures"],boundary)
        def consent_live():
            root=self.base("consent-A",label="A");self.import_named(root,"grant_B")
            bRoot=self.base("consent-B",label="B");self.import_named(bRoot,"grant_B")
            out=self.outputs/"A-live-consent.bin";start=int(time.time()*1000)
            result=self.cli(root,"master","countersign-revocation","--outer-signer-public-key",ref.PUBS["B"].hex(),"--revoked-public-key",ref.PUBS["B"].hex(),"--output",out)
            finish=int(time.time()*1000);package=out.read_bytes();check(len(package)==229 and package[:13]==ref.DOMAIN,"Exact signed consent package grammar")
            check(package[13:45]==ref.LEDGER and package[69:101]==ref.PUBS["B"] and package[101:133]==ref.PUBS["B"] and package[133:165]==ref.PUBS["A"],"Independent consent offsets/roles")
            when=struct.unpack(">q",package[61:69])[0];check(start<=when<=finish,"A generates current UTC")
            Ed25519PublicKey.from_public_bytes(ref.PUBS["A"]).verify(package[-64:],package[:165])
            check(result["bytes"]==229 and result["consent_sha512"]==ref.sha(package[:165]) and result["signature_hex"]==package[-64:].hex(),"Consent response agrees with independent crypto")
            start=int(time.time()*1000);published=self.cli(bRoot,"block","publish","--revocation-consent-file",out);finish=int(time.time()*1000)
            block=ref.verify(pathlib.Path(published["path"]).read_bytes())
            check(len(block["body"])==137 and block["id"]==package[45:61] and start<=block["time"]<=finish,"B generates own current UTC and preserves A GUID")
            check(published["consent_age_ms"]==block["time"]-when and int(published["consent_timestamp_utc_ms"])==when,"Both signed times and delta reported")
            before=inventory(bRoot);again=self.cli(bRoot,"block","publish","--revocation-consent-file",out)
            check(again==published and inventory(bRoot)==before,"B retry after self-revocation returns original historical publication")
        self.case("REV-LIVE",["A-consent","B-publication","current-UTC","idempotent-revocation"],consent_live)
        def expired_retry():
            root=self.base("expired-retry",label="B");self.import_named(root,"grant_B")
            name="expired-durable-revocation";package=ref.consent(name,ref.T+1000,outer="B",target="B",counter="A")
            raw=ref.revoke(name,ref.T+1000,ref.T+1001,outer="B",counter="A",target="B")
            self.import_raw(root,raw);before=inventory(root)
            result=self.cli(root,"block","publish","--revocation-consent-file",self.store_input(name,package))
            check(result["sha512"]==ref.sha(raw) and result["consent_age_ms"]==1 and pathlib.Path(result["path"]).read_bytes()==raw,"Expired receipt-clock retry returns exact already durable block")
            check(inventory(root)==before,"No new files or re-signing on expired durable retry")
        self.case("REV-EXPIRED-RETRY",["idempotent-revocation","signed-time-not-receipt-time"],expired_retry)
        for variant in ("tampered","short","v2","wrong-ledger","wrong-B","expired","unauthorized-A","revoked-A"):
            def invalid_consent(variant=variant):
                root=self.base("invalid-consent-"+variant,label="B");self.import_named(root,"grant_B")
                now=int(time.time()*1000);outer="B";counter="A";ledger=ref.LEDGER;expected="BAD_EVENT";exit=2
                if variant=="wrong-ledger":ledger=ref.PUBS["C"];expected="LEDGER_IDENTITY_MISMATCH";exit=4
                if variant=="wrong-B":outer="C"
                if variant=="expired":now=ref.T;expected="CONSENT_EXPIRED"
                if variant=="unauthorized-A":counter="C";expected="NOT_AUTHORIZED";exit=4
                if variant=="revoked-A":
                    revoke_a=ref.revoke("revoke-A-for-negative",ref.T+200,ref.T+200,outer="B",counter="A",target="A")
                    self.import_raw(root,revoke_a);expected="NOT_AUTHORIZED";exit=4
                package=ref.consent("invalid-consent-"+variant,now,outer=outer,target="B" if outer=="B" else "C",counter=counter,ledger=ledger,domain=b"DDL-REVOKE-v2" if variant=="v2" else ref.DOMAIN)
                if variant=="short":package=package[:-1]
                if variant=="tampered":package=package[:-1]+bytes([package[-1]^1]);expected="BAD_SIGNATURE"
                self.reject_unchanged(root,["block","publish","--revocation-consent-file",self.store_input(variant+"-consent",package)],exit,expected)
            self.case("REV-NEG-"+variant,["revocation-v3","role-authority-validation","no-write-on-reject"],invalid_consent)

    def batches(self):
        for inverted in (False,True):
            def batch(inverted=inverted):
                root=self.base("batch-"+str(inverted));names=["grant_B","B_data"]
                if inverted:names.reverse()
                raws=[self.raw[x] for x in names];path=self.store_input("batch-"+str(inverted),ref.ddlb(raws));result=self.cli(root,"block","import","--input",path)
                check(result["durable_count"]==2 and result["total_count"]==2,"Two independently durable batch items")
                for i,raw in enumerate(raws):
                    item=result["results"][i];check(item["index"]==i and item["sha512"]==ref.sha(raw) and item["durable"] is True and item["retryable"] is False,"Results retain transport index and final durable state")
                    check(block_path(root,raw).read_bytes()==raw,"Batch byte preservation")
                self.counts(root,3)
            self.case("BATCH-ORDER-"+str(inverted),["DDLB","canonical-order","grant-dependency"],batch)
        def failed_grant():
            root=self.base("batch-failed-grant");target=block_path(root,self.raw["grant_B"]);target.mkdir(parents=True)
            before=inventory(root);path=self.store_input("blocked-grant",ref.ddlb([self.raw["B_data"],self.raw["grant_B"]]))
            result=self.cli(root,"block","import","--input",path,exit=4,code="AUTHORITY_UNRESOLVED")
            items=result["details"]["results"];check(not any(x["durable"] for x in items) and items[0]["retryable"] is True,"Failed grant cannot authorize durable dependent")
            check(before==inventory(root),"Failed prerequisite must not leave a dependent or temp incoming file")
        self.case("BATCH-FAILED-GRANT",["grant-durability","no-dependent-without-prerequisite"],failed_grant)
        def mixed():
            root=self.base("batch-mixed");self.import_named(root,"grant_B");self.import_named(root,"revoke_B")
            unauthorized=ref.sign_block(b"\x11"+ref.TABLE,"mixed-unauthorized",signer="B",timestamp=ref.T+450)
            raws=[self.raw["drop_table"],self.raw["grant_B"],unauthorized,self.raw["unknown_D"],self.raw["quiet_nan"] if "quiet_nan" in self.raw else self.raw["real_quiet_nan"]]
            result=self.cli(root,"block","import","--input",self.store_input("mixed",ref.ddlb(raws)),exit=3,code="PARTIAL_IMPORT")
            details=result["details"];check((details["durable_count"],details["duplicate_count"],details["rejected_count"],details["total_count"])==(1,1,3,5),"Truthful mixed partial counts")
            items=details["results"];check([x["sha512"] for x in items[:4]]==[ref.sha(x) for x in raws[:4]],"Original input ordering")
            check(items[2]["reason"]=="NOT_AUTHORIZED" and items[2]["retryable"] is False,"Known unauthorized partial item")
            check(items[3]["reason"]=="AUTHORITY_UNRESOLVED" and items[3]["retryable"] is True,"Missing-authority partial item")
            for raw in raws[2:4]:check(not block_path(root,raw).exists() and not block_path(root,raw,True).exists(),"Rejected batch item not persisted")
            self.counts(root,4)
        self.case("BATCH-MIXED",["PARTIAL_IMPORT","ordered-results","retryable","DDLB"],mixed)
        for shape in ("truncated","trailing","overflow","zero"):
            def framing(shape=shape):
                root=self.base("framing-"+shape);raw=ref.ddlb([self.raw["grant_B"],self.raw["B_data"]])
                if shape=="truncated":raw=raw[:-10]
                if shape=="trailing":raw+=b"\xff"
                if shape=="overflow":raw=b"DDLB\x00\x01"+struct.pack(">Q",1)+struct.pack(">Q",2**64-1)
                if shape=="zero":raw=ref.ddlb([])
                expected=0 if shape=="zero" else 2 if shape=="overflow" else 3
                result=self.cli(root,"block","import","--input",self.store_input("framing-"+shape,raw),exit=expected,code=None if expected==0 else "BAD_FRAME" if expected==2 else "PARTIAL_IMPORT")
                if shape=="truncated":check(result["details"]["durable_count"]==1,"Complete grant before truncated dependent remains durable")
            self.case("DDLB-"+shape,["DDLB-framing","partial-durability"],framing)
        def batch_limit():
            root=self.base("batch-memory-limit");self.cli(root,"config","set","--name","max_inflight_import_bytes","--value","4096")
            raw=ref.ddlb([self.raw["drop_table"],self.raw["rename_table"],self.raw["alter_column"]])
            before=inventory(root);result=self.cli(root,"block","import","--input",self.store_input("memory-limited",raw),exit=5,code="RESOURCE_LIMIT")
            check(inventory(root)==before,"Resource-rejected batch must not spill input to disk")
        self.case("BATCH-MEMORY",["bounded-memory","no-temporary-spillover"],batch_limit)

    def recovery_config(self):
        def exact_exports():
            root=self.base("exact-exports");self.import_named(root,"grant_B")
            g=ref.verify(self.raw["genesis"]);out=self.outputs/"exact-genesis.BLK"
            self.cli(root,"block","get","--id",str(uuid.UUID(bytes=g["id"])),"--output",out)
            check(out.read_bytes()==self.raw["genesis"],"get is exact raw bytes")
            self.reject_unchanged(root,["block","get","--id",str(uuid.UUID(bytes=g["id"])),"--output",out],4,"OUTPUT_EXISTS")
            check(out.read_bytes()==self.raw["genesis"],"No overwrite of existing output")
            for name,args,expected in (("all",[],[self.raw["genesis"],self.raw["grant_B"]]),("one",["--id",str(uuid.UUID(bytes=g["id"]))],[self.raw["genesis"]]),("none",["--from-timestamp-ms",str((1<<63)-1)],[])):
                dest=self.outputs/("export-"+name+".DDLB");self.cli(root,"block","export","--output",dest,*args)
                check(ref.read_ddlb(dest.read_bytes())==expected,"Always DDLB, canonical order, unchanged signed bytes, including zero/one")
        self.case("EXPORT-01",["DDLB","exact-get-export","no-overwrite"],exact_exports)
        def unbound_selection():
            root=self.fresh("multi-ledger");other=ref.sign_block(None,"genesis-C",signer="C",ledger=ref.PUBS["C"],timestamp=ref.T)
            self.import_named(root,"genesis");self.import_raw(root,other)
            self.cli(root,"block","export","--output",self.outputs/"ambiguous.DDLB",exit=4,code="AMBIGUOUS_LEDGER")
            out=self.outputs/"selected.DDLB";self.cli(root,"block","export","--ledger-id",ref.LEDGER.hex(),"--output",out)
            check(ref.read_ddlb(out.read_bytes())==[self.raw["genesis"]],"Explicit ledger selection avoids mixing history")
        self.case("EXPORT-02",["unbound-multi-ledger","explicit-selection"],unbound_selection)
        for corrupt in (False,True):
            def missing_key(corrupt=corrupt):
                root=self.base("missing-key-"+str(corrupt));backup=self.outputs/("identity-backup-"+str(corrupt))
                self.cli(root,"key","backup","--directory",backup);original=json.loads((root/"instance.identity.json").read_bytes())
                if corrupt:(root/"signing-key.v1.json").write_text("{}",encoding="utf-8")
                else:(root/"signing-key.v1.json").unlink()
                status=self.cli(root,"status");check(status["instance"]["state"]=="recovery_required" and status["blocks"] is None and status["signing"]["available"] is False,"Missing/corrupt original identity blocks ordinary processing")
                body=self.store_input("forbidden-body",b"\x11"+ref.TABLE)
                commands=[["ledger","init"],["block","import","--input",self.paths["grant_B"]],["block","export","--output",self.outputs/("forbidden-"+str(corrupt))],
                          ["block","get","--id",str(uuid.UUID(bytes=ref.uid("genesis"))),"--output",self.outputs/"forbidden-get"],
                          ["block","publish","--event-file",body],["config","get"],["key","backup","--directory",self.outputs/"forbidden-backup"],
                          ["master","countersign-revocation","--outer-signer-public-key",ref.PUBS["B"].hex(),"--revoked-public-key",ref.PUBS["B"].hex(),"--output",self.outputs/"forbidden-consent"]]
                before=inventory(root)
                for command in commands:self.cli(root,*command,exit=4,code="RECOVERY_REQUIRED")
                check(inventory(root)==before,"Identity gate cannot process or mutate retained history")
                self.cli(root,"key","restore","--directory",backup);check(json.loads((root/"instance.identity.json").read_bytes())==original,"Original backup restores same identity and trust")
                self.counts(root,1)
            self.case("IDENTITY-"+("corrupt" if corrupt else "missing"),["FI-18","identity-gate","original-backup-restore"],missing_key,fault=True)
        def key_only():
            root=self.instances/"key-only";root.mkdir();key,manifest=ref.identity_files("D");write_json(root/"signing-key.v1.json",key)
            before=(root/"signing-key.v1.json").read_bytes()
            self.cli(root,"instance","recover","--from",root/"signing-key.v1.json",exit=2,code="INVALID_ARGUMENT")
            for _ in range(2):self.cli(root,"instance","recover","--from",root/"signing-key.v1.json","--confirm-unbound-initialization")
            check(json.loads((root/"instance.identity.json").read_bytes())==manifest and (root/"signing-key.v1.json").read_bytes()==before,"Key-only recovery uses exact original identity and manifest v2")
        self.case("IDENTITY-KEY-ONLY",["key-only-recovery","manifest-v2"],key_only)
        def config():
            root=self.base("configuration");original=inventory(root);defaults=self.cli(root,"config","get")["limits"]
            check(defaults==dict(max_raw_block_bytes=16777216,max_decompressed_event_bytes=67108864,max_inflight_import_bytes=134217728,per_block_timeout_seconds=60),"Approved config defaults")
            self.cli(root,"config","set","--name","per_block_timeout_seconds","--value","31")
            check(self.cli(root,"config","get")["limits"]["per_block_timeout_seconds"]==31,"Config persists across CLI processes")
            path=root/"instance.config.json";saved=path.read_bytes();obj=json.loads(saved)
            check(obj["version"]==1 and set(obj)=={"format","version","instance_guid","limits"},"Config v1 exact fields")
            for kind,content,code in (("malformed",b"{ invalid","CONFIG_CORRUPT"),("duplicate",saved.replace(b'"version":1',b'"version":1,"version":1'),"CONFIG_CORRUPT"),
                                      ("guid",json.dumps({**obj,"instance_guid":str(uuid.uuid4())}).encode(),"CONFIG_IDENTITY_MISMATCH")):
                path.write_bytes(content);state=self.cli(root,"status")
                check(state["instance"]["state"]=="bound" and code in state["instance"]["recovery_codes"],"Config diagnostic must preserve healthy identity")
                self.cli(root,"config","get",exit=4,code=code);self.cli(root,"ledger","init",exit=4,code=code)
                path.write_bytes(saved)
            check(inventory(root)["files"]["instance.identity.json"]==original["files"]["instance.identity.json"],"Config must not alter identity/binding")
            check(inventory(root)["files"]["signing-key.v1.json"]==original["files"]["signing-key.v1.json"],"Config must not alter encrypted key")
        self.case("CONFIG-01",["FI-19","config-v1","persistent-limits","corruption-gate"],config,fault=True)
        def manifest_strict():
            for label,edit in (("version",lambda x:{**x,"version":1}),("unknown",lambda x:{**x,"extra":1}),("key-path",lambda x:{**x,"key_file":"../signing-key.v1.json"})):
                root=self.fresh("manifest-"+label);path=root/"instance.identity.json";value=edit(json.loads(path.read_bytes()));write_json(path,value)
                status=self.cli(root,"instance","status");check(status["state"]=="recovery_required" and status["signing_available"] is False,"Strict manifest v2 recovery gate")
                self.cli(root,"config","get",exit=4,code="RECOVERY_REQUIRED")
        self.case("IDENTITY-MANIFEST-STRICT",["manifest-v2","legacy-rejection","identity-gate"],manifest_strict)
        def aad_tampering():
            for field in ("instance_guid","public_key_hex","nonce_b64","tag_b64"):
                root=self.fresh("aad-negative-"+field,label="D");path=root/"signing-key.v1.json";key=json.loads(path.read_bytes())
                if field=="instance_guid":key[field]=str(uuid.uuid4())
                elif field=="public_key_hex":key[field]=ref.PUBS["C"].hex()
                else:
                    raw=base64.b64decode(key[field]);key[field]=base64.b64encode(bytes([raw[0]^1])+raw[1:]).decode()
                write_json(path,key);before=inventory(root)
                status=self.cli(root,"instance","status");check(status["state"]=="recovery_required" and status["signing_available"] is False,"Tampered key AAD or GCM authentication fails closed")
                self.cli(root,"config","get",exit=4,code="RECOVERY_REQUIRED");check(before==inventory(root),"GCM failure must not replace or regenerate key")
        self.case("IDENTITY-AAD-TAMPER",["key-AAD88","AES-GCM-authentication","identity-gate"],aad_tampering)
        def bad_limits():
            root=self.base("invalid-limits")
            for name,value in (("max_raw_block_bytes","226"),("max_inflight_import_bytes","226"),("per_block_timeout_seconds","0"),
                               ("max_decompressed_event_bytes","-1"),("max_raw_block_bytes","+227"),("max_raw_block_bytes","9223372036854775808")):
                self.reject_unchanged(root,["config","set","--name",name,"--value",value],2,"INVALID_ARGUMENT")
        self.case("CONFIG-LIMIT-NEG",["resource-limits","strict-CLI-integers"],bad_limits)

    def faults(self):
        for which,checkpoint in (("01","after-flush:signing-key.v1.json"),("02","after-publish:signing-key.v1.json")):
            def init_crash(which=which,checkpoint=checkpoint):
                root=self.instances/("FI-"+which);self.driver(dict(root=str(root),operation="init",barrier=checkpoint))
                saved=inventory(root);self.cli(root,"instance","init",exit=4,code="RECOVERY_REQUIRED");check(inventory(root)==saved,"No replacement initialization after interrupted key publication")
                if which=="02":
                    key=json.loads((root/"signing-key.v1.json").read_bytes())
                    self.cli(root,"instance","recover","--from",root/"signing-key.v1.json","--confirm-unbound-initialization")
                    manifest=json.loads((root/"instance.identity.json").read_bytes());check(manifest["instance_guid"]==key["instance_guid"] and manifest["public_key_hex"]==key["public_key_hex"] and manifest["version"]==2,"Recover original key/instance as v2 unbound")
                else:
                    temps=list(root.glob(".uddl-*.tmp"));check(len(temps)==1,"Original key survives in one flushed temp")
                    key=json.loads(temps[0].read_bytes());check(key["format"]=="UDDL-ED25519-KEY","Temp is intact original key evidence")
            self.case("FI-"+which,["FI-"+which,"key-manifest-crash"],init_crash,fault=True)
        def recover_crash():
            root=self.instances/"FI-03";root.mkdir();key,manifest=ref.identity_files("D");write_json(root/"signing-key.v1.json",key)
            key_hash=ref.sha((root/"signing-key.v1.json").read_bytes());self.driver(dict(root=str(root),operation="key-recover",barrier="after-flush:instance.identity.json"))
            self.cli(root,"instance","recover","--from",root/"signing-key.v1.json","--confirm-unbound-initialization")
            check(json.loads((root/"instance.identity.json").read_bytes())==manifest and ref.sha((root/"signing-key.v1.json").read_bytes())==key_hash,"Retry after recovery manifest temp uses same key and unbound identity")
        self.case("FI-03",["FI-03","key-only-recovery"],recover_crash,fault=True)
        for op,checkpoint,suffix in (("genesis","genesis-durable-before-binding","init"),("join","genesis-durable-before-binding","join"),("genesis","after-flush:instance.identity.json","manifest")):
            def genesis_crash(op=op,checkpoint=checkpoint,suffix=suffix):
                root=self.fresh("FI-genesis-"+suffix);key=(root/"signing-key.v1.json").read_bytes();original=json.loads((root/"instance.identity.json").read_bytes())
                request=dict(root=str(root),operation=op,barrier=checkpoint)
                if op=="join":request.update(ledger=ref.LEDGER.hex(),hash=ref.sha(self.raw["genesis"]),input=str(self.paths["genesis"]))
                self.driver(request)
                blocks=list(root.rglob("*.BLK"));check(len(blocks)==1,"Exactly one signed durable genesis before binding")
                raw=blocks[0].read_bytes();b=ref.verify(raw);check(b["kind"] is None,"Independently authenticated surviving genesis")
                self.cli(root,"ledger","init",exit=4,code="GENESIS_BINDING_INCOMPLETE")
                for _ in range(2):self.cli(root,"ledger","recover-genesis","--ledger-id",b["ledger"].hex(),"--genesis-sha512",b["hash"])
                manifest=json.loads((root/"instance.identity.json").read_bytes())
                check(manifest["public_key_hex"]==original["public_key_hex"] and manifest["instance_guid"]==original["instance_guid"] and (root/"signing-key.v1.json").read_bytes()==key,"Recovery preserves local instance identity")
                check(manifest["ledger_binding"]==dict(ledger_id_hex=b["ledger"].hex(),genesis_sha512=b["hash"]),"Exact trusted binding after retry")
                if op=="join":check(manifest["public_key_hex"]!=b["signer"].hex(),"Replica recovery cannot require own key equals genesis signer")
                self.counts(root,1)
            self.case("FI-GENESIS-"+suffix,["FI-04" if suffix!="manifest" else "FI-05","interrupted-"+op,"recovery-idempotence"],genesis_crash,fault=True)
        def block_flush():
            root=self.base("FI-06",label="A");body=self.store_input("FI06-body",b"\x11"+ref.TABLE);ident=str(uuid.UUID(bytes=ref.uid("FI06-publish")))
            self.driver(dict(root=str(root),operation="publish",input=str(body),block=ident,barrier="after-block-flush"))
            self.counts(root,1);temps=list((root/"BLK").rglob(".uddl-*.tmp"));check(len(temps)==1,"Complete block exists only as temp")
            check(ref.verify(temps[0].read_bytes())["id"]==uuid.UUID(ident).bytes,"Independent temp block signature/hash")
            self.cli(root,"block","publish","--event-file",body,"--block-guid",ident,exit=6,code="DURABILITY_UNCERTAIN")
        self.case("FI-06",["FI-06","temporary-publication","retry-durability-diagnosis"],block_flush,fault=True)
        for ident,reason in (("FI-07","MoveFileEx(WRITE_THROUGH) exposes no callback between final rename and directory durability confirmation"),
                             ("FI-09","Management DDLB has no injectable per-item completed-prefix barrier"),
                             ("FI-09-GRANT","Management DDLB has no observable grant-durable/before-dependent checkpoint; sequential adapter calls would not prove the actual batch boundary"),
                             ("FI-15","Atomic config replacement and directory durability are one MoveFileEx(WRITE_THROUGH) call; no intermediate hook")):
            def blocked(reason=reason):raise Blocked(reason+"; production hooks unchanged")
            self.case(ident,[ident,"missing-fault-hook"],blocked,fault=True)
        def after_durable():
            root=self.base("FI-08");raw=self.raw["drop_table"]
            self.driver(dict(root=str(root),operation="import",input=str(self.paths["drop_table"]),barrier="after-publish:"+block_path(root,raw).name))
            check(block_path(root,raw).read_bytes()==raw,"Durable original survives pre-ack process termination")
            result=self.import_named(root,"drop_table");check(result["duplicate_count"]==1 and result["durable_count"]==0,"Retry import is duplicate, no new retained count")
            self.counts(root,2)
        self.case("FI-08",["FI-08","durable-before-ack","idempotent-import"],after_durable,fault=True)
        for number,after,restoring in ((10,False,False),(11,True,False),(12,False,True),(13,True,True)):
            def transition(number=number,after=after,restoring=restoring):
                root=self.base("FI-"+str(number))
                for name in ("grant_B","B_data"):self.import_named(root,name)
                if restoring:self.import_named(root,"revoke_B");self.counts(root,4,0,1)
                raw=self.raw["B_data"];dest=block_path(root,raw,not restoring)
                prefix="after-rename:" if after else "before-rename:"
                trigger="regrant_B" if restoring else "revoke_B"
                self.driver(dict(root=str(root),operation="import",input=str(self.paths[trigger]),barrier=prefix+dest.name))
                self.counts(root,5 if restoring else 4,0,0 if restoring else 1)
                check(dest.read_bytes()==raw,"Restart recomputes archive classification without changing bytes")
                if not restoring:
                    self.cli(root,"block","get","--id",str(uuid.UUID(bytes=ref.verify(raw)["id"])),"--sha512",ref.sha(raw),"--output",self.outputs/("FI"+str(number)+".BLK"),exit=4,code="BLOCK_NOT_FOUND")
            self.case("FI-"+str(number),["FI-"+str(number),"archive-crash-recovery"],transition,fault=True)
        for suffix,checkpoint,value in (("14","after-flush:instance.config.json",6000),("15-POST","after-rename:instance.config.json",8000)):
            def config_crash(suffix=suffix,checkpoint=checkpoint,value=value):
                root=self.base("FI-config-"+suffix);self.cli(root,"config","set","--name","max_raw_block_bytes","--value","6000")
                identity=(root/"instance.identity.json").read_bytes();key=(root/"signing-key.v1.json").read_bytes()
                self.driver(dict(root=str(root),operation="config",value=8000,barrier=checkpoint))
                check(self.cli(root,"config","get")["limits"]["max_raw_block_bytes"]==value,"Complete expected config survives named boundary")
                check((root/"instance.identity.json").read_bytes()==identity and (root/"signing-key.v1.json").read_bytes()==key,"Config crash leaves identity and trust unchanged")
            self.case("FI-"+suffix,["FI-14" if suffix=="14" else "FI-15-post-return-only","config-atomic-replacement"],config_crash,fault=True)
        def consent_flush():
            root=self.base("FI-16",label="A");self.import_named(root,"grant_B");out=self.outputs/"FI16-consent.bin"
            self.driver(dict(root=str(root),operation="consent",outer=ref.PUBS["B"].hex(),target=ref.PUBS["B"].hex(),output=str(out),barrier="after-flush:"+out.name))
            check(not out.exists(),"No final partial consent file after temp flush crash")
            packages=[]
            for p in self.outputs.glob(".uddl-*.tmp"):
                data=p.read_bytes()
                if len(data)==229 and data[:13]==ref.DOMAIN:packages.append(data)
            check(bool(packages),"Complete signed consent survives as temp evidence")
            for data in packages:Ed25519PublicKey.from_public_bytes(data[133:165]).verify(data[-64:],data[:165])
            self.cli(root,"master","countersign-revocation","--outer-signer-public-key",ref.PUBS["B"].hex(),"--revoked-public-key",ref.PUBS["B"].hex(),"--output",out)
            check(out.read_bytes()[45:61] not in [x[45:61] for x in packages],"ARCH-014 fresh consent attempt generates a new GUID, superseding historical v1 repeated-intent expectation")
        self.case("FI-16",["FI-16","consent-publication"],consent_flush,fault=True)
        def identity_lock():
            root=self.base("FI-17");copied=self.instances/"FI-17-copy";shutil.copytree(root,copied)
            held=self.driver(dict(root=str(root),operation="lease"),kill=False)
            try:
                self.cli(root,"config","get",exit=4,code="DUPLICATE_INSTANCE")
                self.cli(copied,"config","get",exit=4,code="DUPLICATE_INSTANCE")
            finally:self.release_driver(held,terminate=True)
            self.cli(root,"config","get");self.counts(root,1)
        self.case("FI-17",["FI-17","same-host-lock","process-termination"],identity_lock,fault=True)
        def held_config():
            root=self.base("FI-20");self.cli(root,"config","set","--name","max_raw_block_bytes","--value","6000")
            path=root/"instance.config.json";original=path.read_bytes();held=self.driver(dict(root=str(root),operation="hold-file",input=str(path)),kill=False)
            try:self.cli(root,"config","set","--name","max_raw_block_bytes","--value","8000",exit=6,code="IO_ERROR")
            finally:self.release_driver(held,terminate=True)
            check(path.read_bytes()==original,"Exclusive external file handle cannot cause destructive config replacement")
            self.cli(root,"config","set","--name","max_raw_block_bytes","--value","8000")
            check(self.cli(root,"config","get")["limits"]["max_raw_block_bytes"]==8000,"Operation succeeds after external handle release")
        self.case("FI-20-HELD-FILE",["FI-20","external-lock","no-overwrite"],held_config,fault=True)
        def revocation_ack():
            root=self.base("FI-revocation-ack",label="B");self.import_named(root,"grant_B")
            now=int(time.time()*1000);name="FI-revocation-ack";package=ref.consent(name,now,outer="B",counter="A",target="B")
            expected_id=ref.uid(name);path=self.store_input(name,package)
            # Existing callback gives exact final block filename only after timestamp creation.
            # after-block-flush would be pre-durability; use a two-stage selector in the QA
            # adapter only when an already-existing callback matches after-publish:*.BLK.
            self.driver(dict(root=str(root),operation="publish-consent",input=str(path),barrier="after-block-publish"))
            originals=[p.read_bytes() for p in root.rglob("*.BLK") if ref.verify(p.read_bytes())["id"]==expected_id]
            check(len(originals)==1,"One durable revocation survives before normal success acknowledgment")
            result=self.cli(root,"block","publish","--revocation-consent-file",path)
            check(result["sha512"]==ref.sha(originals[0]),"Retry returns original surviving revocation")
        self.case("FI-REVOCATION-ACK",["revocation-durable-before-ack","idempotent-revocation"],revocation_ack,fault=True)

    def finish(self):
        counts=dict(collections.Counter(x["status"] for x in self.events))
        summary=dict(suite="candidate",candidate_executed=True,context=self.a.context,counts=counts,cases=self.events,
                     physical_power_loss_proven=False,formal_step1_acceptance="NOT_DECLARED")
        write_json(self.root/"candidate-results.json",summary)
        matrix=["case_id,requirements,status,execution_context"]
        matrix += [x["id"]+",\""+";".join(x["requirements"])+"\","+x["status"]+","+x["execution_context"] for x in self.events]
        (self.root/"coverage-results.csv").write_text("\n".join(matrix)+"\n",encoding="utf-8")
        print(json.dumps(dict(suite="candidate",counts=counts,formal_step1_acceptance="NOT_DECLARED")),flush=True)
        return 1 if counts.get("FAIL",0) else 2 if counts.get("BLOCKED",0) or counts.get("NOT-RUN",0) else 0


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--candidate",required=True);parser.add_argument("--dotnet",required=True)
    parser.add_argument("--fixtures",required=True);parser.add_argument("--driver",default="");parser.add_argument("--evidence",required=True)
    parser.add_argument("--context",choices=["CodexSandboxPreflight","OrdinaryWindowsPowerShell"],required=True)
    parser.add_argument("--functional-only",action="store_true")
    args=parser.parse_args();suite=Suite(args)
    try:
        suite.functional()
        if not args.functional_only:suite.faults()
        else:
            def not_run():raise Blocked("Fault suite intentionally not requested by --functional-only; not native acceptance")
            suite.case("FAULT-SUITE-NOT-RUN",["FI-01..FI-20"],not_run,fault=True)
    except Exception:
        (suite.root/"harness-fatal.txt").write_text(traceback.format_exc(),encoding="utf-8")
        suite.events.append(dict(id="HARNESS-FATAL",requirements=["runner"],status="FAIL",execution_context=args.context,detail=traceback.format_exc(),category="harness_or_environment"))
    raise SystemExit(suite.finish())
