"""
Изометрический риг для запекания спрайтов зданий.

Смысл существования: генерации в Gemini дают каждый раз свой угол, свой свет
и свой масштаб, и собрать из них одну сцену нельзя — здания не встают в общую
систему. Здесь угол и свет заданы один раз камерой и лампами, поэтому все
здания выходят согласованными по построению, а не по везению.

Так работает настоящий цех: мобильные ситибилдеры отгружают 2.5D-спрайты,
запеченные из 3D под фиксированной ортокамерой.

Запуск:
    blender --background --python design/blender/iso_rig.py -- <имя_здания> <выход.png>
"""

import math
import sys

import bpy

# --- Параметры рига. Меняются здесь и только здесь ---------------------------

# Классический изометрический угол: наклон 60 градусов, поворот 45.
CAM_TILT_DEG = 60.0
CAM_SPIN_DEG = 45.0
CAM_DISTANCE = 12.0
ORTHO_SCALE = 9.5

RESOLUTION = 1024
SAMPLES = 96

# Палитра эталонов колонии: кремовый корпус, темные панели, оранжевый акцент,
# бирюзовое стекло. Взята с принятых кадров, не придумана здесь.
#
# Значения подняты по насыщенности после первого прогона: холодное небо
# обесцвечивало корпус до серого пластика. Материал в рендере всегда выходит
# бледнее собственного цвета, поэтому исходник берется с запасом.
CREAM = (0.95, 0.86, 0.68, 1.0)
DARK = (0.18, 0.20, 0.26, 1.0)
ORANGE = (0.95, 0.40, 0.08, 1.0)
TEAL = (0.10, 0.78, 0.82, 1.0)
SAND = (0.82, 0.62, 0.38, 1.0)
GREEN = (0.30, 0.70, 0.22, 1.0)

# Обводка. У принятых эталонов она есть, и без нее рендер читается как
# служебный макет, а не как игровой ассет: силуэт теряется на любом фоне.
OUTLINE_COLOR = (0.10, 0.07, 0.06)
OUTLINE_THICKNESS = 2.6


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.lights):
        for item in list(block):
            block.remove(item)


def find_node(node_tree, type_name, create=True):
    """
    Узел по ТИПУ, а не по имени.

    Blender 5.2 не гарантирует узел с именем «Background» или «Principled BSDF»
    сразу после создания блока данных: `use_nodes` объявлен устаревшим, и дерево
    иногда приезжает пустым. Поиск по имени падал через раз — буквально один
    рендер проходил, следующий с тем же скриптом валился с KeyError.
    """
    for node in node_tree.nodes:
        if node.type == type_name:
            return node
    if not create:
        return None
    node = node_tree.nodes.new(
        {"BSDF_PRINCIPLED": "ShaderNodeBsdfPrincipled", "BACKGROUND": "ShaderNodeBackground"}[
            type_name
        ]
    )
    output = find_node_output(node_tree)
    if output:
        node_tree.links.new(node.outputs[0], output.inputs[0])
    return node


def find_node_output(node_tree):
    for node in node_tree.nodes:
        if node.type in ("OUTPUT_MATERIAL", "OUTPUT_WORLD"):
            return node
    return None


def make_material(name, color, metallic=0.0, roughness=0.55, emission=0.0):
    """Материал на Principled BSDF. Имена входов сверяются с версией Blender."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = find_node(mat.node_tree, "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission > 0:
        # В 4.x вход переименован в Emission Color; поддерживаем оба имени.
        key = "Emission Color" if "Emission Color" in bsdf.inputs else "Emission"
        bsdf.inputs[key].default_value = color
        bsdf.inputs["Emission Strength"].default_value = emission
    return mat


def shade(obj, mat, bevel=0.02, smooth=False):
    """Фаска обязательна: острые ребра в мелком спрайте читаются как мусор."""
    obj.data.materials.append(mat)
    if bevel > 0:
        mod = obj.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 3
        mod.limit_method = "ANGLE"
    if smooth:
        bpy.ops.object.shade_smooth()


def setup_camera():
    cam_data = bpy.data.cameras.new("iso_cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ORTHO_SCALE
    cam = bpy.data.objects.new("iso_cam", cam_data)
    bpy.context.collection.objects.link(cam)

    tilt = math.radians(CAM_TILT_DEG)
    spin = math.radians(CAM_SPIN_DEG)
    cam.location = (
        CAM_DISTANCE * math.sin(tilt) * math.sin(spin),
        -CAM_DISTANCE * math.sin(tilt) * math.cos(spin),
        CAM_DISTANCE * math.cos(tilt),
    )
    cam.rotation_euler = (tilt, 0.0, spin)
    bpy.context.scene.camera = cam


def setup_lights():
    """
    Свет слева сверху — то же правило, что в контракте передачи арта.

    После первого прогона схема переписана: ключевой свет сделан теплым и
    сильным, заполняющий приглушен, добавлен контровой. Прежняя схема с
    холодным небом в шесть десятых мощности съедала всю палитру — кремовый
    корпус приезжал серым, оранжевый кант пропадал.
    """
    key = bpy.data.lights.new("key", type="SUN")
    key.energy = 5.5
    key.color = (1.0, 0.94, 0.84)  # теплый, как солнце сквозь марсианскую пыль
    key.angle = math.radians(9)
    key_obj = bpy.data.objects.new("key", key)
    key_obj.rotation_euler = (math.radians(48), 0, math.radians(-40))
    bpy.context.collection.objects.link(key_obj)

    fill = bpy.data.lights.new("fill", type="AREA")
    fill.energy = 140.0
    fill.color = (0.78, 0.86, 1.0)
    fill.size = 14.0
    fill_obj = bpy.data.objects.new("fill", fill)
    fill_obj.location = (-7, 6, 4)
    fill_obj.rotation_euler = (math.radians(62), 0, math.radians(200))
    bpy.context.collection.objects.link(fill_obj)

    # Контровой отделяет силуэт от фона. В изометрии объекты стоят вплотную,
    # и без него соседние здания слипаются в одно пятно.
    rim = bpy.data.lights.new("rim", type="AREA")
    rim.energy = 260.0
    rim.color = (1.0, 0.82, 0.62)
    rim.size = 8.0
    rim_obj = bpy.data.objects.new("rim", rim)
    rim_obj.location = (6, -7, 5)
    rim_obj.rotation_euler = (math.radians(60), 0, math.radians(40))
    bpy.context.collection.objects.link(rim_obj)

    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = find_node(world.node_tree, "BACKGROUND")
    bg.inputs[0].default_value = (0.62, 0.60, 0.58, 1)
    bg.inputs[1].default_value = 0.35
    bpy.context.scene.world = world


def setup_outline():
    """
    Обводка средствами Freestyle. Дает ту же темную линию по силуэту и
    складкам, что у принятых эталонов, и не требует ни шейдера, ни второго
    прохода — рендерится вместе с картинкой.
    """
    scene = bpy.context.scene
    scene.render.use_freestyle = True
    scene.render.line_thickness_mode = "ABSOLUTE"
    scene.render.line_thickness = OUTLINE_THICKNESS

    view_layer = bpy.context.view_layer
    view_layer.use_freestyle = True
    settings = view_layer.freestyle_settings
    settings.as_render_pass = False

    lineset = settings.linesets.new("outline")
    lineset.select_silhouette = True
    lineset.select_border = True
    lineset.select_crease = True
    lineset.select_edge_mark = False
    lineset.linestyle.color = OUTLINE_COLOR
    lineset.linestyle.thickness = OUTLINE_THICKNESS
    lineset.linestyle.alpha = 0.9


def add_cylinder(radius, depth, location, verts=48):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=verts, radius=radius, depth=depth, location=location
    )
    return bpy.context.object


def add_dome(radius, height, location, segments=48):
    """
    Полусфера, а не шар.

    Первый прогон ставил целый эллипсоид: нижняя половина торчала из-под пола
    и в изометрии читалась как второй купол под землей. Срезаем все, что ниже
    экватора, средствами bmesh — модификатор Boolean тут дороже и капризнее.
    """
    import bmesh

    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=(0, 0, 0), segments=segments)
    obj = bpy.context.object

    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    doomed = [v for v in bm.verts if v.co.z < -1e-4]
    bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    bm.to_mesh(mesh)
    bm.free()

    obj.scale = (1, 1, height / radius)
    obj.location = location
    return obj


def add_box(size, location, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_cube_add(size=size, location=location)
    obj = bpy.context.object
    obj.scale = scale
    return obj


def build_food_module():
    """Пищевой модуль: платформа, корпус-цилиндр, купол, пристройка, трубы."""
    cream = make_material("cream", CREAM, roughness=0.5)
    dark = make_material("dark", DARK, metallic=0.6, roughness=0.4)
    orange = make_material("orange", ORANGE, roughness=0.45)
    teal = make_material("teal", TEAL, roughness=0.15, emission=2.2)
    sand = make_material("sand", SAND, roughness=0.85)

    base = add_box(1.0, (0, 0, 0.15), scale=(3.2, 3.2, 0.3))
    shade(base, sand, bevel=0.05)

    rim = add_box(1.0, (0, 0, 0.32), scale=(3.3, 3.3, 0.06))
    shade(rim, orange, bevel=0.02)

    body = add_cylinder(1.5, 1.6, (0, 0, 1.1))
    shade(body, cream, bevel=0.04)

    band = add_cylinder(1.53, 0.34, (0, 0, 1.45))
    shade(band, teal, bevel=0.01)

    dome = add_dome(1.5, 0.85, (0, 0, 1.88), segments=48)
    shade(dome, cream, bevel=0, smooth=True)

    collar = add_cylinder(1.56, 0.14, (0, 0, 1.9))
    shade(collar, dark, bevel=0.02)

    annex = add_box(1.0, (2.0, 0.4, 0.85), scale=(1.0, 1.4, 1.1))
    shade(annex, cream, bevel=0.05)

    annex_roof = add_box(1.0, (2.0, 0.4, 1.44), scale=(1.08, 1.48, 0.08))
    shade(annex_roof, dark, bevel=0.02)

    for i, y in enumerate((-0.3, 0.4, 1.1)):
        win = add_box(1.0, (2.55, y, 0.9), scale=(0.06, 0.34, 0.34))
        shade(win, teal, bevel=0.01)

    for x in (-1.9, -1.45):
        pipe = add_cylinder(0.16, 2.4, (x, -1.7, 1.2), verts=24)
        shade(pipe, dark, bevel=0.02)

    vent = add_cylinder(0.35, 0.5, (-0.6, -0.6, 2.85), verts=32)
    shade(vent, dark, bevel=0.03)


def build_dome_greenhouse():
    """
    Купол-гидропоника: стеклянный купол, под ним видны грядки.

    Переписано после первого прогона: непрозрачное стекло превращало купол в
    белое яйцо, и главное — грядки, ради которых игрок сюда тапает, — не было
    видно вовсе. Теперь купол приземистый и полупрозрачный, ребра идут поверх
    него темной сеткой, внутри лежит пол и три грядки с урожаем.
    """
    cream = make_material("cream", CREAM, roughness=0.5)
    dark = make_material("dark", DARK, metallic=0.5, roughness=0.4)
    orange = make_material("orange", ORANGE, roughness=0.45)
    sand = make_material("sand", SAND, roughness=0.85)
    green = make_material("green", GREEN, roughness=0.7)
    soil = make_material("soil", (0.28, 0.20, 0.15, 1.0), roughness=0.9)

    glass = bpy.data.materials.new("glass")
    glass.use_nodes = True
    gb = find_node(glass.node_tree, "BSDF_PRINCIPLED")
    gb.inputs["Base Color"].default_value = (0.60, 0.90, 0.95, 1.0)
    gb.inputs["Roughness"].default_value = 0.08
    gb.inputs["Alpha"].default_value = 0.30  # сквозь него обязаны читаться грядки
    glass.blend_method = "BLEND" if hasattr(glass, "blend_method") else glass.blend_method

    base = add_box(1.0, (0, 0, 0.15), scale=(3.4, 3.4, 0.3))
    shade(base, sand, bevel=0.05)

    rim = add_box(1.0, (0, 0, 0.34), scale=(3.5, 3.5, 0.07))
    shade(rim, orange, bevel=0.02)

    floor = add_cylinder(2.45, 0.16, (0, 0, 0.45))
    shade(floor, cream, bevel=0.02)

    ring = add_cylinder(2.5, 0.34, (0, 0, 0.62))
    shade(ring, dark, bevel=0.03)

    # Грядки: земля и урожай над ней. Две ступени, иначе в мелком размере
    # читается одна зеленая полоска без смысла.
    for x in (-1.1, 0.0, 1.1):
        bed = add_box(1.0, (x, 0, 0.62), scale=(0.44, 1.8, 0.14))
        shade(bed, soil, bevel=0.03)
        crop = add_box(1.0, (x, 0, 0.80), scale=(0.38, 1.7, 0.12))
        shade(crop, green, bevel=0.05)

    dome = add_dome(2.5, 1.7, (0, 0, 0.72), segments=56)
    shade(dome, glass, bevel=0, smooth=True)

    # Ребра — дуги ТОЛЬКО над полом. Кольца-торы в прошлом прогоне опоясывали
    # купол целиком, включая подземную половину, и здание читалось как мяч.
    for angle in (0, 45, 90, 135):
        rib = add_dome(2.53, 1.73, (0, 0, 0.72), segments=56)
        solid = rib.modifiers.new("solid", "SOLIDIFY")
        solid.thickness = 0.03
        mask = add_box(1.0, (0, 0, 1.6), scale=(5.4, 0.07, 2.2))
        mask.rotation_euler = (0, 0, math.radians(angle))
        boolean = rib.modifiers.new("cut", "BOOLEAN")
        boolean.operation = "INTERSECT"
        boolean.object = mask
        mask.hide_render = True
        shade(rib, dark, bevel=0, smooth=True)

    cap = add_cylinder(0.42, 0.22, (0, 0, 1.94), verts=32)
    shade(cap, dark, bevel=0.03)

    airlock = add_box(1.0, (2.55, 0, 0.78), scale=(0.72, 0.85, 0.6))
    shade(airlock, cream, bevel=0.06)

    door = add_box(1.0, (3.16, 0, 0.74), scale=(0.06, 0.48, 0.44))
    shade(door, dark, bevel=0.01)


def build_warehouse():
    """Склад: два бака-силоса, крытая площадка и ворота погрузки."""
    cream = make_material("cream", CREAM, roughness=0.5)
    dark = make_material("dark", DARK, metallic=0.5, roughness=0.4)
    orange = make_material("orange", ORANGE, roughness=0.45)
    sand = make_material("sand", SAND, roughness=0.85)
    teal = make_material("teal", TEAL, roughness=0.2, emission=1.8)

    base = add_box(1.0, (0, 0, 0.15), scale=(3.4, 3.0, 0.3))
    shade(base, sand, bevel=0.05)

    rim = add_box(1.0, (0, 0, 0.34), scale=(3.5, 3.1, 0.07))
    shade(rim, orange, bevel=0.02)

    hall = add_box(1.0, (-0.5, 0, 1.05), scale=(2.0, 2.4, 1.5))
    shade(hall, cream, bevel=0.08)

    roof = add_box(1.0, (-0.5, 0, 1.85), scale=(2.15, 2.55, 0.12))
    shade(roof, dark, bevel=0.03)

    stripe = add_box(1.0, (-0.5, 0, 1.62), scale=(2.03, 2.43, 0.1))
    shade(stripe, orange, bevel=0.02)

    # Ворота: главный опознавательный знак склада в мелком размере.
    gate = add_box(1.0, (0.52, 0, 0.85), scale=(0.08, 1.3, 1.05))
    shade(gate, dark, bevel=0.02)
    for y in (-0.55, 0.0, 0.55):
        slat = add_box(1.0, (0.57, y, 0.85), scale=(0.04, 0.34, 1.0))
        shade(slat, cream, bevel=0.01)

    for y in (-1.6, 1.6):
        silo = add_cylinder(0.62, 2.2, (-1.9, y, 1.4))
        shade(silo, cream, bevel=0.05)
        top = add_cylinder(0.66, 0.2, (-1.9, y, 2.55), verts=32)
        shade(top, dark, bevel=0.03)
        band = add_cylinder(0.64, 0.18, (-1.9, y, 1.9))
        shade(band, teal, bevel=0.01)

    for i, (x, y) in enumerate(((1.3, -0.9), (1.55, -0.35), (1.3, 0.85))):
        crate = add_box(1.0, (x, y, 0.62), scale=(0.34, 0.34, 0.34))
        shade(crate, dark if i % 2 else cream, bevel=0.04)


def build_construction_site():
    """Стройплощадка: фундамент, леса, ящики модулей, кран."""
    cream = make_material("cream", CREAM, roughness=0.5)
    dark = make_material("dark", DARK, metallic=0.6, roughness=0.4)
    orange = make_material("orange", ORANGE, roughness=0.45)
    sand = make_material("sand", SAND, roughness=0.85)
    teal = make_material("teal", TEAL, roughness=0.2, emission=1.6)

    base = add_box(1.0, (0, 0, 0.14), scale=(3.2, 3.2, 0.28))
    shade(base, sand, bevel=0.05)

    slab = add_box(1.0, (0, 0, 0.34), scale=(2.4, 2.4, 0.12))
    shade(slab, dark, bevel=0.03)

    # Леса: четыре стойки и обвязка. Читаются как стройка в любом размере.
    for x in (-1.9, 1.9):
        for y in (-1.9, 1.9):
            post = add_box(1.0, (x, y, 1.1), scale=(0.12, 0.12, 1.5))
            shade(post, orange, bevel=0.02)

    for z in (1.0, 2.0):
        for axis in range(2):
            for sign in (-1, 1):
                loc = (sign * 1.9, 0, z) if axis == 0 else (0, sign * 1.9, z)
                scale = (0.09, 1.9, 0.09) if axis == 0 else (1.9, 0.09, 0.09)
                beam = add_box(1.0, loc, scale=scale)
                shade(beam, orange, bevel=0.02)

    for i, (x, y, z) in enumerate(((-0.7, -0.7, 0.75), (0.1, -0.9, 0.75), (-0.4, 0.0, 1.25))):
        crate = add_box(1.0, (x, y, z), scale=(0.42, 0.42, 0.42))
        shade(crate, cream if i % 2 else dark, bevel=0.04)

    beacon = add_cylinder(0.12, 0.3, (1.6, -1.6, 2.5), verts=20)
    shade(beacon, teal, bevel=0.02)


BUILDERS = {
    "food_module": build_food_module,
    "dome_greenhouse": build_dome_greenhouse,
    "construction_site": build_construction_site,
    "warehouse": build_warehouse,
}


def render(out_path):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    # CPU намеренно: headless-рендер на GPU требует настроенного HIP и молча
    # падает обратно на процессор. Предсказуемость важнее пары минут.
    scene.cycles.device = "CPU"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.render.resolution_x = RESOLUTION
    scene.render.resolution_y = RESOLUTION
    scene.render.film_transparent = True  # прозрачный фон, хромакей не нужен
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = out_path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    name = argv[0] if argv else "food_module"
    out = argv[1] if len(argv) > 1 else f"design/blender/out/{name}.png"

    clear_scene()
    setup_camera()
    setup_lights()
    setup_outline()
    BUILDERS[name]()
    render(out)
    print(f"ГОТОВО: {out}")


main()
