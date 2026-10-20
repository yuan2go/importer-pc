"""Rebuild the offline prototype without npm or third-party Python libraries."""
from pathlib import Path
import base64

HERE=Path(__file__).resolve().parent
out=HERE.parent/'Sportmaster-Prototype-Rebuilt.html'
faces=''
for weight in [400,700]:
 raw=base64.b64encode((HERE/'assets'/f'noto-ui-{weight}.woff').read_bytes()).decode()
 faces+=f"@font-face{{font-family:'Noto UI';font-weight:{weight};font-display:block;src:url(data:font/woff;base64,{raw}) format('woff')}}\n"
css=(HERE/'styles.css').read_text()
js='\n\n'.join((HERE/p).read_text() for p in ['app.js','aux.js','scenarios.js'])
html='''<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; font-src data:; img-src data: blob:; connect-src 'none'"><title>Sportmaster 供包台 · 交互设计原型</title><style>'''+faces+css+'''</style></head><body><div id="app"></div><div id="dialogs"></div><div id="tray"></div><div id="toast-area" aria-live="polite"></div><script>'''+js+'''</script></body></html>'''
out.write_text(html,encoding='utf-8')
print(out)
