#!/usr/bin/env python3
"""Build the independent reference source against a frozen contract, then sign exact bytes.

Signing key is explicitly supplied, never generated into a client/repository or added to trust.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

p = argparse.ArgumentParser()
p.add_argument('--contract', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--key', type=Path, required=True)
p.add_argument('--publisher', required=True)
p.add_argument('--trust', type=Path, required=True)
p.add_argument('--version', default='1.0.0')
p.add_argument('--dotnet', default='dotnet')
a = p.parse_args()
root = Path(__file__).resolve().parent.parent
contract = a.contract.resolve(strict=True)
destination = a.output.resolve()
if destination.exists():
    raise FileExistsError('Use a new package path; retain old versions')
project = root / 'examples/BarcodeExtension/BarcodeExtension.csproj'
if subprocess.check_output(['git', 'status', '--porcelain', '--', 'examples/BarcodeExtension'], cwd=root, text=True).strip():
    raise ValueError('Independent plugin publication requires committed example source inputs')
if subprocess.check_output(['git', 'rev-parse', '--is-shallow-repository'], cwd=root, text=True).strip() == 'true':
    raise ValueError('Fetch full history before assigning the independent plugin source commit')

host_before = {f.name: hashlib.sha256(f.read_bytes()).hexdigest() for f in contract.parent.glob('Ipc.*.dll')}
commit = subprocess.check_output(['git', 'log', '-1', '--format=%H', '--', 'examples/BarcodeExtension'], cwd=root, text=True).strip()
if not commit:
    raise ValueError('Commit the independent example sources before building distribution packages')
with tempfile.TemporaryDirectory(prefix='ipc-extension-build-') as temporary:
    work = Path(temporary)
    for command in [
        [a.dotnet, 'restore', str(project), '--locked-mode', f'-p:IpcContractPath={contract}'],
        [a.dotnet, 'build', str(project), '--no-restore', '-c', 'Release', '-m:1', '-nr:false', f'-p:IpcContractPath={contract}', f'-p:Version={a.version}', '-o', str(work / 'bin')],
    ]:
        subprocess.run(command, cwd=root, check=True)
    binary = work / 'bin/Ipc.Reference.BarcodeExtension.dll'
    parameters = [
        dict(Key='health.interval-ms', Group='健康 / 可靠确认', Label='合成心跳间隔', Type='integer', Required=True, Default='1000', Minimum=100, Maximum=2000, Unit='ms'),
        dict(Key='input.prefix', Group='输入 / 合成条码', Label='允许的条码前缀', Type='string', Default='', MaximumLength=32, Description='不匹配时产生无码观察；不沿用旧码。仅合成模式。'),
        dict(Key='input.labels', Group='输入 / 合成条码', Label='来源标签（每行一项）', Type='string-list', Default='[]', MaximumLength=256, MaximumItems=16),
        dict(Key='metadata.contract', Group='身份 / 版本', Label='来源契约', Type='string', Default='SIMULATION-OBSERVATION-v1', ReadOnly=True),
        dict(Key='credential.reference', Group='身份 / 凭据引用', Label='凭据引用（不输入密码）', Type='secret-reference', Sensitive=True, Description='只存 credential:名称；本合成源不使用或读取生产凭据。'),
        dict(Key='fault.fail-start', Group='工程 / 显式模拟故障', Label='启动失败注入', Type='boolean', Default='false', Description='只用于参考环境验证升级失败保旧。'),
        dict(Key='fault.stop-delay-ms', Group='工程 / 显式模拟故障', Label='停止延迟注入', Type='integer', Default='0', Minimum=0, Maximum=20000, Unit='ms', Description='验证Stop未终止时保留依赖和实际资源租约。'),
    ]
    manifest = dict(SchemaVersion=1, Id='reference.barcode', Version=a.version, Publisher=a.publisher, Runtime='net10.0', Rid='any',
                    ContractMajor=1, Mode='Simulation', SourceCommit=commit, Dependencies=[],
                    Files=[dict(Path=binary.name, Length=binary.stat().st_size, Sha256=hashlib.sha256(binary.read_bytes()).hexdigest().upper())],
                    Modules=[dict(Id='barcode-source', Assembly=binary.name, EntryType='Ipc.Reference.BarcodeExtension.BarcodeModule', Kind='observation', MaximumInstances=8, Parameters=parameters)], Prerequisites=[],
                    HostContractSha256=hashlib.sha256(contract.read_bytes()).hexdigest().upper())
    manifest_file = work / 'manifest.json'
    manifest_file.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    signature = work / 'manifest.sig'
    subprocess.run(['openssl', 'dgst', '-sha256', '-sign', str(a.key.resolve(strict=True)), '-sigopt', 'rsa_padding_mode:pss', '-sigopt', 'rsa_pss_saltlen:digest', '-out', str(signature), str(manifest_file)], check=True)
    trust = json.loads(a.trust.read_text())
    publisher = next(x for x in trust['Publishers'] if x['Id'] == a.publisher)
    if trust['SchemaVersion'] != 1 or publisher['TestOnly'] is not True:
        raise ValueError('Independent reference package needs explicitly separate TEST ONLY trust')
    public = work / 'publisher.pem'
    public.write_text(publisher['PublicKeyPem'])
    subprocess.run(['openssl', 'dgst', '-sha256', '-verify', str(public), '-sigopt', 'rsa_padding_mode:pss', '-sigopt', 'rsa_pss_saltlen:digest', '-signature', str(signature), str(manifest_file)], check=True)
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(destination, 'x', zipfile.ZIP_DEFLATED) as archive:
        for file in [manifest_file, signature, binary]:
            archive.write(file, file.name)
    host_after = {f.name: hashlib.sha256(f.read_bytes()).hexdigest() for f in contract.parent.glob('Ipc.*.dll')}
    if host_before != host_after:
        raise ValueError('Frozen host changed during independent extension build')
    result = dict(Package=destination.name, PackageSha256=hashlib.sha256(destination.read_bytes()).hexdigest(), PluginSourceCommit=commit,
                  HostContractSha256=hashlib.sha256(contract.read_bytes()).hexdigest(), FrozenHostUnchanged=True, Publisher=a.publisher,
                  Version=a.version, ExtensionContract=1, Signature='RSA-PSS/SHA256', Mode='Simulation',
                  PluginSourceSha256=hashlib.sha256(b''.join(f.relative_to(root).as_posix().encode() + b'\0' + f.read_bytes() + b'\0' for f in sorted(project.parent.iterdir()) if f.is_file())).hexdigest())
    Path(str(destination) + '.identity.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result))
