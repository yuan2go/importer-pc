"""SM evidence joins over an already verified, immutable current-format backup.

Source clocks select events; backup-time states never stand in for event times.
Only durable identities join facts. Barcodes are evidence, never join keys.
"""
import collections
import datetime as dt
import hashlib
import json
import math
import re
import time


def runtime(value):
    return str(value or '').replace('-', '').lower()


def document(value):
    if not value:
        return {}
    try:
        parsed = json.loads(value)
        return parsed if isinstance(parsed, dict) else {}
    except (ValueError, TypeError):
        return {}


def instant(value):
    if not value:
        return None
    parsed = dt.datetime.fromisoformat(value)
    if parsed.tzinfo is None:
        raise ValueError('Business event clock has no timezone')
    return parsed


def distribution(values):
    ordered = sorted(values)
    return dict(Samples=len(ordered), Maximum=max(ordered) if ordered else None,
                P95=ordered[max(0, math.ceil(len(ordered) * .95) - 1)] if ordered else None,
                P99=ordered[max(0, math.ceil(len(ordered) * .99) - 1)] if ordered else None)


def receipt_bytes(db, database, occurrence, digest):
    if not re.fullmatch('[a-fA-F0-9]{32}', occurrence):
        raise ValueError('Invalid original receipt identity')
    file = database.parent / (database.name + '.recovery') / 'receipts' / (occurrence + '.json')
    if file.exists():
        with file.open('rb') as source:
            raw = source.read(8 * 1024 * 1024 + 1)
    else:
        row = db.execute('SELECT CASE WHEN length(body)<=8388608 THEN body ELSE NULL END FROM parcel_archived_receipts WHERE id=?', (occurrence,)).fetchone()
        if row is None or row[0] is None:
            raise ValueError('Receipt original missing or exceeds analysis budget')
        raw = row[0]
    if len(raw) > 8 * 1024 * 1024 or hashlib.sha256(raw).hexdigest().upper() != digest:
        raise ValueError('Receipt original differs from verified custody')
    return json.loads(raw)


def analyze_business(db, database, collection, dispositions, events, deadline):
    selected = runtime(collection['RuntimeId'])
    start, end = instant(collection['NormalStart']), instant(collection['NormalEnd'])
    inventory = collections.Counter()
    counts = collections.Counter()
    rcs = collections.Counter()
    missing = []
    rounds, commands, links = {}, {}, {}
    operations = set()
    seen_attempts = set()
    expected_attempts = set()
    rcs_ms, round_ms = [], []
    evidence_rows = 0
    output_bytes = 0
    unknown_attempt_time = False

    def checkpoint():
        if time.monotonic() > deadline:
            raise TimeoutError('Business evidence analysis time budget exceeded')
        if max(len(rounds), len(commands), len(operations), len(links)) > 1000000:
            raise ValueError('Business identity analysis budget exceeded')

    def gap(kind, **identity):
        if len(missing) >= 10000:
            raise ValueError('Too many missing business identities; preserve evidence for investigation')
        missing.append(dict(Kind=kind, **identity))

    with dispositions.open('x', encoding='utf-8') as inputs, events.open('x', encoding='utf-8') as journal:
        def event(kind, at, **identity):
            nonlocal output_bytes
            checkpoint()
            parsed = instant(at)
            inside = parsed is not None and start <= parsed <= end
            if inside:
                counts[kind] += 1
            line = json.dumps(dict(Event=kind, At=at, InWindow=inside,
                Scope='WindowEvent' if inside else 'CrossWindowContext' if parsed else 'TimeEvidenceMissing',
                RuntimeId=collection['RuntimeId'], **identity), ensure_ascii=False) + '\n'
            output_bytes += len(line.encode('utf-8'))
            if output_bytes > 2 * 1024**3:
                raise ValueError('Business evidence output capacity exceeded')
            journal.write(line)
            return inside

        for round_id, body, digest in db.execute('SELECT id,body,hash FROM sm_parcel_rounds'):
            checkpoint()
            if hashlib.sha256(body.encode()).hexdigest().upper() != digest:
                raise ValueError('Round original hash differs')
            round_ = json.loads(body)
            if round_['Id'] != round_id:
                raise ValueError('Round identity differs')
            if runtime(round_.get('ClockRuntime')) != selected:
                continue
            evidence_rows += 1
            rounds[round_id] = dict(OperationId=round_.get('OperationId'), CommandId=round_.get('CommandId'), Position=round_['PositionId'])
            if round_.get('OperationId'):
                operations.add(round_['OperationId'])
            event('RoundsStarted', round_['StartedAt'], RoundId=round_id, **rounds[round_id])
            if round_.get('EndedAt'):
                event('RoundsEnded', round_['EndedAt'], RoundId=round_id, EndReason=round_.get('EndReason'), **rounds[round_id])
            rcs['SnapshotRound:' + round_['State']] += 1

        for seq, at, detail in db.execute("SELECT seq,at,detail FROM parcel_audit WHERE event='OperatorBarcodeSubmitted' ORDER BY seq"):
            checkpoint()
            source = document(detail)
            command = source.get('Command') or {}
            if runtime(command.get('RuntimeId')) != selected:
                continue
            evidence_rows += 1
            command_id = command.get('CommandId')
            if not command_id or command_id in commands:
                raise ValueError('Manual command admission identity missing or duplicated')
            row = db.execute('SELECT receipt FROM parcel_commands WHERE id=?', (command_id,)).fetchone()
            if row is None:
                gap('ManualCommandReceiptMissing', CommandId=command_id)
                receipt = {}
            else:
                terminal = document(row[0])
                receipt = document(terminal.get('Reason')) if terminal.get('Status') == 'BarcodeResult' else {}
                rcs['SnapshotManualCommand:' + terminal.get('Status', 'Unknown')] += 1
            operation = receipt.get('OperationId')
            commands[command_id] = dict(OperationId=operation, RoundId=command.get('ExpectedRoundId'), Position=command.get('PositionId'))
            if operation:
                operations.add(operation)
            event('ManualCommandsSubmitted', at, CommandId=command_id, Kind=command.get('Kind'), AuditSequence=seq, **commands[command_id])

        query = 'SELECT id,position,state,received_at,completed_at,body,body_hash,detail FROM parcel_ingress WHERE replace(session,\'-\',\'\')=? ORDER BY received_at,id'
        for input_id, position, state, received, completed, body, digest, detail in db.execute(query, (selected,)):
            checkpoint()
            evidence_rows += 1
            if hashlib.sha256(body.encode()).hexdigest().upper() != digest:
                raise ValueError('Input original hash differs')
            observation = json.loads(body).get('Observation') or {}
            resolved = document(detail)
            linked = resolved.get('Input') or {}
            operation = resolved.get('OperationId') or linked.get('OperationId')
            round_id = resolved.get('RoundId')
            if operation:
                operations.add(operation)
            outcome = resolved.get('Disposition')
            if state == 'Processed' and not outcome:
                gap('InputDispositionMissing', InputId=input_id)
            identity = dict(InputId=input_id, Position=position, RoundId=round_id, OperationId=operation,
                            SourceEvent=observation.get('SourceEventId'), Device=observation.get('DeviceId'), Epoch=observation.get('Epoch'))
            inside = event('CameraInputsReceived', received, StateAtBackup=state, **identity)
            if completed:
                event('CameraInputsCompleted', completed, Outcome=outcome, **identity)
            if inside:
                inventory[position + ':' + str(outcome or state)] += 1
                line = json.dumps(dict(Id=input_id, Position=position, State=state, Outcome=outcome or state,
                    RoundId=round_id, OperationId=operation, SourceEvent=observation.get('SourceEventId'), Device=observation.get('DeviceId'),
                    Epoch=observation.get('Epoch'), Sequence=observation.get('Sequence'), Codes=observation.get('Codes'),
                    ReceivedAt=received, CompletedAt=completed, CompletionInWindow=completed is not None and start <= instant(completed) <= end,
                    StateScope='BackupSnapshot', OriginalSha256=digest), ensure_ascii=False) + '\n'
                output_bytes += len(line.encode('utf-8'))
                inputs.write(line)

        facts = {}
        for op_id, body, digest in db.execute('SELECT id,body,NULL FROM parcel_operations UNION ALL SELECT id,body,body_hash FROM parcel_archives'):
            checkpoint()
            op = json.loads(body)
            command_id = (op.get('OperatorInput') or {}).get('CommandId')
            round_id = op.get('RoundId')
            if op_id not in operations and round_id not in rounds and command_id not in commands:
                continue
            if op['Id'] != op_id or digest and hashlib.sha256(body.encode()).hexdigest().upper() != digest:
                raise ValueError('Operation original identity/hash differs')
            if op_id in facts:
                raise ValueError('Duplicate original operation identity')
            if round_id and round_id not in rounds:
                gap('OperationRoundRuntimeMissing', OperationId=op_id, RoundId=round_id)
            if command_id and command_id not in commands:
                gap('OperationManualCommandMissing', OperationId=op_id, CommandId=command_id)
            if command_id in commands and commands[command_id]['OperationId'] not in (None, op_id):
                raise ValueError('Manual command receipt links a different operation')
            stage = op.get('Wcs')
            facts[op_id] = True
            if not stage:
                gap('FrozenRcsRequestMissing', OperationId=op_id)
                continue
            request = stage['Request']
            if request['OperationId'] != op_id:
                raise ValueError('Frozen request operation identity differs')
            identity = dict(OperationId=op_id, RoundId=round_id, CommandId=command_id, RequestId=request['Id'],
                            ProtocolRequestId=(request.get('IntegrationRequest') or {}).get('RequestId'), Position=op.get('PositionId'))
            links[op_id] = {key: value for key, value in identity.items() if key != 'OperationId'}
            if event('RequestsPrepared', request.get('PreparedAt'), **identity):
                rcs['FrozenNewIntents'] += 1
            else:
                rcs['PriorOrLaterWindowOriginals'] += 1
            rcs['SnapshotPreRegisteredAttempts'] += stage['Attempts']
            if stage['Attempts']:
                expected_attempts.add((op_id, request['Id']))
            rcs['SnapshotOutcome:' + stage['State']] += 1
            for occurrence, receipt_state, raw_hash in db.execute("SELECT id,state,raw_hash FROM parcel_receipts WHERE operation=? AND stage='wcs-slot' AND request=?", (op_id, request['Id'])):
                rcs['SnapshotReceipt:' + receipt_state] += 1
                reply = None
                if raw_hash is not None:
                    reply = receipt_bytes(db, database, occurrence, raw_hash)
                    if reply['RequestId'] != request['Id'] or reply['PayloadHash'] != request['PayloadHash']:
                        raise ValueError('Receipt request/payload identity differs')
                row = db.execute("SELECT detail FROM parcel_audit WHERE owner=? AND event IN ('ReceiptRawDurable','CallCompletedNoReply') AND json_valid(detail) AND json_extract(detail,'$.OccurrenceId')=?", (op_id, occurrence)).fetchall()
                if len(row) > 1:
                    raise ValueError('Duplicate local call custody evidence')
                call = document(row[0][0]) if row else {}
                if not call or runtime(call.get('RuntimeId')) != selected:
                    gap('ReceiptCallRuntimeClockMissing', ReceiptId=occurrence, **identity)
                    continue
                if call.get('RequestId') != request['Id'] or call.get('RawSha256') != raw_hash:
                    raise ValueError('Local call custody identity/hash differs')
                if event('CallsCompleted', call.get('CompletedAt'), ReceiptId=occurrence, **identity):
                    rcs['LocalCompletedCallsInWindow'] += 1
                    if reply is not None and reply.get('HttpResponse') is not None:
                        rcs['HttpResponsesInWindow'] += 1
                    elapsed = call.get('ElapsedMs')
                    if isinstance(elapsed, (int, float)) and math.isfinite(elapsed) and elapsed >= 0:
                        rcs_ms.append(elapsed)
                    else:
                        gap('CallElapsedMissing', ReceiptId=occurrence, **identity)
                else:
                    rcs['ReceiptOutsideRuntimeWindow'] += 1

        for operation in operations - facts.keys():
            gap('OperationMissing', OperationId=operation)
        for seq, at, kind, owner, detail in db.execute("SELECT seq,at,event,owner,detail FROM parcel_audit WHERE event IN ('Dispatching','CommandCompleted','ReceiptRawDurable','ReceiptApplied','RoundEnded') ORDER BY seq"):
            checkpoint()
            evidence = document(detail)
            if kind == 'CommandCompleted':
                command_id = evidence.get('Id')
                if command_id in commands:
                    extra = evidence.get('Evidence') or {}
                    if runtime(extra.get('RuntimeId')) != selected:
                        gap('ManualCompletionRuntimeMissing', CommandId=command_id, AuditSequence=seq)
                        continue
                    event('ManualCommandsCompleted', at, CommandId=command_id, RoundId=extra.get('RoundId') or commands[command_id]['RoundId'],
                          OperationId=extra.get('OperationId') or commands[command_id]['OperationId'], AuditSequence=seq)
                continue
            if kind == 'RoundEnded':
                if evidence.get('RoundId') in rounds and runtime(evidence.get('RuntimeId')) == selected and start <= instant(at) <= end:
                    elapsed = evidence.get('RoundElapsedMs')
                    if isinstance(elapsed, (int, float)) and math.isfinite(elapsed) and elapsed >= 0:
                        round_ms.append(elapsed)
                continue
            identity = links.get(owner)
            if not identity:
                continue
            if kind == 'Dispatching':
                # Capture/upload have their own durable attempts on the same
                # operation; they are not missing or conflicting RCS attempts.
                if evidence.get('Stage') not in (None, 'wcs-slot'):
                    continue
                if runtime(evidence.get('RuntimeId')) != selected or evidence.get('RequestId') != identity['RequestId']:
                    unknown_attempt_time = True
                    gap('AttemptRuntimeClockMissing', OperationId=owner, AuditSequence=seq)
                    continue
                key = (owner, evidence['RequestId'])
                if key in seen_attempts:
                    raise ValueError('More than one first RCS attempt for one original request')
                seen_attempts.add(key)
                event('SendAttempts', at, OperationId=owner, AuditSequence=seq, **identity)
            elif runtime(evidence.get('RuntimeId')) == selected and evidence.get('RequestId') == identity['RequestId']:
                event('RawReceiptsDurable' if kind == 'ReceiptRawDurable' else 'ReceiptsApplied', at,
                      OperationId=owner, ReceiptId=evidence.get('OccurrenceId'), AuditSequence=seq, **identity)
        for operation, request in expected_attempts - seen_attempts:
            unknown_attempt_time = True
            gap('AttemptTimeUnconfirmed', OperationId=operation, RequestId=request)
        if not evidence_rows:
            raise ValueError('Backup has no camera, manual command or round evidence for this runtime')
    return dict(Dispositions=inventory, Rcs=rcs, LinkedRequestIds=links, Missing=missing,
                EventsInWindow=dict(counts), SendAttemptsInWindow=None if unknown_attempt_time else counts['SendAttempts'],
                RcsElapsedMs=distribution(rcs_ms), RoundElapsedMs=distribution(round_ms),
                Scope='Separate durable event clocks; outside-window facts are context; Snapshot fields describe backup time; no barcode joins')
