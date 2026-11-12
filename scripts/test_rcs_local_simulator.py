import concurrent.futures
from contextlib import closing
import hashlib
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import time
import unittest
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


class LocalRcs(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='ipc-synthetic-rcs-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.identity = 0
        self.process = None
        self.addCleanup(self.stop)

    def start(self):
        self.process = subprocess.Popen([sys.executable, str(Path(__file__).with_name('rcs-local-simulator.py')),
                                         '--port', '0', '--data-root', str(self.root)],
                                        stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        for _ in range(100):
            if self.process.poll() is not None:
                self.fail(self.process.stderr.read().decode())
            try:
                self.endpoint = json.loads((self.root / 'endpoint.json').read_text())['endpoint']
                self.status()
                return
            except (FileNotFoundError, ValueError, URLError):
                time.sleep(.03)
        self.fail('Simulator did not become ready')

    def stop(self):
        if self.process is not None:
            self.process.terminate()
            self.process.wait(timeout=5)
            self.process.stderr.close()
            self.process = None

    def status(self):
        with urlopen(self.endpoint.rsplit('/rcs/', 1)[0] + '/status', timeout=2) as response:
            return json.load(response)

    def post(self, items, identity=None):
        if identity is None:
            self.identity += 1
            identity = self.identity
        body = {'requestId': identity, 'data': items}
        with urlopen(Request(self.endpoint, data=json.dumps(body).encode(),
                             headers={'Content-Type': 'application/json'}), timeout=2) as response:
            self.assertEqual(response.headers['X-Ipc-Simulator'], 'LOCAL_ONLY_NOT_REAL_RCS')
            return json.load(response)

    @staticmethod
    def destination(bcr, barcode='SYNTHETIC-SAME-CODE', index=0):
        return {'index': index, 'command': 'sorter.task.dest_request',
                'params': {'stationCode': 'SIM-STATION', 'bcrCode': bcr, 'loadCode': barcode, 'quantity': 1}}

    def heartbeat(self, codes=('LEFT', 'RIGHT')):
        result = self.post([{'index': index, 'command': 'sorter.task.station_heartbeat',
                             'params': {'stationCode': 'SIM-STATION', 'bcrCode': code}}
                            for index, code in enumerate(codes)])
        return {item['data']['bcrCode']: item['data']['agvNo'] for item in result['result']}

    def test_independent_arrivals_same_code_and_original_request_replay(self):
        self.start()
        first = self.heartbeat()
        self.assertEqual(self.heartbeat(), first)
        result = self.post([self.destination('LEFT')])
        after_left = self.heartbeat()
        self.assertNotEqual(after_left['LEFT'], first['LEFT'])
        self.assertEqual(after_left['RIGHT'], first['RIGHT'])
        self.assertEqual(self.post([self.destination('LEFT')], result['requestId']), result)
        self.assertEqual(self.heartbeat(), after_left)
        second = self.post([self.destination('LEFT')])
        self.assertNotEqual(second['result'][0]['data']['taskId'], result['result'][0]['data']['taskId'])
        after_second = self.heartbeat()
        self.assertNotEqual(after_second['LEFT'], after_left['LEFT'])
        self.assertEqual(after_second['RIGHT'], first['RIGHT'])
        self.assertEqual(self.heartbeat(('RIGHT',)), {'RIGHT': first['RIGHT']})
        self.assertEqual(self.post([])['result'], [])
        with self.assertRaises(HTTPError) as conflict:
            self.post([self.destination('LEFT', 'CHANGED')], result['requestId'])
        self.assertEqual(conflict.exception.code, 409)
        conflict.exception.close()
        self.assertEqual(self.heartbeat(), after_second)

    def test_parallel_lanes_restart_and_invalid_batch_keep_binding(self):
        self.start()
        initial = self.heartbeat()
        commands = [(100, self.destination('LEFT')), (101, self.destination('RIGHT'))]
        with concurrent.futures.ThreadPoolExecutor(2) as pool:
            replies = list(pool.map(lambda pair: self.post([pair[1]], pair[0]), commands))
        arrived = self.heartbeat()
        self.assertTrue(all(arrived[code] != initial[code] for code in initial))
        with closing(sqlite3.connect(self.root / 'simulator.sqlite')) as db:
            bindings = db.execute('SELECT bcr,vehicle,next_vehicle FROM assignments ORDER BY bcr').fetchall()
        self.assertEqual(bindings, [(code, initial[code], arrived[code]) for code in sorted(initial)])
        self.stop()
        self.start()
        self.assertEqual(self.heartbeat(), arrived)
        self.assertEqual(self.post([commands[0][1]], 100), replies[0])
        with self.assertRaises(HTTPError) as invalid:
            self.post([self.destination('LEFT'), {'index': 1, 'command': 'invalid', 'params': {}}])
        self.assertEqual(invalid.exception.code, 400)
        invalid.exception.close()
        self.assertEqual(self.heartbeat(), arrived)
        self.assertEqual(self.status()['uniqueAssignments'], 2)

    def test_rejection_holds_vehicle_and_protocol_failures_are_explicit(self):
        self.start()
        initial = self.heartbeat()
        (self.root / 'mode.json').write_text(json.dumps({'mode': 'reject'}))
        rejected = self.post([self.destination('LEFT')])
        self.assertFalse(rejected['result'][0]['success'])
        self.assertIsNone(rejected['result'][0]['data'])
        self.assertEqual(self.heartbeat(), initial)
        for mode in ('wrong-id', 'malformed'):
            (self.root / 'mode.json').write_text(json.dumps({'mode': mode}))
            result = self.post([self.destination('LEFT')])
            if mode == 'wrong-id':
                self.assertEqual(result['requestId'], self.identity + 1)
            else:
                self.assertNotIn('result', result)
        (self.root / 'mode.json').write_text(json.dumps({'mode': 'assigned'}))
        final = self.heartbeat()
        self.assertNotEqual(final['LEFT'], initial['LEFT'])
        self.assertEqual(final['RIGHT'], initial['RIGHT'])
        self.assertEqual(self.status()['uniqueAssignments'], 2)

    def test_old_ledger_is_refused_without_modification(self):
        database = self.root / 'simulator.sqlite'
        with closing(sqlite3.connect(database)) as db:
            db.execute('CREATE TABLE requests (body TEXT)')
            db.execute('INSERT INTO requests VALUES(?)', ('preserve original',))
            db.commit()
        before = hashlib.sha256(database.read_bytes()).digest()
        process = subprocess.run([sys.executable, str(Path(__file__).with_name('rcs-local-simulator.py')),
                                  '--port', '0', '--data-root', str(self.root)], capture_output=True, timeout=5)
        self.assertNotEqual(process.returncode, 0)
        self.assertIn(b'Unsupported simulator ledger', process.stderr)
        self.assertEqual(hashlib.sha256(database.read_bytes()).digest(), before)


if __name__ == '__main__':
    unittest.main()
