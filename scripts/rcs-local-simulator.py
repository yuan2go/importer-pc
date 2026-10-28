"""Loopback-only RCS protocol simulator, explicitly requested for local validation.

No credentials, customer identifiers or private ledger belong in Git. A simulated
assignment is not a physical completion or acceptance by a real RCS deployment.
"""
import argparse
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import sqlite3
import threading
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=18848)
    parser.add_argument('--data-root', type=Path, required=True)
    args = parser.parse_args()
    root = args.data_root.resolve()
    if not args.data_root.is_absolute():
        raise ValueError('An explicit absolute private data root is required')
    root.mkdir(parents=True, exist_ok=True)
    db = sqlite3.connect(root / 'simulator.sqlite', check_same_thread=False)
    db.executescript('''CREATE TABLE IF NOT EXISTS requests (
        id INTEGER PRIMARY KEY AUTOINCREMENT, request_id INTEGER, received TEXT,
        sha256 TEXT, body TEXT, response TEXT, mode TEXT);
        CREATE TABLE IF NOT EXISTS assignments (
        barcode TEXT PRIMARY KEY, task TEXT NOT NULL, chute TEXT NOT NULL);''')
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = 'HTTP/1.1'

        def log_message(self, *unused):
            pass  # The private ledger is authoritative; no raw codes on stdout.

        def send(self, status, value):
            data = json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode()
            self.send_response(status)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.send_header('X-Ipc-Simulator', 'LOCAL_ONLY_NOT_REAL_RCS')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            try:
                self.wfile.write(data)
            except (BrokenPipeError, ConnectionResetError):
                pass  # Caller timed out; original request/response stays in ledger.

        def do_GET(self):
            if self.path != '/status':
                self.send(404, {'error': 'not-found'})
                return
            with lock:
                requests = db.execute('SELECT count(*) FROM requests').fetchone()[0]
                assignments = db.execute('SELECT count(*) FROM assignments').fetchone()[0]
            self.send(200, dict(simulated=True, requests=requests, uniqueAssignments=assignments))

        def do_POST(self):
            if self.path != '/rcs/sorter/api':
                self.send(404, {'error': 'not-found', 'simulated': True})
                return
            try:
                length = int(self.headers.get('Content-Length', '0'))
                if not 0 < length <= 65536:
                    self.send(413, {'error': 'bounded-body-required'})
                    return
                raw = self.rfile.read(length)
                request = json.loads(raw)
                request_id = request['requestId']
                if not isinstance(request_id, int) or isinstance(request_id, bool):
                    raise ValueError('numeric requestId required')
                mode_file = root / 'mode.json'
                mode = json.loads(mode_file.read_text()).get('mode', 'assigned') if mode_file.exists() else 'assigned'
                if mode not in ('assigned', 'timeout', 'reject', 'wrong-id', 'malformed'):
                    raise ValueError('unsupported simulation mode')
                response = dict(requestId=request_id + (1 if mode == 'wrong-id' else 0), result=[])
                with lock:
                    prior = db.execute('SELECT sha256 FROM requests WHERE request_id=? LIMIT 1', (request_id,)).fetchone()
                    digest = hashlib.sha256(raw).hexdigest()
                    if prior and prior[0] != digest:
                        self.send(409, {'error': 'request-id-payload-conflict'})
                        return
                    for item in request['data']:
                        command = item['command']
                        params = item['params']
                        data = None
                        if command == 'sorter.task.dest_request':
                            barcode = params['loadCode']
                            if not isinstance(barcode, str) or not barcode or len(barcode) > 4096 or params['quantity'] != 1:
                                raise ValueError('invalid barcode/quantity')
                            assigned = db.execute('SELECT task,chute FROM assignments WHERE barcode=?', (barcode,)).fetchone()
                            if assigned is None and mode != 'reject':
                                count = db.execute('SELECT count(*) FROM assignments').fetchone()[0] + 1
                                assigned = (f'SIM-TASK-{count:06d}', f'SIM-CHUTE-{(count - 1) % 6 + 1:02d}')
                                db.execute('INSERT INTO assignments VALUES(?,?,?)', (barcode, *assigned))
                            if assigned:
                                data = dict(taskId=assigned[0], chuteCode=assigned[1], skuId='SIM-SKU',
                                            length=None, width=None, height=None, weight=None)
                        elif command != 'sorter.task.station_heartbeat':
                            raise ValueError('unsupported command')
                        rejected = mode == 'reject' and command == 'sorter.task.dest_request'
                        response['result'].append(dict(index=item['index'], command=command, success=not rejected,
                            code='SIM_REJECT' if rejected else None, message='SIMULATED', args=None, data=data))
                    db.execute('INSERT INTO requests(request_id,received,sha256,body,response,mode) VALUES(?,datetime("now"),?,?,?,?)',
                               (request_id, digest, raw.decode('utf-8'), json.dumps(response, ensure_ascii=False), mode))
                    db.commit()
                if mode == 'timeout':
                    time.sleep(8)
                self.send(200, response if mode != 'malformed' else {'simulated': 'invalid-response-shape'})
            except (ValueError, KeyError, TypeError, json.JSONDecodeError):
                self.send(400, {'error': 'invalid-envelope', 'simulated': True})

    server = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    (root / 'endpoint.json').write_text(json.dumps(dict(endpoint=f'http://127.0.0.1:{server.server_port}/rcs/sorter/api', simulated=True)))
    print(f'LOCAL RCS SIMULATOR http://127.0.0.1:{server.server_port}/; private ledger enabled', flush=True)
    try:
        server.serve_forever()
    finally:
        server.server_close()
        db.close()


if __name__ == '__main__':
    main()
