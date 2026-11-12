from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

from verify_product_coverage import MODULES, REQUIRED_FILES, analyze, choose_report, passed_test_count


class ProductCoverage(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)

    def report(self, name='real.cobertura.xml'):
        coverage = ET.Element('coverage')
        packages = ET.SubElement(coverage, 'packages')
        files = [*REQUIRED_FILES, 'src/Ipc.Application.Contracts/BarcodeFiltering.cs', 'src/Ipc.Application.Sportmaster/Application.cs']
        for module in MODULES:
            classes = ET.SubElement(ET.SubElement(packages, 'package', name=module), 'classes')
            for filename in files:
                if not filename.startswith('src/' + module + '/'):
                    continue
                source = ET.SubElement(classes, 'class', name='SyntheticType', filename='D:/synthetic/' + filename)
                lines = ET.SubElement(source, 'lines')
                ET.SubElement(lines, 'line', number='1', hits='1')
        path = self.root / name
        ET.ElementTree(coverage).write(path, encoding='utf-8')
        return path

    def test_actual_product_source_identity_and_deduplication(self):
        path = self.report()
        root = ET.parse(path).getroot()
        classes = root.find('./packages/package/classes')
        first = classes.find('class')
        duplicate = ET.SubElement(classes, 'class', name='SyntheticAsyncState', filename=first.get('filename'))
        ET.SubElement(ET.SubElement(duplicate, 'lines'), 'line', number='1', hits='2')
        ET.ElementTree(root).write(path)
        result = analyze(path)
        self.assertEqual(8, result['total']['valid'])
        self.assertEqual(8, result['total']['covered'])
        self.assertFalse(result['wholeProductCoverage'])
        self.assertFalse(result['testAssembliesIncluded'])

    def test_wrapper_only_or_unknown_modules_cannot_count(self):
        path = self.report()
        root = ET.parse(path).getroot()
        root.find('./packages/package').set('name', 'Ipc.Core.Tests')
        ET.ElementTree(root).write(path)
        with self.assertRaisesRegex(ValueError, 'Unexpected/test'):
            analyze(path)

    def test_missing_child_product_module_and_zero_are_rejected(self):
        path = self.report()
        root = ET.parse(path).getroot()
        packages = root.find('packages')
        packages.remove(list(packages)[-1])
        ET.ElementTree(root).write(path)
        with self.assertRaisesRegex(ValueError, 'Missing expected'):
            analyze(path)
        path = self.report()
        root = ET.parse(path).getroot()
        for line in root.findall('.//line'):
            line.set('hits', '0')
        ET.ElementTree(root).write(path)
        with self.assertRaisesRegex(ValueError, 'all-zero'):
            analyze(path)

    def test_critical_source_missing_or_wrong_project_is_rejected(self):
        path = self.report()
        root = ET.parse(path).getroot()
        first = root.find('./packages/package/classes/class')
        first.set('filename', 'D:/synthetic/src/Ipc.Platform/Other.cs')
        ET.ElementTree(root).write(path)
        with self.assertRaisesRegex(ValueError, 'empty'):
            analyze(path)
        first.set('filename', 'D:/synthetic/tests/Ipc.Application.Verification/Fake.cs')
        ET.ElementTree(root).write(path)
        with self.assertRaisesRegex(ValueError, 'source path'):
            analyze(path)

    def test_identical_copies_count_once_and_mixed_sessions_refuse(self):
        path = self.report()
        duplicate = self.root / 'copy.cobertura.xml'
        duplicate.write_bytes(path.read_bytes())
        self.assertEqual(2, choose_report(self.root)[2])
        duplicate.write_text(path.read_text() + '\n', encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'Different coverage sessions'):
            choose_report(self.root)

    def test_failed_or_missing_execution_cannot_qualify_coverage(self):
        root = ET.Element('TestRun')
        summary = ET.SubElement(root, 'ResultSummary', outcome='Completed')
        counters = ET.SubElement(summary, 'Counters', total='3', executed='3', passed='3', failed='0', notExecuted='0')
        rows = ET.SubElement(root, 'Results')
        for i in range(3):
            ET.SubElement(rows, 'UnitTestResult', testName=str(i), outcome='Passed')
        trx = self.root / 'core.trx'
        ET.ElementTree(root).write(trx)
        self.assertEqual(3, passed_test_count(self.root))
        summary.set('outcome', 'Aborted')
        ET.ElementTree(root).write(trx)
        with self.assertRaisesRegex(ValueError, 'failed core tests'):
            passed_test_count(self.root)
        summary.set('outcome', 'Completed')
        counters.set('total', '4')
        ET.ElementTree(root).write(trx)
        with self.assertRaisesRegex(ValueError, 'failed core tests'):
            passed_test_count(self.root)
        counters.set('total', '3')
        counters.set('failed', '1')
        ET.ElementTree(root).write(trx)
        with self.assertRaisesRegex(ValueError, 'failed core tests'):
            passed_test_count(self.root)
        counters.set('failed', '0')
        counters.set('notExecuted', '1')
        ET.ElementTree(root).write(trx)
        with self.assertRaisesRegex(ValueError, 'failed core tests'):
            passed_test_count(self.root)
        counters.set('notExecuted', '0')
        rows.remove(list(rows)[0])
        ET.ElementTree(root).write(trx)
        with self.assertRaisesRegex(ValueError, 'failed core tests'):
            passed_test_count(self.root)
        counters.set('executed', '0')
        counters.set('passed', '0')
        counters.set('total', '0')
        rows.clear()
        ET.ElementTree(root).write(trx)
        with self.assertRaisesRegex(ValueError, 'failed core tests'):
            passed_test_count(self.root)

    def test_report_budget_checked_before_xml_decoding(self):
        path = self.root / 'excessive.cobertura.xml'
        with path.open('wb') as stream:
            stream.truncate(32 * 1024 * 1024 + 1)
        with self.assertRaisesRegex(ValueError, 'budget'):
            analyze(path)


if __name__ == '__main__':
    unittest.main()
