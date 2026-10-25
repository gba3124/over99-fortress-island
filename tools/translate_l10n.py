import re, json, time, urllib.parse, urllib.request, os, sys
src = open('HtfFortress/L10n.cs', encoding='utf-8').read()
start = src.index('private static readonly Dictionary<string, string[]> Table')
start = src.index('{', src.index('Table =', start)); i = start + 1; entries = []
while i < len(src):
    j = src.find('["', i)
    if j < 0: break
    k = src.index('"]', j + 2); key = src[j + 2:k]
    m = re.match(r'\s*=\s*new\[\]\s*\{', src[k + 2:])
    if not m: i = k + 2; continue
    p = k + 2 + m.end(); vals = []; q = p
    while q < len(src):
        while q < len(src) and src[q] in ' \t\r\n,': q += 1
        if q < len(src) and src[q] == '}': q += 1; break
        if q >= len(src) or src[q] != '"': raise SystemExit('parse error at ' + key)
        r = q + 1; out = ''
        while r < len(src):
            if src[r] == '\\': out += src[r:r + 2]; r += 2; continue
            if src[r] == '"': break
            out += src[r]; r += 1
        vals.append(out); q = r + 1
    entries.append((key, vals)); i = q
print('entries', len(entries), flush=True)
texts = [v[0] for k, v in entries]
keys = [k for k, v in entries]

TARGETS = ['fr', 'de', 'it', 'es', 'pt', 'ru', 'pl', 'tr', 'uk', 'ja', 'ko', 'sv']
cache_path = 'tools/l10n-cache.json'
cache = json.load(open(cache_path, encoding='utf-8')) if os.path.exists(cache_path) else {}
if cache: print('cached languages', list(cache), flush=True)

def protect(t):
    # Keep literals that must survive translation: \n escapes and format braces.
    return t.replace('\\n', '\u27e6NL\u27e7')

def unprotect(t):
    return t.replace('\u27e6NL\u27e7', '\\n')

def need(s):
    return sorted({int(x) for x in re.findall(r'\{(\d+)(?:[,:][^}]*)?\}', s)})

def translate_batch(batch, tl):
    joined = '\n\u27e6HTS\u27e7\n'.join(protect(x) for x in batch)
    url = ('https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=' + tl
           + '&dt=t&q=' + urllib.parse.quote(joined))
    last = None
    for attempt in range(5):
        try:
            with urllib.request.urlopen(url, timeout=30) as r:
                data = json.loads(r.read().decode('utf-8'))
            out = ''.join(x[0] for x in data[0])
            parts = out.split('\u27e6HTS\u27e7')
            if len(parts) == len(batch):
                return [unprotect(x).strip('\n') for x in parts]
            last = 'split ' + str(len(parts))
        except Exception as e:
            last = repr(e)
        time.sleep(0.7 * (attempt + 1))
    print('fallback singles', tl, last, flush=True)
    got = []
    for x in batch:
        u = ('https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=' + tl
             + '&dt=t&q=' + urllib.parse.quote(protect(x)))
        for attempt in range(4):
            try:
                with urllib.request.urlopen(u, timeout=30) as r:
                    d = json.loads(r.read().decode('utf-8'))
                got.append(unprotect(''.join(y[0] for y in d[0])).strip('\n'))
                break
            except Exception:
                time.sleep(1.0 * (attempt + 1))
        else:
            got.append(x)
    return got

for tl in TARGETS:
    if tl in cache and len(cache[tl]) == len(texts):
        print('skip', tl, flush=True); continue
    out = []
    B = 35
    for bi in range(0, len(texts), B):
        batch = texts[bi:bi + B]
        got = translate_batch(batch, tl)
        for en, tr in zip(batch, got):
            if need(en) != need(tr):
                print('placeholder fallback', tl, repr(en)[:60], flush=True)
                tr = en
            out.append(tr)
        if bi % (B * 4) == 0:
            print(tl, bi, '/', len(texts), flush=True)
            cache[tl] = out
            json.dump(cache, open(cache_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        time.sleep(0.15)
    while len(out) < len(texts): out.append(texts[len(out)])
    cache[tl] = out
    json.dump(cache, open(cache_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('done', tl, flush=True)
print('ALL DONE', flush=True)
