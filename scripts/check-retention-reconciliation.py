"""Windows-only negative verification of the read-only synthetic retention reconciler."""
import argparse
from contextlib import contextmanager
import importlib.util
import json
from pathlib import Path
import shutil
import sqlite3
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--run', type=Path, required=True)
args = parser.parse_args()
spec = importlib.util.spec_from_file_location('retention_reconcile', Path(__file__).with_name('reconcile-retention-qualification.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
source = json.loads(args.run.read_text(encoding='utf-8-sig'))
case = next(c for c in source['Cases'] if c['Status'] == 'PASS_PROTECTED_CLI_SYNTHETIC')
with tempfile.TemporaryDirectory(prefix='retention-negative-check-') as folder:
    root = Path(folder)
    target = root / f"case-{case['Count']}"
    target.mkdir()
    original = Path(case['CaseRoot'])
    fixture = json.loads((original / 'FIXTURE.json').read_text(encoding='utf-8-sig'))
    database = target / 'data/Simulation/retention-qualification/sportmaster/sportmaster.db'
    database.parent.mkdir(parents=True)
    shutil.copyfile(fixture['Database'], database)
    shutil.copyfile(original / 'complete-before-expiry.db', target / 'complete-before-expiry.db')
    shutil.copytree(original / 'complete-before-expiry.db.recovery', target / 'complete-before-expiry.db.recovery')
    if 'IsolatedRestoreSeconds' in case:
        shutil.copytree(original / 'restored', target / 'restored')
    fixture['Database'] = str(database)
    (target / 'FIXTURE.json').write_text(json.dumps(fixture), encoding='utf-8')
    derived = dict(source, ProtectedRoot=str(root), Status='UNIT_TEST_CLONE', Cases=[dict(case, CaseRoot=str(target))])
    run = root / 'unit-run.json'
    run.write_text(json.dumps(derived), encoding='utf-8')
    with module.closed_snapshots((database,)):
        connection = None
        denied = False
        try:
            connection = sqlite3.connect(database)
            connection.execute('UPDATE parcel_meta SET revision=revision+1')
            connection.commit()
        except sqlite3.OperationalError:
            denied = True
        finally:
            if connection is not None:
                connection.close()
        assert denied, 'Snapshot sharing guard accepted a writer'
    wal = Path(str(database) + '-wal')
    wal.write_bytes(b'explicit synthetic nonempty WAL guard test')
    try:
        module.reconcile(run, root / 'must-refuse-wal.json')
    except ValueError as error:
        assert 'nonempty WAL' in str(error), error
    else:
        raise AssertionError('Reconciliation ignored a nonempty WAL')
    wal.unlink()  # Only this explicitly created fake WAL in the disposable unit-test clone.
    original_sharing = module.closed_snapshots
    @contextmanager
    def wal_after_initial_check(paths):
        with original_sharing(paths):
            wal.write_bytes(b'synthetic WAL left by a writer before snapshot handles were acquired')
            yield
    module.closed_snapshots = wal_after_initial_check
    try:
        module.reconcile(run, root / 'must-refuse-late-wal.json')
    except ValueError as error:
        assert 'nonempty WAL after writer exclusion' in str(error), error
    else:
        raise AssertionError('Reconciliation ignored a WAL created between precheck and sharing lock')
    finally:
        module.closed_snapshots = original_sharing
        wal.unlink()  # Disposable synthetic clone only, never an original database WAL.
    connection = sqlite3.connect(database)
    try:
        connection.execute('UPDATE parcel_retired_operations SET body_hash=? WHERE rowid=1', ('A' * 64,))
        connection.commit()
    finally:
        connection.close()
    try:
        module.reconcile(run, root / 'must-refuse-hash.json')
    except ValueError as error:
        assert 'original body hash' in str(error), error
    else:
        raise AssertionError('Reconciliation accepted a changed retirement hash')
    assert not (root / 'must-refuse-hash.json').exists()
print('PASS: Windows writer sharing refusal, nonempty-WAL refusal, post-lock WAL recheck, changed-original-hash refusal; original fixtures untouched')
