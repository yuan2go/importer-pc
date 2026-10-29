"""Current launcher dispatch with real native, side-effect-free child executables.

The real checksum/argument/error paths run against a synthetic release. Only the
OS gate is excluded for portable hosts. This does not certify Windows identity/ACL.
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
        cls.native = tempfile.TemporaryDirectory(prefix='ipc-dispatch-native-')
        project = Path(cls.native.name)
        (project/'fixture.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><AssemblyName>Ipc.Assembly</AssemblyName><UseAppHost>true</UseAppHost></PropertyGroup></Project>')
        (project/'Program.cs').write_text(r'''using System;
using System.IO;
using System.Linq;
using System.Text.Json;
var name = Path.GetFileName(Environment.ProcessPath);
File.AppendAllText(Environment.GetEnvironmentVariable("IPC_TRACE"), JsonSerializer.Serialize(new[] { name }.Concat(args)) + "\n");
if (args.SequenceEqual(new[] { "--action", "installed-location" })) {
    Console.WriteLine(JsonSerializer.Serialize(new { DataRoot = Environment.GetEnvironmentVariable("IPC_INSTALLED_ROOT"), ConfigurationDirectory = Environment.GetEnvironmentVariable("IPC_CONFIG") }));
    return 0;
}
if (Environment.GetEnvironmentVariable("IPC_DENY") == "1" &&
    (args.Contains("runtime-check") || args.Contains("runtime-prepare") || name == "Ipc.Desktop.exe" && !args.Contains("--setup"))) return 42;
return 0;
''')
        cls.output = project/'out'
        subprocess.run(['dotnet', 'build', str(project/'fixture.csproj'), '-c', 'Release', '-o', str(cls.output), '-m:1', '-nr:false'], check=True, capture_output=True, timeout=120)

    @classmethod
    def tearDownClass(cls):
        cls.native.cleanup()

    def launch(self, action, deny=False, default_data=False):
        cls_output = self.output
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
            for directory in [release/'app', release/'desktop']:
                for source_file in cls_output.iterdir():
                    if source_file.is_file(): shutil.copy2(source_file, directory/source_file.name)
            executable = 'Ipc.Assembly.exe' if os.name == 'nt' else 'Ipc.Assembly'
            for name in ['app/Ipc.Assembly.exe', 'desktop/Ipc.Desktop.exe']:
                target = release/name
                if target != target.parent/executable: shutil.copy2(cls_output/executable, target)
            (release/'SHA256SUMS').write_text(''.join(
                hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.relative_to(release).as_posix()+'\n'
                for p in sorted(release.rglob('*')) if p.is_file()))
            trace = root/'trace.jsonl'
            data = root/'data'
            data.mkdir()
            (data/'original.db').write_bytes(b'original synthetic database bytes')
            before = {p.relative_to(data).as_posix(): p.read_bytes() for p in data.rglob('*') if p.is_file()}
            arguments = [PWSH, '-NoProfile', '-File', str(release/'application.ps1'), '-Action', action,
                         '-Database', str(data/'original.db'),
                         '-File', str(root/'input.json'),
                         '-Destination', str(root/'output')]
            if not default_data:
                arguments += ['-DataRoot', str(data)]
            result = subprocess.run(arguments,
                                    env=dict(os.environ, IPC_TRACE=str(trace), IPC_DENY='1' if deny else '0',
                                             IPC_INSTALLED_ROOT=str(Path(tempfile.gettempdir())/'ipc-dispatch-default'), IPC_CONFIG=str(release/'desktop/config')),
                                    capture_output=True, text=True, timeout=30)
            calls = [json.loads(line) for line in trace.read_text().splitlines()] if trace.exists() else []
            after = {p.relative_to(data).as_posix(): p.read_bytes() for p in data.rglob('*') if p.is_file()}
            self.assertEqual(before, after, 'Synthetic original data must remain unchanged')
            return result, calls

    def test_runtime_dispatch_and_denial_preserve_original_data(self):
        # The native fixture only traces arguments/exit status. Product identity denial
        # before configuration or device access is exercised by RuntimeIdentityBoundary.
        for action in ['Run', 'Console', 'Diagnose']:
            for deny in [False, True]:
                with self.subTest(action=action, deny=deny):
                    result, calls = self.launch(action, deny=deny)
                    self.assertEqual(result.returncode == 0, not deny, result.stderr)
                    self.assertEqual(calls[0][1:], ['--action', 'installed-location'])
                    work = calls[1:]
                    if action == 'Run':
                        self.assertEqual(len(work), 1)
                        self.assertEqual(work[0][0], 'Ipc.Desktop.exe')
                        self.assertIn('--config', work[0]); self.assertIn('--data-root', work[0])
                        self.assertEqual(work[0][-2:], ['--ui', 'supply-station'])
                    else:
                        preflight = 'runtime-prepare' if action == 'Console' else 'runtime-check'
                        self.assertEqual(work[0][1:], ['--action', preflight])
                        if deny:
                            self.assertEqual(len(work), 1, 'denial must prevent all business/deployment dispatch')
                        elif action == 'Console':
                            self.assertEqual(work[-1][1], '--config'); self.assertIn('--data-root', work[-1])
                        else:
                            self.assertEqual([c[2] for c in work[1:]], ['business-identity', 'deployment-check'])

    def test_runtime_dispatch_uses_registered_root_without_creating_it(self):
        expected = str(Path(tempfile.gettempdir())/'ipc-dispatch-default')
        self.assertFalse(Path(expected).exists())
        for action in ['Run', 'Console']:
            result, calls = self.launch(action, default_data=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            launch = calls[-1]
            self.assertEqual(launch[launch.index('--data-root') + 1], expected)
        self.assertFalse(Path(expected).exists())

    def test_install_and_offline_actions_use_their_own_authority_entry(self):
        for action, operation in [('Inspect', 'maintenance-inspect'), ('Backup', 'backup-offline'),
                                  ('PlanRestore', 'recovery-plan'), ('Restore', 'restore'), ('Export', 'diagnostic-export'), ('Install', None)]:
            with self.subTest(action=action):
                result, calls = self.launch(action, deny=True)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertFalse(any('runtime-prepare' in c or 'runtime-check' in c for c in calls))
                if operation is None:
                    self.assertEqual(calls[-1][0:2], ['Ipc.Desktop.exe', '--setup'])
                else:
                    self.assertEqual(calls[-1][1:3], ['--action', operation])

    def test_supply_offline_maintenance_keeps_its_separate_authority_entry(self):
        # Offline maintenance branches return before ProductHost/StartAsync and retain
        # their own authority checks; a daily-runtime gate must not be moved above them.
        program = (ROOT/'src/Ipc.Product/Program.cs').read_text()
        maintenance = program.index('if (action is "backup" or "preflight" or "restore")')
        host = program.index('var host = ProductHost.Build(')
        self.assertNotIn('RequireRuntimeIdentity', program[:host])
        self.assertLess(program.index('MachineRuntimePolicy.ResolveLeaseRoot(null);', maintenance),
                        program.index('Directory.CreateDirectory(directory)', maintenance))
        self.assertIn('return;', program[maintenance:host])

if __name__ == '__main__':
    unittest.main()
