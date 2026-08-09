# Каталог всей партии: каждая модель измеряется и ставится в общую сетку
# рядом с фигурой человека. Для обзорного кадра геометрия упрощается,
# а текстуры ужимаются до 512 - иначе 36 объектов по две карты 4096
# не влезают в память.
import sys
import os
import math
import glob
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
models_dir = argv[0]
out_prefix = argv[1]
blend_out = argv[2]

CELL = 2.0
HUMAN = 1.8
STEP = 3.2          # шаг сетки витрины
COLS = 6
FIT = 2.0           # каждую модель вписываем в клетку по длинной горизонтали
TRI_BUDGET = 0      # 0 - не упрощать: упрощение рвет тонкие детали
TEX_MAX = 1024      # 35 моделей по две карты 4096 это 4.5 ГБ, столько не поднять

SKIP = {'shuttle-v2-1536', 'shuttle-20000'}  # дубли шаттла, чтобы не мусорить

bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgba, rough=0.9):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = rgba
    b.inputs['Roughness'].default_value = rough
    return m


ground_mat = mat('grunt', (0.62, 0.34, 0.24, 1))
line_mat = mat('razmetka', (0.32, 0.17, 0.13, 1))
human_mat = mat('chelovek', (0.88, 0.88, 0.90, 1))


def bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    return lo, hi


def shrink_images():
    for img in bpy.data.images:
        if img.size[0] > TEX_MAX:
            img.scale(TEX_MAX, TEX_MAX)


def opaque_materials():
    """Модели приезжают с режимом прозрачности HASHED. Это стохастическая
    альфа: в отрисовке она сыплет случайными черными точками по всей
    поверхности. Объекты сплошные, прозрачность им не нужна."""
    for m in bpy.data.materials:
        for attr, value in (('blend_method', 'OPAQUE'), ('shadow_method', 'OPAQUE')):
            if hasattr(m, attr):
                try:
                    setattr(m, attr, value)
                except (TypeError, AttributeError):
                    pass
        if not m.use_nodes:
            continue
        for node in m.node_tree.nodes:
            if node.type != 'BSDF_PRINCIPLED':
                continue
            alpha = node.inputs.get('Alpha')
            if alpha is not None and not alpha.is_linked:
                alpha.default_value = 1.0
            elif alpha is not None:
                for link in list(m.node_tree.links):
                    if link.to_socket == alpha:
                        m.node_tree.links.remove(link)
                alpha.default_value = 1.0


def simplify(obj, budget):
    obj.data.calc_loop_triangles()
    n = len(obj.data.loop_triangles)
    if not budget or n <= budget:
        return n
    bpy.context.view_layer.objects.active = obj
    m = obj.modifiers.new('collapse', 'DECIMATE')
    m.decimate_type = 'COLLAPSE'
    m.ratio = budget / n
    bpy.ops.object.modifier_apply(modifier='collapse')
    return n


files = sorted(glob.glob(os.path.join(models_dir, '*.glb')))
rows = []
placed = 0

for path in files:
    name = os.path.splitext(os.path.basename(path))[0]
    if name in SKIP:
        continue
    before = set(bpy.context.scene.objects)
    try:
        bpy.ops.import_scene.gltf(filepath=path)
    except Exception as exc:
        print(f'ОШИБКА ИМПОРТА {name}: {exc}')
        continue
    new = [o for o in bpy.context.scene.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    if not meshes:
        print(f'ПУСТО {name}')
        continue

    lo, hi = bbox(meshes)
    size = hi - lo
    dims = sorted([size.x, size.y, size.z])
    tonkost = dims[0] / dims[2] if dims[2] > 0 else 0

    # TRI_BUDGET = 0 означает "не упрощать". Через max(1, ...) ноль
    # превращался в единицу и резал объект до одной грани.
    budget = (TRI_BUDGET // len(meshes)) if TRI_BUDGET else 0
    tris = 0
    for o in meshes:
        tris += simplify(o, budget)

    root = bpy.data.objects.new(f'obj_{name}', None)
    bpy.context.scene.collection.objects.link(root)
    for o in new:
        if o.parent is None:
            o.parent = root
    bpy.context.view_layer.update()

    k = FIT / max(size.x, size.y)
    root.scale = (k, k, k)
    bpy.context.view_layer.update()

    lo2, hi2 = bbox(meshes)
    mid = (lo2 + hi2) / 2.0
    col = placed % COLS
    row = placed // COLS
    tx = (col - (COLS - 1) / 2.0) * STEP
    ty = -(row * STEP)
    root.location = (tx - mid.x, ty - mid.y, -lo2.z)
    bpy.context.view_layer.update()

    rows.append((name, size.x, size.y, size.z, tonkost, tris, (hi2 - lo2).z * 1.0))
    placed += 1

shrink_images()
opaque_materials()

# грунт под всей витриной
span = COLS * STEP + 6
depth = (placed // COLS + 2) * STEP + 6
bpy.ops.mesh.primitive_plane_add(size=1, location=(0, -depth / 2 + STEP, 0))
ground = bpy.context.object
ground.scale = (span, depth, 1)
ground.data.materials.append(ground_mat)

for i in range(-12, 13):
    for axis in (0, 1):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.004))
        ln = bpy.context.object
        if axis == 0:
            ln.scale = (0.014, depth, 0.004)
            ln.location = (i * CELL, -depth / 2 + STEP, 0.004)
        else:
            ln.scale = (span, 0.014, 0.004)
            ln.location = (0, i * CELL - depth / 2 + STEP, 0.004)
        ln.data.materials.append(line_mat)

bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
human = bpy.context.object
human.scale = (0.42, 0.26, HUMAN)
human.location = (-(COLS + 1) / 2.0 * STEP, 0, HUMAN / 2)
human.name = 'chelovek_1m80'
human.data.materials.append(human_mat)

bpy.ops.object.light_add(type='SUN', location=(10, -14, 18))
sun = bpy.context.object
sun.data.energy = 4.5
sun.data.angle = math.radians(3)
sun.rotation_euler = (math.radians(46), 0, math.radians(35))
bpy.ops.object.light_add(type='AREA', location=(-14, 10, 12))
fill = bpy.context.object
fill.data.energy = 600
fill.data.size = 20
fill.rotation_euler = (math.radians(-40), 0, math.radians(-150))

world = bpy.data.worlds.new('nebo')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.36, 0.31, 0.35, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.8
bpy.context.scene.world = world

scene = bpy.context.scene
engines = scene.render.bl_rna.properties['engine'].enum_items.keys()
scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else engines[0]
scene.render.resolution_x = 1800
scene.render.resolution_y = 1400

cam_data = bpy.data.cameras.new('cam')
cam_data.type = 'ORTHO'
cam_data.ortho_scale = max(span, depth) * 1.05
cam = bpy.data.objects.new('cam', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
target = Vector((0, -depth / 2 + STEP, 0.8))

el, az, d = math.radians(38), math.radians(-58), 80
cam.location = target + Vector((d * math.cos(el) * math.cos(az), d * math.cos(el) * math.sin(az), d * math.sin(el)))
cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.filepath = f'{out_prefix}-izometriya.png'
bpy.ops.render.render(write_still=True)

cam.location = target + Vector((0.01, 0.01, 70))
cam.rotation_euler = (0, 0, 0)
scene.render.filepath = f'{out_prefix}-sverkhu.png'
bpy.ops.render.render(write_still=True)

bpy.ops.wm.save_as_mainfile(filepath=blend_out)

print('=== ТАБЛИЦА ===')
print(f'{"модель":<28}{"габарит исходный":>26}{"тонкость":>10}{"треуг.":>10}')
for name, sx, sy, sz, t, tris, _ in sorted(rows, key=lambda r: r[4]):
    flag = 'БЛИН' if t < 0.20 else ''
    print(f'{name:<28}{sx:>8.3f}{sy:>9.3f}{sz:>9.3f}{t:>10.3f}{tris:>10}  {flag}')
print(f'ВСЕГО {len(rows)}')
