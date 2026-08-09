# Ищем дефект "объект на плите": генератор восстановил подложку из картинки
# как геометрию, и настоящий объект занимает малую долю пятна застройки.
# Тонкость такой дефект не ловит - у плиты есть толщина.
#
# Метрика: доля клеток пятна, в которых есть геометрия выше 25% высоты.
import sys
import os
import glob
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
models_dir = argv[0]
GRID = 48
LEVEL = 0.25

files = sorted(glob.glob(os.path.join(models_dir, '*.glb')))
rows = []

for path in files:
    name = os.path.splitext(os.path.basename(path))[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        bpy.ops.import_scene.gltf(filepath=path)
    except Exception as exc:
        print(f'ОШИБКА {name}: {exc}')
        continue
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        continue

    pts = []
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in meshes:
        mw = o.matrix_world
        for v in o.data.vertices:
            w = mw @ v.co
            pts.append(w)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))

    size = hi - lo
    if size.x <= 0 or size.y <= 0 or size.z <= 0:
        continue

    cut = lo.z + size.z * LEVEL
    low_cells = set()
    high_cells = set()
    for w in pts:
        cx = min(GRID - 1, int((w.x - lo.x) / size.x * GRID))
        cy = min(GRID - 1, int((w.y - lo.y) / size.y * GRID))
        low_cells.add((cx, cy))
        if w.z >= cut:
            high_cells.add((cx, cy))

    dolya = len(high_cells) / len(low_cells) if low_cells else 0
    dims = sorted([size.x, size.y, size.z])
    tonkost = dims[0] / dims[2]
    rows.append((name, tonkost, dolya, len(pts)))

print('=== ПЛИТА ===')
print(f'{"модель":<26}{"тонкость":>10}{"доля объекта":>14}   вердикт')
for name, t, d, n in sorted(rows, key=lambda r: r[2]):
    if t < 0.20:
        v = 'блин'
    elif d < 0.35:
        v = 'ПЛИТА: объект на подложке'
    elif d < 0.55:
        v = 'подложка заметна'
    else:
        v = 'чисто'
    print(f'{name:<26}{t:>10.3f}{d:>14.2f}   {v}')
