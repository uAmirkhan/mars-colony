"""
Запекание ГОТОВОЙ модели в изометрический спрайт.

Зачем отдельно от `iso_rig.py`: тот лепит здания из примитивов кодом, и это
тупик. Обаяние формы не набирается перечислением координат кубов — модель,
собранную так, видно за версту. Профессиональную геометрию берем готовой
(CC0-наборы KayKit, Kenney, Quaternius), а своим оставляем то, что скрипт
действительно делает лучше человека: единый угол, единый свет, единую
палитру и повторяемость.

Материалы импортированной модели заменяются тоновыми с сохранением базового
цвета — иначе чужой набор приедет в своем стиле и рядом с нашими ассетами
будет выглядеть заимствованием.

Запуск:
    blender --background --python design/blender/bake_asset.py -- <модель> <выход.png> [масштаб]
"""

import math
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from toon import toon_material, toon_over_texture  # noqa: E402

CAM_TILT_DEG = 60.0
CAM_SPIN_DEG = 45.0
CAM_DISTANCE = 20.0
RESOLUTION = 1024
SAMPLES = 64
OUTLINE_COLOR = (0.09, 0.06, 0.05)
OUTLINE_THICKNESS = 3.0

# Перекраска чужого набора в палитру колонии. Ключ — приблизительный оттенок
# исходного материала, значение — наш цвет. Без этого пак выглядит чужим.
PALETTE_MAP = [
    # (исходный оттенок H в градусах, наш цвет)
    ((0, 25), (0.95, 0.40, 0.10)),  # красное -> оранжевый акцент
    ((25, 55), (0.96, 0.87, 0.70)),  # желто-бежевое -> кремовый корпус
    ((55, 160), (0.34, 0.72, 0.24)),  # зеленое -> зелень гидропоники
    ((160, 260), (0.15, 0.80, 0.84)),  # голубое -> бирюзовое стекло
    ((260, 360), (0.72, 0.28, 0.12)),  # пурпурное -> ржавый
]


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.worlds):
        for item in list(block):
            block.remove(item)


def rgb_to_h(color):
    r, g, b = color[:3]
    mx, mn = max(r, g, b), min(r, g, b)
    if mx - mn < 1e-5:
        return None  # серое: оттенка нет, красим как металл
    if mx == r:
        h = 60 * (((g - b) / (mx - mn)) % 6)
    elif mx == g:
        h = 60 * (((b - r) / (mx - mn)) + 2)
    else:
        h = 60 * (((r - g) / (mx - mn)) + 4)
    return h % 360


def mapped_color(base):
    """Цвет исходного материала переводится в ближайший из нашей палитры."""
    h = rgb_to_h(base)
    if h is None:
        v = max(base[:3])
        return (0.42, 0.47, 0.56) if v > 0.35 else (0.20, 0.22, 0.29)
    for (lo, hi), color in PALETTE_MAP:
        if lo <= h < hi:
            return color
    return (0.88, 0.83, 0.74)


def base_color_of(mat):
    if not mat or not mat.use_nodes:
        return (0.8, 0.8, 0.8)
    for node in mat.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            return tuple(node.inputs["Base Color"].default_value)[:3]
        if node.type == "EMISSION":
            return tuple(node.inputs["Color"].default_value)[:3]
    return (0.8, 0.8, 0.8)


def texture_of(mat):
    """Изображение материала, если оно есть. У чужих наборов весь цвет в нем."""
    if not mat or not mat.use_nodes:
        return None
    for node in mat.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image:
            return node.image
    return None


def retoon(recolor=True):
    """
    Все материалы сцены -> тоновые.

    Развилка по наличию текстуры. Если она есть — полосы света кладутся ПОВЕРХ
    нее, и работа художника сохраняется. Если нет — материал собирается из
    плоского цвета. Первый заход этой развилки не имел и подменял все флэтом
    по оттенку: набор с текстурным атласом приехал целиком стальным.

    Кэш по имени исходного материала: у KayKit один материал висит на десятках
    объектов, без кэша плодились бы сотни одинаковых шейдеров.
    """
    cache = {}
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH" or not obj.data.materials:
            continue
        for i, mat in enumerate(obj.data.materials):
            key = mat.name if mat else "none"
            if key not in cache:
                image = texture_of(mat)
                if image is not None:
                    tint = (0.98, 0.72, 0.42) if recolor else None
                    cache[key] = toon_over_texture(f"toon_{key}", image, tint=tint)
                else:
                    base = base_color_of(mat)
                    color = mapped_color(base) if recolor else base
                    cache[key] = toon_material(f"toon_{key}", color)
            obj.data.materials[i] = cache[key]


def frame_all():
    """
    Ортомасштаб по габариту сцены, а не константой.

    Разные наборы приходят в разных единицах: одна модель метровая, другая
    в сантиметрах. Константа обрезала бы половину или оставляла пустое поле.
    """
    xs, ys, zs = [], [], []
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            world = obj.matrix_world @ __import__("mathutils").Vector(corner)
            xs.append(world.x)
            ys.append(world.y)
            zs.append(world.z)
    if not xs:
        return 6.0, (0, 0, 0)
    center = ((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, (min(zs) + max(zs)) / 2)
    size = max(max(xs) - min(xs), max(ys) - min(ys), (max(zs) - min(zs)) * 1.25)
    return size * 1.45 + 0.6, center


def setup(center, ortho):
    cam_data = bpy.data.cameras.new("iso")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ortho
    cam = bpy.data.objects.new("iso", cam_data)
    bpy.context.collection.objects.link(cam)

    tilt = math.radians(CAM_TILT_DEG)
    spin = math.radians(CAM_SPIN_DEG)
    cam.location = (
        center[0] + CAM_DISTANCE * math.sin(tilt) * math.sin(spin),
        center[1] - CAM_DISTANCE * math.sin(tilt) * math.cos(spin),
        center[2] + CAM_DISTANCE * math.cos(tilt),
    )
    cam.rotation_euler = (tilt, 0.0, spin)
    bpy.context.scene.camera = cam

    key = bpy.data.lights.new("key", type="SUN")
    key.energy = 3.2
    key.color = (1.0, 0.95, 0.86)
    ko = bpy.data.objects.new("key", key)
    ko.rotation_euler = (math.radians(46), 0, math.radians(-42))
    bpy.context.collection.objects.link(ko)

    fill = bpy.data.lights.new("fill", type="SUN")
    fill.energy = 1.1
    fill.color = (0.74, 0.83, 1.0)
    fo = bpy.data.objects.new("fill", fill)
    fo.rotation_euler = (math.radians(62), 0, math.radians(150))
    bpy.context.collection.objects.link(fo)

    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = next((n for n in world.node_tree.nodes if n.type == "BACKGROUND"), None)
    if bg:
        bg.inputs[0].default_value = (0.55, 0.58, 0.66, 1)
        bg.inputs[1].default_value = 0.30
    bpy.context.scene.world = world

    scene = bpy.context.scene
    scene.render.use_freestyle = True
    scene.render.line_thickness_mode = "ABSOLUTE"
    scene.render.line_thickness = OUTLINE_THICKNESS
    vl = bpy.context.view_layer
    vl.use_freestyle = True
    ls = vl.freestyle_settings.linesets.new("outline")
    ls.select_silhouette = True
    ls.select_border = True
    ls.select_crease = True
    ls.linestyle.color = OUTLINE_COLOR
    ls.linestyle.thickness = OUTLINE_THICKNESS


def render(out_path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    for attr, value in (
        ("taa_render_samples", SAMPLES),
        ("use_gtao", True),
        ("gtao_distance", 0.5),
        ("use_soft_shadows", True),
    ):
        if hasattr(scene.eevee, attr):
            setattr(scene.eevee, attr, value)
    scene.render.resolution_x = RESOLUTION
    scene.render.resolution_y = RESOLUTION
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = out_path
    scene.view_settings.view_transform = "Standard"
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    src, out = argv[0], argv[1]
    recolor = (argv[2].lower() != "keep") if len(argv) > 2 else True

    clear_scene()
    ext = os.path.splitext(src)[1].lower()
    if ext in (".gltf", ".glb"):
        bpy.ops.import_scene.gltf(filepath=src)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=src)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=src)
    else:
        raise SystemExit(f"не знаю формат: {ext}")

    retoon(recolor=recolor)
    ortho, center = frame_all()
    setup(center, ortho)
    render(out)
    print(f"ГОТОВО: {out}")


main()
