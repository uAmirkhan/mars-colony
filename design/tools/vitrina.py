# Витрина: все приехавшие модели на размеченном грунте, в одном масштабе,
# рядом с фигурой человека 1.8 м. Клетка 2.0 м.
import sys
import math
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
models_dir = argv[0]
out_prefix = argv[1]
blend_out = argv[2]

CELL = 2.0
HUMAN = 1.8

bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgba, rough=0.9):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = rgba
    b.inputs['Roughness'].default_value = rough
    return m


ground_mat = mat('grunt', (0.62, 0.34, 0.24, 1))
line_mat = mat('razmetka', (0.30, 0.16, 0.12, 1))
human_mat = mat('chelovek', (0.85, 0.85, 0.87, 1))

# грунт
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
ground = bpy.context.object
ground.data.materials.append(ground_mat)

# разметка по клеткам
for i in range(-10, 11):
    for axis in (0, 1):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.004))
        line = bpy.context.object
        if axis == 0:
            line.scale = (0.012, 20.0, 0.004)
            line.location = (i * CELL, 0, 0.004)
        else:
            line.scale = (20.0, 0.012, 0.004)
            line.location = (0, i * CELL, 0.004)
        line.data.materials.append(line_mat)

# фигура человека
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, HUMAN / 2))
human = bpy.context.object
human.scale = (0.42, 0.26, HUMAN)
human.name = 'chelovek_1m80'
human.data.materials.append(human_mat)


def place(path, target_len, at_x, at_y, turn_deg=0.0):
    """Импорт GLB, масштаб по длинной горизонтальной оси, низ в ноль."""
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.context.scene.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    if not meshes:
        return None

    root = bpy.data.objects.new(f'gruppa_{len(bpy.data.objects)}', None)
    bpy.context.scene.collection.objects.link(root)
    for o in new:
        if o.parent is None:
            o.parent = root
    bpy.context.view_layer.update()

    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in meshes:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    size = hi - lo
    k = target_len / max(size.x, size.y)

    root.scale = (k, k, k)
    bpy.context.view_layer.update()

    lo2 = Vector((1e9, 1e9, 1e9))
    hi2 = Vector((-1e9, -1e9, -1e9))
    for o in meshes:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo2 = Vector((min(lo2[i], w[i]) for i in range(3)))
            hi2 = Vector((max(hi2[i], w[i]) for i in range(3)))
    mid = (lo2 + hi2) / 2.0

    root.rotation_euler = (0, 0, math.radians(turn_deg))
    root.location = (at_x - mid.x, at_y - mid.y, -lo2.z)
    bpy.context.view_layer.update()
    return root, size, k


plan = [
    ('shuttle-2000.glb', 6.0, -4.0, 3.0, -25.0),
    ('shuttle-20000.glb', 6.0, 5.0, 3.0, -25.0),
    ('dron-kurier.glb', 1.6, 0.0, -3.0, 20.0),
]

print('=== РАССТАНОВКА ===')
for fname, length, x, y, turn in plan:
    r = place(f'{models_dir}/{fname}', length, x, y, turn)
    if r:
        _, size, k = r
        print(f'{fname}: исходно {size.x:.2f} x {size.y:.2f} x {size.z:.2f}, '
              f'масштаб x{k:.2f}, в сцене {size.x*k:.2f} x {size.y*k:.2f} x {size.z*k:.2f} м')

# свет
bpy.ops.object.light_add(type='SUN', location=(8, -10, 14))
sun = bpy.context.object
sun.data.energy = 4.0
sun.data.angle = math.radians(3)
sun.rotation_euler = (math.radians(48), 0, math.radians(35))

bpy.ops.object.light_add(type='AREA', location=(-10, 8, 8))
fill = bpy.context.object
fill.data.energy = 300
fill.data.size = 14
fill.rotation_euler = (math.radians(-40), 0, math.radians(-150))

world = bpy.data.worlds.new('nebo')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.35, 0.30, 0.34, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.7
bpy.context.scene.world = world

# камера: изометрия, как в игре
cam_data = bpy.data.cameras.new('cam')
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 20.0
cam = bpy.data.objects.new('cam', cam_data)
bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam
el = math.radians(35)
az = math.radians(-55)
d = 30
cam.location = (d * math.cos(el) * math.cos(az), d * math.cos(el) * math.sin(az), d * math.sin(el))
cam.rotation_euler = (Vector((0, 0, 0.8)) - cam.location).to_track_quat('-Z', 'Y').to_euler()

scene = bpy.context.scene
engines = scene.render.bl_rna.properties['engine'].enum_items.keys()
scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else engines[0]
print('движок:', scene.render.engine)
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000
scene.render.film_transparent = False
try:
    scene.eevee.use_shadows = True
    scene.eevee.taa_render_samples = 48
except Exception as exc:
    print('настройки eevee частично не применились:', exc)

scene.render.filepath = f'{out_prefix}-izometriya.png'
bpy.ops.render.render(write_still=True)

# второй кадр: сбоку, чтобы видеть рост относительно человека
cam_data.ortho_scale = 16.0
cam.location = (0, -24, 3.2)
cam.rotation_euler = (math.radians(86), 0, 0)
scene.render.filepath = f'{out_prefix}-sboku.png'
bpy.ops.render.render(write_still=True)

bpy.ops.wm.save_as_mainfile(filepath=blend_out)
print(f'СЦЕНА СОХРАНЕНА {blend_out}')
