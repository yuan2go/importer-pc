"""One inventory for shipped guides and immutable, usable publication links."""
from pathlib import Path
import re
from urllib.parse import quote, unquote, urlsplit

PARCEL_GUIDES = {
    'docs/runtime/parameter-inheritance-02.md': 'PARAMETER-INHERITANCE.md',
    'docs/runtime/commercial-productization-01-operations.md': 'PRODUCT-OPERATIONS.md',
    'docs/runtime/startup-deployment-02.md': 'STARTUP-DEPLOYMENT.md',
    'docs/runtime/windows-application-delivery.md': 'WINDOWS-APPLICATION.md',
    'docs/runtime/version-compatibility-01.md': 'VERSIONS.md',
    'docs/runtime/getting-started.md': 'GETTING-STARTED.md',
    'docs/runtime/architecture-quality-closure-01.md': 'QUALITY-CLOSURE-01.md',
    'docs/runtime/validation/architecture-quality-closure-01-windows.md': 'QUALITY-WINDOWS-ACCEPTANCE.md',
    'docs/runtime/application-architecture-22.md': 'APPLICATIONS.md',
    'docs/design/application-architecture-22.md': 'ARCHITECTURE-22.md',
    'docs/extensions/developer-guide.md': 'EXTENSIONS.md',
}
GUIDES = {
    'docs/runtime/supply-getting-started.md': 'GETTING-STARTED.md',
    'docs/runtime/operations.md': 'OPERATIONS.md',
    'docs/extensions/supply-guide.md': 'EXTENSIONS.md',
    'docs/runtime/validation/ui-maintainability-16-windows.md': 'WINDOWS-VALIDATION.md',
}
SPORTMASTER_GUIDES = {**PARCEL_GUIDES, 'docs/runtime/startup-experience-02.md': 'FIRST-CONFIGURATION.md', 'docs/runtime/sportmaster-rcs-task.md': 'SPORTMASTER-RCS.md',
    'docs/runtime/rcs-heartbeat-01.md': 'RCS-HEARTBEAT.md',
    'docs/runtime/parameter-settings-v3-native-01.md': 'PARAMETERS.md',
    'docs/runtime/parameter-refactor-01.md': 'PARAMETERS-HISTORY.md',
    'docs/runtime/ui-task-redesign-01.md': 'SPORTMASTER-V2.md',
    'docs/runtime/login-session-lifetime-fix-01.md': 'LOGIN-SESSION-FIX.md',
    'docs/runtime/installer-release-info-01.md': 'INSTALLATION-REQUIREMENTS.md',
    'docs/runtime/installer-single-window-01.md': 'INSTALLATION-EXPERIENCE.md',
    'docs/runtime/windows-client-compatibility-01.md': 'WINDOWS-COMPATIBILITY.md',
    'docs/runtime/hikrobot-reader-01.md': 'HIKROBOT-READER.md',
    'docs/runtime/sportmaster-commercial-delivery-01-operations.md': 'SPORTMASTER-DELIVERY.md',
    'docs/runtime/sportmaster-visual-productization-01.md': 'SPORTMASTER-UI.md',
    'docs/runtime/validation/sportmaster-alternate-ui-windows.md': 'SPORTMASTER-UI-WINDOWS.md'}
ALL_GUIDES = {**GUIDES, **SPORTMASTER_GUIDES}

ROOT_INPUTS = ['global.json', 'Directory.Build.props', 'NuGet.Config', 'ImporterPc.slnx', 'docs/licenses/MIT.txt', 'docs/licenses/Apache-2.0.txt']
SOURCE_DIRECTORIES = ['src', 'config', 'scripts', 'examples']
SOURCE_PATHS = [*SOURCE_DIRECTORIES, *ROOT_INPUTS, *ALL_GUIDES]


def source_files(root):
    paths = [root / name for name in [*ROOT_INPUTS, *ALL_GUIDES]]
    for directory in SOURCE_DIRECTORIES:
        paths.extend(f for f in (root / directory).rglob('*') if f.is_file() and
                     not {'bin', 'obj', '__pycache__'}.intersection(f.relative_to(root).parts))
    return sorted(set(paths))


def render(root, source, commit, guides=None):
    guides = GUIDES if guides is None else guides
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
        destination = guides.get(relative)
        if destination is None:
            destination = 'https://github.com/yuan2go/importer-pc/blob/' + commit + '/' + quote(relative, safe='/')
        if parsed.fragment: destination += '#' + parsed.fragment
        return '[' + label + '](' + destination + ')'
    return re.sub(r'\[([^\]\n]+)\]\(([^)\n]+)\)', link, (root / source).read_text(encoding='utf-8'))


def publish(root, output, commit, product='supply'):
    guides = SPORTMASTER_GUIDES if product == 'sportmaster' else PARCEL_GUIDES if product in ('direct-plc', 'wcs-to-plc') else GUIDES
    for source, name in guides.items():
        (output / name).write_text(render(root, source, commit, guides), encoding='utf-8')
