import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen


class LocalRcs(unittest.TestCase):
    def test_assignment_dedup_is_server_side_and_protocol_failures_are_explicit(self):
        with tempfile.TemporaryDirectory(prefix='ipc-synthetic-rcs-') as temp:
            root = Path(temp)
            process = subprocess.Popen([sys.executable, str(Path(__file__).with_name('rcs-local-simulator.py')),
                                        '--port', '0', '--data-root', str(root)], stdout=subprocess.DEVNULL)
            try:
                for _ in range(100):
                    if (root / 'endpoint.json').exists():
                        break
                    if process.poll() is not None:
                        self.fail('Simulator exited before publishing endpoint')
                    time.sleep(.03)
                endpoint = json.loads((root / 'endpoint.json').read_text())['endpoint']
                self.assertTrue(endpoint.startswith('http://127.0.0.1:'))

                def send(identity, code='SYNTHETIC-CODE-0001'):
                    data = {'requestId': identity, 'data': [{'index': 0, 'command': 'sorter.task.dest_request',
                                                            'params': {'loadCode': code, 'quantity': 1}}]}
                    with urlopen(Request(endpoint, data=json.dumps(data).encode(), headers={'Content-Type': 'application/json'}), timeout=2) as response:
                        self.assertEqual(response.headers['X-Ipc-Simulator'], 'LOCAL_ONLY_NOT_REAL_RCS')
                        return json.load(response)

                first, second = send(1), send(2)
                self.assertEqual(first['result'][0]['data'], second['result'][0]['data'])
                self.assertNotEqual(first['requestId'], second['requestId'])
                with self.assertRaises(HTTPError) as conflict:
                    send(1, 'SYNTHETIC-DIFFERENT-0002')
                self.assertEqual(conflict.exception.code, 409)
                conflict.exception.close()
                for mode in ('wrong-id', 'malformed', 'reject'):
                    (root / 'mode.json').write_text(json.dumps({'mode': mode}))
                    result = send(3 + ('wrong-id', 'malformed', 'reject').index(mode))
                    if mode == 'wrong-id':
                        self.assertEqual(result['requestId'], 4)
                    elif mode == 'malformed':
                        self.assertNotIn('result', result)
                    else:
                        self.assertFalse(result['result'][0]['success'])
                with urlopen(endpoint.rsplit('/rcs/', 1)[0] + '/status', timeout=2) as response:
                    status = json.load(response)
                self.assertEqual(status, dict(simulated=True, requests=5, uniqueAssignments=1))
            finally:
                process.terminate()
                process.wait(timeout=5)


if __name__ == '__main__':
    unittest.main()
