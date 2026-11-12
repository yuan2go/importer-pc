"""Plan Industrial core from the checked-out Git diff and verify its final results."""
import argparse
import json
import os
from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[1]
DESKTOPS = ['Sportmaster', 'DirectPlc', 'WcsToPlc']
ROOT_GUIDES = {'AGENTS.md', 'README.md', 'CHANGELOG.md'}


def changed_paths(root, event_name, event):
    if event_name == 'pull_request':
        base = event['pull_request']['base']['sha']
    elif event_name == 'push':
        base = event['before']
    else:
        return None
    if not base or set(base) == {'0'}:
        return None
    try:
        # Both sides of moves/deletions matter; no hosting API page/file limit.
        result = subprocess.run(
            ['git', 'diff', '--name-only', '--no-renames', '-z', base, 'HEAD', '--'],
            cwd=root, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    except subprocess.CalledProcessError:
        return None  # A missing push base must request full verification, never skip it.
    return [name.decode('utf-8') for name in result.stdout.split(b'\0') if name]


def plan(paths, scope=None):
    core = False
    desktops = []
    branches = False
    printing = False
    if scope is not None:
        if scope not in {'regression', 'branches', 'print', 'all'}:
            raise ValueError(f'Unknown Industrial core scope: {scope}')
        core = scope in {'regression', 'all'}
        desktops = DESKTOPS.copy() if core else []
        branches = scope in {'branches', 'all'}
        printing = scope in {'print', 'all'}
        reason = f'Manual scope: {scope}'
    elif paths is None:
        core, desktops = True, DESKTOPS.copy()
        reason = 'Git comparison unavailable: full regression'
    else:
        for path in paths:
            # These guides are embedded by Sportmaster.Desktop; prose elsewhere
            # under docs is not compiled. Non-Markdown inputs (including UI tokens)
            # and any unknown path retain the complete existing verification.
            if path.startswith('docs/operator/') and path.endswith('.md'):
                desktops = ['Sportmaster']
            elif path in ROOT_GUIDES or path.startswith('docs/') and path.endswith('.md'):
                continue
            else:
                core, desktops = True, DESKTOPS.copy()
                break
        reason = ('Code, configuration, tooling or other build inputs: full regression' if core else
                  'Embedded operator guides: SM compilation and document checks' if desktops else
                  'Documentation only: document checks')
    return {'core': core, 'desktops': desktops,
            'branches': branches, 'print': printing, 'reason': reason}


def workflow_outputs(selected):
    return {key: json.dumps(value, separators=(',', ':'))
            for key, value in selected.items() if key != 'reason'}


def check_results(needs):
    repository = needs['repository-checks']
    if repository['result'] != 'success':
        raise ValueError(f"Repository checks did not succeed: {repository['result']}")
    outputs = repository['outputs']
    expected = {
        'core-regression': json.loads(outputs['core']),
        'windows-build': bool(json.loads(outputs['desktops'])),
        'product-branch-evidence': json.loads(outputs['branches']),
        'operator-print': json.loads(outputs['print']),
    }
    errors = []
    for job, required in expected.items():
        actual = needs[job]['result']
        wanted = 'success' if required else 'skipped'
        if actual != wanted:
            errors.append(f'{job}: expected {wanted}, got {actual}')
    if errors:
        raise ValueError('\n'.join(errors))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    commands.add_parser('plan')
    commands.add_parser('gate')
    args = parser.parse_args()
    if args.command == 'plan':
        event_name = os.environ['GITHUB_EVENT_NAME']
        event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text(encoding='utf-8'))
        scope = event.get('inputs', {}).get('scope', 'regression') if event_name == 'workflow_dispatch' else None
        paths = None if scope is not None else changed_paths(ROOT, event_name, event)
        selected = plan(paths, scope)
        outputs = workflow_outputs(selected)
        with Path(os.environ['GITHUB_OUTPUT']).open('a', encoding='utf-8') as output:
            output.writelines(f'{key}={value}\n' for key, value in outputs.items())
        with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a', encoding='utf-8') as summary:
            summary.write('## Industrial core selection\n\n' + selected['reason'] + '\n\n')
            summary.writelines(f'- {key}: `{value}`\n' for key, value in outputs.items())
        print(json.dumps(selected, indent=2))
    else:
        check_results(json.loads(os.environ['IPC_CI_NEEDS']))
        print('PASS Industrial core: every selected job succeeded; only unselected jobs were skipped')


if __name__ == '__main__':
    main()
