"""Real process cancellation/failure ownership; no compiler mocks."""
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
import threading
from unittest.mock import patch
import build_process


class BuildProcessTests(unittest.TestCase):
    def exercise(self, cancel):
        with tempfile.TemporaryDirectory(prefix='ipc-process-owner-') as temporary:
            root = Path(temporary)
            marker = root / 'descendant-writing'
            fail = root / 'fail-compiler'
            child = root / 'child.py'
            child.write_text('import pathlib,time\np=pathlib.Path(' + repr(str(marker)) + ')\nwhile True:\n p.write_text(str(time.monotonic()))\n time.sleep(.01)\n')
            compiler = root / 'compiler.py'
            compiler.write_text('import subprocess,sys,time,pathlib\nsubprocess.Popen([sys.executable,' + repr(str(child)) + '])\n'
                                'fail=pathlib.Path(' + repr(str(fail)) + ')\ndeadline=time.monotonic()+15\n'
                                'while not fail.exists():\n'
                                ' if time.monotonic()>=deadline: raise TimeoutError("Failure trigger not received")\n'
                                ' time.sleep(.01)\nsys.exit(2)\n')
            driver = root / 'driver.py'
            scripts = Path(__file__).resolve().parent
            driver.write_text('import sys,signal\nsys.path.insert(0,' + repr(str(scripts)) + ')\nfrom build_process import run\nsignal.signal(signal.SIGBREAK if sys.platform=="win32" else signal.SIGTERM,lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))\nrun([sys.executable,' + repr(str(compiler)) + '])\n')
            process = subprocess.Popen([sys.executable, str(driver)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                       creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == 'nt' else 0)
            try:
                deadline = time.monotonic() + 5
                while not marker.exists() and time.monotonic() < deadline: time.sleep(.01)
                self.assertTrue(marker.exists(), 'Real descendant must run before exercising cancellation')
                # Windows SIGTERM is TerminateProcess and bypasses Python finally.
                # Exercise an actual handled console cancellation instead.
                if cancel: process.send_signal(signal.CTRL_BREAK_EVENT if os.name == 'nt' else signal.SIGTERM)
                else: fail.touch()  # Fail only after the actual descendant has begun writing.
                self.assertNotEqual(process.wait(timeout=8), 0)
                before = marker.read_bytes()
                time.sleep(.1)
                self.assertEqual(marker.read_bytes(), before, 'Descendant must stop before owner returns')
            finally:
                if process.poll() is None:
                    process.kill(); process.wait()

    def test_cancellation_stops_real_descendants(self): self.exercise(True)
    def test_failure_stops_real_descendants(self): self.exercise(False)

    @unittest.skipUnless(os.name == 'nt', 'Windows suspended-process ownership boundary')
    def test_failed_job_assignment_terminates_the_actual_suspended_process(self):
        launched = threading.Event()
        owned = []
        errors = []
        original = OSError('synthetic job assignment failure')
        real_popen = subprocess.Popen

        def launch(*args, **kwargs):
            child = real_popen(*args, **kwargs)
            owned.append(child); launched.set()
            return child

        def run():
            try: build_process.run([sys.executable, '-c', 'import time; time.sleep(30)'])
            except BaseException as error: errors.append(error)

        with patch.object(build_process.WindowsJob, 'assign', side_effect=original), patch.object(build_process.subprocess, 'Popen', side_effect=launch):
            worker = threading.Thread(target=run)
            worker.start()
            try:
                self.assertTrue(launched.wait(3))
                worker.join(7)
                self.assertFalse(worker.is_alive(), 'assignment failure must not wait indefinitely on an unowned suspended child')
                self.assertIs(errors[0], original)
                self.assertIsNotNone(owned[0].poll(), 'the actual suspended child must be terminated before failure returns')
            finally:
                for child in owned:
                    if child.poll() is None: child.kill()
                    child.wait(timeout=5)
                worker.join(5)


if __name__ == '__main__': unittest.main()
