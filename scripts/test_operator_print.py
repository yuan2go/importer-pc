"""PDF body checks must survive page breaks without hiding missing source text."""
import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest

spec = importlib.util.spec_from_file_location('operator_print', Path(__file__).with_name('build-operator-print.py'))
printer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(printer)


class PrintBodyTests(unittest.TestCase):
    def reader(self, *texts):
        return SimpleNamespace(pages=[SimpleNamespace(extract_text=lambda value=text: value) for text in texts])

    def test_paragraph_crossing_pages_stays_contiguous(self):
        reader = self.reader('fixed footer\n1\n现场操\n', 'fixed footer\n2\n作说明\n')
        self.assertEqual(printer.searchable_body(reader, 'fixed footer'), '现场操作说明')

    def test_identical_body_words_are_preserved(self):
        reader = self.reader('fixed footer\n1\n正文fixed footer正文\n')
        self.assertEqual(printer.searchable_body(reader, 'fixed footer'), '正文fixedfooter正文')

    def test_wrong_missing_or_out_of_order_footer_fails(self):
        for text in ('other footer\n1\n正文', 'fixed footer\n2\n正文', '正文'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                printer.searchable_body(self.reader(text), 'fixed footer')

    def test_missing_source_text_is_still_rejected_before_font_check(self):
        reader = self.reader('fixed footer\n1\n现场说明')
        with self.assertRaisesRegex(ValueError, 'Source text missing'):
            printer.verify_text(reader, ['现场操作说明'], 'fixed footer')


if __name__ == '__main__':
    unittest.main()
