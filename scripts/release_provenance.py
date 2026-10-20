"""Human-readable source identity, frozen into the existing release inventories."""
import datetime
import json
import re
import subprocess

MODULE_SHORT_NAMES = {'sportmaster': 'SM', 'direct-plc': 'DIRECTPLC', 'wcs-to-plc': 'WCSTOPLC'}


def git(root, *args):
    return subprocess.check_output(['git', *args], cwd=root, text=True).strip()


def capture(root, source, source_branch, application, version, target):
    if source_branch is None:
        result = subprocess.run(['git', 'symbolic-ref', '--quiet', '--short', 'HEAD'], cwd=root,
                                text=True, stdout=subprocess.PIPE)
        if result.returncode:
            source_branch = 'DETACHED'
        else:
            source_branch = result.stdout.strip()
    verified = source_branch != 'DETACHED'
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._/-]{0,254}', source_branch):
        raise ValueError('Use a verifiable Git branch name, not a URL or arbitrary label')
    if verified:
        subprocess.run(['git', 'check-ref-format', '--branch', source_branch], cwd=root,
                       check=True, stdout=subprocess.DEVNULL)
        if git(root, 'rev-parse', '--verify', '--end-of-options', source_branch + '^{commit}') != source:
            raise ValueError('Source branch and frozen HEAD must identify the same commit')
    if git(root, 'rev-parse', 'HEAD') != source:
        raise ValueError('Source branch and frozen HEAD must identify the same commit')
    return dict(SchemaVersion=1, Product='CainiaoSupplyStation', ApplicationId=application,
                BusinessModuleShortName=MODULE_SHORT_NAMES[application],
                BusinessVersion=version, Target=target, SourceBranch=source_branch, SourceCommit=source,
                SourceBranchVerified=verified,
                SourceCommitUtc=datetime.datetime.fromisoformat(git(root, 'show', '-s', '--format=%cI', source)).astimezone(datetime.timezone.utc).isoformat(),
                PackagedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='seconds'),
                SourceRepository='https://github.com/yuan2go/importer-pc', FrozenGitExport=True)


def publish(output, record):
    text = ('CainiaoSupplyStation - Build information\n'
            + '\n'.join(f'{name}: {record[name]}' for name in
                        ('ApplicationId', 'BusinessModuleShortName', 'BusinessVersion', 'Target', 'SourceBranch', 'SourceCommit',
                         'SourceCommitUtc', 'PackagedUtc', 'SourceRepository'))
            + '\n\nDiagnostic record only. The protected deployment registration remains authoritative.\n')
    encoded = json.dumps(record, ensure_ascii=False, indent=2) + '\n'
    for directory in [output, output / 'app', *([output / 'desktop'] if (output / 'desktop').exists() else [])]:
        (directory / 'BUILD-INFO.json').write_text(encoded, encoding='utf-8', newline='\n')
        (directory / 'BUILD-INFO.txt').write_text(text, encoding='utf-8', newline='\n')


def installer_name(application, version):
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', version) or any(int(part) > 65535 for part in version.split('.')):
        raise ValueError('Installer needs a numeric three-part business version')
    if application not in MODULE_SHORT_NAMES:
        raise ValueError('Installer needs a supported selected business module')
    return f'CainiaoSupplyStation_{MODULE_SHORT_NAMES[application]}_Setup_v{version}_win-x64.exe'
