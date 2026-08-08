"""
Огранка сгенерированной модели под стиль набора.

    blender -b -P facet-glb.py -- вход.glb выход.glb [угол] [треугольники]

Генератор отдает гладкую поверхность: мелкие треугольники плюс включенное
сглаживание нормалей. Набор сцены держится на обратном — крупные плоские грани
с резкими ребрами. Рядом друг с другом это читается как две разные игры.

Лечится в два приема, и первый важнее второго.

1. **Выключить сглаживание.** Оно не меняет геометрию, только врет о ней:
   рисует плоские грани так, будто поверхность круглая. Часть «мыла» уходит от
   одного этого, без единого удаленного треугольника.

2. **Планарная децимация.** Сливает соседние грани, отличающиеся по наклону
   меньше заданного угла, в одну большую. Именно это дает КРУПНЫЕ грани, а не
   просто уменьшает счетчик: обычная децимация схлопывает ребра и оставляет ту
   же кашу, только реже.

Порядок обратный привычному: сначала планарная, потом добор количества. Если
сначала загнать в бюджет обычной децимацией, планарной уже нечего сливать —
плоских областей не останется.
"""

import sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src = argv[0]
dst = argv[1]
# Угол в градусах: грани, отличающиеся меньше чем на столько, сливаются в одну.
# Больше угол — крупнее грани и грубее форма.
angle = float(argv[2]) if len(argv) > 2 else 20.0
target_tris = int(argv[3]) if len(argv) > 3 else 0

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
if not meshes:
    raise SystemExit("в файле нет ни одного меша")

bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active

obj.data.calc_loop_triangles()
tris_before = len(obj.data.loop_triangles)

# --- 1. Долой сглаживание ---------------------------------------------------

for poly in obj.data.polygons:
    poly.use_smooth = False
# Автосглаживание в новых версиях живет отдельным модификатором и переживает
# сброс флагов на гранях — снимаем и его.
for mod in [m for m in obj.modifiers if m.type == 'NODES' and 'Smooth' in (m.name or '')]:
    obj.modifiers.remove(mod)

# --- 2. Планарная децимация -------------------------------------------------

planar = obj.modifiers.new(name="planar", type='DECIMATE')
planar.decimate_type = 'DISSOLVE'
planar.angle_limit = angle * 3.14159265 / 180.0
# Границы материалов и швы UV сохраняем: иначе покраска поедет вслед за
# геометрией, и объект придется красить заново.
planar.delimit = {'MATERIAL', 'UV'}
bpy.ops.object.modifier_apply(modifier=planar.name)

obj.data.calc_loop_triangles()
tris_planar = len(obj.data.loop_triangles)

# --- 3. Добор до бюджета, если после огранки все еще много ------------------

if target_tris > 0 and tris_planar > target_tris:
    collapse = obj.modifiers.new(name="collapse", type='DECIMATE')
    collapse.ratio = target_tris / tris_planar
    bpy.ops.object.modifier_apply(modifier=collapse.name)
    # Схлопывание ребер снова включает сглаживание на новых гранях.
    for poly in obj.data.polygons:
        poly.use_smooth = False

obj.data.calc_loop_triangles()
tris_after = len(obj.data.loop_triangles)

bpy.ops.export_scene.gltf(filepath=dst, export_format='GLB', use_selection=False, export_yup=True)
if dst.lower().endswith('.glb'):
    bpy.ops.export_scene.fbx(
        filepath=dst[:-4] + '.fbx',
        use_selection=False,
        apply_unit_scale=True,
        bake_space_transform=True,
        axis_forward='-Z',
        axis_up='Y',
    )

print("=== ИТОГ ОГРАНКИ ===")
print(f"угол слияния: {angle} градусов")
print(f"треугольники: {tris_before} -> после планарной {tris_planar} -> итог {tris_after}")
print(f"записано: {dst}")
