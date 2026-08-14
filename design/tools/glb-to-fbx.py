# Гейт прогона 7: переживает ли запеченная текстура переход GLB -> FBX.
# Проверка честная: экспортируем FBX, ЗАБЫВАЕМ исходник, импортируем FBX
# обратно в пустую сцену и снимаем кадр. Серый объект на кадре = не доехала.
import sys
import os
import math
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
# Абсолютные пути обязательны: у Blender рендер кадра (bpy.ops.render.render)
# резолвит относительный filepath не через os.getcwd(), а через свой
# внутренний "//"-механизм blend-относительных путей. Когда сцена не
# сохранена (наш случай — read_factory_settings(use_empty=True) каждый раз),
# это резолвится в корень диска, а не в рабочий каталог процесса: кадр уезжал
# в C:\design\... вместо design/models/osmotr/fbx внутри проекта, при этом
# сам FBX (экспортер работает через чистый os.path) писался верно. Замечено
# и починено в прогоне 7, узел 0.
src = os.path.abspath(argv[0])
outdir = os.path.abspath(argv[1])
# Потолок check-model.mjs — 12000 (verdict(), 'стоп' выше этого числа), не
# 20000: см. loop/run-7/coder.md, узел 0. Дефолт держим тем же, чем реально
# проходит приемка, а не тем, что казалось безопасным на бумаге.
budget = int(argv[2]) if len(argv) > 2 else 12000
# Сторона квадратной текстуры после сжатия. Вес сборки считает байты, не
# полигоны: запеченная карта 4096 весит в разы больше самой модели. Резка —
# не перекраска (та отменена владельцем): палитра и рисунок текстуры не
# меняются, меняется только ее разрешение.
texture = int(argv[3]) if len(argv) > 3 else 1024

os.makedirs(outdir, exist_ok=True)
name = os.path.splitext(os.path.basename(src))[0]
fbx_path = os.path.join(outdir, f'{name}.fbx')


def meshes():
    return [o for o in bpy.context.scene.objects if o.type == 'MESH']


def bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    return lo, hi


def shot(tag):
    ms = meshes()
    lo, hi = bbox(ms)
    ctr = (lo + hi) / 2
    r = max(hi - lo) * 0.5
    cd = bpy.data.cameras.new('c')
    cam = bpy.data.objects.new('c', cd)
    bpy.context.scene.collection.objects.link(cam)
    s = bpy.context.scene
    s.camera = cam
    s.render.engine = 'BLENDER_WORKBENCH'
    s.display.shading.light = 'STUDIO'
    s.display.shading.color_type = 'TEXTURE'
    s.render.resolution_x = 900
    s.render.resolution_y = 700
    s.world = bpy.data.worlds.new('w')
    s.world.color = (0.35, 0.35, 0.38)
    a, e = math.radians(50), math.radians(28)
    d = r * 2.9
    cam.location = ctr + Vector((d * math.cos(e) * math.cos(a), d * math.cos(e) * math.sin(a), d * math.sin(e)))
    cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
    s.render.filepath = os.path.join(outdir, f'{name}-{tag}.png')
    bpy.ops.render.render(write_still=True)
    tex = [f'{i.name} {i.size[0]}x{i.size[1]}' for i in bpy.data.images if i.size[0] > 0]
    tris = 0
    for o in ms:
        o.data.calc_loop_triangles()
        tris += len(o.data.loop_triangles)
    print(f'[{tag}] треугольников {tris} | текстуры в памяти: {tex if tex else "НЕТ"}')


# ---- шаг 1: GLB, режем, экспорт FBX -----------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

# Сжатие текстур ДО кадра, чтобы кадр честно показывал итоговое качество, а
# не карту 4096, которая на экспорт все равно не попадет.
#
# img.pack() после scale() обязателен. Картинки из GLB приходят уже
# упакованными (запеченный бинарник внутри самого .glb), и экспортер FBX при
# embed_textures читает именно упакованные байты, а не текущий пиксельный
# буфер. Без re-pack размер в памяти показывает 1024, а в FBX все равно
# уезжают исходные 4096 — проверено и поймано на этом самом прогоне.
for img in bpy.data.images:
    if img.size[0] > texture or img.size[1] > texture:
        img.scale(texture, texture)
        img.pack()

# Имя картинки без расширения ("Image_0", как отдает импортер GLB) уезжает
# в FBX как имя embedded-файла ("Image_0" без ".png"). Unity распаковывает
# embedded-media по запросу (ModelIntake.ExtractTextures), но файл без
# расширения ассетная база маркирует DefaultImporter, а не TextureImporter,
# и он не становится текстурой — материал остается без картинки. Расширение
# в имени датаблока чинит это на входе, а не в Unity. Проверено и поймано в
# прогоне 7, узел 0.
# Имена картинок делаем УНИКАЛЬНЫМИ детерминированно: импортер GLB дает
# двум картам одно имя (Image_0 / Image_0.001), и при экспорте вторая
# терялась - дно карьера приезжало без текстуры (замеры 3-5 цикла ours).
for i, img in enumerate(list(bpy.data.images)):
    img.file_format = 'PNG'
    img.name = f'tex_{i}.png'

for o in meshes():
    o.data.calc_loop_triangles()
    n = len(o.data.loop_triangles)
    if n > budget:
        bpy.context.view_layer.objects.active = o
        m = o.modifiers.new('c', 'DECIMATE')
        m.decimate_type = 'COLLAPSE'
        m.ratio = budget / n
        bpy.ops.object.modifier_apply(modifier='c')

# основание в ноль, пивот по центру в плане, вписать в клетку 2.0
ms = meshes()
lo, hi = bbox(ms)
size = hi - lo
k = 2.0 / max(size.x, size.y)
for o in ms:
    o.scale = (o.scale.x * k, o.scale.y * k, o.scale.z * k)
bpy.context.view_layer.update()
lo, hi = bbox(meshes())
mid = (lo + hi) / 2
for o in meshes():
    o.location = (o.location.x - mid.x, o.location.y - mid.y, o.location.z - lo.z)
bpy.context.view_layer.update()

shot('1-iz-glb')

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(
    filepath=fbx_path,
    use_selection=True,
    path_mode='COPY',
    embed_textures=True,
    apply_unit_scale=True,
    global_scale=1.0,
    axis_forward='-Z',
    axis_up='Y',
)
print(f'ЭКСПОРТ {fbx_path} {os.path.getsize(fbx_path)/1048576:.1f} МБ')

# ---- шаг 2: чистая сцена, читаем ТОЛЬКО FBX ---------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
for img in list(bpy.data.images):
    bpy.data.images.remove(img)
bpy.ops.import_scene.fbx(filepath=fbx_path)
shot('2-iz-fbx')

lo, hi = bbox(meshes())
sz = hi - lo
print(f'ГАБАРИТ ПОСЛЕ FBX {sz.x:.3f} x {sz.y:.3f} x {sz.z:.3f}, основание z={lo.z:.4f}')
