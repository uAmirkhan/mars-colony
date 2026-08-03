"""
Изометрический риг для запекания спрайтов зданий колонии.

Зачем: генерации в Gemini дают каждый раз свой угол, свой свет и свой масштаб,
поэтому здания не встают в одну сцену. Здесь угол и свет заданы один раз, и все
здания выходят согласованными по построению, а не по везению. Так работает
настоящий цех — мобильные ситибилдеры отгружают 2.5D-спрайты, запеченные из 3D.

Версия вторая. Первая делала здания из голых цилиндров с плоской заливкой и
рендерила в Cycles — вышел программистский арт. Что изменено по итогам разбора
казуального игрового арта:

- **Движок EEVEE вместо Cycles.** Не ради скорости: тун-шейдинг делается узлом
  Shader to RGB, а в Cycles его не существует вовсе.
- **Свет полосами** вместо гладкого градиента, тень уходит в холод, свет в тепло.
- **Силуэт вместо стопки примитивов.** У ангара бочкообразная крыша со свесом,
  у стройки — стрела крана, у силоса — конус и лестница. Здание обязано
  опознаваться по одному черному силуэту, это первое правило жанра.
- **Три уровня детали:** крупная форма, средние объемы, мелкие пропсы. Без
  третьего уровня объект читается как макет.
- **Щедрые фаски.** Острых ребер в казуальном арте не бывает.

Запуск:
    blender --background --python design/blender/iso_rig.py -- <здание> <выход.png>
"""

import math
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from toon import apply, barrel, box, cone, cyl, dome, glass_material, toon_material  # noqa: E402

# --- Риг ---------------------------------------------------------------------

CAM_TILT_DEG = 60.0
CAM_SPIN_DEG = 45.0
CAM_DISTANCE = 14.0
ORTHO_SCALE = 9.0

RESOLUTION = 1024
SAMPLES = 64

OUTLINE_COLOR = (0.09, 0.06, 0.05)
OUTLINE_THICKNESS = 3.0

# Палитра колонии. Значения с запасом по насыщенности: в рендере материал
# всегда выходит бледнее собственного цвета.
CREAM = (0.96, 0.87, 0.70)
BONE = (0.88, 0.83, 0.74)
DARK = (0.20, 0.22, 0.29)
STEEL = (0.42, 0.47, 0.56)
ORANGE = (0.97, 0.42, 0.10)
RUST = (0.72, 0.28, 0.12)
TEAL = (0.15, 0.80, 0.84)
SAND = (0.76, 0.60, 0.44)
GREEN = (0.34, 0.72, 0.24)
SOIL = (0.36, 0.24, 0.17)
YELLOW = (0.99, 0.78, 0.20)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.worlds):
        for item in list(block):
            block.remove(item)


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
    Свет слева сверху, как записано в контракте передачи арта.

    Материалы тут тоновые и сами задают полосы, поэтому лампы нужны не для
    яркости, а чтобы определить, где проходит граница света и тени. Отсюда
    один жесткий ключевой и мягкий заполняющий — второй только чтобы теневая
    сторона не сваливалась в самую темную полосу целиком.
    """
    key = bpy.data.lights.new("key", type="SUN")
    key.energy = 3.2
    key.color = (1.0, 0.95, 0.86)
    key_obj = bpy.data.objects.new("key", key)
    key_obj.rotation_euler = (math.radians(46), 0, math.radians(-42))
    bpy.context.collection.objects.link(key_obj)

    fill = bpy.data.lights.new("fill", type="SUN")
    fill.energy = 1.1
    fill.color = (0.74, 0.83, 1.0)
    fill_obj = bpy.data.objects.new("fill", fill)
    fill_obj.rotation_euler = (math.radians(62), 0, math.radians(150))
    bpy.context.collection.objects.link(fill_obj)

    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = None
    for n in world.node_tree.nodes:
        if n.type == "BACKGROUND":
            bg = n
    if bg is None:
        bg = world.node_tree.nodes.new("ShaderNodeBackground")
        for n in world.node_tree.nodes:
            if n.type == "OUTPUT_WORLD":
                world.node_tree.links.new(bg.outputs[0], n.inputs[0])
    bg.inputs[0].default_value = (0.55, 0.58, 0.66, 1)
    bg.inputs[1].default_value = 0.30
    bpy.context.scene.world = world


def setup_outline():
    """Обводка Freestyle: у принятых эталонов она есть, без нее теряется силуэт."""
    scene = bpy.context.scene
    scene.render.use_freestyle = True
    scene.render.line_thickness_mode = "ABSOLUTE"
    scene.render.line_thickness = OUTLINE_THICKNESS

    vl = bpy.context.view_layer
    vl.use_freestyle = True
    settings = vl.freestyle_settings
    settings.as_render_pass = False
    lineset = settings.linesets.new("outline")
    lineset.select_silhouette = True
    lineset.select_border = True
    lineset.select_crease = True
    lineset.linestyle.color = OUTLINE_COLOR
    lineset.linestyle.thickness = OUTLINE_THICKNESS
    lineset.linestyle.alpha = 0.95


def palette():
    """Материалы одного набора. Общие на все здания — иначе стиль разъедется."""
    return {
        "cream": toon_material("cream", CREAM),
        "bone": toon_material("bone", BONE, gradient=0.12),
        "dark": toon_material("dark", DARK, bands=(0.55, 0.80, 1.10), rim_strength=0.55),
        "steel": toon_material("steel", STEEL, bands=(0.50, 0.78, 1.08), rim_strength=0.5),
        "orange": toon_material("orange", ORANGE, rim_strength=0.35),
        "rust": toon_material("rust", RUST, rim_strength=0.35),
        "teal": toon_material("teal", TEAL, rim_strength=0.4, emission=0.7),
        "sand": toon_material("sand", SAND, gradient=0.0, rim_strength=0.2),
        "green": toon_material("green", GREEN, gradient=0.22),
        "soil": toon_material("soil", SOIL, gradient=0.0, rim_strength=0.15),
        "yellow": toon_material("yellow", YELLOW, rim_strength=0.3, emission=0.5),
        "glass": glass_material("glass"),
    }


# --- Общие узлы --------------------------------------------------------------


def platform(m, half=3.1, depth=2.7, tone="sand"):
    """Основание с крашеным кантом. Даёт зданию опору и место под пропсы."""
    slab = box((0, 0, 0.14), (half * 2, depth * 2, 0.28))
    apply(slab, m[tone], bevel=0.09)
    trim = box((0, 0, 0.30), (half * 2 + 0.12, depth * 2 + 0.12, 0.07))
    apply(trim, m["orange"], bevel=0.03)
    return slab


def crate(m, loc, size=0.34, tone="cream", rot=0.0):
    c = box(loc, (size, size, size), rot_z=rot)
    apply(c, m[tone], bevel=0.05)
    for axis in (0, 1):
        for sign in (-1, 1):
            off = [0, 0, 0]
            off[axis] = sign * size * 0.5
            band = box(
                (loc[0] + off[0], loc[1] + off[1], loc[2]),
                (size * 0.08 if axis == 0 else size * 0.9,
                 size * 0.9 if axis == 0 else size * 0.08,
                 size * 0.9),
                rot_z=rot,
            )
            apply(band, m["dark"], bevel=0.01)
    return c


def ladder(m, loc, height, rot_z=0.0):
    for side in (-0.11, 0.11):
        rail = cyl(0.028, height, (loc[0], loc[1] + side, loc[2]), verts=10)
        apply(rail, m["steel"], bevel=0)
    steps = max(3, int(height / 0.26))
    for i in range(steps):
        z = loc[2] - height / 2 + 0.16 + i * (height - 0.3) / max(1, steps - 1)
        rung = cyl(0.02, 0.22, (loc[0], loc[1], z), verts=8, rot=(math.radians(90), 0, rot_z))
        apply(rung, m["steel"], bevel=0)


def antenna(m, loc, height=0.9):
    mast = cyl(0.03, height, (loc[0], loc[1], loc[2] + height / 2), verts=10)
    apply(mast, m["steel"], bevel=0)
    tip = cyl(0.07, 0.07, (loc[0], loc[1], loc[2] + height), verts=12)
    apply(tip, m["yellow"], bevel=0.02)


def pipe_elbow(m, start, height, run, radius=0.09):
    """Труба с коленом. Прямая труба читается как палка, колено — как труба."""
    v = cyl(radius, height, (start[0], start[1], start[2] + height / 2), verts=14)
    apply(v, m["steel"], bevel=0)
    h = cyl(
        radius,
        run,
        (start[0] + run / 2, start[1], start[2] + height),
        verts=14,
        rot=(0, math.radians(90), 0),
    )
    apply(h, m["steel"], bevel=0)
    knee = cyl(radius * 1.25, radius * 1.6, (start[0], start[1], start[2] + height), verts=14)
    apply(knee, m["dark"], bevel=0.02)


# --- Здания ------------------------------------------------------------------


def build_warehouse(m):
    """
    Склад: ангар с бочкообразной крышей, силос, ворота.

    Силуэт строится на контрасте вертикали силоса и горизонтали ангара —
    без этого склад не отличить от любой другой коробки на карте.
    """
    platform(m, half=3.2, depth=2.6)

    hall = box((-0.35, 0, 1.05), (3.4, 3.6, 1.5))
    apply(hall, m["cream"], bevel=0.1)

    roof = barrel(1.74, 3.7, (-0.35, 0, 1.72))
    roof.rotation_euler = (0, 0, math.radians(90))
    apply(roof, m["rust"], bevel=0.04, smooth=True)

    # Свес крыши за стену — главный признак «построено», а не «выдавлено».
    eave = box((-0.35, 0, 1.73), (3.62, 3.86, 0.1))
    apply(eave, m["dark"], bevel=0.03)

    stripe = box((-0.35, 1.82, 1.05), (3.44, 0.06, 0.22))
    apply(stripe, m["orange"], bevel=0.02)
    stripe2 = box((-0.35, -1.82, 1.05), (3.44, 0.06, 0.22))
    apply(stripe2, m["orange"], bevel=0.02)

    # Ворота: арка, а не прямоугольник. Арка сразу говорит «сюда въезжают».
    gate_frame = box((1.38, 0, 0.95), (0.14, 2.1, 1.3))
    apply(gate_frame, m["dark"], bevel=0.04)
    gate = box((1.44, 0, 0.9), (0.08, 1.8, 1.1))
    apply(gate, m["steel"], bevel=0.03)
    gate_arch = cyl(0.9, 0.1, (1.44, 0, 1.5), verts=32, rot=(0, math.radians(90), 0))
    apply(gate_arch, m["dark"], bevel=0.02)
    for y in (-0.6, -0.2, 0.2, 0.6):
        slat = box((1.49, y, 0.9), (0.04, 0.26, 1.02))
        apply(slat, m["bone"], bevel=0.015)

    lamp = box((1.44, 0, 1.72), (0.24, 0.5, 0.14))
    apply(lamp, m["yellow"], bevel=0.04)

    # Силос: цилиндр, конус, пояс, лестница. Четыре элемента, и он опознается.
    silo = cyl(0.72, 2.5, (-2.55, -1.62, 1.53))
    apply(silo, m["bone"], bevel=0.06)
    silo_band = cyl(0.75, 0.2, (-2.55, -1.62, 2.15))
    apply(silo_band, m["teal"], bevel=0.02)
    silo_band2 = cyl(0.75, 0.16, (-2.55, -1.62, 1.0))
    apply(silo_band2, m["orange"], bevel=0.02)
    silo_top = cone(0.86, 0.16, 0.6, (-2.55, -1.62, 3.05))
    apply(silo_top, m["dark"], bevel=0.03, smooth=True)
    ladder(m, (-1.9, -1.62, 1.6), 2.4)

    pipe_elbow(m, (-2.55, -0.7, 2.55), 0.45, 1.2)

    crate(m, (2.3, 1.35, 0.62), 0.42, "cream", rot=0.3)
    crate(m, (2.42, 0.62, 0.62), 0.42, "steel", rot=-0.2)
    crate(m, (2.3, 1.35, 1.24), 0.36, "orange", rot=0.1)

    barrel_prop = cyl(0.26, 0.6, (2.6, -1.4, 0.72), verts=20)
    apply(barrel_prop, m["orange"], bevel=0.05)

    antenna(m, (-2.0, 1.85, 1.85), 0.8)


def build_dome_greenhouse(m):
    """Купол-гидропоника: стекло, ребра, грядки внутри, шлюз-труба."""
    platform(m, half=3.3, depth=3.3)

    ring = cyl(2.62, 0.5, (0, 0, 0.52))
    apply(ring, m["bone"], bevel=0.07)
    ring_top = cyl(2.66, 0.14, (0, 0, 0.76))
    apply(ring_top, m["dark"], bevel=0.03)

    # Сегменты пояса: без них кольцо читается как гладкая шайба.
    for i in range(10):
        a = i * math.pi / 5
        seg = box((math.cos(a) * 2.62, math.sin(a) * 2.62, 0.52), (0.1, 0.1, 0.44), rot_z=a)
        apply(seg, m["steel"], bevel=0.02)

    floor = cyl(2.5, 0.12, (0, 0, 0.72))
    apply(floor, m["bone"], bevel=0.02)

    for i, x in enumerate((-1.15, 0.0, 1.15)):
        bed = box((x, 0, 0.84), (0.78, 3.3, 0.16))
        apply(bed, m["soil"], bevel=0.04)
        for j in range(5):
            y = -1.3 + j * 0.65
            bush = dome(0.26, 0.2, (x, y, 0.9), segments=18)
            apply(bush, m["green"], bevel=0, smooth=True)

    glass_dome = dome(2.52, 1.85, (0, 0, 0.8), segments=56)
    apply(glass_dome, m["glass"], bevel=0, smooth=True)

    # Меридианы: тонкие дуги поверх стекла, собранные из полусфер с вырезом.
    for angle in (0, 45, 90, 135):
        rib = dome(2.56, 1.88, (0, 0, 0.8), segments=56)
        solid = rib.modifiers.new("solid", "SOLIDIFY")
        solid.thickness = 0.05
        mask = box((0, 0, 1.7), (5.6, 0.11, 2.4), rot_z=math.radians(angle))
        cut = rib.modifiers.new("cut", "BOOLEAN")
        cut.operation = "INTERSECT"
        cut.object = mask
        mask.hide_render = True
        apply(rib, m["steel"], bevel=0, smooth=True)

    equator = cyl(2.58, 0.09, (0, 0, 1.45), verts=48)
    apply(equator, m["steel"], bevel=0.02)

    cap = cone(0.5, 0.34, 0.28, (0, 0, 2.72))
    apply(cap, m["dark"], bevel=0.03)
    antenna(m, (0, 0, 2.84), 0.5)

    # Шлюз: лежачая труба со скруглением и дверью. Вход обязан быть виден.
    tube = cyl(0.62, 1.5, (3.05, 0, 0.92), verts=28, rot=(0, math.radians(90), 0))
    apply(tube, m["cream"], bevel=0.05, smooth=True)
    tube_ring = cyl(0.68, 0.14, (3.6, 0, 0.92), verts=28, rot=(0, math.radians(90), 0))
    apply(tube_ring, m["orange"], bevel=0.02)
    door = cyl(0.44, 0.1, (3.72, 0, 0.92), verts=24, rot=(0, math.radians(90), 0))
    apply(door, m["teal"], bevel=0.02)


def build_food_module(m):
    """Пищевой модуль: корпус со скошенной крышей, бак, труба, навес."""
    platform(m, half=3.0, depth=2.6)

    body = box((-0.4, 0, 1.1), (3.0, 3.2, 1.6))
    apply(body, m["cream"], bevel=0.12)

    roof = barrel(1.55, 3.3, (-0.4, 0, 1.82))
    roof.rotation_euler = (0, 0, math.radians(90))
    apply(roof, m["teal"], bevel=0.04, smooth=True)

    eave = box((-0.4, 0, 1.83), (3.1, 3.42, 0.09))
    apply(eave, m["steel"], bevel=0.03)

    window_band = box((1.08, 0, 1.28), (0.08, 2.5, 0.5))
    apply(window_band, m["teal"], bevel=0.03)
    for y in (-0.85, 0.0, 0.85):
        mullion = box((1.12, y, 1.28), (0.05, 0.07, 0.54))
        apply(mullion, m["dark"], bevel=0.01)

    # Навес над окном: маленькая деталь, которая читается как «жилое».
    awning = box((1.38, 0, 1.62), (0.66, 2.7, 0.08))
    awning.rotation_euler = (0, math.radians(16), 0)
    apply(awning, m["orange"], bevel=0.03)

    tank = cyl(0.78, 1.9, (2.05, -0.55, 1.25))
    apply(tank, m["bone"], bevel=0.07)
    tank_top = dome(0.79, 0.36, (2.05, -0.55, 2.2), segments=28)
    apply(tank_top, m["steel"], bevel=0, smooth=True)
    tank_band = cyl(0.81, 0.18, (2.05, -0.55, 1.62))
    apply(tank_band, m["orange"], bevel=0.02)
    ladder(m, (1.32, -0.55, 1.3), 1.9)

    pipe_elbow(m, (2.05, 0.35, 1.9), 0.42, -1.1)

    chimney = cyl(0.3, 1.1, (-1.7, -0.9, 2.35))
    apply(chimney, m["steel"], bevel=0.05)
    cap = cone(0.46, 0.4, 0.22, (-1.7, -0.9, 2.98))
    apply(cap, m["dark"], bevel=0.03)

    vent = cyl(0.34, 0.3, (-1.7, 1.0, 2.05), verts=24)
    apply(vent, m["dark"], bevel=0.04)

    crate(m, (-1.0, 2.1, 0.64), 0.42, "steel", rot=0.25)
    crate(m, (-1.85, 2.05, 0.64), 0.42, "orange", rot=-0.15)

    lamp = box((1.14, 1.35, 1.72), (0.16, 0.22, 0.13))
    apply(lamp, m["yellow"], bevel=0.03)


def build_construction_site(m):
    """
    Стройплощадка: фундамент, леса с раскосами, кран, ящики модулей.

    Стрела крана — единственный элемент, по которому стройка опознается
    в мелком размере. Без нее это просто ящики на плите.
    """
    platform(m, half=3.1, depth=2.9)

    slab = box((0, 0, 0.38), (4.4, 4.0, 0.2))
    apply(slab, m["steel"], bevel=0.05)

    for sx in (-1, 1):
        for sy in (-1, 1):
            post = box((sx * 1.85, sy * 1.7, 1.35), (0.16, 0.16, 2.0))
            apply(post, m["orange"], bevel=0.03)

    for z in (0.95, 1.85, 2.3):
        for sy in (-1, 1):
            beam = box((0, sy * 1.7, z), (3.86, 0.11, 0.11))
            apply(beam, m["orange"], bevel=0.02)
        for sx in (-1, 1):
            beam = box((sx * 1.85, 0, z), (0.11, 3.5, 0.11))
            apply(beam, m["orange"], bevel=0.02)

    # Раскосы: без диагоналей леса читаются как решетка, а не как каркас.
    for sy in (-1, 1):
        brace = box((0, sy * 1.7, 1.4), (0.09, 0.09, 3.7))
        brace.rotation_euler = (0, math.radians(62), 0)
        apply(brace, m["rust"], bevel=0.02)

    mast = box((-2.55, 2.0, 1.55), (0.22, 0.22, 2.4))
    apply(mast, m["yellow"], bevel=0.03)
    jib = box((-1.25, 2.0, 2.68), (2.9, 0.15, 0.15))
    apply(jib, m["yellow"], bevel=0.02)
    counter = box((-3.1, 2.0, 2.68), (0.55, 0.32, 0.32))
    apply(counter, m["dark"], bevel=0.04)
    cable = cyl(0.022, 0.95, (-0.1, 2.0, 2.16), verts=8)
    apply(cable, m["dark"], bevel=0)
    hook = box((-0.1, 2.0, 1.62), (0.3, 0.3, 0.22))
    apply(hook, m["steel"], bevel=0.04)

    crate(m, (0.55, -2.35, 0.66), 0.5, "cream", rot=0.2)
    crate(m, (1.5, -2.05, 0.66), 0.5, "teal", rot=-0.3)
    crate(m, (-0.4, -2.5, 0.66), 0.5, "orange", rot=0.05)
    crate(m, (0.55, -2.35, 1.3), 0.42, "steel", rot=-0.1)

    for x, y in ((2.55, -0.6), (-2.4, -1.1)):
        marker = cone(0.22, 0.05, 0.42, (x, y, 0.52))
        apply(marker, m["orange"], bevel=0.02)


BUILDERS = {
    "warehouse": build_warehouse,
    "dome_greenhouse": build_dome_greenhouse,
    "food_module": build_food_module,
    "construction_site": build_construction_site,
}


def render(out_path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"

    ee = scene.eevee
    for attr, value in (
        ("taa_render_samples", SAMPLES),
        ("use_gtao", True),
        ("gtao_distance", 0.6),
        ("gtao_factor", 1.0),
        ("use_soft_shadows", True),
        ("use_shadow_high_bitdepth", True),
    ):
        if hasattr(ee, attr):
            setattr(ee, attr, value)

    scene.render.resolution_x = RESOLUTION
    scene.render.resolution_y = RESOLUTION
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = out_path

    # Контраст и легкая подкрутка насыщенности: тоновые полосы без этого
    # выглядят стерильно, а с этим — как крашеный пластик игрушки.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.sequencer_colorspace_settings.name = "sRGB"

    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    name = argv[0] if argv else "warehouse"
    out = argv[1] if len(argv) > 1 else f"out/{name}.png"

    clear_scene()
    setup_camera()
    setup_lights()
    setup_outline()
    BUILDERS[name](palette())
    render(out)
    print(f"ГОТОВО: {out}")


main()
