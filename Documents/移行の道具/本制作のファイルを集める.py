# -*- coding: utf-8 -*-
"""
本制作（宇宙ごみ集め）に必要なファイルを集めて、新しいフォルダへコピーする。

入力：Unity が書き出した依存一覧（main-game-deps.txt。Assets/ からの相対パス）
補う：スクリプト同士の呼び出し（クラス名で辿る）、Resources・Settings・TextMesh Pro の丸ごと
出力：新フォルダ（--dest）。--dry-run のときは集計だけ表示する
"""
import os, re, shutil, subprocess, sys, argparse

ap = argparse.ArgumentParser()
ap.add_argument("--src", required=True)       # 旧リポジトリのルート
ap.add_argument("--deps", required=True)      # main-game-deps.txt
ap.add_argument("--dest", required=True)      # 新フォルダ
ap.add_argument("--dry-run", action="store_true")
args = ap.parse_args()

SRC = args.src.replace("\\", "/").rstrip("/")
PROJ = "StarSweepers"            # Unity プロジェクトのフォルダ名（旧リポジトリのルートからの相対）
ASSETS = f"{SRC}/{PROJ}/Assets"

def norm(p): return p.replace("\\", "/")

# ---- git が管理しているファイルだけを対象にする（Library などの一時ファイルを避ける）
tracked = subprocess.check_output(["git", "-C", SRC, "ls-files", "-z"]).decode("utf-8").split("\0")
tracked = set(t for t in tracked if t)

# ---- 1. Unity が出した依存一覧（Assets/... を StarSweepers/Assets/... に直す）
inc = set()
for line in open(args.deps, encoding="utf-8"):
    line = line.strip()
    if line:
        inc.add(f"{PROJ}/{line}")

# ---- 2. 名前で読み込まれる・設定から参照される場所は丸ごと
WHOLE_DIRS = [
    f"{PROJ}/Assets/Resources",      # Resources.Load で名前で読む
    f"{PROJ}/Assets/Settings",       # URP の設定（ProjectSettings から参照）
    f"{PROJ}/Assets/TextMesh Pro",   # TMP の必須素材
]
for t in tracked:
    if any(t.startswith(d + "/") for d in WHOLE_DIRS) and not t.endswith(".meta"):
        inc.add(t)

# ---- 一回きりの道具（この移行のために作った物）は持っていかない
EXCLUDE = {f"{PROJ}/Assets/Scripts/SpaceJunk/Editor/MainGameDependencyExporter.cs"}
inc -= EXCLUDE

# ---- 3. コードの依存：クラス名で辿る
all_cs = [t for t in tracked if t.endswith(".cs") and t.startswith(f"{PROJ}/Assets/") and t not in EXCLUDE]
decl_re = re.compile(r"\b(?:class|struct|enum|interface)\s+([A-Za-z_]\w*)")
ident_re = re.compile(r"\b[A-Za-z_]\w*\b")
attr_re = re.compile(r"\[\s*(?:CustomEditor|CustomPropertyDrawer)\s*\(\s*typeof\s*\(\s*([A-Za-z_]\w*)")

def strip_code(txt):
    """コメントと文字列を空白に置き換える（参照の判定に使わないため）"""
    out = []; i = 0; n = len(txt)
    while i < n:
        c = txt[i]
        if txt.startswith("//", i):
            j = txt.find("\n", i); j = n if j < 0 else j
            out.append(" " * (j - i)); i = j
        elif txt.startswith("/*", i):
            j = txt.find("*/", i + 2); j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", txt[i:j])); i = j
        elif c == '"':
            j = i + 1
            if i > 0 and txt[i-1] == '@':
                while j < n and not (txt[j] == '"' and txt[j+1:j+2] != '"'): j += 2 if txt[j:j+2] == '""' else 1
            else:
                while j < n and txt[j] != '"' and txt[j] != "\n":
                    j += 2 if txt[j] == "\\" else 1
            j = min(j + 1, n); out.append(" " * (j - i)); i = j
        elif c == "'":
            j = i + 1
            while j < n and txt[j] != "'" and txt[j] != "\n": j += 2 if txt[j] == "\\" else 1
            j = min(j + 1, n); out.append(" " * (j - i)); i = j
        else:
            out.append(c); i += 1
    return "".join(out)

src_text = {}
name_to_files = {}
editor_for = {}       # エディタ拡張 → 対象クラス
init_on_load = set()
for f in all_cs:
    try:
        raw = open(f"{SRC}/{f}", encoding="utf-8-sig").read()
    except UnicodeDecodeError:
        raw = open(f"{SRC}/{f}", encoding="cp932", errors="ignore").read()
    txt = strip_code(raw)
    src_text[f] = txt
    # ファイル直下（namespace の中も可）の型名だけを登録する。入れ子の型は親の名前で辿れる
    top_depth = 1 if re.search(r"\bnamespace\s+[\w.]+\s*\{", txt) else 0
    depth = 0
    for m in re.finditer(r"[{}]|\b(?:class|struct|enum|interface)\s+([A-Za-z_]\w*)", txt):
        if m.group(0) == "{": depth += 1
        elif m.group(0) == "}": depth -= 1
        elif depth <= top_depth:
            name_to_files.setdefault(m.group(1), set()).add(f)
    for m in attr_re.finditer(txt):
        editor_for.setdefault(f, set()).add(m.group(1))
    if "InitializeOnLoad" in txt: init_on_load.add(f)

def refs(f):
    out = set()
    for ident in set(ident_re.findall(src_text[f])):
        for g in name_to_files.get(ident, ()):
            if g != f:
                out.add(g)
    return out

def is_editor(f): return "/Editor/" in f

pulled_by_code = {}   # 新しく入ったファイル → きっかけになったファイル
changed = True
while changed:
    changed = False
    # 通常のスクリプト：使っているクラスのファイルを足す
    for f in [x for x in inc if x.endswith(".cs")]:
        for g in refs(f):
            if g not in inc:
                inc.add(g); pulled_by_code[g] = f; changed = True
    # エディタ用スクリプト：次のどれかなら足す
    #  (a) 持っていくクラスのインスペクター拡張（CustomEditor / CustomPropertyDrawer）
    #  (b) 自動テスト（*Tests.cs）で、使っているクラスがすべて揃っている物
    #  (c) 起動時に動く物（InitializeOnLoad）で、使っているクラスがすべて揃っている物
    #  (e) エディタ用ではないが、**再生開始時に自分で生まれる物**（RuntimeInitializeOnLoadMethod）。
    #      どこからも参照されないので (1)〜(3) では拾えない。使っているクラスがすべて揃っている物だけ足す
    #      （2026/11/5：TitleSceneFlow と NetworkManagerCleanup を落として、ホストになってもロビーへ移らなくなった）
    for f in all_cs:
        if f in inc:
            continue
        r = refs(f)
        if not is_editor(f):
            if "RuntimeInitializeOnLoadMethod" in src_text[f] and r <= inc:
                inc.add(f); pulled_by_code[f] = "(runtime: self-starting, deps all included)"; changed = True
            continue
        why = None
        targets = editor_for.get(f, set())
        if targets and any(any(g in inc for g in name_to_files.get(t, ())) for t in targets):
            why = "(editor: CustomEditor of included class)"
        elif f.endswith("Tests.cs") and r and r <= inc:
            why = "(editor: tests, deps all included)"
        elif f in init_on_load and r and r <= inc:
            why = "(editor: InitializeOnLoad, deps all included)"
        #  (d) 本制作の部品そのもののフォルダ（Player / UI / Network / Common）の道具で、使っているクラスが揃っている物
        elif f.split("/")[3] in ("Player", "UI", "Network", "Common") and r and r <= inc:
            why = "(editor: main-game folder tool, deps all included)"
        if why:
            inc.add(f); pulled_by_code[f] = why; changed = True

# ---- 4. .meta と、親フォルダの .meta を足す
def with_meta(paths):
    out = set()
    for p in paths:
        out.add(p)
        if p + ".meta" in tracked: out.add(p + ".meta")
        parts = p.split("/")
        for i in range(2, len(parts)):          # StarSweepers/Assets.meta から
            d = "/".join(parts[:i])
            if d + ".meta" in tracked: out.add(d + ".meta")
    return out

files = with_meta(inc)

# ---- 5. プロジェクトの設定など、Assets の外で必ず要る物
for t in tracked:
    if t.startswith(f"{PROJ}/ProjectSettings/") or t.startswith(f"{PROJ}/Packages/"):
        files.add(t)
for t in [f"{PROJ}/.gitignore", f"{PROJ}/steam_appid.txt", f"{PROJ}/.vsconfig",
          ".gitignore", ".gitattributes", "AGENTS.md", "CLAUDE.md"]:
    if t in tracked: files.add(t)
for t in tracked:
    if t.startswith(".claude/commands/"): files.add(t)

# ---- 集計
cs_inc = sorted(x for x in inc if x.endswith(".cs"))
print(f"[集計] Assets 内のファイル {len(inc)} 件（うち .cs {len(cs_inc)} / 全 .cs {len(all_cs)}）、.meta と設定を含めて {len(files)} 件")
by_dir = {}
for x in inc:
    key = "/".join(x.split("/")[1:4]) if x.startswith(f"{PROJ}/Assets/Scripts/") or x.startswith(f"{PROJ}/Assets/Prefabs/") or x.startswith(f"{PROJ}/Assets/Scenes/") else "/".join(x.split("/")[1:3])
    by_dir[key] = by_dir.get(key, 0) + 1
for k in sorted(by_dir): print(f"  {by_dir[k]:4d}  {k}")
print("\n[コードの依存で追加されたスクリプト]")
for g in sorted(pulled_by_code):
    print(f"  {g}   <- {pulled_by_code[g]}")
print("\n[持っていかない .cs]")
for f in sorted(set(all_cs) - inc): print("  " + f)

if args.dry_run:
    sys.exit(0)

# ---- 6. コピー
DEST = args.dest.replace("\\", "/").rstrip("/")
n = 0
for f in sorted(files):
    s = f"{SRC}/{f}"; d = f"{DEST}/{f}"
    os.makedirs(os.path.dirname(d), exist_ok=True)
    shutil.copy2(s, d); n += 1
print(f"\n[コピー] {n} 件 → {DEST}")

# ---- 7. ビルド対象のシーン一覧から、持っていかないシーンを外す
ebs = f"{DEST}/{PROJ}/ProjectSettings/EditorBuildSettings.asset"
txt = open(ebs, encoding="utf-8").read()
head, sep, body = txt.partition("  m_Scenes:\n")
entries = re.findall(r"  - enabled: \d\n    path: (.*)\n    guid: .*\n", body)
blocks = re.findall(r"(  - enabled: \d\n    path: (.*)\n    guid: .*\n)", body)
kept = [b for b, p in blocks if f"{PROJ}/{p}" in inc]
rest = body
for b, p in blocks: rest = rest.replace(b, "")
new = head + sep + "".join(kept) + rest
open(ebs, "w", encoding="utf-8", newline="\n").write(new)
print(f"[ビルド設定] シーン {len(blocks)} 件 → {len(kept)} 件に絞った")
for b, p in blocks:
    if f"{PROJ}/{p}" not in inc: print("  外した: " + p)

open(f"{DEST}/../copied-files.txt", "w", encoding="utf-8").write("\n".join(sorted(files)))
