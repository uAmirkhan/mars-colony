"""Опыт: держит ли форму децимация моделей v3.

Раздел 7 передачи опыта закрывает децимацию СКАНОПОДОБНЫХ сеток: барак
60000 -> 11316 дал решето. Но там же оговорено, что v3 сделана ремешем
(поверхность перестроена заново), а не схлопыванием ребер, и через ту мясорубку
не проходила. Это разные случаи, и вопрос открыт.

Запуск через Blender без окна:
    blender.exe -b --factory-startup --python decimate.py -- ИМЯ ВХОД ВЫХОД
"""

import os
import sys

import bpy


def argv_after_dashes():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    try:
        bpy.ops.import_scene.fbx(filepath=path)
    except AttributeError:
        import addon_utils
        addon_utils.enable("io_scene_fbx", default_set=False, persistent=True)
        bpy.ops.import_scene.fbx(filepath=path)


def tris():
    n = 0
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        me = o.data
        me.calc_loop_triangles()
        n += len(me.loop_triangles)
    return n


def decimate(ratio):
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        bpy.context.view_layer.objects.active = o
        m = o.modifiers.new("dec", "DECIMATE")
        m.decimate_type = "COLLAPSE"
        m.ratio = ratio
        bpy.ops.object.modifier_apply(modifier=m.name)


def export_fbx(path):
    bpy.ops.export_scene.fbx(filepath=path, path_mode="COPY", embed_textures=True)


def main():
    a = argv_after_dashes()
    if len(a) < 2:
        print("нужно: ПАПКА_ВХОД ПАПКА_ВЫХОД [ratio]")
        return
    vhod, vyhod = a[0], a[1]
    ratio = float(a[2]) if len(a) > 2 else 0.30
    tag = f"{int(round(ratio * 100)):02d}"
    os.makedirs(vyhod, exist_ok=True)

    # 30% выбрано опытом: силуэт меняется на 7.8%, форма цела. На 15% уже
    # гранит панели, на 8% рвет. Замер в PROHOD-1-PLOTNOST.md.
    faily = sorted(f for f in os.listdir(vhod) if f.lower().endswith(".fbx"))
    for f in faily:
        imya = os.path.splitext(f)[0]
        out = os.path.join(vyhod, f"{imya}-dec{tag}.fbx")
        if os.path.exists(out):
            print(f"PROPUSK {imya}: уже есть")
            continue
        clear()
        import_fbx(os.path.join(vhod, f))
        bylo = tris()
        decimate(ratio)
        stalo = tris()
        export_fbx(out)
        print(f"GOTOVO {imya}: {bylo} -> {stalo} ({stalo / max(bylo,1):.3f})")


main()
