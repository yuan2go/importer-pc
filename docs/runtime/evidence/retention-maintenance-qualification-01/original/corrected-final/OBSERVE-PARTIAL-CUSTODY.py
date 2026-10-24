"""Closed-snapshot diagnostic after an incomplete synthetic maintenance run; never grants full PASS."""
from pathlib import Path
from datetime import datetime,timezone
import importlib.util,json,sqlite3
root=Path('D:/projects/ipc-retention-full-scale-corrected-20261024')
run=json.loads((root/'PROTECTED-RESULT.json').read_text(encoding='utf-8-sig'))
if run['Status']=='InProgress':raise ValueError('Wait for the actual maintenance terminal state')
case=next(c for c in run['Cases'] if c['Count']==604800);n=case['Count'];folder=Path(case['CaseRoot'])
fixture=json.loads((folder/'FIXTURE.json').read_text(encoding='utf-8-sig'));database=Path(fixture['Database']);backup=folder/'complete-before-expiry.db'
output=root/'PARTIAL-CUSTODY-DIAGNOSTIC.json'
if output.exists():raise ValueError('New diagnostic output required')
spec=importlib.util.spec_from_file_location('reconcile',Path('D:/projects/importer-pc-retention-verification/scripts/reconcile-retention-qualification.py'));module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
for path in (database,backup):
 wal=Path(str(path)+'-wal')
 if wal.exists() and wal.stat().st_size:raise ValueError('Nonempty WAL must not be ignored')
checks=[]
def check(value,label):
 if not value:raise ValueError(label)
 checks.append(label)
with module.closed_snapshots((database,backup)):
 db=sqlite3.connect(database.as_uri()+'?mode=ro&immutable=1',uri=True)
 try:
  db.execute('PRAGMA query_only=ON');db.execute('ATTACH DATABASE ? AS original',(backup.as_uri()+'?mode=ro&immutable=1',))
  def scalar(sql):return db.execute(sql).fetchone()[0]
  operations=scalar('SELECT count(*) FROM parcel_retired_operations');inputs=scalar('SELECT count(*) FROM parcel_retired_inputs');remaining=scalar('SELECT count(*) FROM parcel_ingress')
  check(operations<=n and inputs<=n,'retired counts within original eligible population')
  check(scalar('SELECT count(*) FROM parcel_retired_operations t LEFT JOIN original.parcel_archives a ON a.id=t.id WHERE a.id IS NULL OR t.body_hash!=a.body_hash')==0,'every committed operation retirement matches its original body hash')
  check(scalar("SELECT count(*) FROM parcel_retired_inputs t LEFT JOIN original.parcel_ingress i ON i.id=t.id WHERE i.id IS NULL OR t.body_hash!=i.body_hash OR t.state!=i.state OR i.state NOT IN ('Processed','Inspected')")==0,'every committed input retirement matches its original body hash and disposition')
  check(scalar('SELECT count(*) FROM parcel_ingress i JOIN parcel_retired_inputs t ON t.id=i.id')==0 and inputs+remaining==n+6,'all original input identities partition between retired and remaining without overlap')
  check(scalar('SELECT count(*) FROM parcel_ingress i LEFT JOIN original.parcel_ingress o ON o.id=i.id WHERE o.id IS NULL OR i.body!=o.body OR i.body_hash!=o.body_hash OR i.state!=o.state')==0,'every remaining input is unchanged from the complete backup')
  guards={module.guid(1,k) for k in range(n+1,n+7)}
  retained=dict(db.execute('SELECT id,body FROM parcel_operations UNION ALL SELECT id,body FROM parcel_archives'))
  before=dict(db.execute('SELECT id,body FROM original.parcel_archives WHERE id>?',(module.guid(1,n),)))
  check(guards<=set(retained) and all(retained[k]==before[k] for k in guards),'all six protected operation bodies remain unchanged')
  check(list(db.execute('SELECT * FROM parcel_commands ORDER BY id'))==list(db.execute('SELECT * FROM original.parcel_commands ORDER BY id')),'pending command and original receipt responsibility unchanged')
  check(list(db.execute('SELECT * FROM parcel_identity_conflicts ORDER BY id'))==list(db.execute('SELECT * FROM original.parcel_identity_conflicts ORDER BY id')),'original conflict and acknowledgement responsibility unchanged')
  check(module.fingerprint(db,'SELECT id,admission,observation,position FROM parcel_history_index ORDER BY seq')==fixture['IdentityFingerprint'],'complete permanent admission fingerprint unchanged')
  check(module.fingerprint(db,'SELECT request,id,hash FROM parcel_reply_identities ORDER BY request,id')==fixture['ReplyFingerprint'],'complete permanent reply fingerprint unchanged')
 finally:db.close()
result={'Status':'PARTIAL_CUSTODY_INVARIANTS_VERIFIED_NOT_FULL_MAINTENANCE_PASS','AtUtc':datetime.now(timezone.utc).isoformat(),'ActualRunStatus':run['Status'],'SourceCommit':run['SourceCommit'],'ProductSha256':run['ProductSha256'],'Eligible':n,'RetiredOperations':operations,'RetiredInputs':inputs,'RemainingInputs':remaining,'AllEligibleInputsRetired':inputs==n,'Checks':checks,'Scope':'Closed-snapshot partial accounting only. Retains actual CLI failure/incompletion; cannot replace the exact all-N verifier, two-hour maintenance result, protected restore, target hardware or field qualification.'}
output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print(json.dumps(result))
