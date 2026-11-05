# -*- coding: utf-8 -*-
"""本制作（ビルドに入るシーン＋Resources）から使われていない物を探す（読むだけ。何も消さない）。
参照のたどり方：
  アセット … シーン・プレハブ・マテリアルなどの中の GUID をたどる
  スクリプト … プレハブ・シーンに付いている物 ＋ 再生開始時に自分で生まれる物 から、クラス名でたどる
通信の一覧（DefaultNetworkPrefabs.asset）だけから参照されている物は、別に数える。"""
import io, os, re, subprocess, sys, collections
sys.stdout.reconfigure(encoding="utf-8")
REPO = sys.argv[1].rstrip("/")
PROJ = REPO + "/StarSweepers"
A = PROJ + "/Assets"

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
    elif f.endswith((".fbx", ".obj")):
        # モデルの .meta に書かれたマテリアルの差し替え
        txt = io.open(PROJ + "/" + f + ".meta", encoding="utf-8", errors="ignore").read()
        edges[f] = {path_of[g] for g in set(ref_re.findall(txt)) if g in path_of and path_of[g] != f}

# ---- 起点
build = re.findall(r"path: (Assets/.*\.unity)", io.open(PROJ + "/ProjectSettings/EditorBuildSettings.asset", encoding="utf-8").read())
roots = set(b for b in build if b in guid_of)
roots |= {f for f in files if "/Resources/" in f}
for ps in os.listdir(PROJ + "/ProjectSettings"):      # 描画の設定などが指している物
    txt = io.open(PROJ + "/ProjectSettings/" + ps, encoding="utf-8", errors="ignore").read()
    roots |= {path_of[g] for g in set(ref_re.findall(txt)) if g in path_of}
NETLIST = "Assets/DefaultNetworkPrefabs.asset"
if "--drop-old" in sys.argv:                          # 昔のマップ（SpaceJunkMap01〜04）を起点から外した場合
    roots = {r for r in roots if "/SpaceJunkMap0" not in r}
    for k in edges:                                   # マップの一覧（SpaceJunkMapList.asset）などからも、昔のマップをたどらない
        edges[k] = {e for e in edges[k] if "/SpaceJunkMap0" not in e}
if "--with-hans" in sys.argv:                         # Hansさんのステージ（ビルドには入っていない）も起点にする
    roots |= {f for f in files if "/HansStages/" in f and f.endswith(".unity")}

def reach(start, skip=()):
    seen, stack = set(), list(start)
    while stack:
        x = stack.pop()
        if x in seen: continue
        seen.add(x)
        if x in skip: continue
        stack.extend(edges.get(x, ()))
    return seen

used_no_net = reach(roots, skip={NETLIST})            # 通信の一覧の先はたどらない
used_all = reach(roots)

# ---- スクリプト：クラス名でたどる
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
src, raw_src, decl = {}, {}, {}
for f in cs:
    b = open(PROJ + "/" + f, "rb").read()
    try: raw = b.decode("utf-8-sig")
    except UnicodeDecodeError: raw = b.decode("cp932", errors="ignore")
    raw_src[f] = raw; t = strip_code(raw); src[f] = t
    top = 1 if re.search(r"\bnamespace\s+[\w.]+\s*\{", t) else 0
    depth = 0
    for m in re.finditer(r"[{}]|\b(?:class|struct|enum|interface)\s+([A-Za-z_]\w*)", t):
        if m.group(0) == "{": depth += 1
        elif m.group(0) == "}": depth -= 1
        elif depth <= top: decl.setdefault(m.group(1), set()).add(f)
ident = re.compile(r"\b[A-Za-z_]\w*\b")
def refs(f):
    out = set()
    for i in set(ident.findall(src[f])):
        for g in decl.get(i, ()):
            if g != f: out.add(g)
    return out
is_editor = lambda f: "/Editor/" in f

def script_closure(asset_set):
    seeds = {f for f in asset_set if f.endswith(".cs")}
    seeds |= {f for f in cs if not is_editor(f) and "RuntimeInitializeOnLoadMethod" in src[f]}     # 自分で生まれる物
    seen, stack = set(), list(seeds)
    while stack:
        x = stack.pop()
        if x in seen: continue
        seen.add(x); stack.extend(r for r in refs(x) if not is_editor(r))
    return seen

run_no_net = script_closure(used_no_net)
run_all = script_closure(used_all)

runtime_cs = [f for f in cs if not is_editor(f)]
editor_cs = [f for f in cs if is_editor(f)]
unused_runtime = sorted(f for f in runtime_cs if f not in run_all)
only_net_runtime = sorted(f for f in runtime_cs if f in run_all and f not in run_no_net)

assets = [f for f in files if not f.endswith(".cs")]
unused_assets = sorted(f for f in assets if f not in used_all and not f.startswith("Assets/TextMesh Pro/"))
only_net_assets = sorted(f for f in assets if f in used_all and f not in used_no_net)

# エディタ用：使われていないランタイムのクラスを触っている道具・テスト
dead = set(unused_runtime)
editor_touch_dead = {f: sorted(r for r in refs(f) if r in dead) for f in editor_cs}
editor_only_dead = sorted(f for f in editor_cs if editor_touch_dead[f] and all((r in dead or is_editor(r)) for r in refs(f)))

def group(lst):
    d = collections.OrderedDict()
    for f in lst: d.setdefault(os.path.dirname(f).replace("Assets/", ""), []).append(os.path.basename(f))
    return d

total_in = collections.Counter(os.path.dirname(f).replace("Assets/", "") for f in files)
print(f"追跡ファイル {len(files)}（スクリプト {len(cs)}：ランタイム {len(runtime_cs)}・エディタ用 {len(editor_cs)}／それ以外 {len(assets)}）")
print(f"起点：ビルドに入るシーン {len(build)}、Resources {sum(1 for f in files if '/Resources/' in f)}")
print(f"\n===== A. どこからも使われていないアセット：{len(unused_assets)} 件 =====")
for d, names in group(unused_assets).items(): print(f"  [{d}]（{len(names)}/{total_in[d]}） " + ", ".join(names))
print(f"\n===== B. 通信の一覧（DefaultNetworkPrefabs）からだけ使われているアセット：{len(only_net_assets)} 件 =====")
for d, names in group(only_net_assets).items(): print(f"  [{d}]（{len(names)}/{total_in[d]}） " + ", ".join(names))
print(f"\n===== C. どこからも使われていないスクリプト（ランタイム）：{len(unused_runtime)} 本 =====")
for d, names in group(unused_runtime).items(): print(f"  [{d}]（{len(names)}/{total_in[d]}） " + ", ".join(n[:-3] for n in names))
print(f"\n===== D. B のプレハブからだけ使われているスクリプト：{len(only_net_runtime)} 本 =====")
for d, names in group(only_net_runtime).items(): print(f"  [{d}]（{len(names)}/{total_in[d]}） " + ", ".join(n[:-3] for n in names))
print(f"\n===== E. C のスクリプトだけを相手にしているエディタ用（道具・テスト）：{len(editor_only_dead)} 本 =====")
for d, names in group(editor_only_dead).items(): print(f"  [{d}] " + ", ".join(n[:-3] for n in names))
print("\n===== F. フォルダごとの集計（そのフォルダ直下。使われていない＝A＋C） =====")
unused_all = set(unused_assets) | set(unused_runtime)
netonly = set(only_net_assets) | set(only_net_runtime)
for d in sorted(total_in):
    if d.startswith("TextMesh Pro"): continue
    here = [f for f in files if os.path.dirname(f).replace("Assets/", "") == d]
    u = sum(1 for f in here if f in unused_all); n = sum(1 for f in here if f in netonly)
    if u or n: print(f"  {d or '(Assets 直下)'}: 全 {len(here)}／使われていない {u}／通信の一覧からだけ {n}" + ("  ★フォルダごと使われていない" if u == len(here) else ""))
print("\n===== G. C・D のスクリプトを参照しているエディタ用（消すなら、こちらも直す・消す） =====")
for f in unused_runtime + only_net_runtime:
    who = sorted(os.path.basename(e)[:-3] for e in editor_cs if f in refs(e))
    print(f"  {os.path.basename(f)[:-3]}: " + (", ".join(who) if who else "（参照なし）"))
print("\n===== H. A・B のプレハブ・設定を、コードがパスの文字列で読んでいる所 =====")
for f in unused_assets + only_net_assets:
    if not f.endswith((".prefab", ".asset", ".mat")): continue
    who = sorted(os.path.basename(c)[:-3] for c in cs if f in raw_src[c] or ('"' + os.path.splitext(os.path.basename(f))[0] + '"') in raw_src[c])
    if who: print(f"  {f.replace('Assets/', '')}: " + ", ".join(who))
io.open(os.path.dirname(os.path.abspath(__file__)) + "/result_sets" + ("_dropold" if "--drop-old" in sys.argv else "") + ".txt", "w", encoding="utf-8").write(
    "\n".join(["#unused_assets"] + unused_assets + ["#only_net_assets"] + only_net_assets + ["#unused_runtime"] + unused_runtime + ["#only_net_runtime"] + only_net_runtime + ["#editor_only_dead"] + editor_only_dead))
