"""PDF body checks must survive page breaks without hiding missing source text."""
import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch
from reportlab.lib.styles import getSampleStyleSheet

spec = importlib.util.spec_from_file_location('operator_print', Path(__file__).with_name('build-operator-print.py'))
printer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(printer)


class PrintBodyTests(unittest.TestCase):
    def reader(self, *texts):
        class Page(dict):
            def __init__(self, text):
                self.text = text
                fonts = {name: SimpleNamespace(get_object=lambda value=name: {'/BaseFont': 'SUBSET+' + value, '/FontDescriptor': {'/FontFile2': True}})
                         for name in ('IpcPrintRegular', 'IpcPrintMedium')}
                super().__init__({'/Resources': {'/Font': fonts}})
            def extract_text(self): return self.text
        return SimpleNamespace(pages=[Page(text) for text in texts])

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

    def test_repeated_source_occurrences_and_order_are_enforced(self):
        for source, body in (('A\nB\nA', 'AB'), ('A\nB', 'BA')):
            with self.subTest(source=source), self.assertRaisesRegex(ValueError, 'Source text missing'):
                printer.verify_text(self.reader('fixed footer\n1\n' + body), [source], 'fixed footer')
        self.assertEqual(printer.verify_text(self.reader('fixed footer\n1\nABA'), ['A\nB\nA'], 'fixed footer'), 3)

    def test_fenced_heading_and_numbered_markdown_stay_literal(self):
        lines = ['## example', '1. `literal` [link](https://example.invalid)']
        source = '```\n' + '\n'.join(lines) + '\n```'
        style = getSampleStyleSheet()['Normal']
        with patch.object(printer, 'styles', return_value=dict.fromkeys(('body', 'title', 'heading', 'sub', 'note', 'table'), style)):
            story = printer.content(source, False)
        self.assertEqual([part.getPlainText() for part in story], lines)
        self.assertEqual(printer.verify_text(self.reader('fixed footer\n1\n' + '\n'.join(lines)), [source], 'fixed footer'), 2)
        with self.assertRaisesRegex(ValueError, 'Source text missing'):
            printer.verify_text(self.reader('fixed footer\n1\nexample\n1. literal link'), [source], 'fixed footer')


if __name__ == '__main__':
    unittest.main()
