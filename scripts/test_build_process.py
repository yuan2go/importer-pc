"""Real process cancellation/failure ownership; no compiler mocks."""
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest


class BuildProcessTests(unittest.TestCase):
    def exercise(self, cancel):
        with tempfile.TemporaryDirectory(prefix='ipc-process-owner-') as temporary:
            root = Path(temporary)
            marker = root / 'descendant-writing'
            child = root / 'child.py'
            child.write_text('import pathlib,time\np=pathlib.Path(' + repr(str(marker)) + ')\nwhile True:\n p.write_text(str(time.monotonic()))\n time.sleep(.01)\n')
            compiler = root / 'compiler.py'
            compiler.write_text('import subprocess,sys,time\nsubprocess.Popen([sys.executable,' + repr(str(child)) + '])\ntime.sleep(' + ('60' if cancel else '.25') + ')\nsys.exit(2)\n')
            driver = root / 'driver.py'
            scripts = Path(__file__).resolve().parent
            driver.write_text('import sys,signal\nsys.path.insert(0,' + repr(str(scripts)) + ')\nfrom build_process import run\nsignal.signal(signal.SIGTERM,lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))\nrun([sys.executable,' + repr(str(compiler)) + '])\n')
            process = subprocess.Popen([sys.executable, str(driver)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            try:
                deadline = time.monotonic() + 5
                while not marker.exists() and time.monotonic() < deadline: time.sleep(.01)
                self.assertTrue(marker.exists(), 'Real descendant must run before exercising cancellation')
                if cancel: process.send_signal(signal.SIGTERM)
                self.assertNotEqual(process.wait(timeout=8), 0)
                before = marker.read_bytes()
                time.sleep(.1)
                self.assertEqual(marker.read_bytes(), before, 'Descendant must stop before owner returns')
            finally:
                if process.poll() is None:
                    process.kill(); process.wait()

    def test_cancellation_stops_real_descendants(self): self.exercise(True)
    def test_failure_stops_real_descendants(self): self.exercise(False)


if __name__ == '__main__': unittest.main()
