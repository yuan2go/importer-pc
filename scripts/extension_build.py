"""Declarative, committed-source extension build and immutable publication."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tempfile
import zipfile
from build_process import run


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def decode_json(value):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError('Duplicate JSON property: ' + key)
            result[key] = value
        return result
    return json.loads(value, object_pairs_hook=unique)


def read_json(path):
    return decode_json(path.read_text(encoding='utf-8'))


def relative(value):
    if not isinstance(value, str):
        raise ValueError('Expected a safe relative path')
    p = PurePosixPath(value)
    if not value or '\\' in value or ':' in value or p.is_absolute() or any(x in ('', '.', '..') for x in value.split('/')):
        raise ValueError('Expected a safe relative path: ' + str(value))
    return p


def contained(root, value):
    p = root.joinpath(*relative(value).parts)
    if p.is_symlink() or any(x.is_symlink() for x in p.parents if x != root.parent):
        raise ValueError('Linked source or payload refused: ' + str(value))
    resolved = p.resolve(strict=True)
    if not resolved.is_relative_to(root):
        raise ValueError('Path escapes its declared root: ' + value)
    return resolved


def git(root, *args):
    return subprocess.check_output(['git', *args], cwd=root, text=True).strip()


def source_snapshot(definition):
    definition = definition.resolve(strict=True)
    repo = Path(git(definition.parent, 'rev-parse', '--show-toplevel')).resolve()
    declaration = read_json(definition)
    expected = {'SchemaVersion', 'Project', 'SourceRoots', 'BuildInputs', 'Manifest', 'Modules', 'ParameterSchemas', 'Content', 'PublicContracts'}
    if set(declaration) != expected or declaration['SchemaVersion'] != 1:
        raise ValueError('Unsupported extension build declaration fields/version')
    scopes = declaration['SourceRoots'] + declaration['BuildInputs']
    if not declaration['SourceRoots'] or any(not isinstance(x, str) for x in scopes):
        raise ValueError('Declare source roots and build input files')
    for scope in scopes:
        if scope != '.':
            contained(repo, scope)
    if git(repo, 'rev-parse', '--is-shallow-repository') == 'true':
        raise ValueError('Fetch complete plugin history before assigning source identity')
    if git(repo, 'status', '--porcelain', '--untracked-files=all', '--', *scopes):
        raise ValueError('Plugin source and build declarations must be clean committed inputs')
    names = subprocess.check_output(['git', 'ls-files', '-z', '--', *scopes], cwd=repo).decode().split('\0')
    files = {name: contained(repo, name).read_bytes() for name in sorted(set(filter(None, names)))}
    required = [definition.relative_to(repo).as_posix(), declaration['Project'], declaration['Manifest'], declaration['Modules'], *declaration['ParameterSchemas'].values()]
    if any(name not in files for name in required):
        raise ValueError('Project, definition, manifest, modules and schemas must belong to the committed source snapshot')
    commit = git(repo, 'log', '-1', '--format=%H', '--', *scopes)
    if not re.fullmatch('[0-9a-f]{40}', commit):
        raise ValueError('No committed source identity')
    fingerprint = hashlib.sha256(b''.join(name.encode() + b'\0' + data + b'\0' for name, data in files.items())).hexdigest()
    return declaration, files, commit, fingerprint


def validate_payload_path(value):
    relative(value)
    if len(value) > 200 or any(ord(c) < 32 or ord(c) > 126 or c in '<>"|?*' for c in value):
        raise ValueError('Unsafe package path: ' + value)
    for part in value.split('/'):
        if len(part) > 100 or part.endswith(('.', ' ')) or part.split('.')[0].upper() in {'CON', 'PRN', 'AUX', 'NUL', *(f'COM{i}' for i in range(1, 10)), *(f'LPT{i}' for i in range(1, 10))}:
            raise ValueError('Unsafe Windows package path: ' + value)
    if value.lower() in {'manifest.json', 'manifest.sig'}:
        raise ValueError('Reserved package metadata path')


def host_inventory(directory):
    return {f.relative_to(directory).as_posix(): digest(f) for f in sorted(directory.rglob('*')) if f.is_file()}


def build(args):
    contract = args.contract.resolve(strict=True)
    if contract.name != 'Ipc.Platform.dll':
        raise ValueError('--contract must name the frozen Ipc.Platform.dll')
    host = (args.check_host or contract.parent / 'Ipc.Product.dll').resolve(strict=True)
    if host.name != 'Ipc.Product.dll':
        raise ValueError('The frozen static checker must be Ipc.Product.dll')
    destination = args.output.resolve()
    identity_destination = Path(str(destination) + '.identity.json')
    if destination.exists() or identity_destination.exists():
        raise FileExistsError('Use a new package path; retain all original package identities')
    declaration, sources, commit, fingerprint = source_snapshot(args.definition)
    contract_names = ['Ipc.Platform', *declaration['PublicContracts']]
    checker_contracts = {}
    for name in contract_names:
        original = contract if name == 'Ipc.Platform' else contained(contract.parent, name + '.dll')
        checker = contained(host.parent, name + '.dll')
        if digest(original) != digest(checker):
            raise ValueError('Static checker public contract differs from frozen target: ' + name)
        checker_contracts[name] = digest(checker)
    checker_before = host_inventory(host.parent)
    trust_path = args.trust.resolve(strict=True)
    trust_before = digest(trust_path)
    trust = read_json(trust_path)
    if any(re.search(br'-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----', data) for data in sources.values()):
        raise ValueError('Private keys must stay outside committed plugin source inputs')
    if set(trust) != {'SchemaVersion', 'Publishers'} or trust['SchemaVersion'] != 1:
        raise ValueError('Unsupported independent trust policy')
    publishers = trust['Publishers']
    selected = [p for p in publishers if p['Id'] == args.publisher]
    if len(selected) != 1 or len({p['Id'] for p in publishers}) != len(publishers):
        raise ValueError('Publisher must be uniquely present in independent trust')
    publisher = selected[0]
    if 'PRIVATE KEY' in publisher['PublicKeyPem']:
        raise ValueError('Trust policy must contain public keys only')
    manifest = decode_json(sources[declaration['Manifest']])
    expected_manifest = {'SchemaVersion', 'Id', 'Version', 'Runtime', 'Rid', 'ContractMajor', 'Mode', 'Dependencies', 'Prerequisites'}
    if set(manifest) != expected_manifest or manifest['SchemaVersion'] != 1 or manifest['Mode'] not in ('Simulation', 'Production'):
        raise ValueError('Manifest template must declare exact package metadata without generated identities')
    if manifest['Mode'] == 'Production' and publisher['TestOnly']:
        raise ValueError('TestOnly publisher is forbidden for a Production package')
    version = args.version or manifest['Version']
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version):
        raise ValueError('An immutable three-part package version is required')
    dependencies = [p.resolve(strict=True) for p in args.dependency_package]
    dependency_before = {str(p): digest(p) for p in dependencies}
    dependency_dlls = {}
    for dependency in dependencies:
        with zipfile.ZipFile(dependency) as archive:
            metadata = archive.getinfo('manifest.json')
            if metadata.file_size > 512 * 1024:
                raise ValueError('Dependency manifest quota exceeded')
            dep = decode_json(archive.read(metadata))
            for file in dep['Files']:
                if file['Path'].lower().endswith('.dll'):
                    name = Path(file['Path']).name.lower()
                    if name in dependency_dlls and dependency_dlls[name] != file['Sha256'].lower():
                        raise ValueError('Conflicting dependency DLL content')
                    dependency_dlls[name] = file['Sha256'].lower()
    before = host_inventory(contract.parent)
    with tempfile.TemporaryDirectory(prefix='ipc-extension-build-') as temporary:
        work = Path(temporary).resolve()
        isolated = work / 'source'
        isolated.mkdir()
        for name, data in sources.items():
            copy = isolated / name
            copy.parent.mkdir(parents=True, exist_ok=True)
            copy.write_bytes(data)
        project = contained(isolated, declaration['Project'])
        output = work / 'bin'
        properties = [f'-p:IpcContractPath={contract}', f'-p:IpcContractDirectory={contract.parent}', f'-p:SourceRevisionId={commit}', f'-p:PathMap={work}=/_/independent-extension', '-p:Deterministic=true', f'-p:Version={version}']
        run([args.dotnet, 'restore', str(project), '--locked-mode', *properties], isolated)
        run([args.dotnet, 'build', str(project), '--no-restore', '-c', 'Release', '-m:1', '-nr:false', *properties, '-o', str(output)], isolated)
        payload = {}
        for item in declaration['Content']:
            if set(item) != {'Root', 'Source', 'Path'} or item['Root'] not in ('build', 'source'):
                raise ValueError('Each payload needs explicit Root, Source and Path')
            name = item['Path']
            validate_payload_path(name)
            if name.lower() in {n.lower() for n in payload}:
                raise ValueError('Duplicate package payload path')
            file = contained(output if item['Root'] == 'build' else isolated, item['Source'])
            if item['Root'] == 'source' and item['Source'] not in sources:
                raise ValueError('Payload source is not a committed input')
            if file.suffix.lower() in ('.pem', '.key', '.pfx', '.p12'):
                raise ValueError('Key or certificate payloads do not establish trust and are refused')
            payload[name] = file.read_bytes()
        if not 1 <= len(payload) <= 256 or any(len(v) > 32 * 1024 * 1024 for v in payload.values()) or sum(map(len, payload.values())) > 128 * 1024 * 1024:
            raise ValueError('Package payload quota exceeded')
        # All non-host managed DLLs must be explicitly owned by this package or
        # a signed exact dependency; accidental copy-local outputs cannot vanish.
        declared_dlls = {Path(name).name.lower() for name in payload if name.lower().endswith('.dll')}
        host_dlls = {f.name.lower() for f in contract.parent.glob('*.dll') if f.name.startswith('Ipc.')}
        exact_dependency_dlls = {f.name.lower() for f in output.glob('*.dll') if dependency_dlls.get(f.name.lower()) == digest(f)}
        undeclared = {f.name.lower() for f in output.glob('*.dll')} - declared_dlls - host_dlls - exact_dependency_dlls
        if undeclared:
            raise ValueError('Declare private dependency DLLs in Content: ' + ', '.join(sorted(undeclared)))
        if any(Path(name).name.lower() in host_dlls for name in payload):
            raise ValueError('Frozen host DLLs cannot be repackaged as plugin payload')
        modules = decode_json(sources[declaration['Modules']])
        if not isinstance(modules, list) or len(modules) > 32:
            raise ValueError('Modules must be a bounded array')
        if set(declaration['ParameterSchemas']) != {m['Id'] for m in modules}:
            raise ValueError('Every module must have exactly one parameter schema mapping')
        for module in modules:
            if set(module) != {'Id', 'Assembly', 'EntryType', 'Kind', 'MaximumInstances'}:
                raise ValueError('Module declaration fields are invalid; schema is supplied separately')
            module['Parameters'] = decode_json(sources[declaration['ParameterSchemas'][module['Id']]])
            if module['Assembly'] not in payload:
                raise ValueError('Module assembly absent from exact payload')
        manifest.update(Version=version, Publisher=args.publisher, SourceCommit=commit, Modules=modules,
                        Files=[dict(Path=name, Length=len(data), Sha256=hashlib.sha256(data).hexdigest().upper()) for name, data in sorted(payload.items())],
                        HostContractSha256=digest(contract).upper())
        if declaration['PublicContracts']:
            manifest['PublicContracts'] = {name: digest(contained(contract.parent, name + '.dll')).upper() for name in declaration['PublicContracts']}
        manifest_file = work / 'manifest.json'
        manifest_file.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        if manifest_file.stat().st_size > 512 * 1024:
            raise ValueError('Manifest quota exceeded')
        signature = work / 'manifest.sig'
        options = ['-sigopt', 'rsa_padding_mode:pss', '-sigopt', 'rsa_pss_saltlen:digest']
        run(['openssl', 'dgst', '-sha256', '-sign', str(args.key.resolve(strict=True)), *options, '-out', str(signature), str(manifest_file)])
        public = work / 'publisher.pem'
        public.write_text(publisher['PublicKeyPem'], encoding='utf-8')
        run(['openssl', 'dgst', '-sha256', '-verify', str(public), *options, '-signature', str(signature), str(manifest_file)])
        staged = work / 'package.ipcx'
        with zipfile.ZipFile(staged, 'x', zipfile.ZIP_DEFLATED) as archive:
            for name, data in sorted({'manifest.json': manifest_file.read_bytes(), 'manifest.sig': signature.read_bytes(), **payload}.items()):
                info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o100644 << 16
                archive.writestr(info, data)
        dependency_file = work / 'dependencies.json'
        dependency_file.write_text(json.dumps([str(p) for p in dependencies]), encoding='utf-8')
        run([args.dotnet, str(host), '--action', 'extension-check', '--mode', manifest['Mode'], '--file', str(staged), '--extension-trust', str(args.trust.resolve()), '--dependency-files', str(dependency_file)])
        if trust_before != digest(trust_path):
            raise ValueError('Independent trust changed during build; no package published')
        if dependency_before != {str(p): digest(p) for p in dependencies}:
            raise ValueError('Dependency package changed during build; no package published')
        if before != host_inventory(contract.parent):
            raise ValueError('Frozen host changed during independent build; no package published')
        if checker_before != host_inventory(host.parent):
            raise ValueError('Frozen checker changed during independent build; no package published')
        # Recheck committed inputs after build/check; never publish a stale identity.
        _, after_sources, after_commit, after_fingerprint = source_snapshot(args.definition)
        if (after_sources, after_commit, after_fingerprint) != (sources, commit, fingerprint):
            raise ValueError('Plugin inputs changed during build; no package published')
        identity = dict(Package=destination.name, PackageSha256=digest(staged), PluginSourceCommit=commit, PluginSourceSha256=fingerprint,
                        DefinitionSha256=digest(args.definition.resolve()),
                        HostContractSha256=digest(contract), FrozenHostUnchanged=True, FrozenCheckerUnchanged=True, CheckerContractsSha256=checker_contracts, Publisher=args.publisher, Version=version,
                        ExtensionContract=manifest['ContractMajor'], Signature='RSA-PSS/SHA256', Mode=manifest['Mode'],
                        ManifestSha256=digest(manifest_file), TrustSha256=trust_before, PayloadSha256={name: hashlib.sha256(data).hexdigest() for name, data in sorted(payload.items())},
                        DependencyPackages=[dict(Package=p.name, Sha256=digest(p)) for p in dependencies], StaticAcceptance='PASS_FROZEN_HOST_EXTENSION_CHECK')
        destination.parent.mkdir(parents=True, exist_ok=True)
        # Exclusive creation prevents races/overwriting a previously distributed identity.
        # On any interruption remove only the new paths created by this invocation.
        created = []
        try:
            # Hard-link a fully written sibling into place: exclusive and atomic.
            # The signed archive is the final commit marker after its identity.
            for target, data in [(identity_destination, (json.dumps(identity, indent=2) + '\n').encode()), (destination, staged.read_bytes())]:
                with tempfile.NamedTemporaryFile(dir=destination.parent, prefix='.ipc-publish-', delete=False) as file:
                    pending = Path(file.name)
                    try:
                        file.write(data)
                        file.flush()
                        os.fsync(file.fileno())
                        os.link(pending, target)
                        created.append(target)
                    finally:
                        pending.unlink(missing_ok=True)
        except BaseException:
            for path in reversed(created):
                path.unlink(missing_ok=True)
            raise
        return identity
