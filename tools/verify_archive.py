#!/usr/bin/env python3
"""Read-only archive verification. No network, extraction, repair or business execution."""
import argparse
import hashlib
import json
import posixpath
import re
import stat
import sys
import unicodedata
import xml.etree.ElementTree as ET
import zipfile
from collections import Counter
from html.parser import HTMLParser
from pathlib import Path, PurePosixPath
from urllib.parse import unquote, urlsplit


def digest(data):
    return hashlib.sha256(data).hexdigest()


def safe_name(name):
    p = PurePosixPath(name)
    return bool(name) and not p.is_absolute() and '..' not in p.parts and not re.search(r'[\\\x00-\x1f:]', name)


def zip_check(path, expected=None):
    if expected and digest(path.read_bytes()) != expected:
        raise ValueError('ZIP SHA-256 mismatch')
    with zipfile.ZipFile(path) as z:
        names = set()
        total = 0
        for i in z.infolist():
            mode = i.external_attr >> 16
            canonical = PurePosixPath(i.filename).as_posix()
            if not safe_name(i.filename) or canonical != i.filename.rstrip('/') or canonical in names:
                raise ValueError('unsafe or duplicate ZIP path')
            if stat.S_IFMT(mode) not in (0, stat.S_IFREG, stat.S_IFDIR) or i.flag_bits & 1:
                raise ValueError('ZIP special link or encryption')
            if i.file_size > 100_000_000 or i.file_size / max(i.compress_size, 1) > 10000:
                raise ValueError('ZIP expansion limit')
            names.add(canonical)
            total += i.file_size
        if total > 500_000_000 or z.testzip() is not None:
            raise ValueError('ZIP size or CRC failure')
        return sum(not i.is_dir() for i in z.infolist())


class HTMLReferences(HTMLParser):
    def __init__(self):
        super().__init__()
        self.refs = []
        self.ids = set()
        self.styles = []

    def handle_starttag(self, tag, attrs):
        for key, value in attrs:
            if not value:
                continue
            if key in ('id', 'name'):
                self.ids.add(value)
            if key == 'style':
                self.styles.append(value)
            if key in ('src', 'href', 'poster', 'data', 'xlink:href'):
                self.refs.append(value)
            if key == 'srcset' and not value.startswith('data:'):
                self.refs.extend(x.strip().split()[0] for x in value.split(',') if x.strip())


def css_references(text):
    # Quoted data SVGs can contain parentheses and quotes of the opposite kind.
    text = re.sub(r'/\*.*?\*/', '', text, flags=re.S)
    refs = []
    for m in re.finditer(r'url\(\s*(?:"([^"]*)"|\x27([^\x27]*)\x27|([^\s)]*))\s*\)', text, re.I):
        refs.append(next(x for x in m.groups() if x is not None))
    refs.extend(m.group(2) for m in re.finditer(r'@import\s+(["\x27])(.*?)\1', text, re.I))
    return refs


def svg_references(text):
    tree = ET.fromstring(text)
    refs, styles = [], []
    for node in tree.iter():
        if node.tag.split('}')[-1] == 'style':
            styles.append(node.text or '')
        for key, value in node.attrib.items():
            local_key = key.split('}')[-1]
            if local_key in ('href', 'src'):
                refs.append(value)
            elif local_key == 'style':
                styles.append(value)
    return refs + css_references('\n'.join(styles))


def markdown(text):
    clean = re.sub(r'^\s*(`{3,}|~{3,}).*?^\s*\1\s*$', '', text, flags=re.M | re.S)
    refs = []
    # Balanced parentheses also cover file names and URL parentheses.
    for m in re.finditer(r'\]\(', clean):
        start = m.end()
        if start < len(clean) and clean[start] == '<':
            end = clean.find('>', start)
            if end >= 0:
                refs.append(clean[start + 1:end])
            continue
        depth = 1
        end = start
        while end < len(clean) and depth:
            if clean[end] == '(' and (end == 0 or clean[end - 1] != '\\'):
                depth += 1
            if clean[end] == ')' and (end == 0 or clean[end - 1] != '\\'):
                depth -= 1
            end += 1
        if depth == 0:
            refs.append(re.split(r'\s+["\x27]', clean[start:end - 1])[0].replace('\\(', '(').replace('\\)', ')'))
    definitions = dict(re.findall(r'^\s*\[([^\]]+)\]:\s*<?(\S+?)>?(?:\s|$)', clean, re.M))
    refs.extend(definitions.values())
    for label, key in re.findall(r'\[([^\]]+)\]\[([^\]]*)\]', clean):
        if (key or label) not in definitions:
            refs.append('missing-reference-definition:' + (key or label))
    ids = set()
    counts = Counter()
    for heading in re.findall(r'^\s{0,3}#{1,6}\s+(.+?)\s*#*$', clean, re.M):
        heading = re.sub(r'<[^>]+>', '', heading).lower()
        heading = re.sub(r'\[([^\]]+)\]\([^)]*\)', r'\1', heading)
        slug = ''.join(c for c in heading if c in ' -_' or unicodedata.category(c)[0] in 'LNM')
        slug = slug.replace(' ', '-')
        n = counts[slug]
        ids.add(slug + ('-' + str(n) if n else ''))
        counts[slug] += 1
    ids.update(re.findall(r'\b(?:id|name)=["\x27]([^"\x27]+)', clean))
    return refs, ids


def verify(root, manifest_path, scope, registry_path=None):
    errors, registered, external = [], [], []
    counts = Counter()
    root = root.resolve()
    def fail(code, path):
        errors.append({'code': code, 'path': str(path)})
    manifest = json.loads(manifest_path.read_text())
    exclusions = set(manifest['self_verification']['excluded_from_file_records'])
    # Exclusion policy is deliberately narrow; arbitrary directories cannot disappear.
    if exclusions != {'docs/manifest.json', 'docs/manifest.sha256'}:
        raise ValueError('self exclusions must be exactly the manifest and its checksum')
    rows = manifest['files']
    names = [r['path'] for r in rows]
    if len(names) != len(set(names)):
        fail('duplicate_manifest_path', manifest_path.relative_to(root))
    expected = set(names) | exclusions
    files = []
    for f in sorted(root.rglob('*')):
        rel = f.relative_to(root).as_posix()
        if '.git' in f.relative_to(root).parts or '__pycache__' in f.relative_to(root).parts:
            continue
        if f.is_symlink():
            fail('symlink', rel)
        elif f.is_file():
            files.append(f)
            if rel not in expected:
                fail('unlisted_file', rel)
    for r in rows:
        name = r['path']
        if not safe_name(name):
            fail('unsafe_manifest_path', name)
            continue
        p = root / name
        if not p.is_file() or p.is_symlink() or not p.resolve().is_relative_to(root):
            fail('missing_file', name)
            continue
        data = p.read_bytes()
        if digest(data) != r['sha256'] or len(data) != r.get('size_bytes', r.get('size')):
            fail('content_identity', name)
        if scope == 'public' and r.get('storage_scope') != 'public_repository':
            fail('public_scope_not_recorded', name)
    checksum = (root / 'docs/manifest.sha256').read_text().strip().split()
    if len(checksum) != 2 or checksum[1] != 'manifest.json' or checksum[0] != digest(manifest_path.read_bytes()):
        fail('manifest_checksum', 'docs/manifest.sha256')
    policy = json.loads(registry_path.read_text()) if registry_path else {}
    exceptions = {(r['source'], r['target']): r for r in policy.get('exceptions', [])}
    used = set()
    package_exceptions = {(r['container'], r['part']): r for r in policy.get('package_exceptions', [])}
    package_used = set()
    ids = {}
    def anchors(path):
        if path not in ids:
            t = path.read_text()
            if path.suffix == '.md':
                ids[path] = markdown(t)[1]
            else:
                parser = HTMLReferences(); parser.feed(t); ids[path] = parser.ids
        return ids[path]
    def reference(source, target):
        if not target or target.startswith(('data:', 'mailto:', 'tel:', 'javascript:')):
            return
        rel = source.relative_to(root).as_posix()
        key = (rel, target)
        if key in exceptions:
            item = exceptions[key]
            if not item.get('reason') or not item.get('status'):
                fail('invalid_exception', rel)
            registered.append({'source': rel, 'target': target, 'status': item.get('status')})
            used.add(key)
            return
        u = urlsplit(target)
        if u.scheme in ('http', 'https') or u.netloc:
            external.append({'source': rel, 'target_sha256': digest(target.encode())})
            return
        if u.scheme:
            fail('unsupported_reference_scheme', rel)
            return
        dest = (source.parent / unquote(u.path)).resolve() if u.path else source.resolve()
        if not dest.is_relative_to(root) or not dest.is_file() or dest.is_symlink():
            fail('missing_or_unsafe_reference', rel + ' -> ' + target)
        elif u.fragment and dest.suffix in ('.md', '.html', '.htm') and unquote(u.fragment) not in anchors(dest):
            fail('missing_anchor', rel + ' -> ' + target)
        counts['references'] += 1
    for f in files:
        rel = f.relative_to(root).as_posix()
        suffix = f.suffix.lower()
        counts['files'] += 1
        try:
            if suffix in ('.dll', '.exe', '.msi', '.ttf', '.otf', '.woff', '.woff2', '.odttf'):
                fail('excluded_binary', rel)
            if suffix == '.json':
                value = json.loads(f.read_text()); counts['json'] += 1
                if isinstance(value, list) and value and all(isinstance(b, dict) and 'objects' in b for b in value):
                    for b in value:
                        if not all(isinstance(b.get(k), (int, float)) and b[k] > 0 for k in ('width', 'height')):
                            raise ValueError('invalid board dimensions')
                        for o in b['objects']:
                            if o.get('type') == 'svg':
                                for target in svg_references(o['svg']):
                                    if not target.startswith('#'): reference(f, target)
                            for key in ('src', 'image', 'file', 'asset'):
                                if isinstance(o.get(key), str): reference(f, o[key])
                        counts['boards'] += 1
            if suffix in ('.md', '.html', '.htm', '.css', '.js', '.jsx', '.json', '.py', '.txt'):
                t = f.read_text()
                if re.search(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY|(?:gh[pousr]_[A-Za-z0-9]{30,})|[?&](?:X-Amz-Signature|Signature|auth_key|access_token)=', t, re.I):
                    fail('secret_marker', rel)
                if scope == 'public' and re.search(r'(?:AKIA[0-9A-Z]{16})|(?:api[_-]?key|secret|password)\s*[:=]\s*["\x27][^"\x27\n]{12,}', t, re.I):
                    fail('credential_review_required', rel)
                if scope == 'public' and re.search(r'alidocs\.(?:dingtalk|oss-)|canva\.com/(?:d|design)/|/(?:mnt/data|Users/[^/]+|Volumes/[^/]+)/', t):
                    fail('private_metadata_marker', rel)
                if suffix == '.md':
                    for target in markdown(t)[0]: reference(f, target)
                if suffix in ('.html', '.htm'):
                    parser = HTMLReferences(); parser.feed(t)
                    for target in parser.refs: reference(f, target)
                if suffix in ('.css', '.html', '.htm'):
                    css = t if suffix == '.css' else '\n'.join(re.findall(r'<style\b[^>]*>(.*?)</style>', t, re.I | re.S) + parser.styles)
                    for target in css_references(css):
                        if not target.startswith('#'): reference(f, target)
            if suffix in ('.png', '.jpg', '.jpeg', '.gif', '.webp'):
                from PIL import Image
                with Image.open(f) as im: im.verify()
                with Image.open(f) as im: im.load()
                counts['images'] += 1
            if suffix == '.pptx':
                zip_check(f)
                with zipfile.ZipFile(f) as z:
                    parts = set(z.namelist())
                    required = {'[Content_Types].xml', '_rels/.rels', 'ppt/presentation.xml', 'ppt/_rels/presentation.xml.rels'}
                    if not required <= parts:
                        fail('pptx_required_parts', rel)
                    for name in sorted(parts):
                        if name.endswith('/'):
                            continue
                        if '/embeddings/' in name.lower() or 'vbaproject' in name.lower():
                            fail('pptx_embedded_object_review_required', rel + ':' + name)
                        if name.lower().endswith(('.ttf', '.otf', '.odttf', '.woff', '.woff2')) or '/fonts/' in name.lower():
                            fail('embedded_font', rel)
                        if name.endswith(('.xml', '.rels')):
                            tree = ET.fromstring(z.read(name))
                            if name.endswith('.rels'):
                                parent = PurePosixPath(name).parent.parent
                                for item in tree:
                                    if item.get('TargetMode') == 'External':
                                        fail('pptx_external_relationship', rel + ':' + name)
                                    else:
                                        dest = posixpath.normpath(str(parent / unquote(item.get('Target', ''))))
                                        if dest not in parts: fail('pptx_missing_part', rel + ':' + name)
                        if re.fullmatch(r'ppt/slides/slide\d+\.xml', name): counts['slides'] += 1
                        if name.lower().endswith('.svg'):
                            for value in svg_references(z.read(name)):
                                if not value.startswith(('#', 'data:')):
                                    fail('pptx_svg_external_asset', rel + ':' + name)
                        if name.lower().endswith(('.png', '.jpg', '.jpeg', '.gif', '.webp')):
                            from io import BytesIO
                            from PIL import Image
                            data = z.read(name)
                            if data.lstrip().startswith((b'<svg', b'<?xml')) and ET.fromstring(data).tag.split('}')[-1] == 'svg':
                                for value in svg_references(data):
                                    if not value.startswith(('#', 'data:')):
                                        fail('pptx_svg_external_asset', rel + ':' + name)
                                key = (rel, name)
                                item = package_exceptions.get(key, {})
                                if item.get('sha256') == digest(data) and item.get('reason') and item.get('status') == 'HISTORICAL_FORMAT_MISMATCH':
                                    package_used.add(key)
                                    registered.append({'source': rel, 'target': name, 'status': item['status']})
                                else:
                                    fail('pptx_media_type_mismatch', rel + ':' + name)
                            else:
                                with Image.open(BytesIO(data)) as im: im.verify()
                                with Image.open(BytesIO(data)) as im: im.load()
                counts['pptx'] += 1
        except (ValueError, OSError, KeyError, ET.ParseError, ImportError, zipfile.BadZipFile) as e:
            fail(type(e).__name__, rel)
    for key in sorted(set(exceptions) - used):
        fail('unused_exception', key[0] + ' -> ' + key[1])
    for key in sorted(set(package_exceptions) - package_used):
        fail('unused_package_exception', key[0] + ':' + key[1])
    # Explicit evidence locators are checked independently of textual link syntax.
    for r in policy.get('locators', []):
        target = root / r['path']
        if not safe_name(r['path']) or not target.is_file() or target.is_symlink():
            fail('missing_evidence_locator', r['path'])
        elif r.get('sha256') and digest(target.read_bytes()) != r['sha256']:
            fail('evidence_identity', r['path'])
        elif r.get('line_end') and len(target.read_text().splitlines()) < r['line_end']:
            fail('evidence_line_range', r['path'])
    for r in policy.get('historical_omissions', []):
        if not safe_name(r['path']) or not r.get('reason') or r.get('status') != 'MISSING':
            fail('invalid_omission', r.get('path', ''))
        elif (root / r['path']).exists():
            fail('stale_missing_registration', r['path'])
        else:
            registered.append({'source': r.get('source'), 'target': r['path'], 'status': 'MISSING'})
    return {'status': 'FAIL' if errors else 'PASS', 'scope': scope, 'counts': dict(counts),
            'errors': errors, 'registered_reference_gaps': registered, 'external_references': external,
            'business_tests': 'NOT_RUN', 'network_access': False}


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--root', type=Path)
    ap.add_argument('--manifest', type=Path, default=Path('docs/manifest.json'))
    ap.add_argument('--scope', choices=('public', 'local'), default='public')
    ap.add_argument('--registry', type=Path)
    ap.add_argument('--zip', type=Path)
    ap.add_argument('--sha256')
    args = ap.parse_args()
    try:
        out = {}
        if args.zip:
            out['zip_file_entries'] = zip_check(args.zip, args.sha256)
        if args.root:
            out.update(verify(args.root, args.root / args.manifest, args.scope, args.root / args.registry if args.registry else None))
        elif not args.zip:
            ap.error('provide --root or --zip')
        out.setdefault('status', 'PASS')
    except (ValueError, OSError, KeyError, zipfile.BadZipFile) as e:
        out = {'status': 'FAIL', 'error': type(e).__name__, 'detail': str(e)}
    print(json.dumps(out, ensure_ascii=False, indent=2))
    return 0 if out['status'] == 'PASS' else 1


if __name__ == '__main__':
    sys.exit(main())
