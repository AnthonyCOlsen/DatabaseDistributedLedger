"""QA-017 provenance-checked launcher; new evidence only, no production builds/edits."""
import argparse
import datetime as dt
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import traceback
import uuid

BASE = 'fbb38781faa29eb5cead9de3dcda0e760b63c7a1'
CANDIDATE = '83cbcb5ec7933d1d83cb61a9712353c169ba7788'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def execute(a):
    work = pathlib.Path(a.work_root).resolve()
    candidate = work / 'candidate'
    qa = pathlib.Path(__file__).resolve().parents[1]
    evidence = pathlib.Path(a.evidence).resolve() if a.evidence else work / (
        ('native-' if a.context == 'OrdinaryWindowsPowerShell' else 'preflight-') +
        dt.datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8])
    if not evidence.is_relative_to(work) or evidence.exists():
        raise ValueError('Use a new evidence directory inside this isolated QA-017 work root')
    evidence.mkdir()
    status = dict(assignment='QA-017', context=a.context, baseline=BASE, candidate=CANDIDATE,
                  native_candidate='NOT-RUN', disposition='QA NOT READY', exit_code=1,
                  antivirus='Unchanged; scans not independently monitored', formal_owner_approval='NOT_DECLARED')
    commands = []
    env = os.environ.copy()
    env.update(DOTNET_CLI_HOME=str(evidence / 'dotnet-home'), MSBuildEnableWorkloadResolver='false',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
               DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true')

    def write(name, obj):
        (evidence / name).write_text(json.dumps(obj, indent=2) + '\n', encoding='utf-8')

    def run(name, argv, check=True):
        argv = [str(x) for x in argv]
        started = dt.datetime.now(dt.timezone.utc).isoformat()
        p = subprocess.run(argv, cwd=evidence, env=env, capture_output=True)
        out = evidence / (name + '.stdout.txt'); err = evidence / (name + '.stderr.txt')
        out.write_bytes(p.stdout); err.write_bytes(p.stderr)
        receipt = dict(arguments=argv, exit_code=p.returncode, utc_start=started,
                       utc_finish=dt.datetime.now(dt.timezone.utc).isoformat(),
                       stdout_sha256=digest(out), stderr_sha256=digest(err))
        write(name + '.process.json', receipt); commands.append(name)
        if check and p.returncode:
            raise RuntimeError(name + ' exit ' + str(p.returncode) + ': ' + p.stderr.decode(errors='replace')[-1000:] + p.stdout.decode(errors='replace')[-1500:])
        return p

    try:
        build = json.loads((work / 'independent-build-provenance.json').read_text())
        eq = json.loads((work / 'equivalence/equivalence-results.json').read_text())
        if build['baseline'] != BASE or build['candidate'] != CANDIDATE or eq['pass'] is not True:
            raise RuntimeError('Independent source/build/equivalence provenance missing or failed')
        for assembly in eq['assemblies']:
            if any(assembly[label + '_sha256'] != build['builds'][label]['hashes'][assembly['name']]
                   for label in ('baseline', 'candidate')):
                raise RuntimeError('Equivalence results refer to a different build')
        if run('candidate-head', ['git', '-C', candidate, 'rev-parse', 'HEAD']).stdout.decode().strip() != CANDIDATE:
            raise RuntimeError('Candidate worktree moved')
        dirty = run('candidate-production-status', ['git', '-C', candidate, 'status', '--porcelain', '--', 'src', 'Directory.Build.props', 'global.json']).stdout
        if dirty.strip():
            raise RuntimeError('Candidate production source is dirty')
        source_hashes = json.loads((work / 'candidate-tracked-hashes.json').read_text())
        for name, expected in source_hashes.items():
            if digest(candidate / name) != expected:
                raise RuntimeError('Tracked source/evidence changed: ' + name)
        write('build-provenance.json', build); write('independent-equivalence.json', eq)
        write('windows-version.json', dict(platform=sys.platform, windows_version=list(sys.getwindowsversion()) if sys.platform == 'win32' else None))
        dotnet = shutil.which('dotnet')
        if not dotnet:
            raise RuntimeError('Existing .NET SDK required; no installation allowed')
        version = run('dotnet-version', [dotnet, '--version']).stdout.decode().strip()
        if version != build['sdk']:
            raise RuntimeError('SDK differs from independent build SDK')
        run('dotnet-runtimes', [dotnet, '--list-runtimes'])
        run('python-version', [sys.executable, '-B', '-c', 'import sys,cryptography,lzma;print(sys.version);print(cryptography.__version__)'])
        if a.context == 'OrdinaryWindowsPowerShell' and sys.platform != 'win32':
            raise RuntimeError('Windows context requires Windows execution')
        inputs = evidence / 'suite-snapshot'; inputs.mkdir()
        for p in qa.rglob('*'):
            if any(part in ('bin', 'obj', '__pycache__') for part in p.relative_to(qa).parts):
                continue
            if p.is_file() and p.suffix in ('.py', '.cs', '.csproj', '.ps1', '.json'):
                shutil.copy2(p, inputs / p.name)
        write('suite-hashes.json', {p.name: digest(p) for p in inputs.iterdir()})
        snapshot = evidence / 'candidate-snapshot'; snapshot.mkdir()
        build_artifacts = pathlib.Path(build['builds']['candidate']['artifacts_directory'])
        for name, expected in build['builds']['candidate']['hashes'].items():
            if digest(build_artifacts / name) != expected:
                raise RuntimeError('Built artifact changed: ' + name)
            shutil.copy2(build_artifacts / name, snapshot / name)
            if digest(snapshot / name) != expected:
                raise RuntimeError('Copied artifact differs: ' + name)
        write('candidate-artifact-hashes.json', build['builds']['candidate']['hashes'])
        fixtures = inputs / 'QA010_FIXTURES_103.json'
        run('reference', [sys.executable, '-B', inputs / 'QA010_REFERENCE.py', '--fixtures', fixtures,
                          '--results', evidence / 'reference-results.json'])
        reference = json.loads((evidence / 'reference-results.json').read_text())
        reference_counts = {s: sum(c['status'] == s for c in reference) for s in ('PASS', 'FAIL', 'BLOCKED', 'NOT-RUN')}
        if len(reference) != 76 or reference_counts['PASS'] != 76:
            raise RuntimeError('Independent reference receipt set incomplete or failed')
        run('source-order', [sys.executable, '-B', inputs / 'QA016_GATE_ORDER_CHECK.py', '--source-root', candidate,
                             '--output', evidence / 'source-order-results.json'])
        for directory, prefix in (('driver', 'QA010_FaultDriver'), ('byte-verifier', 'QA013_Verify')):
            target = evidence / directory; target.mkdir()
            for ext in ('.cs', '.csproj'):
                shutil.copy2(inputs / (prefix + ext), target / (prefix + ext))
            (target / 'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>', encoding='utf-8')
            run(directory + '-build', [dotnet, 'build', target / (prefix + '.csproj'), '-c', 'Release', '--nologo', '-m:1',
                                      '/p:BuildInParallel=false', '/p:UseSharedCompilation=false', '/p:MSBuildEnableWorkloadResolver=false', '/p:CandidateDirectory=' + str(snapshot)])
        runtime = evidence / 'runtime'; runtime.mkdir()
        p = run('candidate', [sys.executable, '-B', inputs / 'QA017_RUN.py', '--candidate', snapshot / 'ledgerctl.dll',
                             '--dotnet', dotnet, '--fixtures', fixtures,
                             '--driver', evidence / 'driver/bin/Release/net10.0/QA010_FaultDriver.dll',
                             '--verifier', evidence / 'byte-verifier/bin/Release/net10.0/QA013_Verify.dll',
                             '--evidence', runtime, '--context', a.context], check=False)
        result = json.loads((runtime / 'qa017-candidate-results.json').read_text())
        status.update(candidate_counts=result['counts'], reference_counts=reference_counts,
                      source_counts=json.loads((evidence / 'source-order-results.json').read_text())['counts'],
                      exit_code=p.returncode, native_candidate='EXECUTED' if a.context == 'OrdinaryWindowsPowerShell' else 'NOT-RUN',
                      candidate_hashes={n: build['builds']['candidate']['hashes'][n] for n in ('ledgerctl.dll', 'UserDimensions.Ledger.dll')})
        status['disposition'] = 'QA READY' if a.context == 'OrdinaryWindowsPowerShell' and p.returncode == 0 and result['counts']['PASS'] == 53 else 'QA NOT READY'
    except Exception:
        status['launcher_error'] = traceback.format_exc()
        (evidence / 'launcher-error.txt').write_text(status['launcher_error'], encoding='utf-8')
    finally:
        write('run-status.json', status)
        if 'candidate_counts' in status:
            try:
                run('evidence-validation', [sys.executable, '-B', qa / 'suites/QA017_VALIDATE.py', evidence])
            except Exception:
                status.update(disposition='QA NOT READY', exit_code=1, validation_error=traceback.format_exc())
                write('run-status.json', status)
        write('launcher-command-names.json', commands)
        (evidence / 'exit-code.txt').write_text(str(status['exit_code']) + '\n')
        print('QA-017 candidate: ' + json.dumps(status.get('candidate_counts', {'NOT-RUN': 53})))
        print('Disposition: ' + status['disposition'] + '; context=' + a.context)
        print('Evidence: ' + str(evidence))
        print('Exit code: ' + str(status['exit_code']))
        print('Maintenance verification only. Accepted baseline unchanged; separate Owner approval required.')
    return status['exit_code']


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--work-root', required=True)
    ap.add_argument('--context', required=True, choices=('CodexSandboxPreflight', 'OrdinaryWindowsPowerShell'))
    ap.add_argument('--evidence')
    raise SystemExit(execute(ap.parse_args()))
