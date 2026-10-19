"""Sportmaster delivery evidence policy. No deployment/configuration authority."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import os
import shutil
import subprocess

CASES = {
    'core': ('engineering', '冻结实际DLL的COMM夹具；CAS/首配修复/Unknown/备份/支持摘要'),
    'wpf': ('engineering', '冻结WPF：分步导航、输入保留、检查失效、帮助搜索、共享实例'),
    'media': ('engineering', '统一发行器双目标库存/ZIP/源码身份全部校验'),
    'removal': ('engineering', '冻结卸载内核：白名单/真实占用/中断重入；不等于系统卸载'),
    'windows-install': ('production', '隔离Windows：指定SID、非默认目录、helper ACL、登记/快捷方式、非提权启动、错误账户拒绝'),
    'windows-repair': ('production', '同一介质：未确认配置启动失败修复、系统登记修复、并排升级及中断继续、错误介质拒绝'),
    'windows-uninstall': ('production', '系统入口保留数据卸载、真实占用重启、中断恢复、共享组件及数据保留'),
    'windows-migration': ('production', '完整恢复集、新根隔离、旧根退役、双账户/换机DPAPI拒绝、原请求不重发'),
    'field': ('production', '真实SDK/合同/设备/实体许可、连续运行与异常恢复、目标触控/DPI、部署资格'),
    'signoff': ('production', '可信分发签名、逐项资格和获授权负责方放行；不能用手填PASS代替证书/批准'),
}
STATES = {'PASS', 'FAIL', 'NOT_RUN', 'BLOCKED'}


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def artifact(root, name):
    if not isinstance(name, str) or not name or '\\' in name or ':' in name or any(p in ('', '.', '..') for p in name.split('/')) or PurePosixPath(name).is_absolute():
        raise ValueError('Unsafe evidence path')
    root = Path(root).resolve()
    path = root.joinpath(name)
    for part in (path, *path.parents):
        if part == root:
            break
        if part.is_symlink() or part.is_junction():
            raise ValueError('Linked evidence refused')
    if not path.resolve().is_relative_to(root) or not path.is_file():
        raise ValueError('Missing/escaping evidence')
    return path


def evaluate(root, report, source, inventory, level):
    if level not in ('engineering', 'production') or not re.fullmatch('[0-9a-f]{40}', source):
        raise ValueError('Invalid gate level/source')
    if report.get('SourceCommit') != source or report.get('MediumInventorySha256') != inventory:
        raise ValueError('Evidence belongs to another source/medium')
    rows = report['Cases']
    if len(rows) != len(CASES) or {r['Id'] for r in rows} != set(CASES):
        raise ValueError('Missing, duplicate or unknown mandatory cases')
    pending = []
    for row in rows:
        case, status = row['Id'], row['Status']
        if status not in STATES or not str(row.get('Reason', '')).strip():
            raise ValueError('Invalid/missing case status/reason: ' + case)
        if status == 'PASS':
            if not row.get('Evidence'):
                raise ValueError('PASS without evidence: ' + case)
            for entry in row['Evidence']:
                path = artifact(root, entry['Path'])
                if digest(path) != entry['Sha256']:
                    raise ValueError('Changed evidence: ' + case)
            # Engineering receipts are produced by the real frozen-media fixtures.
            # Human production attestations still need independent authorized review.
            if CASES[case][0] == 'engineering':
                receipt = json.loads(artifact(root, row['Evidence'][0]['Path']).read_bytes())
                if receipt.get('Status') != 'PASS' or receipt.get('SourceCommit') != source or receipt.get('MediumInventorySha256') != inventory:
                    raise ValueError('Frozen execution receipt differs: ' + case)
                if case != 'media' and receipt.get('ExitCode') != 0:
                    raise ValueError('Execution failed: ' + case)
                if case == 'core' and receipt.get('FixtureCommit', source) != source:
                    raise ValueError('Foreign core fixture')
            elif not str(row.get('Reviewer', '')).strip():
                raise ValueError('Production attestation needs accountable reviewer: ' + case)
        if (level == 'production' or CASES[case][0] == 'engineering') and status != 'PASS':
            pending.append({'Id': case, 'Status': status, 'Reason': row['Reason']})
    return {'Status': 'FAIL' if pending else 'PASS', 'Level': level, 'SourceCommit': source,
            'MediumInventorySha256': inventory, 'Outstanding': pending,
            'ProductionApproval': 'NOT_GRANTED_BY_THIS_TOOL',
            'TrustBoundary': 'Hashes prove custody, not truth of test/qualification/approval attestations. Authorized release review remains mandatory.'}


def plan(source, inventory):
    return dict(SchemaVersion=1, SourceCommit=source, MediumInventorySha256=inventory,
                Cases=[dict(Id=key, Status='BLOCKED' if key in ('field', 'signoff') else 'NOT_RUN',
                            Reason=text, Evidence=[]) for key, (_, text) in CASES.items()])


def distribution_signature(medium, approved_signer):
    # A release manifest string or a manually supplied PASS is never signature evidence.
    if not approved_signer:
        return dict(Status='BLOCKED', Reason='No externally approved public signer certificate supplied; no distribution signature qualification')
    shell = shutil.which('pwsh')
    if os.name != 'nt' or not shell:
        return dict(Status='BLOCKED', Reason='Actual Windows Authenticode verification requires Windows PowerShell 7')
    result = subprocess.run([shell, '-NoProfile', '-File', str(Path(__file__).with_name('verify-distribution-signature.ps1')),
                             '-Media', str(medium), '-ApprovedSigner', str(approved_signer)], capture_output=True, text=True, encoding='utf-8')
    if result.returncode != 0:
        return dict(Status='BLOCKED', Reason='Windows rejected distribution signatures or approved certificate', Detail=result.stdout.strip())
    receipt = json.loads(result.stdout)
    if receipt.get('Status') != 'PASS' or not receipt.get('Files'):
        raise ValueError('Signature verifier returned incomplete evidence')
    return receipt
