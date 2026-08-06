"""
Доводка сгенерированной модели до контракта сцены.

    blender -b -P fit-glb.py -- вход.glb выход.glb [целевые_треугольники] [клетки]

Генератор отдает модель как есть: сотни тысяч треугольников, случайный масштаб,
основание где угодно. Ни один из этих трех дефектов не виден на картинке
предпросмотра, и все три ловятся приемкой. Здесь они чинятся числами, а не
руками в редакторе: руками это десять минут на модель и разный результат
каждый раз.

Что делает, по порядку:

1. Сводит все меши импорта в один объект. Генератор иногда отдает несколько
   кусков, а сцене нужен один объект на клетку.
2. Упрощает до целевого числа треугольников. Порог набора — до 4000, типовая
   модель 200-2000; на входе бывает 300 тысяч.
3. Масштабирует так, чтобы объект занял заданную долю клетки 2.0.
4. Кладет основание ровно в ноль и центрирует по горизонтали.
5. Ставит пивот в основание объекта, а не в центр масс: раскладка сцены ставит
   объекты на грунт, и пивот в середине высоты means каждый объект пришлось бы
   поднимать вручную на половину собственной высоты.

Порядок важен. Упрощение ДО масштабирования: децимация работает по долям, а не
по абсолютным размерам, но сдвиг основания после нее иначе пришлось бы считать
дважды.
"""

import sys
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src = argv[0]
dst = argv[1]
target_tris = int(argv[2]) if len(argv) > 2 else 2000
cells = float(argv[3]) if len(argv) > 3 else 1.0

GRID = 2.0

# Чистая сцена: стартовый куб и лампа попали бы в экспорт.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
if not meshes:
    raise SystemExit("в файле нет ни одного меша")

# --- 1. Один объект ---------------------------------------------------------

bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active

# Трансформации импорта вносим в геометрию: дальше считаем по вершинам, и
# незапеченный поворот дал бы габарит повернутой коробки вместо объекта.
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

obj.data.calc_loop_triangles()
tris_before = len(obj.data.loop_triangles)

# --- 2. Упрощение -----------------------------------------------------------

if tris_before > target_tris:
    mod = obj.modifiers.new(name="decimate", type='DECIMATE')
    mod.ratio = target_tris / tris_before
    bpy.ops.object.modifier_apply(modifier=mod.name)

obj.data.calc_loop_triangles()
tris_after = len(obj.data.loop_triangles)


def bounds(o):
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


# --- 3. Масштаб под клетку --------------------------------------------------

lo, hi = bounds(obj)
size = hi - lo
# По следу на грунте, не по высоте: в клетку садится план объекта, а башня
# может быть сколь угодно высокой и это нормально.
footprint = max(size.x, size.y)
if footprint > 0:
    k = (GRID * cells) / footprint
    obj.scale = (k, k, k)
    bpy.ops.object.transform_apply(scale=True)

# --- 4 и 5. Основание в ноль, пивот туда же ---------------------------------

lo, hi = bounds(obj)
center_xy = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))

# Курсор — точка, куда переносится начало координат объекта.
bpy.context.scene.cursor.location = center_xy
bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
obj.location = (0, 0, 0)

# Без этой строки отчет врет. Присваивание `location` не пересчитывает
# `matrix_world` немедленно, а `bounds` читает именно его — и печатает габарит
# от ПРЕЖНЕГО положения. Экспорт при этом уходит верный, потому что берет
# трансформацию объекта, а не кэш. То есть скрипт делал правильную модель и
# отчитывался неправильными числами: ровно тот случай, когда доверять отчету
# опаснее, чем не иметь его вовсе.
bpy.context.view_layer.update()

lo, hi = bounds(obj)
size = hi - lo

bpy.ops.export_scene.gltf(
    filepath=dst,
    export_format='GLB',
    use_selection=False,
    export_yup=True,
)

# FBX рядом: Unity не читает glTF без отдельного пакета, а витрина собрана в
# Unity. Один вызов здесь дешевле, чем ручной перегон на той стороне.
if dst.lower().endswith('.glb'):
    bpy.ops.export_scene.fbx(
        filepath=dst[:-4] + '.fbx',
        use_selection=False,
        apply_unit_scale=True,
        bake_space_transform=True,
        axis_forward='-Z',
        axis_up='Y',
    )

print("=== ИТОГ ===")
print(f"треугольники: {tris_before} -> {tris_after}")
print(f"габарит: {size.x:.3f} x {size.y:.3f} x {size.z:.3f}")
print(f"основание z = {lo.z:.4f}")
print(f"центр в плане: x = {(lo.x + hi.x) / 2:.4f}, y = {(lo.y + hi.y) / 2:.4f}")
print(f"записано: {dst}")
