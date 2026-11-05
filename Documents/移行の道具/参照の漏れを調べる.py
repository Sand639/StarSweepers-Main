# -*- coding: utf-8 -*-
"""新フォルダの中のシーン・プレハブなどが参照している GUID のうち、
旧リポジトリの Assets にはあるのに新フォルダに無い物（＝コピーし忘れ）を探す。"""
import os, re, sys
OLD = r"D:\010_GitHub\StarSweepers\StarSweepers\Assets"
NEW = r"D:\010_GitHub\StarSweepers-Main\StarSweepers\Assets"
guid_meta = re.compile(r"^guid:\s*([0-9a-f]{32})", re.M)
guid_ref = re.compile(r"guid:\s*([0-9a-f]{32})")

def metas(root):
    d = {}
    for dp, _, fs in os.walk(root):
        for f in fs:
            if f.endswith(".meta"):
                p = os.path.join(dp, f)
                m = guid_meta.search(open(p, encoding="utf-8", errors="ignore").read())
                if m: d[m.group(1)] = os.path.relpath(p[:-5], root)
    return d

old = metas(OLD); new = metas(NEW)
missing = {}
for dp, _, fs in os.walk(NEW):
    for f in fs:
        if f.split(".")[-1] in ("unity", "prefab", "asset", "mat", "controller", "anim", "inputactions", "shadergraph", "overrideController"):
            p = os.path.join(dp, f)
            for g in set(guid_ref.findall(open(p, encoding="utf-8", errors="ignore").read())):
                if g not in new and g in old:
                    missing.setdefault(old[g], set()).add(os.path.relpath(p, NEW))
import shutil
if "--fix" in sys.argv:
    for rel in sorted(missing):
        parts = rel.split(os.sep)
        for i in range(1, len(parts) + 1):
            sub = os.sep.join(parts[:i])
            for suffix in ("", ".meta"):
                src = os.path.join(OLD, sub + suffix); dst = os.path.join(NEW, sub + suffix)
                if os.path.isfile(src) and not os.path.exists(dst):
                    os.makedirs(os.path.dirname(dst), exist_ok=True); shutil.copy2(src, dst)
                    print("  コピー: " + sub + suffix)
print(f"新フォルダ .meta {len(new)} 件 / 旧 {len(old)} 件")
print(f"コピーし忘れ: {len(missing)} 件")
for k in sorted(missing):
    print(f"  {k}   <- " + ", ".join(sorted(missing[k])[:3]))
