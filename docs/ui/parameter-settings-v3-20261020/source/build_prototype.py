#!/usr/bin/env python3
"""Build an offline, self-contained prototype. Fonts are SIL OFL Noto subsets.

Initial font build:
  python source/build_prototype.py --regular /path/NotoSansCJKsc-Regular.otf --bold /path/NotoSansCJKsc-Bold.otf
Subsequent builds reuse source/assets/*.woff. No downloads and no production calls.
"""
from pathlib import Path
import argparse, base64, hashlib, json

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'source'

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--regular', type=Path)
    parser.add_argument('--bold', type=Path)
    args = parser.parse_args()
    names = ['model.js', 'views.js', 'app.js']
    script = '\n\n'.join((SOURCE / n).read_text(encoding='utf-8') for n in names)
    style = (SOURCE / 'styles.css').read_text(encoding='utf-8')
    assets = SOURCE / 'assets'
    assets.mkdir(exist_ok=True)
    manifest = []
    if args.regular and args.bold:
        from fontTools import subset
        from fontTools.ttLib import TTFont
        text = script + style + ''.join(chr(i) for i in range(32,127))
        for weight, original in [('400', args.regular), ('700', args.bold)]:
            output = assets / f'noto-ui-{weight}.woff'
            font = TTFont(original)
            options = subset.Options()
            options.flavor = 'woff'
            sub = subset.Subsetter(options=options)
            sub.populate(text=text)
            sub.subset(font)
            font.flavor = 'woff'
            font.save(output)
            manifest.append({'file':output.name,'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'bytes':output.stat().st_size,'source':original.name,'sourceSha256':hashlib.sha256(original.read_bytes()).hexdigest(),'license':'SIL OFL 1.1; Noto-OFL.txt','purpose':'Offline synthetic UI; native Windows uses system fonts'})
        (assets / 'fonts-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for marker, weight in [('__FONT_REGULAR__','400'),('__FONT_BOLD__','700')]:
        style=style.replace(marker,base64.b64encode((assets/f'noto-ui-{weight}.woff').read_bytes()).decode('ascii'))
    html=(SOURCE/'template.html').read_text(encoding='utf-8').replace('/*__STYLE__*/',style).replace('/*__SCRIPT__*/',script.replace('</script','<\\/script'))
    (ROOT/'Prototype.html').write_text(html,encoding='utf-8')
    print(json.dumps({'prototype':str(ROOT/'Prototype.html'),'bytes':len(html.encode()),'sha256':hashlib.sha256(html.encode()).hexdigest()},ensure_ascii=False))

if __name__=='__main__': main()
