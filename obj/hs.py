import io
p='scripts/hit-stats.py'
s=io.open(p,encoding='utf-8',newline='').read()
crlf='\r\n' in s; s=s.replace('\r\n','\n')
def rep(a,b):
    global s
    assert a in s, a
    s=s.replace(a,b,1)
rep('''"""Summarizes the [MPB-HIT] lines that MPBalancer writes to the server log.

Usage:
  ssh aesten@7900x 'grep "\\[MPB-HIT\\]" ~/bannerlord/server.log' | python scripts/hit-stats.py
  ssh aesten@7900x 'zgrep -h "\\[MPB-HIT\\]" ~/bannerlord/logs/server-*.log.gz' | python scripts/hit-stats.py   (earlier runs)
  python scripts/hit-stats.py saved-log.txt
''','''"""Summarizes MPBalancer's hit data: the hits-<start>.csv files it writes per server run (see HitLog.cs), or the
[MPB-HIT] lines of older server logs.

Usage:
  ssh aesten@7900x 'cat $(ls -t ~/bannerlord/metrics/hits-*.csv | head -1)' | python scripts/hit-stats.py   (this run)
  ssh aesten@7900x 'zcat ~/bannerlord/metrics/hits-*.csv.gz' | python scripts/hit-stats.py                  (earlier runs)
  python scripts/hit-stats.py hits.csv [--settings N]
  ssh aesten@7900x 'zgrep -h "\\[MPB-HIT\\]" ~/bannerlord/logs/server-*.log.gz' | python scripts/hit-stats.py   (old logs)

--settings N keeps only hits made with balance configuration N; settings-<start>.csv says which values each
number stands for (a new number after every !mpb change). Without it, all configurations are mixed.
''')
rep('''import re
import sys''','''import csv
import re
import sys''')
rep('''def parse(lines):
    for line in lines:
        if "[MPB-HIT]" not in line:''','''def parse(lines):
    lines = iter(lines)
    first = next(lines, "")
    if first.startswith("time,"):
        # CSV (HitLog): a header, then one row per hit. zcat of several runs repeats the header; skip those.
        header = first.strip().split(",")
        for row in csv.reader(lines):
            if not row or row[0] == "time":
                continue
            hit = dict(zip(header, row))
            for key in ("speed", "bonus", "armor", "vanilla", "before", "after"):
                hit[key] = float(hit[key])
            hit["base"] = hit["vanilla"]
            yield hit
        return
    for line in [first] + list(lines):
        if "[MPB-HIT]" not in line:''')
rep('''def main():
    source = open(sys.argv[1], encoding="utf-8", errors="replace") if len(sys.argv) > 1 else sys.stdin
    hits = list(parse(source))''','''def main():
    args = sys.argv[1:]
    settings = None
    if "--settings" in args:
        i = args.index("--settings")
        settings = args[i + 1]
        del args[i:i + 2]
    source = open(args[0], encoding="utf-8", errors="replace") if args else sys.stdin
    hits = list(parse(source))
    if settings is not None:
        hits = [h for h in hits if h.get("settings") == settings]''')
if crlf: s=s.replace('\n','\r\n')
io.open(p,'w',encoding='utf-8',newline='').write(s)
print("ok")
