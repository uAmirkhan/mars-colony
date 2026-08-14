# Фундамент колонии по ядру gamedev-pipeline:
# - земля ОДИН сплошной меш, не тайлы (иначе поднос с ячейками);
# - ровные площадки под здания вырезаны маской: z = lerp(высота_площадки, шум, t);
# - карьер - утопленная чаша, модель ямы садится в нее вровень с краем;
# - посадка объектов лучом сверху вниз через BVHTree (не парит, не тонет);
# - декор кластерами, не равномерно (равномерность читается обоями).
import sys
import os
import math
import bpy
from mathutils import Vector, noise
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
models = os.path.abspath(argv[0])
outdir = os.path.abspath(argv[1])
blend_out = os.path.abspath(argv[2])
os.makedirs(outdir, exist_ok=True)

GROUND = 26.0        # сторона основания в метрах
SUBDIV = 130
AMP = 0.55           # амплитуда рельефа
TRI = 60000
TEX = 1024

# площадки: (x, y, радиус, высота_плато). Высота 0 - уровень земли
PADS = [
    (-6.0, 2.0, 3.4, 0.0),    # жилой кластер
    (-1.0, -5.5, 3.0, 0.0),   # посадочная площадка шаттла
    (6.2, 3.0, 2.6, 0.0),     # зона добычи, техника
    (5.0, -2.5, 3.0, 0.0),    # площадка карьера: модель сама несет яму
]

bpy.ops.wm.read_factory_settings(use_empty=True)


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


# ---- земля -------------------------------------------------------------------
bpy.ops.mesh.primitive_grid_add(x_subdivisions=SUBDIV, y_subdivisions=SUBDIV, size=GROUND)
ground = bpy.context.object
ground.name = 'zemlya'

for v in ground.data.vertices:
    x, y = v.co.x, v.co.y
    h = AMP * noise.fractal(Vector((x * 0.16, y * 0.16, 0.0)), 1.0, 2.0, 4)
    h += 0.18 * noise.noise(Vector((x * 0.55, y * 0.55, 7.0)))
    # площадки: чем ближе к центру, тем строже ровняем к высоте плато
    for px, py, pr, ph in PADS:
        d = math.hypot(x - px, y - py)
        t = smoothstep(pr * 0.55, pr, d)   # 0 в центре, 1 за краем
        h = ph * (1 - t) + h * t
    # край мира мягко поднимаем, чтобы не обрывался доской
    edge = max(abs(x), abs(y)) / (GROUND / 2)
    h += 0.9 * smoothstep(0.82, 1.0, edge)
    v.co.z = h

ground.data.update()
for p in ground.data.polygons:
    p.use_smooth = True

# материал земли: марсианский, темнее во впадинах (читаемость ямы по тону)
m = bpy.data.materials.new('mars_grunt')
m.use_nodes = True
nt = m.node_tree
bsdf = nt.nodes['Principled BSDF']
bsdf.inputs['Roughness'].default_value = 1.0
geo = nt.nodes.new('ShaderNodeNewGeometry')
sep = nt.nodes.new('ShaderNodeSeparateXYZ')
ramp = nt.nodes.new('ShaderNodeValToRGB')
mapr = nt.nodes.new('ShaderNodeMapRange')
nz = nt.nodes.new('ShaderNodeTexNoise')
mix = nt.nodes.new('ShaderNodeMix')
mix.data_type = 'RGBA'
nz.inputs['Scale'].default_value = 6.0
nz.inputs['Detail'].default_value = 6.0
mapr.inputs['From Min'].default_value = -1.0
mapr.inputs['From Max'].default_value = 1.0
ramp.color_ramp.elements[0].position = 0.0
ramp.color_ramp.elements[0].color = (0.23, 0.09, 0.06, 1)   # дно ям, темный
ramp.color_ramp.elements[1].position = 1.0
ramp.color_ramp.elements[1].color = (0.52, 0.23, 0.12, 1)   # верх, светлый
e = ramp.color_ramp.elements.new(0.55)
e.color = (0.45, 0.19, 0.11, 1)
nt.links.new(geo.outputs['Position'], sep.inputs['Vector'])
nt.links.new(sep.outputs['Z'], mapr.inputs['Value'])
nt.links.new(mapr.outputs['Result'], ramp.inputs['Fac'])
nt.links.new(ramp.outputs['Color'], mix.inputs['A'])
nt.links.new(nz.outputs['Color'], mix.inputs['B'])
mix.inputs['Factor'].default_value = 0.24
nt.links.new(mix.outputs['Result'], bsdf.inputs['Base Color'])
ground.data.materials.append(m)

# BVH для посадки
deps = bpy.context.evaluated_depsgraph_get()
bvh = BVHTree.FromObject(ground, deps)


def ground_z(x, y):
    hit = bvh.ray_cast(Vector((x, y, 50.0)), Vector((0, 0, -1)), 200.0)
    return hit[0].z if hit[0] else 0.0


# ---- загрузка моделей --------------------------------------------------------
cache = {}


def load(name):
    if name in cache:
        return cache[name]
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(models, f'{name}.glb'))
    new = [o for o in bpy.context.scene.objects if o not in before]
    ms = [o for o in new if o.type == 'MESH']
    for o in ms:
        o.data.calc_loop_triangles()
        n = len(o.data.loop_triangles)
        if n > TRI:
            bpy.context.view_layer.objects.active = o
            mod = o.modifiers.new('c', 'DECIMATE')
            mod.decimate_type = 'COLLAPSE'
            mod.ratio = TRI / n
            bpy.ops.object.modifier_apply(modifier='c')
    for img in bpy.data.images:
        if img.size[0] > TEX:
            img.scale(TEX, TEX)

    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in ms:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    for o in ms:
        o.hide_viewport = True
        o.hide_render = True
    cache[name] = (ms, lo, hi)
    return cache[name]


def put(name, x, y, target, rot=0.0, sink=0.0, sink_frac=0.0):
    """target - желаемый размер по длинной горизонтали, метры."""
    ms, lo, hi = load(name)
    size = hi - lo
    k = target / max(size.x, size.y)
    mid = (lo + hi) / 2
    root = bpy.data.objects.new(f'{name}_{x:.0f}_{y:.0f}', None)
    bpy.context.scene.collection.objects.link(root)
    for src in ms:
        cp = bpy.data.objects.new(src.name + '_c', src.data)
        cp.matrix_world = src.matrix_world.copy()
        bpy.context.scene.collection.objects.link(cp)
        cp.parent = root
        cp.matrix_parent_inverse = root.matrix_world.inverted()
    root.scale = (k, k, k)
    z = ground_z(x, y)
    total = sink + sink_frac * size.z * k
    root.location = (x - mid.x * k, y - mid.y * k, z - lo.z * k - total)
    root.rotation_euler = (0, 0, math.radians(rot))
    return root


# ---- раскладка: кластеры, не сетка ------------------------------------------
# жилой кластер на площадке слева
put('kupol-tunnel', -6.8, 2.8, 2.6, 15)
put('sklad-angar', -4.6, 1.0, 2.2, 105)
put('dekor-01', -7.6, 0.2, 1.4, 40, sink_frac=0.30)

# посадочная площадка: шаттл
put('shuttle', -1.0, -5.6, 3.4, 205)

# зона добычи: карьер в чаше + буровая рядом на плато
put('grunt-regolit-4', 5.0, -2.5, 4.2, 0, sink_frac=0.10)  # врыт, борт читается валом
put('burovaya-05', 6.6, 3.4, 2.4, 250)
put('dekor-09', 8.2, 1.2, 1.9, 130)          # скальный выход у добычи

# лед: кластер из трех на севере (один меш, три поворота - дешевая вариативность)
put('grunt-led-1', -1.5, 6.5, 2.0, 0, sink_frac=0.45)
put('grunt-led-3', 0.6, 7.3, 1.7, 140, sink_frac=0.45)
put('grunt-led-1', 1.9, 5.9, 1.3, 260, sink_frac=0.50)

# камни-декор двумя кластерами по краям
put('dekor-07', -8.5, -4.5, 1.8, 0)
put('dekor-07', 8.4, -6.2, 1.4, 200)
put('dekor-09', 10.4, -3.2, 1.1, 80)

# ---- свет, небо, камера ------------------------------------------------------
bpy.ops.object.light_add(type='SUN')
sun = bpy.context.object
sun.data.energy = 4.2
sun.data.angle = math.radians(2.5)
sun.rotation_euler = (math.radians(48), 0, math.radians(38))
bpy.ops.object.light_add(type='AREA', location=(-18, 14, 16))
fill = bpy.context.object
fill.data.energy = 1400
fill.data.size = 28
fill.rotation_euler = (math.radians(-40), 0, math.radians(-142))

w = bpy.data.worlds.new('nebo')
w.use_nodes = True
w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.45, 0.33, 0.32, 1)
w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.75
bpy.context.scene.world = w

scene = bpy.context.scene
eng = scene.render.bl_rna.properties['engine'].enum_items.keys()
scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in eng else list(eng)[0]
scene.render.resolution_x = 1800
scene.render.resolution_y = 1200

cd = bpy.data.cameras.new('cam')
cam = bpy.data.objects.new('cam', cd)
scene.collection.objects.link(cam)
scene.camera = cam
target = Vector((0, 0, 0.3))
for tag, (az, el, d, lens) in {
    'obshchiy': (-55, 33, 34, 42),
    'karier': (-25, 26, 16, 50),
}.items():
    cd.lens = lens
    a, e = math.radians(az), math.radians(el)
    cam.location = target + Vector((d * math.cos(e) * math.cos(a), d * math.cos(e) * math.sin(a), d * math.sin(e)))
    if tag == 'karier':
        look = Vector((5.0, -2.5, -0.4))
    else:
        look = target
    cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = os.path.join(outdir, f'fundament-{tag}.png')
    bpy.ops.render.render(write_still=True)
    print(f'кадр {scene.render.filepath}')

bpy.ops.wm.save_as_mainfile(filepath=blend_out)
print('сцена сохранена', blend_out)
