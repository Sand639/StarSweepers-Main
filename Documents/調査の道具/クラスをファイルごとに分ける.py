# -*- coding: utf-8 -*-
# 1つのクラスを、行の範囲ごとに partial のファイルへ分ける（--apply で書き込む。無しなら確かめるだけ）
# 使い方：python -I クラスをファイルごとに分ける.py 指定.json [--apply]（2026/11/6 に長いスクリプト9本を分けたときに使った。AIの申し送り.md）
# spec: {"file": パス, "class": クラス名, "prefix": 新しいファイル名の頭, "parts": {suffix: 説明}, "anchors": [[行, suffix or "main"], ...]}
import io, json, os, re, sys
sys.stdout.reconfigure(encoding="utf-8")
# このファイルの場所（Documents/調査の道具/）から、Assets/Scripts を探す
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../StarSweepers/Assets/Scripts")).replace("\\", "/") + "/"

def strip_code(line, state):
    # 文字列・文字・コメントを消して、{ } を数えられるようにする（/* */ は state で行をまたぐ）
    out = []; i = 0; n = len(line)
    while i < n:
        if state["block"]:
            j = line.find("*/", i)
            if j < 0: return "".join(out)
            state["block"] = False; i = j + 2; continue
        c = line[i]
        if line.startswith("//", i): break
        if line.startswith("/*", i): state["block"] = True; i += 2; continue
        if c == '"' or (c in "$@" and i + 1 < n and line[i + 1] == '"'):
            j = i + (2 if c in "$@" else 1)
            while j < n and line[j] != '"':
                j += 2 if line[j] == "\\" else 1
            i = j + 1; continue
        if c == "'":
            j = i + 1
            while j < n and line[j] != "'":
                j += 2 if line[j] == "\\" else 1
            i = j + 1; continue
        out.append(c); i += 1
    return "".join(out)

def balance(lines):
    state = {"block": False}; depth = 0; low = 0; pp = 0
    for l in lines:
        s = strip_code(l, state)
        depth += s.count("{") - s.count("}")
        low = min(low, depth)
        t = l.strip()
        if t.startswith("#if"): pp += 1
        elif t.startswith("#endif"): pp -= 1
    return depth, low, pp

def adjust_up(L, idx):
    # 宣言の上にある ///・[属性]・// コメントの行も一緒に動かす（空行で止まる）
    while idx - 1 >= 0:
        t = L[idx - 1].strip()
        if t.startswith("///") or t.startswith("[") or (t.startswith("//") and not t.startswith("// ---")) :
            idx -= 1
        elif t.startswith("// ---"):
            idx -= 1
        else:
            break
    return idx

def run(spec, apply):
    path = ROOT + spec["file"]
    raw = open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in text else "\n"
    L = text.split(nl)
    cls = spec["class"]
    # クラスの宣言・本体の始まりと終わり
    decl = next(i for i, l in enumerate(L) if re.match(r"public (partial )?class " + cls + r"\b", l))
    open_i = next(i for i in range(decl, len(L)) if L[i].strip() == "{")
    state = {"block": False}; depth = 0; close_i = None
    for i in range(open_i, len(L)):
        s = strip_code(L[i], state); depth += s.count("{") - s.count("}")
        if depth == 0: close_i = i; break
    anchors = sorted((adjust_up(L, a - 1), t) for a, t in spec["anchors"])
    segs = []
    starts = [(open_i + 1, "main")] + anchors + [(close_i, None)]
    for (s, t), (e, _) in zip(starts, starts[1:]):
        segs.append((s, e, t))       # [s, e)
    ok = True
    moved = {}
    for s, e, t in segs:
        d, low, pp = balance(L[s:e])
        flag = "" if (d == 0 and low >= 0 and pp == 0) else "  ★釣り合っていない"
        if flag: ok = False
        first = next((L[k].strip() for k in range(s, e) if L[k].strip()), "")[:60]
        print(f"  {s+1:5d}-{e:5d} {t:14s} {e-s:4d}行 {first}{flag}")
        if t != "main":
            moved.setdefault(t, []).append(L[s:e])
    if not ok:
        print("  → 書き込まない"); return False
    if not apply:
        return True
    usings = [l for l in L[:decl] if l.startswith("using ")]
    for t, chunks in moved.items():
        body = []
        for c in chunks:
            while c and not c[0].strip(): c = c[1:]
            while c and not c[-1].strip(): c = c[:-1]
            if body: body.append("")
            body += c
        out = usings + ["", f"/// <summary>{cls} の「{spec['parts'][t]}」（{spec.get('from', cls + '.cs')} から分けた部分）。</summary>",
                        f"public partial class {cls}", "{"] + body + ["}", ""]
        newpath = os.path.join(os.path.dirname(path), f"{spec['prefix']}.{t}.cs")
        assert not os.path.exists(newpath), newpath
        data = nl.join(out).encode("utf-8")
        open(newpath, "wb").write((b"\xef\xbb\xbf" if bom else b"") + data)
    keep = L[:open_i + 1]
    for s, e, t in segs:
        if t == "main": keep += L[s:e]
    keep += L[close_i:]
    keep[decl] = re.sub(r"public class " + cls + r"\b", "public partial class " + cls, keep[decl])
    # 空行が続いたら1つにする
    cleaned = []
    for l in keep:
        if not l.strip() and cleaned and not cleaned[-1].strip(): continue
        cleaned.append(l)
    open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + nl.join(cleaned).encode("utf-8"))
    return True

specs = json.load(io.open(sys.argv[1], encoding="utf-8"))
apply = "--apply" in sys.argv
for spec in specs:
    print("==", spec["file"])
    run(spec, apply)
