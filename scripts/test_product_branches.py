"""Parser trust-boundary checks; these XML fixtures are not runtime evidence."""
import importlib.util
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('branches', Path(__file__).with_name('report-product-branches.py'))
branches = importlib.util.module_from_spec(spec)
spec.loader.exec_module(branches)


class BranchEvidence(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'src/Ipc.Example/Production.cs'
        self.source.parent.mkdir(parents=True)
        self.source.write_text('class Example {}', encoding='utf-8')
        (self.source.parent / 'NotMeasured.cs').write_text('class Other {}', encoding='utf-8')
        self.results = self.root / 'evidence'; self.results.mkdir()

    def report(self, name, hit, identity='original-bytes'):
        tree = ET.Element('CoverageSession')
        ET.SubElement(tree, 'Summary', numBranchPoints='2')
        module = ET.SubElement(ET.SubElement(tree, 'Modules'), 'Module', hash='collector-guid-is-not-a-byte-identity')
        ET.SubElement(module, 'ModuleName').text = 'Ipc.Example'
        ET.SubElement(ET.SubElement(module, 'Files'), 'File', uid='1', fullPath=self.source.as_posix())
        method = ET.SubElement(ET.SubElement(ET.SubElement(ET.SubElement(module, 'Classes'), 'Class'), 'Methods'), 'Method')
        ET.SubElement(method, 'Name').text = 'System.Boolean Example::Run()'
        ET.SubElement(method, 'FileRef', uid='1')
        points = ET.SubElement(method, 'BranchPoints')
        for path, visits in enumerate([hit, 0]):
            ET.SubElement(points, 'BranchPoint', sl='1', vc=str(visits), offset='1', path=str(path), offsetend='3')
        data = {'Ipc.Example.dll': {self.source.as_posix(): {'Example': {'System.Boolean Example::Run()': {
            'Lines': {}, 'Branches': [{'Line': 1, 'Hits': visits, 'Offset': 1, 'Path': path, 'EndOffset': 3, 'Ordinal': path} for path, visits in enumerate([hit, 0])]
        }}}}}
        (self.results / (name + '.json')).write_text(json.dumps(data), encoding='utf-8')
        ET.ElementTree(tree).write(self.results / (name + '.opencover.xml'), encoding='utf-8')
        (self.results / (name + '.modules.json')).write_text(json.dumps([{'name': 'Ipc.Example', 'sha256': hashlib.sha256(identity.encode()).hexdigest()}]), encoding='utf-8')

    def test_actual_paths_and_unknown_source_stay_distinct(self):
        self.report('one', 1)
        result = branches.analyze(self.root, self.results)
        self.assertEqual((result['coveredBranches'], result['measuredBranches'], result['unmeasuredFiles']), (1, 2, 1))
        self.assertFalse(result['wholeProductBranchCoverage'])
        self.assertIsNone(next(f for f in result['files'] if f['source'].endswith('NotMeasured.cs'))['knownBranches'])

    def test_same_bytes_merge_hits_but_other_variants_keep_their_paths(self):
        self.report('one', 1); self.report('two', 2); self.report('other', 1, 'other-bytes')
        result = branches.analyze(self.root, self.results)
        self.assertEqual((result['coveredBranches'], result['measuredBranches']), (2, 4))
        self.assertEqual(len(result['variants']), 2)

    def test_missing_manifest_or_all_zero_cannot_be_success_evidence(self):
        self.report('zero', 0)
        with self.assertRaises(ValueError): branches.analyze(self.root, self.results)
        (self.results / 'zero.modules.json').unlink()
        with self.assertRaises(FileNotFoundError): branches.analyze(self.root, self.results)
