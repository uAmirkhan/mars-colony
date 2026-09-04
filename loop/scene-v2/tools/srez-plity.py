"""Срез плиты под постройкой.

Замер показал: у построек корневого слоя нижний срез ровно 2.00 в единицах
модели при теле 0.55-0.80, то есть под зданием сидит квадратная плита два на
два. Это земля, достроенная генератором image-to-3d под объектом.

Последствие не косметическое: масштабирование по общей высоте растягивает
плиту, и купол высотой 6 м занимает в плане 41 м. Разложить такое в кадре
70 на 71 м невозможно, отсюда и наложения построек.

Режем по высоте: все, что ниже границы плиты, удаляется. Граница ищется как
уровень, выше которого ширина перестает падать.

Запуск:
    blender.exe -b --factory-startup --python srez-plity.py -- ВХОД ВЫХОД
"""
import os, sys
import numpy as np
import bpy, bmesh, addon_utils

addon_utils.enable("io_scene_fbx", default_set=False, persistent=True)


def argv():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def vershiny():
    vs = []
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        mw = o.matrix_world
        for v in o.data.vertices:
            w = mw @ v.co
            vs.append((w.x, w.y, w.z))
    return np.array(vs) if vs else None


def granitsa_plity(a):
    """Уровень, на котором ширина перестает резко падать. Плита широкая и
    тонкая, тело узкое: ищем первый слой снизу, где ширина упала до 1.4 раза
    от ширины тела."""
    z = a[:, 2]
    lo, hi = z.min(), z.max()
    H = hi - lo
    if H <= 0:
        return None
    def shir(f0, f1):
        m = (z >= lo + H * f0) & (z <= lo + H * f1)
        if m.sum() < 8:
            return 0.0
        s = a[m]
        return float(max(np.ptp(s[:, 0]), np.ptp(s[:, 1])))
    telo = shir(0.35, 0.75)
    niz = shir(0.0, 0.06)
    if telo <= 0 or niz / telo < 1.6:
        return None            # плиты нет, резать нечего
    for i in range(1, 40):
        f = i / 40.0
        if shir(f, min(f + 0.05, 1.0)) <= telo * 1.4:
            return lo + H * f
    return None


def rezat(porog):
    for o in list(bpy.data.objects):
        if o.type != "MESH":
            continue
        bpy.context.view_layer.objects.active = o
        me = o.data
        bm = bmesh.new(); bm.from_mesh(me)
        mw = o.matrix_world
        udalit = [v for v in bm.verts if (mw @ v.co).z < porog]
        bmesh.ops.delete(bm, geom=udalit, context="VERTS")
        bm.to_mesh(me); bm.free()
        me.update()
    # пустые объекты убрать
    for o in list(bpy.data.objects):
        if o.type == "MESH" and len(o.data.vertices) == 0:
            bpy.data.objects.remove(o, do_unlink=True)


def main():
    a = argv()
    if len(a) < 2:
        print("нужно: ПАПКА_ВХОД ПАПКА_ВЫХОД"); return
    vhod, vyhod = a[0], a[1]
    os.makedirs(vyhod, exist_ok=True)
    for f in sorted(x for x in os.listdir(vhod) if x.lower().endswith(".fbx")):
        imya = os.path.splitext(f)[0]
        out = os.path.join(vyhod, imya + ".fbx")
        if os.path.exists(out):
            print(f"PROPUSK {imya}"); continue
        bpy.ops.wm.read_factory_settings(use_empty=True)
        try:
            bpy.ops.import_scene.fbx(filepath=os.path.join(vhod, f))
        except Exception as e:
            print(f"OSHIBKA {imya}: {e}"); continue
        v = vershiny()
        if v is None:
            print(f"PUSTO {imya}"); continue
        z0, z1 = v[:, 2].min(), v[:, 2].max()
        porog = granitsa_plity(v)
        if porog is None:
            print(f"BEZ_PLITY {imya}"); continue
        rezat(porog)
        v2 = vershiny()
        if v2 is None:
            print(f"SREZALO_VSE {imya}"); continue
        h0 = z1 - z0
        h1 = v2[:, 2].max() - v2[:, 2].min()
        sh0 = float(max(np.ptp(v[:, 0]), np.ptp(v[:, 1])))
        sh1 = float(max(np.ptp(v2[:, 0]), np.ptp(v2[:, 1])))
        bpy.ops.export_scene.fbx(filepath=out, path_mode="COPY", embed_textures=True)
        print(f"SREZ {imya}: высота {h0:.2f}->{h1:.2f}  ширина {sh0:.2f}->{sh1:.2f}")


main()
