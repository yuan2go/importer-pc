"""Portable launch-order regression; child executables are synthetic, not Windows evidence.

IPC_TEST_PWSH must name a PowerShell 7 executable. Only the Windows OS/build gate
is removed from a temporary script copy. The real checksum and action dispatch
code runs against a synthetic release; no Windows identity/ACL result is inferred.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
PWSH = os.environ.get('IPC_TEST_PWSH') or shutil.which('pwsh')


class RuntimeIdentityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not PWSH:
            raise RuntimeError('PowerShell 7 required: set IPC_TEST_PWSH; tests must not silently skip')

    def launch(self, action, deny=False):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            release = root/'release'
            (release/'app').mkdir(parents=True)
            (release/'desktop/config').mkdir(parents=True)
            source = (ROOT/'scripts/application.ps1').read_text()
            source, count = re.subn(r"(?m)^if \(-not \$IsWindows[^\n]*\) \{\n.*?\n\}",
                                    '# Portable dispatch harness: Windows gate excluded.', source, count=1, flags=re.S)
            self.assertEqual(count, 1, 'Harness must remove exactly the platform gate')
            (release/'application.ps1').write_text(source)
            (release/'ASSEMBLY.json').write_text('{"ApplicationId":"sportmaster","SourceCommit":"synthetic"}')
            for name in ['app/application-release.json', 'desktop/application-release.json', 'desktop/config/business.json']:
                (release/name).write_text('{}')
            child = '''#!/usr/bin/env python3
import json, os, pathlib, sys
with open(os.environ['IPC_TRACE'], 'a') as stream:
    stream.write(json.dumps([pathlib.Path(sys.argv[0]).name, *sys.argv[1:]])+'\\n')
if sys.argv[1:] == ['--action', 'runtime-check'] and os.environ['IPC_DENY'] == '1':
    print('Synthetic runtime identity denied', file=sys.stderr)
    sys.exit(42)
'''
            for name in ['app/Ipc.Assembly.exe', 'desktop/Ipc.Desktop.exe']:
                path = release/name
                path.write_text(child)
                path.chmod(0o755)
            (release/'SHA256SUMS').write_text(''.join(
                hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.relative_to(release).as_posix()+'\n'
                for p in sorted(release.rglob('*')) if p.is_file()))
            trace = root/'trace.jsonl'
            data = root/'data'
            data.mkdir()
            (data/'original.db').write_bytes(b'original synthetic database bytes')
            before = {p.relative_to(data).as_posix(): p.read_bytes() for p in data.rglob('*') if p.is_file()}
            result = subprocess.run([PWSH, '-NoProfile', '-File', str(release/'application.ps1'),
                                     '-Action', action, '-DataRoot', str(data),
                                     '-Database', str(data/'original.db')],
                                    env=dict(os.environ, IPC_TRACE=str(trace), IPC_DENY='1' if deny else '0'),
                                    capture_output=True, text=True, timeout=30)
            calls = [json.loads(line) for line in trace.read_text().splitlines()] if trace.exists() else []
            after = {p.relative_to(data).as_posix(): p.read_bytes() for p in data.rglob('*') if p.is_file()}
            self.assertEqual(before, after, 'Synthetic original data must remain unchanged')
            return result, calls

    def test_denial_stops_every_runtime_entry_before_other_children(self):
        for action in ['Run', 'Console', 'Diagnose']:
            with self.subTest(action=action):
                result, calls = self.launch(action, deny=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(calls, [['Ipc.Assembly.exe', '--action', 'runtime-check']], result.stderr)

    def test_allowed_dispatch_preserves_existing_launch_arguments(self):
        for action in ['Run', 'Console', 'Diagnose']:
            with self.subTest(action=action):
                result, calls = self.launch(action)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(calls[0], ['Ipc.Assembly.exe', '--action', 'runtime-check'])
                if action == 'Run':
                    self.assertEqual(calls[1][1:3], ['--action', 'deployment-check'])
                    self.assertEqual(calls[2][0], 'Ipc.Desktop.exe')
                    self.assertEqual(calls[2][-2:], ['--ui', 'classic'])
                elif action == 'Console':
                    self.assertEqual(calls[1][1], '--config')
                    self.assertIn('--data-root', calls[1])
                else:
                    self.assertEqual(calls[1][1:], ['--action', 'business-identity'])
                    self.assertEqual(calls[2][1:3], ['--action', 'deployment-check'])

    def test_maintenance_dispatch_does_not_require_runtime_identity(self):
        result, calls = self.launch('Inspect', deny=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][1:3], ['--action', 'maintenance-inspect'])

    def test_direct_runtime_guard_precedes_configuration_and_storage(self):
        source = (ROOT/'src/Ipc.Application.ParcelFlow/ParcelRuntime.cs').read_text()
        start = source.index('public async Task StartAsync(')
        guard = source.index('MachineRuntimePolicy.RequireRuntimeIdentity();', start)
        self.assertLess(guard, source.index('started = true;', start))
        self.assertLess(guard, source.index('File.ReadAllText(', start))
        self.assertLess(guard, source.index('lease = new(', start))
        assembly = (ROOT/'src/Ipc.Assembly/ParcelAssembly.cs').read_text()
        self.assertLess(assembly.index('MachineRuntimePolicy.RequireRuntimeIdentity();'),
                        assembly.index('ParcelCapabilityFactory.RegisterContracts();'))
        # Shortcut and CMD inherit the checked Run path; neither invokes desktop directly.
        script = (ROOT/'scripts/application.ps1').read_text().split('    Shortcut {', 1)[1].split('    Inspect {', 1)[0]
        self.assertIn('-Action Run -DataRoot', script)
        self.assertNotIn('Ipc.Desktop.exe', script)


if __name__ == '__main__':
    unittest.main()
