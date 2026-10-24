from pathlib import Path
from datetime import datetime,timezone
import json,sqlite3,time
root=Path('D:/projects/ipc-retention-full-scale-corrected-20261024')
run=json.loads((root/'PROTECTED-RESULT.json').read_text(encoding='utf-8-sig'))
case=next(c for c in run['Cases'] if c['Count']==604800)
fixture=json.loads((Path(case['CaseRoot'])/'FIXTURE.json').read_text(encoding='utf-8-sig'))
path=Path(fixture['Database'])
assert Path(str(path)+'-wal').exists() and Path(str(path)+'-shm').exists()
assert (Path(case['CaseRoot'])/'complete-before-expiry.db.recovery/COMPLETED.json').is_file()
started=time.perf_counter()
connection=sqlite3.connect(path.as_uri()+'?mode=ro',uri=True,timeout=0.1)
try:
 connection.execute('PRAGMA query_only=ON')
 connection.execute('BEGIN')
 operations=connection.execute('SELECT count(*) FROM parcel_retired_operations').fetchone()[0]
 inputs=connection.execute('SELECT count(*) FROM parcel_retired_inputs').fetchone()[0]
 connection.rollback()
finally:
 connection.close()
result={'AtUtc':datetime.now(timezone.utc).isoformat(),'Status':'OBSERVED_COMMITTED_SYNTHETIC_PROGRESS_ONLY','SQLiteReadOnlyVersion':sqlite3.sqlite_version,'RetiredOperations':operations,'RetiredInputs':inputs,'Eligible':604800,'ReadSeconds':time.perf_counter()-started,'Mode':'SQLite mode=ro, query_only, short shared-WAL read transaction; no immutable, database writes, lease/ACL changes or checkpoint','Scope':'Progress only, not completed retention, final validation or a forecast; host is not isolated'}
with (root/'LIVE-RETIREMENT-OBSERVATIONS.jsonl').open('a',encoding='utf-8') as stream:stream.write(json.dumps(result)+'\n')
print(json.dumps(result))
