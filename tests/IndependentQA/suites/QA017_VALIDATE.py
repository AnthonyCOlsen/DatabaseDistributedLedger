"""Validate new QA-017 receipts; never edits cases or historical evidence."""
import argparse
import collections
import hashlib
import json
import pathlib


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate(root):
    status = json.loads((root / 'run-status.json').read_text())
    cases = json.loads((root / 'runtime/qa017-candidate-results.json').read_text())
    assert len(cases['cases']) == len({c['id'] for c in cases['cases']}) == 53
    counts = collections.Counter(c['status'] for c in cases['cases'])
    assert {s: counts[s] for s in ('PASS', 'FAIL', 'BLOCKED', 'NOT-RUN')} == status['candidate_counts']
    assert all(c['execution_context'] == status['context'] for c in cases['cases'])
    source = json.loads((root / 'source-order-results.json').read_text())
    assert len(source['cases']) == 6 and source['counts'] == status['source_counts']
    reference = json.loads((root / 'reference-results.json').read_text())
    assert len(reference) == 76
    assert collections.Counter(c['status'] for c in reference)['PASS'] == status['reference_counts']['PASS']
    build = json.loads((root / 'build-provenance.json').read_text())
    for name, digest in build['builds']['candidate']['hashes'].items():
        assert sha(root / 'candidate-snapshot' / name) == digest
    inputs = json.loads((root / 'suite-hashes.json').read_text())
    for name, digest in inputs.items():
        assert sha(root / 'suite-snapshot' / name) == digest
    receipts = []
    for record in sorted(root.rglob('*.process.json')):
        obj = json.loads(record.read_text())
        prefix = record.name[:-len('.process.json')]
        stdout = record.with_name(prefix + '.stdout.txt')
        stderr = record.with_name(prefix + '.stderr.txt')
        assert stdout.is_file() and stderr.is_file(), str(record)
        if 'stdout_sha256' in obj:
            assert sha(stdout) == obj['stdout_sha256'], str(stdout)
            assert sha(stderr) == obj['stderr_sha256'], str(stderr)
        command = obj.get('arguments', [])
        if len(command) > 1 and pathlib.Path(command[1]).name == 'ledgerctl.dll':
            raw = stdout.read_bytes()
            assert raw.endswith(b'\n') and raw.count(b'\n') == 1
            response = json.loads(raw)
            assert set(response) == {'ok', 'data', 'error'}
            assert response['ok'] == (obj['exit_code'] == 0)
        receipts.append(dict(path=record.relative_to(root).as_posix(), exit_code=obj['exit_code'],
                             stdout_sha256=sha(stdout), stderr_sha256=sha(stderr)))
    result = dict(assignment='QA-017', receipts_verified=len(receipts), candidate_cases=53,
                  source_cases=6, reference_cases=76, context=status['context'], receipt_inventory=receipts,
                  original_assertions_preserved=True, native_claim_verified_by_operator_context_only=True)
    (root / 'receipt-validation.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k != 'receipt_inventory'}))
    return 0


if __name__ == '__main__':
    ap = argparse.ArgumentParser(); ap.add_argument('evidence'); args = ap.parse_args()
    raise SystemExit(validate(pathlib.Path(args.evidence)))
