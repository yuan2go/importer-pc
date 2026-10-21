"""Compile twice through the host TEMP spelling; compare actual embedded-PDB DLLs."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from release_build import build_properties


class FrozenBuildTests(unittest.TestCase):
    def test_independent_workspaces_produce_identical_dlls(self):
        with tempfile.TemporaryDirectory(prefix='ipc-pathmap-regression-') as temporary:
            binaries = []
            for name in ('medium', 'installer'):
                workspace = Path(temporary) / name
                source = workspace / 'source-snapshot'
                source.mkdir(parents=True)
                artifacts = workspace / 'intermediates'
                (source / 'Sample.csproj').write_text(
                    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                    '<TargetFramework>net10.0</TargetFramework><Deterministic>true</Deterministic>'
                    '</PropertyGroup></Project>', encoding='utf-8')
                (source / 'Sample.cs').write_text(
                    'public class Sample { public static string Source('
                    '[System.Runtime.CompilerServices.CallerFilePath] string path = "") => path; }',
                    encoding='utf-8')
                result = subprocess.run(
                    ['dotnet', 'build', 'Sample.csproj', '-c', 'Release', '-m:1', '-nr:false',
                     *build_properties('a' * 40, source, artifacts),
                     '--artifacts-path', str(artifacts)], cwd=source,
                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
                self.assertEqual(result.returncode, 0, result.stdout)
                binaries.append((artifacts / 'bin/Sample/release/Sample.dll').read_bytes())
            self.assertEqual(binaries[0], binaries[1],
                             'Different temporary paths must not leak into embedded source/debug identity')


if __name__ == '__main__':
    unittest.main()
