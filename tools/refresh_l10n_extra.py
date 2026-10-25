import re, json, time, urllib.parse, urllib.request
# Parse the hand-written table.
src = open('HtfFortress/L10n.cs', encoding='utf-8').read()
start = src.index('private static readonly Dictionary<string, string[]> Table'); start = src.index('{', src.index('Table =', start)); i = start + 1; entries = []
while i < len(src):
    j = src.find('["', i)
    if j < 0: break
    k = src.index('"]', j + 2); key = src[j + 2:k]; m = re.match(r'\s*=\s*new\[\]\s*\{', src[k + 2:])
    if not m: i = k + 2; continue
    p = k + 2 + m.end(); vals = []; q = p
    while q < len(src):
        while q < len(src) and src[q] in ' \t\r\n,': q += 1
        if q < len(src) and src[q] == '}': q += 1; break
        if q >= len(src) or src[q] != '"': raise SystemExit('parse ' + key)
        r = q + 1; out = ''
        while r < len(src):
            if src[r] == '\\': out += src[r:r + 2]; r += 2; continue
            if src[r] == '"': break
            out += src[r]; r += 1
        vals.append(out); q = r + 1
    entries.append((key, vals)); i = q
keys = [k for k, v in entries]
index = {k: n for n, k in enumerate(keys)}
cache = json.load(open('tools/l10n-cache.json', encoding='utf-8'))
langs = ['fr', 'de', 'it', 'es', 'pt', 'ru', 'pl', 'tr', 'uk', 'ja', 'ko', 'sv']
changed = ['tip_souls', 'helpSoul', 'keeperNeed100']

def need(s): return sorted(int(x) for x in re.findall(r'\{(\d+)(?:[,:][^}]*)?\}', s))
def translate(text, tl):
    u = 'https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=' + tl + '&dt=t&q=' + urllib.parse.quote(text)
    for n in range(5):
        try:
            with urllib.request.urlopen(u, timeout=30) as r:
                d = json.loads(r.read().decode('utf-8'))
            return ''.join(x[0] for x in d[0])
        except Exception:
            time.sleep(1 + n)
    return text

for tl in langs:
    for key in changed:
        en = entries[index[key]][1][0]
        tr = translate(en, tl).strip()
        if need(en) != need(tr): tr = en
        cache[tl][index[key]] = tr
    print('updated', tl, flush=True)
json.dump(cache, open('tools/l10n-cache.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

# Regenerate the compiled dictionaries.
def cs(x): return json.dumps(x, ensure_ascii=False)
names = [('Fr','fr'),('De','de'),('It','it'),('Es','es'),('PtBr','pt'),('Ru','ru'),('Pl','pl'),('Tr','tr'),('Uk','uk'),('Ja','ja'),('Ko','ko'),('Sv','sv')]
out = ['using System.Collections.Generic;','','namespace HtfFortress','{','    // Machine-translated from the English column (see tools/translate_l10n.py). Kept out of L10n.cs','    // so the hand-written English / 繁中 / 简中 table stays readable.','    internal static class L10nExtra','    {']
for name, code in names:
    out += [f'        internal static readonly Dictionary<string,string> {name} = new Dictionary<string,string>', '        {']
    for key, row in entries:
        out.append(f'            [{cs(key)}] = {cs(cache[code][index[key]])},')
    out.append('        };')
out += ['    }','}','']
open('HtfFortress/L10nExtra.cs','w',encoding='utf-8').write('\n'.join(out))
print('regenerated', len(out), 'lines')
