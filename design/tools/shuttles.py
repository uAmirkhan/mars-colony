# Все варианты шаттла рядом, без упрощения, с непрозрачными материалами.
import sys
import math
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
models_dir = argv[0]
out_prefix = argv[1]
blend_out = argv[2]

NAMES = ['shuttle-v2-1536', 'shuttle', 'shuttle-20000', 'shuttle-2000']
STEP = 8.0
LEN = 6.0
HUMAN = 1.8

bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgba):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = rgba
    m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.9
    return m


def opaque():
    for m in bpy.data.materials:
        for attr in ('blend_method', 'shadow_method'):
            if hasattr(m, attr):
                try:
                    setattr(m, attr, 'OPAQUE')
                except (TypeError, AttributeError):
                    pass
        if not m.use_nodes:
            continue
        for node in m.node_tree.nodes:
            if node.type == 'BSDF_PRINCIPLED':
                a = node.inputs.get('Alpha')
                if a is not None:
                    for link in list(m.node_tree.links):
                        if link.to_socket == a:
                            m.node_tree.links.remove(link)
                    a.default_value = 1.0


def bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    return lo, hi


bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
bpy.context.object.data.materials.append(mat('grunt', (0.62, 0.34, 0.24, 1)))

bpy.ops.mesh.primitive_cube_add(size=1)
human = bpy.context.object
human.scale = (0.42, 0.26, HUMAN)
human.location = (-STEP * 1.9, 0, HUMAN / 2)
human.data.materials.append(mat('chelovek', (0.9, 0.9, 0.92, 1)))

for i, name in enumerate(NAMES):
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=f'{models_dir}/{name}.glb')
    new = [o for o in bpy.context.scene.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    tris = 0
    for o in meshes:
        o.data.calc_loop_triangles()
        tris += len(o.data.loop_triangles)
    root = bpy.data.objects.new(f'v_{name}', None)
    bpy.context.scene.collection.objects.link(root)
    for o in new:
        if o.parent is None:
            o.parent = root
    bpy.context.view_layer.update()
    lo, hi = bbox(meshes)
    k = LEN / max((hi - lo).x, (hi - lo).y)
    root.scale = (k, k, k)
    bpy.context.view_layer.update()
    lo2, hi2 = bbox(meshes)
    mid = (lo2 + hi2) / 2
    root.rotation_euler = (0, 0, math.radians(-20))
    root.location = ((i - 1.5) * STEP - mid.x, -mid.y, -lo2.z)
    bpy.context.view_layer.update()
    print(f'{name}: {tris} треугольников, в сцене {(hi2-lo2).x:.2f} x {(hi2-lo2).y:.2f} x {(hi2-lo2).z:.2f} м')

opaque()

bpy.ops.object.light_add(type='SUN', location=(10, -14, 18))
bpy.context.object.data.energy = 4.5
bpy.context.object.rotation_euler = (math.radians(46), 0, math.radians(35))
bpy.ops.object.light_add(type='AREA', location=(-14, 10, 12))
bpy.context.object.data.energy = 500
bpy.context.object.data.size = 20
bpy.context.object.rotation_euler = (math.radians(-40), 0, math.radians(-150))
w = bpy.data.worlds.new('nebo')
w.use_nodes = True
w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.36, 0.31, 0.35, 1)
bpy.context.scene.world = w

scene = bpy.context.scene
engines = scene.render.bl_rna.properties['engine'].enum_items.keys()
scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else engines[0]
scene.render.resolution_x = 1900
scene.render.resolution_y = 800
cam_data = bpy.data.cameras.new('cam')
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 38
cam = bpy.data.objects.new('cam', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
target = Vector((0, 0, 1.5))
el, az, d = math.radians(24), math.radians(-70), 60
cam.location = target + Vector((d * math.cos(el) * math.cos(az), d * math.cos(el) * math.sin(az), d * math.sin(el)))
cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.filepath = f'{out_prefix}-ryadom.png'
bpy.ops.render.render(write_still=True)

bpy.ops.wm.save_as_mainfile(filepath=blend_out)
print('СОХРАНЕНО', blend_out)
