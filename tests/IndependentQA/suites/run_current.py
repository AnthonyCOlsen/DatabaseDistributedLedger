"""Run current Step 1 regression bodies without changing historical assertions.

Historical launchers retain their exact candidate pins. This entry point verifies
the approved source tree, freezes supplied binaries, and reports new runs alone.
"""
import argparse
import collections
import datetime as dt
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import traceback
from types import SimpleNamespace

APPROVED_SOURCE_TREE = 'f7766464788ac4991a02ea0fc3bb981f57a4d00f'
SQLITE_TREE = '9e69364cf105a1d38e1d17ee66290967f640f78d'
DISPOSITIONS = {
    'FI-07': 'Original OS-internal checkpoint replaced by QA-012 observable tests.',
    'FI-15': 'Original OS-internal checkpoint replaced by QA-012 observable tests.',
    'FI-09': 'Superseded missing-hook declaration; actual callback tested by QA-011.',
    'FI-09-GRANT': 'Superseded missing-hook declaration; actual callback tested by QA-011.',
    'FI09-LIVE-RESULT-SNAPSHOT': 'NOT OBSERVED; Owner-approved paired evidence retained.',
    'POLICY-ACCEPTED-STATE': 'Superseded policy question; current QA-015 diagnostics tested.',
    'POLICY-ACCEPTED-REPAIR': 'Automatic repair is Step 4, outside current Step 1 tests.',
}


def verify_fresh_callbacks(previous, context, candidate):
    """Bind QA-012 to this run's QA-011 results, not a different historical build."""
    result = json.loads((pathlib.Path(previous) / 'candidate-results.json').read_text())
    required = {'JOIN-01', 'JOIN-NEG-ddlb', 'FI-09', 'FI-09-GRANT', 'FI09-READY-RELEASE-RESULT'}
    observed = {c['id']: c['status'] for c in result['cases'] if c['id'] in required}
    if set(observed) != required or set(observed.values()) != {'PASS'} or result['context'] != context:
        raise RuntimeError('Fresh callback prerequisites incomplete or from another context')
    for p in (pathlib.Path(previous) / 'commands').glob('*.process.json'):
        command = json.loads(p.read_text()).get('arguments', [])
        if len(command) > 1 and pathlib.Path(command[1]).name == 'ledgerctl.dll':
            if pathlib.Path(command[1]).resolve() != pathlib.Path(candidate).resolve():
                raise RuntimeError('Callback prerequisites used another candidate')
    return dict(source=str(previous), case_results=observed, newly_executed=True,
                context=context, candidate=str(candidate))


def main(a):
    repo = pathlib.Path(a.repo).resolve()
    evidence = pathlib.Path(a.evidence).resolve()
    allowed = repo / 'local/qa'
    if not evidence.is_relative_to(allowed) or evidence.exists():
        raise ValueError('Use a new evidence directory under local/qa')
    evidence.mkdir(parents=True)
    env = os.environ.copy()
    env.update(DOTNET_CLI_HOME=str(repo / 'artifacts/cache/dotnet-home'),
               MSBuildEnableWorkloadResolver='false', DOTNET_CLI_TELEMETRY_OPTOUT='1',
               DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false')
    status = dict(scope='Repository-layout regression, not new milestone acceptance',
                  context=a.context, candidate='NOT-RUN', exit_code=1)

    def write(name, value):
        (evidence / name).write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')

    def sha(p):
        return hashlib.sha256(p.read_bytes()).hexdigest()

    def run(name, args):
        args = [str(x) for x in args]
        start = dt.datetime.now(dt.timezone.utc).isoformat()
        p = subprocess.run(args, cwd=evidence, env=env, capture_output=True)
        out = evidence / (name + '.stdout.txt'); err = evidence / (name + '.stderr.txt')
        out.write_bytes(p.stdout); err.write_bytes(p.stderr)
        write(name + '.process.json', dict(arguments=args, exit_code=p.returncode,
              utc_start=start, utc_finish=dt.datetime.now(dt.timezone.utc).isoformat(),
              stdout_sha256=sha(out), stderr_sha256=sha(err)))
        if p.returncode:
            raise RuntimeError(name + ': ' + p.stdout.decode(errors='replace')[-1500:] + p.stderr.decode(errors='replace')[-1500:])
        return p.stdout.decode('utf-8-sig').strip()

    try:
        status['head'] = run('git-head', ['git', '-C', repo, 'rev-parse', 'HEAD'])
        if run('source-tree', ['git', '-C', repo, 'rev-parse', 'HEAD:src']) != APPROVED_SOURCE_TREE:
            raise RuntimeError('Review a changed production tree before using this selection')
        if run('sqlite-tree', ['git', '-C', repo, 'rev-parse', 'HEAD:third_party/sqlite']) != SQLITE_TREE:
            raise RuntimeError('SQLite source changed')
        if run('production-status', ['git', '-C', repo, 'status', '--porcelain', '--', 'src', 'third_party/sqlite']):
            raise RuntimeError('Production source is dirty')
        qa = repo / 'tests/IndependentQA'
        originals = json.loads((qa / 'suites/legacy-dev003/ORIGINALS.manifest.json').read_text())
        for entry in originals['files']:
            if sha(qa / 'suites/legacy-dev003' / entry['name']) != entry['sha256']:
                raise RuntimeError('Original QA input changed: ' + entry['name'])
        snapshot = evidence / 'candidate-snapshot'; snapshot.mkdir()
        for p in pathlib.Path(a.candidate).resolve().parent.iterdir():
            if p.is_file():
                shutil.copy2(p, snapshot / p.name)
                if sha(snapshot / p.name) != sha(p):
                    raise RuntimeError('Build changed while copying')
        write('binary-hashes.json', {p.name: sha(p) for p in snapshot.iterdir()})
        suites = evidence / 'suite-snapshot'; suites.mkdir()
        inputs = {}
        for folder in ('suites', 'fixtures', 'adapters'):
            for p in (qa / folder).rglob('*'):
                if not p.is_file() or any(x in ('bin', 'obj', '__pycache__', 'legacy-dev003') for x in p.relative_to(qa).parts):
                    continue
                if p.suffix not in ('.py', '.json', '.cs', '.csproj'):
                    continue
                if (suites / p.name).exists():
                    raise RuntimeError('Ambiguous QA input name: ' + p.name)
                shutil.copy2(p, suites / p.name); inputs[p.name] = sha(p)
        write('suite-hashes.json', inputs)
        run('dotnet-version', [a.dotnet, '--version'])
        run('python-version', [sys.executable, '-B', '-c', 'import sys,cryptography,lzma;print(sys.version);print(cryptography.__version__)'])
        fixtures = suites / 'QA010_FIXTURES_103.json'
        run('reference-103', [sys.executable, '-B', suites / 'QA010_REFERENCE.py', '--fixtures', fixtures, '--results', evidence / 'reference-103.json'])
        run('reference-corruption', [sys.executable, '-B', suites / 'QA013_MUTATIONS.py', '--fixtures', suites / 'QA013_FIXTURES.json', '--results', evidence / 'reference-corruption.json'])
        run('reference-integrity', [sys.executable, '-B', suites / 'QA014_INTEGRITY_REVIEW.py', '--fixtures', fixtures, '--reference-only', '--results', evidence / 'reference-integrity.json'])
        run('reference-original-bundle', [sys.executable, '-B', qa / 'suites/legacy-dev003/QA_DEV003_OFFLINE_INDEPENDENT_CHECK.py', '--json-out', evidence / 'reference-original-bundle.json'])
        run('source-order', [sys.executable, '-B', suites / 'QA016_GATE_ORDER_CHECK.py', '--source-root', repo, '--output', evidence / 'source-order.json'])
        for directory, name in (('driver', 'QA010_FaultDriver'), ('verifier', 'QA013_Verify'), ('equivalence-tool', 'QA017_Equivalence')):
            dest = evidence / directory; dest.mkdir()
            for ext in ('.cs', '.csproj'):
                shutil.copy2(suites / (name + ext), dest / (name + ext))
            (dest / 'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
            run(directory + '-build', [a.dotnet, 'build', dest / (name + '.csproj'), '-c', 'Release', '--nologo', '-m:1',
                '/p:UseSharedCompilation=false', '/p:MSBuildEnableWorkloadResolver=false', '/p:CandidateDirectory=' + str(snapshot)])
        # Import only the immutable run snapshot; original suite bodies stay unchanged.
        sys.path.insert(0, str(suites))
        from QA010_RUN import Suite
        from QA011_RUN import Focused
        from QA012_RUN import Qualification
        from QA013_RUN import Corruption
        from QA014_RUN import FinalRegression, NEW
        from QA014_INTEGRITY_REVIEW import IntegrityReview
        from QA015_RUN import Additional
        from QA016_RUN import GateRuntime
        all_cases = []; dispositions = []

        class Selection:
            def carried(self):
                # Original QA-012 pinned old QA-011 binary receipts. For the shared
                # regression use the just-executed callback group on this candidate.
                dependency = verify_fresh_callbacks(evidence / 'batch-callbacks', a.context, self.a.candidate)
                (self.root / 'fresh-callback-dependency.json').write_text(json.dumps(dependency, indent=2) + '\n')

            def case(self, ident, requirements, function, **kwargs):
                if ident in DISPOSITIONS and not (self.group == 'batch-callbacks' and ident in ('FI-09', 'FI-09-GRANT')):
                    dispositions.append(dict(group=self.group, id=ident, disposition=DISPOSITIONS[ident], executed=False))
                    return
                return super().case(ident, requirements, function, **kwargs)

        def group(name, cls, method, selected=None):
            target = evidence / name; target.mkdir()
            args = SimpleNamespace(candidate=str(snapshot / 'ledgerctl.dll'), dotnet=a.dotnet,
                fixtures=str(fixtures), driver=str(evidence / 'driver/bin/Release/net10.0/QA010_FaultDriver.dll'),
                verifier=str(evidence / 'verifier/bin/Release/net10.0/QA013_Verify.dll'),
                evidence=str(target), context=a.context, selection='All')
            suite = type('Layout' + cls.__name__, (Selection, cls), {})(args); suite.group = name
            if selected is not None:
                suite.selected = set(selected)
            try:
                for entry in method:
                    getattr(suite, entry)()
            except Exception:
                (target / 'harness-error.txt').write_text(traceback.format_exc())
                suite.events.append(dict(id='HARNESS-ERROR', status='FAIL', requirements=['harness'], detail=traceback.format_exc(), execution_context=a.context))
            Suite.finish(suite)
            all_cases.extend(dict(c, group=name) for c in suite.events)

        group('broad', Suite, ['functional', 'faults'])
        group('batch-callbacks', Focused, ['focused'])
        group('durability', Qualification, ['qualification'])
        group('corruption', Corruption, ['corruption'])
        group('archive-contracts', FinalRegression, ['targeted'], NEW)
        group('duplicates-and-missing-genesis', Additional, ['focused'])
        group('integrity-diagnostics', IntegrityReview, ['focused'])
        target = evidence / 'fresh-publication'; target.mkdir()
        args = SimpleNamespace(candidate=str(snapshot / 'ledgerctl.dll'), dotnet=a.dotnet, fixtures=str(fixtures),
            evidence=str(target), context=a.context, driver='', verifier='')
        fresh = GateRuntime(args)
        for condition in ('corrupt', 'missing', 'healthy'):
            fresh.case('FRESH-NO-GUID-' + condition.upper(), ['pre-signing-gate'], lambda condition=condition: fresh.publication(condition))
        Suite.finish(fresh); all_cases.extend(dict(c, group='fresh-publication') for c in fresh.events)
        counts = collections.Counter(c['status'] for c in all_cases)
        status.update(candidate='PASS' if not counts['FAIL'] and not counts['BLOCKED'] and not counts['NOT-RUN'] else 'FAIL',
            candidate_counts={s: counts[s] for s in ('PASS', 'FAIL', 'BLOCKED', 'NOT-RUN')}, cases=all_cases,
            carried_dispositions=dispositions, source_checks=json.loads((evidence / 'source-order.json').read_text())['counts'],
            reference_checks=dict(protocol=76, corruption=12, integrity=4, original_bundle='See separate original verifier receipt'),
            exit_code=1 if counts['FAIL'] else 2 if counts['BLOCKED'] or counts['NOT-RUN'] else 0)
        # Verify stream hashes for every candidate subprocess, including expected errors.
        receipts = 0
        for p in evidence.rglob('*.process.json'):
            receipt = json.loads(p.read_text()); stem = p.name[:-len('.process.json')]
            for stream in ('stdout', 'stderr'):
                expected = receipt.get(stream + '_sha256')
                if expected and sha(p.with_name(stem + '.' + stream + '.txt')) != expected:
                    raise RuntimeError('Command receipt mismatch: ' + str(p))
            receipts += 1
        status['process_receipts_verified'] = receipts
    except Exception:
        status['launcher_error'] = traceback.format_exc()
        (evidence / 'launcher-error.txt').write_text(status['launcher_error'], encoding='utf-8')
        status['exit_code'] = 1
    finally:
        write('run-status.json', status)
        print('Current independent QA: ' + status['candidate'])
        if 'candidate_counts' in status:
            print('Candidate cases: ' + json.dumps(status['candidate_counts']))
        print('Evidence: ' + str(evidence))
        print('Exit code: ' + str(status['exit_code']))
    return status['exit_code']


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('repo', 'candidate', 'dotnet', 'evidence', 'context'):
        parser.add_argument('--' + name, required=True)
    raise SystemExit(main(parser.parse_args()))
