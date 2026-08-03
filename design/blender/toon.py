"""
Тун-материалы и примитивы с характером.

Отделено от рига после провала первой версии. Тот прогон делал здания из
голых цилиндров с плоской заливкой Principled — получился программистский
арт: форма без характера, цвет без градиента, свет без стилизации.

Три вещи, которые отличают казуальный игровой ассет от макета, и все три
живут здесь:

1. **Свет полосами, а не градиентом.** Diffuse -> Shader to RGB -> ColorRamp
   с постоянной интерполяцией. Работает только в EEVEE, в Cycles узла
   Shader to RGB нет вовсе — поэтому весь риг переехал на EEVEE.
2. **Контровая подсветка по кромке.** Fresnel по краю формы отделяет силуэт
   от фона. Без нее объекты слипаются в пятно на общей карте.
3. **Вертикальный градиент по объекту.** Верх светлее низа. Это то, что
   читается как «нарисовано», а не «отрендерено».
"""

import bpy


def _node(nt, kind, x=0, y=0):
    n = nt.nodes.new(kind)
    n.location = (x, y)
    return n


def _find(nt, type_name):
    for n in nt.nodes:
        if n.type == type_name:
            return n
    return None


def _mul(color, k, tint=(1.0, 1.0, 1.0)):
    return (
        min(1.0, color[0] * k * tint[0]),
        min(1.0, color[1] * k * tint[1]),
        min(1.0, color[2] * k * tint[2]),
        1.0,
    )


# Тень уходит в холод, свет — в тепло. Это базовое правило цветовой работы:
# нейтральная тень читается как грязь, а не как тень.
SHADOW_TINT = (0.72, 0.80, 1.05)
LIGHT_TINT = (1.06, 1.01, 0.92)


def toon_material(
    name,
    color,
    bands=(0.44, 0.74, 1.02),
    rim_color=(1.0, 0.86, 0.62),
    rim_strength=0.55,
    gradient=0.16,
    emission=0.0,
):
    """
    Материал из трех полос света плюс кромка.

    `bands` — множители яркости базового цвета для тени, полутени и света.
    `gradient` — насколько верх объекта светлее низа; ноль отключает.
    """
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    out = _find(nt, "OUTPUT_MATERIAL")

    shadow = _mul(color, bands[0], SHADOW_TINT)
    mid = _mul(color, bands[1])
    light = _mul(color, bands[2], LIGHT_TINT)

    diffuse = _node(nt, "ShaderNodeBsdfDiffuse", -1000, 200)
    diffuse.inputs["Color"].default_value = (1, 1, 1, 1)

    to_rgb = _node(nt, "ShaderNodeShaderToRGB", -820, 200)
    nt.links.new(diffuse.outputs[0], to_rgb.inputs[0])

    ramp = _node(nt, "ShaderNodeValToRGB", -640, 200)
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = shadow
    ramp.color_ramp.elements[1].position = 0.34
    ramp.color_ramp.elements[1].color = mid
    third = ramp.color_ramp.elements.new(0.68)
    third.color = light
    nt.links.new(to_rgb.outputs[0], ramp.inputs[0])

    shaded = ramp.outputs[0]

    # Вертикальный градиент по локальной оси Z объекта: верх ловит больше
    # неба, низ уходит в тень. Дешевле любой текстуры и держит стиль.
    if gradient > 0:
        tex_co = _node(nt, "ShaderNodeTexCoord", -1200, -200)
        sep = _node(nt, "ShaderNodeSeparateXYZ", -1020, -200)
        nt.links.new(tex_co.outputs["Object"], sep.inputs[0])
        rng = _node(nt, "ShaderNodeMapRange", -840, -200)
        rng.inputs[1].default_value = -1.2
        rng.inputs[2].default_value = 2.2
        rng.inputs[3].default_value = 1.0 - gradient
        rng.inputs[4].default_value = 1.0 + gradient
        nt.links.new(sep.outputs["Z"], rng.inputs[0])

        grad_mix = _node(nt, "ShaderNodeMix", -420, 120)
        grad_mix.data_type = "RGBA"
        grad_mix.blend_type = "MULTIPLY"
        grad_mix.inputs["Factor"].default_value = 1.0
        nt.links.new(shaded, grad_mix.inputs[6])
        fac_to_col = _node(nt, "ShaderNodeCombineColor", -620, -60)
        nt.links.new(rng.outputs[0], fac_to_col.inputs[0])
        nt.links.new(rng.outputs[0], fac_to_col.inputs[1])
        nt.links.new(rng.outputs[0], fac_to_col.inputs[2])
        nt.links.new(fac_to_col.outputs[0], grad_mix.inputs[7])
        shaded = grad_mix.outputs[2]

    # Кромка. Только у самого края, узкой полосой.
    #
    # Первая сборка графа выбила всю палитру в белый: умножение с фактором
    # меньше единицы дает `A*(1-f) + A*B*f`, то есть при нулевой рампе на
    # поверхность все равно ложится постоянная добавка. Кремовый, оранжевый
    # и бирюзовый приезжали одинаково белыми. Теперь умножение идет с
    # фактором ровно единица, а силу задает фактор сложения.
    if rim_strength > 0:
        lw = _node(nt, "ShaderNodeLayerWeight", -1000, -520)
        lw.inputs["Blend"].default_value = 0.55
        rim_ramp = _node(nt, "ShaderNodeValToRGB", -820, -520)
        rim_ramp.color_ramp.interpolation = "LINEAR"
        # Полоса начинается на 0.78: ниже этого Френель покрывает половину
        # выпуклой формы, и «кромка» превращается в общую засветку.
        rim_ramp.color_ramp.elements[0].position = 0.78
        rim_ramp.color_ramp.elements[0].color = (0, 0, 0, 1)
        rim_ramp.color_ramp.elements[1].position = 0.99
        rim_ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
        nt.links.new(lw.outputs["Fresnel"], rim_ramp.inputs[0])

        rim_col = _node(nt, "ShaderNodeMix", -620, -520)
        rim_col.data_type = "RGBA"
        rim_col.blend_type = "MULTIPLY"
        rim_col.inputs["Factor"].default_value = 1.0
        rim_col.inputs[6].default_value = (*rim_color, 1.0)
        nt.links.new(rim_ramp.outputs[0], rim_col.inputs[7])

        add = _node(nt, "ShaderNodeMix", -240, 0)
        add.data_type = "RGBA"
        add.blend_type = "ADD"
        add.inputs["Factor"].default_value = rim_strength
        nt.links.new(shaded, add.inputs[6])
        nt.links.new(rim_col.outputs[2], add.inputs[7])
        shaded = add.outputs[2]

    emit = _node(nt, "ShaderNodeEmission", -60, 0)
    emit.inputs["Strength"].default_value = 1.0 + emission
    nt.links.new(shaded, emit.inputs["Color"])
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    return mat


def toon_over_texture(name, image, rim_strength=0.5, gradient=0.10, tint=None):
    """
    Тоновые полосы ПОВЕРХ готовой текстуры.

    Нужен для чужих наборов: у KayKit и Kenney весь цвет живет в текстурном
    атласе, а базовый цвет материала белый. Первый заход подменял материал
    плоской заливкой по оттенку — и весь набор приехал одинаково стальным,
    вся работа художника пропала.

    Здесь рампа отдает не цвет, а множитель яркости, и умножается на текстуру.
    Тень дополнительно уводится в холод отдельным смешиванием — иначе полосы
    читаются как грязь, а не как свет.
    """
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    out = _find(nt, "OUTPUT_MATERIAL")

    tex = _node(nt, "ShaderNodeTexImage", -1400, 400)
    tex.image = image
    tex.interpolation = "Closest"  # атлас: сглаживание тянет соседние клетки

    base = tex.outputs["Color"]
    if tint is not None:
        tint_mix = _node(nt, "ShaderNodeMix", -1200, 400)
        tint_mix.data_type = "RGBA"
        tint_mix.blend_type = "COLOR"
        tint_mix.inputs["Factor"].default_value = 0.55
        nt.links.new(base, tint_mix.inputs[6])
        tint_mix.inputs[7].default_value = (*tint, 1.0)
        base = tint_mix.outputs[2]

    diffuse = _node(nt, "ShaderNodeBsdfDiffuse", -1200, 0)
    diffuse.inputs["Color"].default_value = (1, 1, 1, 1)
    to_rgb = _node(nt, "ShaderNodeShaderToRGB", -1020, 0)
    nt.links.new(diffuse.outputs[0], to_rgb.inputs[0])

    ramp = _node(nt, "ShaderNodeValToRGB", -840, 0)
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0.62, 0.68, 0.82, 1)  # тень с холодом
    ramp.color_ramp.elements[1].position = 0.34
    ramp.color_ramp.elements[1].color = (0.88, 0.89, 0.92, 1)
    ramp.color_ramp.elements.new(0.68).color = (1.06, 1.03, 0.96, 1)  # свет с теплом
    nt.links.new(to_rgb.outputs[0], ramp.inputs[0])

    shade_mix = _node(nt, "ShaderNodeMix", -600, 200)
    shade_mix.data_type = "RGBA"
    shade_mix.blend_type = "MULTIPLY"
    shade_mix.inputs["Factor"].default_value = 1.0
    nt.links.new(base, shade_mix.inputs[6])
    nt.links.new(ramp.outputs[0], shade_mix.inputs[7])
    shaded = shade_mix.outputs[2]

    if gradient > 0:
        tex_co = _node(nt, "ShaderNodeTexCoord", -1400, -300)
        sep = _node(nt, "ShaderNodeSeparateXYZ", -1220, -300)
        nt.links.new(tex_co.outputs["Object"], sep.inputs[0])
        rng = _node(nt, "ShaderNodeMapRange", -1040, -300)
        rng.inputs[1].default_value = -1.0
        rng.inputs[2].default_value = 2.0
        rng.inputs[3].default_value = 1.0 - gradient
        rng.inputs[4].default_value = 1.0 + gradient
        nt.links.new(sep.outputs["Z"], rng.inputs[0])
        comb = _node(nt, "ShaderNodeCombineColor", -860, -300)
        for i in range(3):
            nt.links.new(rng.outputs[0], comb.inputs[i])
        gmix = _node(nt, "ShaderNodeMix", -420, 160)
        gmix.data_type = "RGBA"
        gmix.blend_type = "MULTIPLY"
        gmix.inputs["Factor"].default_value = 1.0
        nt.links.new(shaded, gmix.inputs[6])
        nt.links.new(comb.outputs[0], gmix.inputs[7])
        shaded = gmix.outputs[2]

    if rim_strength > 0:
        lw = _node(nt, "ShaderNodeLayerWeight", -1000, -560)
        lw.inputs["Blend"].default_value = 0.55
        rr = _node(nt, "ShaderNodeValToRGB", -820, -560)
        rr.color_ramp.elements[0].position = 0.80
        rr.color_ramp.elements[0].color = (0, 0, 0, 1)
        rr.color_ramp.elements[1].position = 0.99
        rr.color_ramp.elements[1].color = (1, 1, 1, 1)
        nt.links.new(lw.outputs["Fresnel"], rr.inputs[0])
        rc = _node(nt, "ShaderNodeMix", -620, -560)
        rc.data_type = "RGBA"
        rc.blend_type = "MULTIPLY"
        rc.inputs["Factor"].default_value = 1.0
        rc.inputs[6].default_value = (1.0, 0.86, 0.62, 1.0)
        nt.links.new(rr.outputs[0], rc.inputs[7])
        add = _node(nt, "ShaderNodeMix", -240, 0)
        add.data_type = "RGBA"
        add.blend_type = "ADD"
        add.inputs["Factor"].default_value = rim_strength
        nt.links.new(shaded, add.inputs[6])
        nt.links.new(rc.outputs[2], add.inputs[7])
        shaded = add.outputs[2]

    emit = _node(nt, "ShaderNodeEmission", -60, 0)
    nt.links.new(shaded, emit.inputs["Color"])
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    return mat


def glass_material(name, color=(0.42, 0.86, 0.92), alpha=0.26):
    """Стекло: почти прозрачное, с холодной кромкой и легким свечением."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    out = _find(nt, "OUTPUT_MATERIAL")

    transp = _node(nt, "ShaderNodeBsdfTransparent", -600, -160)
    emit = _node(nt, "ShaderNodeEmission", -600, 60)
    emit.inputs["Color"].default_value = (*color, 1.0)
    emit.inputs["Strength"].default_value = 0.85

    lw = _node(nt, "ShaderNodeLayerWeight", -900, 220)
    lw.inputs["Blend"].default_value = 0.55
    ramp = _node(nt, "ShaderNodeValToRGB", -760, 220)
    ramp.color_ramp.elements[0].position = 0.10
    ramp.color_ramp.elements[0].color = (alpha, alpha, alpha, 1)
    ramp.color_ramp.elements[1].position = 0.85
    ramp.color_ramp.elements[1].color = (0.92, 0.92, 0.92, 1)
    nt.links.new(lw.outputs["Fresnel"], ramp.inputs[0])

    mix = _node(nt, "ShaderNodeMixShader", -320, 0)
    nt.links.new(ramp.outputs[0], mix.inputs[0])
    nt.links.new(transp.outputs[0], mix.inputs[1])
    nt.links.new(emit.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])

    if hasattr(mat, "blend_method"):
        mat.blend_method = "BLEND"
    if hasattr(mat, "show_transparent_back"):
        mat.show_transparent_back = False
    return mat


# --- Примитивы с характером --------------------------------------------------


def apply(obj, mat, bevel=0.045, segments=4, smooth=False, shade_auto=True):
    """
    Фаска щедрая и в несколько сегментов.

    Острое ребро в казуальном арте не встречается вообще: свет по нему не
    скользит, и объект читается как коробка из тестовой сцены. Скругление —
    самый дешевый способ отличить ассет от заглушки.
    """
    if obj.data.materials:
        obj.data.materials.clear()
    obj.data.materials.append(mat)
    if bevel > 0:
        mod = obj.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = "ANGLE"
        mod.angle_limit = 1.05
        mod.harden_normals = False
    if smooth:
        bpy.ops.object.shade_smooth()
    elif shade_auto and hasattr(bpy.ops.object, "shade_auto_smooth"):
        try:
            bpy.ops.object.shade_auto_smooth(angle=0.9)
        except Exception:
            pass
    return obj


def box(loc, scale, rot_z=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.object
    o.scale = scale
    o.rotation_euler = (0, 0, rot_z)
    return o


def cyl(radius, depth, loc, verts=40, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc)
    o = bpy.context.object
    o.rotation_euler = rot
    return o


def cone(r1, r2, depth, loc, verts=32):
    bpy.ops.mesh.primitive_cone_add(
        vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc
    )
    return bpy.context.object


def dome(radius, height, loc, segments=44):
    """Полусфера: шар, срезанный по экватору."""
    import bmesh

    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=(0, 0, 0), segments=segments)
    o = bpy.context.object
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-4], context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    o.scale = (1, 1, height / radius)
    o.location = loc
    return o


def barrel(radius, length, loc, rot_y=0.0):
    """Полуцилиндр — бочкообразная крыша. Ангар без нее читается как ящик."""
    import bmesh

    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=radius, depth=length, location=(0, 0, 0))
    o = bpy.context.object
    o.rotation_euler = (0, 1.5707963, 0)
    bpy.ops.object.transform_apply(rotation=True)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-4], context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    o.location = loc
    o.rotation_euler = (0, rot_y, 0)
    return o
