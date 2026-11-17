"""Exercise the real launcher with isolated, controlled engine executables."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest


class LauncherTests(unittest.TestCase):
    def setUp(self):
        git = shutil.which('git')
        self.bash = Path(git).parents[1] / 'bin/bash.exe' if os.name == 'nt' and git else Path('/bin/sh')
        if not self.bash.exists():
            self.skipTest('No installed POSIX shell for the shell launcher')
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        scripts = self.root / 'skill/scripts'
        scripts.mkdir(parents=True)
        self.launcher = scripts / 'impeccable'
        shutil.copyfile(Path(__file__).parents[1] / 'impeccable', self.launcher)
        (scripts / 'VERSION').write_text('fixture-version\n')
        self.env = dict(os.environ, HOME=str(self.root / 'home'), IMPECCABLE_HOME=str(self.root / 'cache'))
        self.env.pop('IMPECCABLE_BIN', None)
        self.env.pop('IMPECCABLE_LAUNCHER_PROBE', None)
        self.env.pop('IMPECCABLE_OVERRIDE_EXECUTED', None)
        suffix = '.exe' if os.name == 'nt' else ''
        self.home = self.root / ('home/.impeccable/bin/impeccable' + suffix)
        self.engine(self.root / ('cache/bin/fixture-version/impeccable' + suffix), 'echo CACHE')

    def engine(self, path, body):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('#!/bin/sh\n' + body + '\n', encoding='utf-8', newline='\n')
        path.chmod(0o700)

    def run_launcher(self):
        return subprocess.run([str(self.bash), str(self.launcher), 'context'], env=self.env,
                              capture_output=True, text=True, timeout=8)

    def test_failed_probe_cannot_impersonate_a_valid_engine(self):
        self.engine(self.home, 'if [ "$1" = engine-probe ]; then echo "impeccable-engine 1.2.3"; exit 7; fi\necho INVALID')
        result = self.run_launcher()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('CACHE', result.stdout.strip())

    def test_partial_handshake_is_rejected(self):
        self.engine(self.home, 'echo "impeccable-engine garbage"')
        self.assertEqual('CACHE', self.run_launcher().stdout.strip())

    def test_multiline_handshake_is_rejected(self):
        self.engine(self.home, 'printf "impeccable-engine 1.2.3\\nINJECTED\\n"')
        self.assertEqual('CACHE', self.run_launcher().stdout.strip())

    def test_hung_probe_is_bounded(self):
        self.engine(self.home, 'sleep 30')
        started = time.monotonic()
        result = self.run_launcher()
        self.assertLess(time.monotonic() - started, 5)
        self.assertEqual('CACHE', result.stdout.strip())

    def test_recursive_override_fails_without_replacing_itself_forever(self):
        self.env['IMPECCABLE_BIN'] = str(self.launcher)
        self.launcher.chmod(0o700)
        result = self.run_launcher()
        self.assertEqual(126, result.returncode)
        self.assertIn('recursively selects the launcher', result.stderr)


if __name__ == '__main__':
    unittest.main()
