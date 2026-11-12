"""CI selection must keep build inputs and cannot turn incomplete work green."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

import industrial_ci as ci


class IndustrialCiTests(unittest.TestCase):
    def test_prose_skips_compilation_but_embedded_guides_and_inputs_do_not(self):
        prose = ci.plan(['AGENTS.md', 'README.md', 'docs/runtime/industrial-ci-policy-01.md'])
        self.assertFalse(prose['core'])
        self.assertEqual([], prose['desktops'])
        guides = ci.plan(['docs/operator/quick-guide.md', 'docs/design/sm-parcel-rounds.md'])
        self.assertFalse(guides['core'])
        self.assertEqual(['Sportmaster'], guides['desktops'])
        for path in ['docs/ui/tokens/tokens.json', 'src/Ipc.Application.ParcelFlow/ParcelRuntime.cs',
                     'config/sportmaster/business.json', 'tests/Ipc.Core.Tests/CoreRegression.cs',
                     '.github/workflows/industrial-core.yml', 'scripts/release_documents.py',
                     'Directory.Build.props', 'new-build-input']:
            with self.subTest(path=path):
                selected = ci.plan(['docs/operator/quick-guide.md', path])
                self.assertTrue(selected['core'])
                self.assertEqual(ci.DESKTOPS, selected['desktops'])

    def test_manual_specialties_are_independent(self):
        for scope, expected in [('regression', (True, False, False)),
                                ('branches', (False, True, False)),
                                ('print', (False, False, True)),
                                ('all', (True, True, True))]:
            with self.subTest(scope=scope):
                selected = ci.plan(None, scope)
                self.assertEqual(expected, (selected['core'], selected['branches'], selected['print']))
                self.assertEqual(bool(selected['desktops']), selected['core'])
        with self.assertRaises(ValueError):
            ci.plan(None, 'typo')

    def test_git_diff_keeps_deleted_and_renamed_code_and_more_than_300_paths(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)

            def git(*args):
                return subprocess.check_output(['git', *args], cwd=root, stderr=subprocess.PIPE).decode().strip()

            def commit():
                git('add', '.')
                git('-c', 'user.name=CI test', '-c', 'user.email=ci@example.invalid', 'commit', '-m', 'fixture')
                return git('rev-parse', 'HEAD')

            git('init')
            source = root / 'production.cs'
            source.write_text('class Production {}', encoding='utf-8')
            removed = root / 'removed.cs'
            removed.write_text('class Removed {}', encoding='utf-8')
            base = commit()
            docs = root / 'docs'
            docs.mkdir()
            source.rename(docs / 'renamed.md')
            removed.unlink()
            for index in range(305):
                (docs / f'guide-{index}.md').write_text('# Guide', encoding='utf-8')
            commit()
            for event, payload in [('push', {'before': base}),
                                   ('pull_request', {'pull_request': {'base': {'sha': base}}})]:
                with self.subTest(event=event):
                    paths = ci.changed_paths(root, event, payload)
                    self.assertGreater(len(paths), 300)
                    self.assertIn('production.cs', paths)
                    self.assertIn('removed.cs', paths)
                    self.assertIn('docs/renamed.md', paths)
                    self.assertTrue(ci.plan(paths)['core'])
            for before in ['0' * 40, 'f' * 40]:
                missing = ci.changed_paths(root, 'push', {'before': before})
                self.assertIsNone(missing)
                self.assertEqual(ci.DESKTOPS, ci.plan(missing)['desktops'])

    def test_gate_requires_completion_of_the_exact_selected_jobs(self):
        for selected in [ci.plan(['README.md']), ci.plan(['docs/operator/quick-guide.md']),
                         ci.plan(None), ci.plan(None, 'branches'), ci.plan(None, 'print'), ci.plan(None, 'all')]:
            outputs = ci.workflow_outputs(selected)
            needs = {'repository-checks': {'result': 'success', 'outputs': outputs}}
            for job, required in [('core-regression', selected['core']),
                                  ('windows-build', bool(selected['desktops'])),
                                  ('product-branch-evidence', selected['branches']),
                                  ('operator-print', selected['print'])]:
                needs[job] = {'result': 'success' if required else 'skipped'}
            ci.check_results(needs)
            for job in needs:
                original = needs[job]['result']
                for failed in ['failure', 'cancelled', 'skipped' if original == 'success' else 'success']:
                    with self.subTest(plan=selected['reason'], job=job, result=failed):
                        needs[job]['result'] = failed
                        with self.assertRaises(ValueError):
                            ci.check_results(needs)
                needs[job]['result'] = original
            broken = json.loads(json.dumps(needs))
            del broken['repository-checks']['outputs']['core']
            with self.assertRaises(KeyError):
                ci.check_results(broken)


if __name__ == '__main__':
    unittest.main()
