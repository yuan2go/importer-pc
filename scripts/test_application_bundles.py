from pathlib import Path
import hashlib
import json
import tempfile
import unittest
import zipfile
from application_bundles import read_packages
import release_documents as docs

class ApplicationBundleTests(unittest.TestCase):
    def archive(self, root, id, version='2.0.0', dependencies=None):
        path=root/(id+'.ipcx'); raw=json.dumps(dict(Id=id, Version=version, Dependencies=dependencies or [])).encode()
        with zipfile.ZipFile(path,'w') as z: z.writestr('manifest.json',raw)
        return path, dict(Id=id,Version=version,ManifestSha256=hashlib.sha256(raw).hexdigest().upper())
    def test_complete_exact_closure_and_duplicates(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary); dependency,reference=self.archive(root,'vendor.driver'); main,_=self.archive(root,'vendor.service',dependencies=[reference])
            selection={'components':[dict(id='platform',delivery='core')]}
            self.assertEqual(set(read_packages([main,dependency],selection)),{'vendor.driver','vendor.service'})
            for files in [[main],[dependency,dependency]]:
                with self.assertRaises(ValueError): read_packages(files,selection)
            bad=dict(reference,ManifestSha256='A'*64); mismatch,_=self.archive(root,'vendor.bad',dependencies=[bad])
            with self.assertRaises(ValueError): read_packages([mismatch,dependency],selection)
    def test_composition_bundle_cannot_disappear(self):
        selection={'components':[dict(id='vendor.driver',version='2.0.0',delivery='offline-bundle')]}
        with self.assertRaises(ValueError): read_packages([],selection)
    def test_product_guides_resolve_actual_operations(self):
        root=Path(__file__).resolve().parents[1]
        with tempfile.TemporaryDirectory() as temporary:
            output=Path(temporary)
            docs.publish(root,output,'a'*40,'sportmaster')
            self.assertIn('Ipc.Assembly.dll',(output/'GETTING-STARTED.md').read_text())
            self.assertIn('StageConfiguration',(output/'EXTENSIONS.md').read_text())
            self.assertNotIn('OPERATIONS.md',{p.name for p in output.iterdir()})
            self.assertNotIn('dotnet /frozen/app/Ipc.Product.dll',(output/'EXTENSIONS.md').read_text())
            docs.publish(root,output,'a'*40,'supply')
            self.assertIn('OpenContext',(output/'GETTING-STARTED.md').read_text())
            self.assertIn('SaveConfig',(output/'EXTENSIONS.md').read_text())

if __name__=='__main__': unittest.main()
