"""
Батч-обертка над `glb-to-fbx.py`: список моделей -> FBX + кадр + строка отчета
на каждую.

    python glb-to-fbx-batch.py выход_папка [бюджет] [текстура] [имя ...]

Запускается ОБЫЧНЫМ python (не Blender): каждая модель — это ОТДЕЛЬНЫЙ
процесс `blender.exe -b -P glb-to-fbx.py -- ...`, дождались завершения одного
перед стартом следующего. Так и задумано: модели тяжелые (сотни тысяч
треугольников, текстуры 4096), грузить несколько в одну сцену Blender
рискованно по памяти — уже роняло прогон. Один процесс на модель гарантирует,
что память отдается системе перед следующей моделью, а не копится.

Без аргументов имен обрабатывает пять моделей грунта прогона 7: они одни
входят в находку "карьер не читается ямой" (`loop/run-7/plan.md`).
"""

import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parent.parent
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
CONVERTER = HERE / "glb-to-fbx.py"
MODELS_DIR = REPO_ROOT / "design" / "models"

# Дефолтный список — пять грунтов, закрывающих находку 25 баллов
# ("карьер не читается ямой", `loop/run-7/plan.md`). Полный список принятых
# моделей дает ломатель отдельным отчетом (`loop/run-7/tester.md`).
DEFAULT_NAMES = [
    "grunt-regolit-2",
    "grunt-regolit-4",
    "grunt-led-1",
    "grunt-led-2",
    "grunt-led-3",
]

argv = sys.argv[1:]
if not argv:
    print(__doc__)
    raise SystemExit(2)

outdir = Path(argv[0]).resolve()
# Дефолт бюджета — 12000: потолок `check-model.mjs` (verdict(), стоп выше
# этого числа). См. loop/run-7/coder.md, узел 0 — 20000 из ранней версии
# плана приемку не проходит, проверено падающим тестом ломателя.
budget = int(argv[1]) if len(argv) > 1 else 12000
texture = int(argv[2]) if len(argv) > 2 else 1024
names = argv[3:] or DEFAULT_NAMES

def extract(text, marker):
    for line in text.splitlines():
        if line.startswith(marker):
            rest = line[len(marker):]
            return rest.split(" ", 1)[0]
    return "?"


outdir.mkdir(parents=True, exist_ok=True)
report_path = outdir / "batch-report.md"
rows = []

print(f"партия: {len(names)} моделей, бюджет {budget} тр. / текстура {texture}, выход {outdir}")

for i, name in enumerate(names, start=1):
    src = MODELS_DIR / f"{name}.glb"
    print(f"\n[{i}/{len(names)}] {name}")
    if not src.exists():
        print(f"  ПРОПУСК: исходника нет ({src})")
        rows.append(f"| {name} | нет исходника | - | - | - | - |")
        continue

    started = time.time()
    # capture_output + text=True декодирует поток кодировкой консоли Windows
    # (cp1251), а Blender печатает по-русски в UTF-8 — падает на первой же
    # кириллице ("треугольников"). Читаем байтами и декодируем сами.
    proc = subprocess.run(
        [
            BLENDER,
            "-b",
            "-P",
            str(CONVERTER),
            "--",
            str(src),
            str(outdir),
            str(budget),
            str(texture),
        ],
        capture_output=True,
    )
    seconds = time.time() - started
    proc_stdout = proc.stdout.decode("utf-8", errors="replace")
    proc_stderr = proc.stderr.decode("utf-8", errors="replace")

    log_path = outdir / f"{name}.blender.log"
    log_path.write_text(proc_stdout + "\n--- stderr ---\n" + proc_stderr, encoding="utf-8")

    if proc.returncode != 0:
        print(f"  ПРОВАЛ (код {proc.returncode}), {seconds:.0f} с — лог {log_path.name}")
        rows.append(f"| {name} | ПРОВАЛ | - | - | - | {seconds:.0f} с |")
        continue

    out = proc_stdout
    tris_glb = extract(out, "[1-iz-glb] треугольников ")
    tris_fbx = extract(out, "[2-iz-fbx] треугольников ")
    gabarit_line = next((line for line in out.splitlines() if line.startswith("ГАБАРИТ ")), "")

    fbx_path = outdir / f"{name}.fbx"
    fbx_mb = fbx_path.stat().st_size / 1048576 if fbx_path.exists() else 0

    print(f"  ок: {tris_glb} -> {tris_fbx} тр., FBX {fbx_mb:.1f} МБ, {seconds:.0f} с")
    print(f"  {gabarit_line}")
    rows.append(
        f"| {name} | ок | {tris_glb} -> {tris_fbx} | {fbx_mb:.1f} МБ | "
        f"{gabarit_line.replace('ГАБАРИТ ПОСЛЕ FBX ', '')} | {seconds:.0f} с |"
    )

report = [
    "# Отчет батча GLB -> FBX",
    "",
    f"Бюджет: {budget} треугольников, текстура {texture}x{texture}.",
    "",
    "| модель | статус | треугольники | вес FBX | габарит | время |",
    "|---|---|---|---|---|---|",
    *rows,
]
report_path.write_text("\n".join(report) + "\n", encoding="utf-8")
print(f"\nотчет: {report_path}")
