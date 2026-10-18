"""One inventory for shipped guides and immutable, usable publication links."""
from pathlib import Path
import re
from urllib.parse import quote, unquote, urlsplit

GUIDES = {
    'docs/runtime/application-architecture-22.md': 'APPLICATIONS.md',
    'docs/design/application-architecture-22.md': 'ARCHITECTURE-22.md',
    'docs/runtime/capability-reliability-20.md': 'APPLICATIONS-20.md',
    'docs/design/capability-reliability-20.md': 'RELIABILITY.md',
    'docs/runtime/application-capability-19.md': 'APPLICATIONS-19.md',
    'docs/design/application-capability-19.md': 'OPTIONAL-CAPABILITIES.md',
    'docs/runtime/operations.md': 'OPERATIONS.md',
    'docs/extensions/developer-guide.md': 'EXTENSIONS.md',
    'docs/runtime/getting-started.md': 'GETTING-STARTED.md',
    'docs/runtime/validation/ui-maintainability-16-windows.md': 'WINDOWS-VALIDATION.md',
}
ROOT_INPUTS = ['global.json', 'Directory.Build.props', 'NuGet.Config', 'ImporterPc.slnx']
SOURCE_DIRECTORIES = ['src', 'config', 'scripts', 'examples']
SOURCE_PATHS = [*SOURCE_DIRECTORIES, *ROOT_INPUTS, *GUIDES]


def source_files(root):
    paths = [root / name for name in [*ROOT_INPUTS, *GUIDES]]
    for directory in SOURCE_DIRECTORIES:
        paths.extend(f for f in (root / directory).rglob('*') if f.is_file() and
                     not {'bin', 'obj', '__pycache__'}.intersection(f.relative_to(root).parts))
    return sorted(set(paths))


def render(root, source, commit):
    if not re.fullmatch('[0-9a-f]{40}', commit):
        raise ValueError('Guide links require a frozen source commit')
    def link(match):
        label, target = match.groups()
        wrapped = target.startswith('<') and target.endswith('>')
        target = target[1:-1] if wrapped else target
        parsed = urlsplit(target)
        if parsed.scheme or target.startswith(('#', '//')):
            return match.group(0)
        path = (root / source).parent.joinpath(unquote(parsed.path)).resolve()
        if not path.is_relative_to(root.resolve()) or not path.is_file():
            raise ValueError('Published guide has missing/escaping local link: ' + source + ': ' + target)
        relative = path.relative_to(root.resolve()).as_posix()
        destination = GUIDES.get(relative)
        if destination is None:
            destination = 'https://github.com/yuan2go/importer-pc/blob/' + commit + '/' + quote(relative, safe='/')
        if parsed.fragment: destination += '#' + parsed.fragment
        return '[' + label + '](' + destination + ')'
    return re.sub(r'\[([^\]\n]+)\]\(([^)\n]+)\)', link, (root / source).read_text(encoding='utf-8'))


def publish(root, output, commit):
    for source, name in GUIDES.items():
        (output / name).write_text(render(root, source, commit), encoding='utf-8')
