# -*- coding: utf-8 -*-
"""Генерация иконок интерфейса. Строго по одной: у провайдера очередь max 1
на IP, любая параллель даёт 429 и пустой файл."""
import io, json, os, subprocess, sys, time

BASE = "C:/Ai/Jarvis/wiki/saas/projects/mars-colony/_promty-vitok1.json"
GEN  = os.path.expanduser("~/.claude/skills/image-gen/gen.py")
OUT  = sys.argv[1]
SEED = int(sys.argv[2]) if len(sys.argv) > 2 else 7301
only = sys.argv[3].split(",") if len(sys.argv) > 3 else None

P = json.load(io.open(BASE, encoding="utf-8"))["prompts"]
names = only if only else list(P)
os.makedirs(OUT, exist_ok=True)

for n in names:
    put = "%s/%s.png" % (OUT, n)
    if os.path.exists(put):
        print("%s uzhe est, propusk" % n, flush=True); continue
    for popytka in range(1, 4):
        seed = SEED + (popytka - 1) * 137
        r = subprocess.run([sys.executable, GEN, P[n], "-o", put, "--aspect", "1:1",
                            "--seed", str(seed), "--timeout", "150"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        if os.path.exists(put):
            json.dump({"seed": seed, "prompt": P[n]},
                      io.open("%s/%s.json" % (OUT, n), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
            print("%s -> OK (seed %d, popytka %d)" % (n, seed, popytka), flush=True)
            break
        print("%s popytka %d pusto: %s" % (n, popytka, ((r.stdout or "") + (r.stderr or ""))[-160:]), flush=True)
        time.sleep(20)
    time.sleep(8)
print("GOTOVO. fajlov:", len([f for f in os.listdir(OUT) if f.endswith(".png")]), flush=True)
