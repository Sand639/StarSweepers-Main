# -*- coding: utf-8 -*-
"""Assets/Scripts の全スクリプトについて「どこで使われているか」の表を作る（読むだけ。何も変えない）。
出力は Markdown。`Documents/スクリプトの使われ方の一覧.md` の「一覧」の部分を、これで作り直せる。

使い方：
  "D:\\999_Tools\\Anaconda3\\python.exe" -I "スクリプトの使われ方の一覧を作る.py" "D:/010_GitHub/StarSweepers-Main" > 出力.md

たどり方（`使われていないファイルを探す.py` と同じ考え方）：
  アセット … ビルドに入るシーン・Resources・ProjectSettings を起点に、ファイルの中の GUID をたどる
  スクリプト … 上でたどれた物に付いているスクリプトと、再生開始時に自分で生まれる物から、クラス名でたどる
  通信の一覧（DefaultNetworkPrefabs.asset）は、そこだけから使われているプレハブを別に扱う。
  ただし SpaceJunkSweeperPlayer.prefab は実際のプレイヤーの体（SweeperOnlineSwitch が実行時に入れ替える）なので、使われている側に入れる。
"""
import io, os, re, subprocess, sys, collections
sys.stdout.reconfigure(encoding="utf-8")
REPO = sys.argv[1].rstrip("/")
PROJ = REPO + "/StarSweepers"

tracked = subprocess.check_output(["git", "-C", REPO, "-c", "core.quotepath=false", "ls-files", "StarSweepers/Assets"]).decode("utf-8").split("\n")
files = sorted(t[len("StarSweepers/"):] for t in tracked if t and not t.endswith(".meta"))

guid_of, path_of = {}, {}
for f in files:
    m = PROJ + "/" + f + ".meta"
    if os.path.exists(m):
        g = re.search(r"^guid:\s*([0-9a-f]{32})", io.open(m, encoding="utf-8", errors="ignore").read(), re.M)
        if g: guid_of[f] = g.group(1); path_of[g.group(1)] = f

TEXT_EXT = (".unity", ".prefab", ".asset", ".mat", ".controller", ".anim", ".inputactions", ".shadergraph", ".overrideController", ".playable", ".renderTexture", ".physicMaterial")
ref_re = re.compile(r"guid:\s*([0-9a-f]{32})")
edges = {}
for f in files:
    if f.endswith(TEXT_EXT):
        txt = io.open(PROJ + "/" + f, encoding="utf-8", errors="ignore").read()
        edges[f] = {path_of[g] for g in set(ref_re.findall(txt)) if g in path_of}

NETLIST = "Assets/DefaultNetworkPrefabs.asset"
REAL_BODY = "Assets/Prefabs/Player/SpaceJunkSweeperPlayer.prefab"

def reach(start, skip=()):
    # ほかのシーンの中まではたどらない（マップの一覧などがシーンを指しているため。シーンはそれぞれ起点になっている）
    seen, stack = set(), list(start)
    while stack:
        x = stack.pop()
        if x in seen: continue
        seen.add(x)
        if x in skip or (x.endswith(".unity") and x not in start): continue
        stack.extend(edges.get(x, ()))
    return seen

# ---- 起点ごとに名前を付けてたどる（どのシーンで使われているかを出すため）
build = re.findall(r"path: (Assets/.*\.unity)", io.open(PROJ + "/ProjectSettings/EditorBuildSettings.asset", encoding="utf-8").read())
roots = collections.OrderedDict()
for b in build:
    if b in guid_of: roots[os.path.splitext(os.path.basename(b))[0]] = {b}
res = {f for f in files if "/Resources/" in f}
if res: roots["Resources"] = res
ps_refs = set()
for ps in os.listdir(PROJ + "/ProjectSettings"):
    txt = io.open(PROJ + "/ProjectSettings/" + ps, encoding="utf-8", errors="ignore").read()
    ps_refs |= {path_of[g] for g in set(ref_re.findall(txt)) if g in path_of and not path_of[g].endswith(".unity")}
if ps_refs: roots["ProjectSettings"] = ps_refs
if REAL_BODY in guid_of: roots["SpaceJunkSweeperPlayer（実際の体）"] = {REAL_BODY}

reached_by = collections.defaultdict(set)          # アセット → 起点の名前
for name, start in roots.items():
    for x in reach(start, skip={NETLIST}): reached_by[x].add(name)
live_assets = set(reached_by)
# 古い体（SpaceJunkPlayer.prefab）は NetworkManager に入っているが、SweeperOnlineSwitch が実行時に新しい体へ入れ替える（新しい体が無いときの予備）
OLD_BODY = "Assets/Prefabs/Player/SpaceJunkPlayer.prefab"
live_without_old = set()
for start in roots.values(): live_without_old |= reach(start, skip={NETLIST, OLD_BODY})
net_only = reach(edges.get(NETLIST, ()), skip={NETLIST}) - live_assets   # 通信の一覧からだけたどれる物
net_prefab_of = collections.defaultdict(set)
for p in edges.get(NETLIST, ()):
    if p in live_assets: continue
    for x in reach({p}, skip={NETLIST}): net_prefab_of[x].add(os.path.splitext(os.path.basename(p))[0])
hans = {f for f in files if "/HansStages/" in f and f.endswith(".unity")}
hans_only = reach(hans, skip={NETLIST}) - live_assets

# ---- スクリプトの中身
def strip_code(txt):
    out = []; i = 0; n = len(txt)
    while i < n:
        c = txt[i]
        if txt.startswith("//", i):
            j = txt.find("\n", i); j = n if j < 0 else j; out.append(" " * (j - i)); i = j
        elif txt.startswith("/*", i):
            j = txt.find("*/", i + 2); j = n if j < 0 else j + 2; out.append(re.sub(r"[^\n]", " ", txt[i:j])); i = j
        elif c == '"':
            j = i + 1
            while j < n and txt[j] != '"' and txt[j] != "\n": j += 2 if txt[j] == "\\" else 1
            j = min(j + 1, n); out.append(" " * (j - i)); i = j
        else:
            out.append(c); i += 1
    return "".join(out)

cs = [f for f in files if f.endswith(".cs")]
src, raw_src, decl, lines = {}, {}, {}, {}
for f in cs:
    b = open(PROJ + "/" + f, "rb").read()
    try: raw = b.decode("utf-8-sig")
    except UnicodeDecodeError: raw = b.decode("cp932", errors="ignore")
    raw_src[f] = raw; lines[f] = raw.count("\n") + (0 if raw.endswith("\n") else 1)
    t = strip_code(raw); src[f] = t
    top = 1 if re.search(r"\bnamespace\s+[\w.]+\s*\{", t) else 0
    depth = 0
    for m in re.finditer(r"[{}]|\b(?:class|struct|enum|interface)\s+([A-Za-z_]\w*)", t):
        if m.group(0) == "{": depth += 1
        elif m.group(0) == "}": depth -= 1
        elif depth <= top: decl.setdefault(m.group(1), set()).add(f)
ident = re.compile(r"\b[A-Za-z_]\w*\b")
refs = {}
for f in cs:
    out = set()
    for i in set(ident.findall(src[f])):
        for g in decl.get(i, ()):
            if g != f: out.add(g)
    refs[f] = out
called_by = collections.defaultdict(set)
for f, rs in refs.items():
    for r in rs: called_by[r].add(f)
is_editor = lambda f: "/Editor/" in f
self_spawn = {f for f in cs if not is_editor(f) and "RuntimeInitializeOnLoadMethod" in src[f]}

def closure(seeds):
    seen, stack = set(), list(seeds)
    while stack:
        x = stack.pop()
        if x in seen: continue
        seen.add(x); stack.extend(r for r in refs[x] if not is_editor(r))
    return seen

attached = {f for f in cs if f in live_assets}
live = closure(attached | self_spawn)
net_live = closure({f for f in cs if f in net_only}) - live
hans_live = closure({f for f in cs if f in hans_only}) - live - net_live

# ---- 表示用
STAGES = [n for n in roots if n.startswith("STAGE_")]
def where(f):
    names = reached_by.get(f, set())
    out = []
    if "TitleScene" in names: out.append("タイトル")
    if "SpaceJunkLobby" in names: out.append("ロビー")
    st = [n for n in STAGES if n in names]
    if st and len(st) == len(STAGES): out.append("全ステージ")
    elif st: out.append("、".join(n.replace("STAGE_", "ST") for n in st))
    if "Resources" in names:
        rp = sorted(os.path.splitext(os.path.basename(r))[0] for r in roots["Resources"] if f in reach({r}, skip={NETLIST}) and r.endswith(".prefab"))
        out.append("Resources（" + "、".join(rp[:3]) + ("ほか" if len(rp) > 3 else "") + "）" if rp else "Resources")
    if "ProjectSettings" in names: out.append("設定")
    if "SpaceJunkSweeperPlayer（実際の体）" in names and not out: out.append("実際の体のプレハブ")
    elif "SpaceJunkSweeperPlayer（実際の体）" in names: out.append("実際の体")
    return "・".join(out)

def short(f): return os.path.splitext(os.path.basename(f))[0]
def name_list(fs, limit=4):
    fs = sorted(short(x) for x in fs)
    return "、".join(f"`{x}`" for x in fs[:limit]) + (f" ほか{len(fs) - limit}本" if len(fs) > limit else "")

def editor_kind(f):
    t = raw_src[f]; kinds = []
    menus = re.findall(r'\[MenuItem\(\s*"([^"]+)"', t)
    if menus: kinds.append("メニュー `" + menus[0] + "`" + (f" ほか{len(menus) - 1}つ" if len(menus) > 1 else ""))
    tests = len(re.findall(r"\[(?:Test|UnityTest)\b", t))
    if tests: kinds.append(f"自動テスト {tests}件")
    ce = re.findall(r"\[CustomEditor\(\s*typeof\((\w+)\)", t) + re.findall(r"\[CustomPropertyDrawer\(\s*typeof\((\w+)\)", t)
    if ce: kinds.append("インスペクター拡張（`" + "`・`".join(ce) + "`）")
    if "InitializeOnLoad" in src[f]: kinds.append("エディタ起動時に動く")
    return "・".join(kinds) or "（ほかのエディタ用から呼ばれる部品）"

rows = collections.OrderedDict()
counts = collections.Counter()
for f in cs:
    if not f.startswith("Assets/Scripts/"): continue
    folder = os.path.dirname(f)[len("Assets/Scripts/"):]
    callers_live = {c for c in called_by[f] if c in live}
    callers_ed = {c for c in called_by[f] if is_editor(c)}
    if f in attached and f not in live_without_old:
        state = "◇ 古い体だけ"; note = "古い体のプレハブ（SpaceJunkPlayer）にだけ付いている。遊ぶときは新しい体に入れ替わるので、ふだんは動かない"
        if callers_live: note += "。呼ばれる：" + name_list(callers_live, 3)
    elif is_editor(f):
        targets = {r for r in refs[f] if not is_editor(r)}
        dead_t = {r for r in targets if r not in live}
        state = "道具"
        note = editor_kind(f)
        if targets and dead_t == targets: state = "道具（使われていない物だけ相手）"; note += "。相手：" + name_list(dead_t)
        elif dead_t: note += "。使われていない物も相手にしている：" + name_list(dead_t)
    elif f in attached:
        state = "◎ シーン・プレハブ"; note = where(f)
        if callers_live: note += "。呼ばれる：" + name_list(callers_live, 3)
    elif f in self_spawn:
        state = "◎ 自分で生まれる"; note = "再生開始時に自動で作られる"
    elif f in live:
        state = "○ スクリプトから"; note = "呼ばれる：" + name_list(callers_live)
    elif f in net_live:
        state = "△ 通信の一覧だけ"; note = "通信の一覧の " + "、".join(sorted({n for x in cs if x in net_only and (x == f or f in closure({x})) for n in net_prefab_of.get(x, ())})) or "通信の一覧から"
    elif f in hans_live:
        state = "△ Hansさんのステージだけ"; note = "ビルドに入っていない HansStage01 にだけ置かれている"
    else:
        state = "× 使われていない"
        note = ("エディタの道具だけが呼ぶ：" + name_list(callers_ed)) if callers_ed else ("ほかの使われていない物だけが呼ぶ：" + name_list(called_by[f]) if called_by[f] else "どこからも呼ばれていない")
    counts[state.split("（")[0]] += 1
    rows.setdefault(folder, []).append((short(f), lines[f], state, note))

print(f"<!-- 自動生成：スクリプトの使われ方の一覧を作る.py（起点：ビルドのシーン {len(build)}・Resources・ProjectSettings・実際の体のプレハブ） -->")
print()
print("| 状態 | 本数 |")
print("| --- | --- |")
for k in ["◎ シーン・プレハブ", "◎ 自分で生まれる", "○ スクリプトから", "◇ 古い体だけ", "△ 通信の一覧だけ", "△ Hansさんのステージだけ", "× 使われていない", "道具"]:
    if counts[k]: print(f"| {k}{'（エディタ用）' if k == '道具' else ''} | {counts[k]} |")
print()
for folder in sorted(rows):
    rs = sorted(rows[folder], key=lambda r: ("道具" in r[2], r[0].lower()))
    print(f"### {folder}（{len(rs)}本・{sum(r[1] for r in rs):,}行）")
    print()
    print("| スクリプト | 行数 | 状態 | どこで使われているか・メモ |")
    print("| --- | ---: | --- | --- |")
    for n, l, s, note in rs:
        print(f"| `{n}` | {l} | {s} | {note} |")
    print()
