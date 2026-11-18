"""QA-017 selection. Calls existing assertion bodies unchanged; no historical writes."""
import argparse
import collections
import copy
import json
import pathlib
from QA010_RUN import Suite, write_json
from QA015_RUN import main as existing_49
from QA016_RUN import GateRuntime


def run(args):
    root = pathlib.Path(args.evidence)
    old = copy.copy(args)
    old_root = root / 'existing-49'
    old.evidence = str(old_root)
    pathlib.Path(old.evidence).mkdir()
    existing_49(old)
    prior = json.loads((old_root / 'candidate-results.json').read_text())
    fresh = copy.copy(args)
    fresh.evidence = str(root / 'fresh-publication')
    pathlib.Path(fresh.evidence).mkdir()
    gate = GateRuntime(fresh)
    for condition in ('corrupt', 'missing', 'healthy'):
        gate.case('FRESH-NO-GUID-' + condition.upper(), ['unchanged-QA016-runtime', 'pre-signing-gate'],
                  lambda condition=condition: gate.publication(condition))
    Suite.finish(gate)
    life = copy.copy(args)
    life.evidence = str(root / 'lifecycle')
    pathlib.Path(life.evidence).mkdir()
    life.case_ids = {'LIFE-01'}
    lifecycle = Suite(life)
    lifecycle.functional()
    Suite.finish(lifecycle)
    cases = [dict(c, evidence_group='existing-49') for c in prior['cases']]
    cases += [dict(c, evidence_group='fresh-publication') for c in gate.events]
    cases += [dict(c, evidence_group='lifecycle') for c in lifecycle.events]
    counts = collections.Counter(c['status'] for c in cases)
    result = dict(assignment='QA-017', context=args.context, planned_cases=53,
                  counts={s: counts[s] for s in ('PASS', 'FAIL', 'BLOCKED', 'NOT-RUN')}, cases=cases,
                  assertions_unchanged=True, historical_results_recounted=False,
                  accepted_baseline_replaced=False, physical_power_loss_proven=False)
    if len(cases) != 53:
        raise RuntimeError('Expected 53 actual case receipts; missing coverage cannot become PASS')
    write_json(root / 'qa017-candidate-results.json', result)
    return 1 if counts['FAIL'] else 2 if counts['BLOCKED'] or counts['NOT-RUN'] else 0


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    for name in ('candidate', 'dotnet', 'fixtures', 'driver', 'verifier', 'evidence', 'context'):
        ap.add_argument('--' + name, required=True)
    raise SystemExit(run(ap.parse_args()))
