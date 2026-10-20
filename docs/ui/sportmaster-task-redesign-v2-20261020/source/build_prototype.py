"""Build the standalone offline UI prototype. Python standard library only.

Run in this checkout: python3 docs/ui/sportmaster-task-redesign-v2-20261020/source/build_prototype.py
All source data are synthetic. No service call, credential or device operation is performed.
"""
from pathlib import Path
import base64
import json

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
REPO = HERE.parents[3]
ASSETS = HERE / 'assets'
fonts = ''.join(
    "@font-face{font-family:'Noto UI';font-weight:" + str(weight) + ";font-display:block;src:url(data:font/woff;base64,"
    + base64.b64encode((ASSETS / f'noto-ui-{weight}.woff').read_bytes()).decode()
    + ") format('woff')}\n" for weight in (400, 700)
)
brand = 'data:image/png;base64,' + base64.b64encode((ASSETS / 'cainiao.png').read_bytes()).decode()
scenes = json.loads((HERE / 'scenes.json').read_text())
script = 'const BRAND_DATA=' + json.dumps(brand) + ';\nconst SCENES=' + json.dumps(scenes, ensure_ascii=False) + ';\n'
script += '\n\n'.join((HERE / name).read_text() for name in ('model.js', 'views.js', 'views-config.js', 'startup.js', 'aux.js', 'app.js', 'scenarios.js'))
css = (HERE / 'styles.css').read_text()
html = '<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
html += '<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; style-src \'unsafe-inline\'; script-src \'unsafe-inline\'; font-src data:; img-src data: blob:; connect-src \'none\'">'
html += '<title>菜鸟供包台 · Sportmaster UI V2 · 离线交互原型</title><style>' + fonts + css + '</style></head><body>'
html += '<div id="app"></div><div id="dialogs"></div><div id="toasts" aria-live="polite"></div><script>' + script + '</script></body></html>'
(OUT / 'Prototype.html').write_text(html, encoding='utf-8')
print(json.dumps({'prototype': str(OUT / 'Prototype.html'), 'named_scenes': len(scenes), 'bytes': len(html.encode())}, ensure_ascii=False))
