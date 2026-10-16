"""Resolve a declared product closure. No latest-version lookup or executable discovery."""
import hashlib
import json
from pathlib import Path


def resolve(file: Path, name: str):
    data = json.loads(file.read_text())
    if set(data) != {'schema', 'components', 'compositions', 'required'} or data['schema'] != 1:
        raise ValueError('Unsupported composition schema')
    components = data['components']
    roots = data['compositions'][name]
    seen, visiting, ordered = set(), set(), []

    def visit(key):
        if key in seen:
            return
        if key in visiting or key not in components:
            raise ValueError('Missing/cyclic composition dependency: ' + key)
        component = components[key]
        if set(component) != {'version', 'contract', 'dependencies', 'delivery'} or component['contract'] != 1:
            raise ValueError('Invalid exact component contract')
        if component['delivery'] not in ('core', 'offline-bundle'):
            raise ValueError('Unknown delivery class')
        visiting.add(key)
        for dependency in component['dependencies']:
            visit(dependency)
        visiting.remove(key)
        seen.add(key)
        ordered.append(dict(id=key, **component))

    for key in roots:
        visit(key)
    if not set(data['required']).issubset(seen):
        raise ValueError('Core management/recovery/business dependencies cannot be removed')
    return dict(schema=1, composition=name, definition_sha256=hashlib.sha256(file.read_bytes()).hexdigest(), components=ordered)
