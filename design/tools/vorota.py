"""Семь числовых ворот приёмки (раздел 7 гида). Печатает строку замера и вердикт.

Запуск: python vorota.py модель.glb потолок_треугольников [порог_плиты]

Порог плиты по умолчанию 1.15. Для полыньи, посадочной площадки и солнечной
батареи на широкой ноге широкое основание законно — там 1.6.
"""

import sys

import numpy as np
import trimesh
from scipy.spatial import ConvexHull

src = sys.argv[1]
ceiling = int(sys.argv[2])
plita_limit = float(sys.argv[3]) if len(sys.argv) > 3 else 1.15

scene = trimesh.load(src)
meshes = list(scene.geometry.values()) if hasattr(scene, "geometry") else [scene]

tris = sum(len(m.faces) for m in meshes)

materials, textures, tex_size = set(), 0, "нет"
for m in meshes:
    mat = getattr(m.visual, "material", None)
    if mat is None:
        continue
    materials.add(getattr(mat, "name", "?"))
    for field in ("baseColorTexture", "metallicRoughnessTexture", "normalTexture",
                  "emissiveTexture", "occlusionTexture", "image"):
        img = getattr(mat, field, None)
        if img is not None and hasattr(img, "size"):
            textures += 1
            tex_size = f"{img.size[0]}x{img.size[1]}"

# Ворота 7: плита. Доля пятна низа к пятну тела. Считается по вершинам, а не по
# габариту: тонкий блин под объектом коробку почти не меняет, а пятно — вдвое.
verts = np.vstack([np.asarray(m.vertices) for m in meshes])
low, high = verts[:, 1].min(), verts[:, 1].max()
height = max(high - low, 1e-9)


def footprint(mask):
    """Площадь силуэта среза в плане.

    Считать долю занятых клеток нельзя: меш полый, и срез на середине высоты
    даёт кольцо вершин, а плоское дно — заполненный круг. Кольцо занимает вдвое
    меньше клеток при том же силуэте, и обычный камень получал отношение 2.0
    при полном отсутствии плиты. Площадь выпуклой оболочки от полости не
    зависит.
    """
    pts = verts[mask][:, [0, 2]]
    if len(pts) < 3:
        return 0.0
    try:
        return float(ConvexHull(pts).volume)  # для 2D volume и есть площадь
    except Exception:
        return 0.0


niz = footprint(verts[:, 1] <= low + 0.08 * height)
# Максимальное сечение по всей высоте, а не на фиксированных 25-35%: у объекта
# на ножках, треноге или ступнях тело там тонкое по устройству, и отношение
# росло само, без всякого блина. Восемь ложных срабатываний из восьми в первой
# партии — ровно этот случай.
sechenia = []
for i in range(10):
    lo = low + height * i / 10
    sechenia.append(footprint((verts[:, 1] >= lo) & (verts[:, 1] <= lo + height / 10)))
maks = max(sechenia) if sechenia else 0.0
plita = niz / maks if maks > 0 else 0.0

# У полусферы, конуса и кучи максимальное сечение и есть низ: отношение всегда
# около 1.0, и тонкий блин подвинет его лишь на десятые — гейт его пропустит.
# Для таких форм число неинформативно, и честнее сказать это вслух, чем
# печатать успокоительную единицу. Триггер: максимальное сечение лежит ниже
# 15% полной высоты.
vysota_maks = sechenia.index(maks) / 10
chislo_ne_rabotaet = vysota_maks < 0.15

size = verts.max(axis=0) - verts.min(axis=0)

# Контракт раздела 6 задания: полоса 15 000 - 28 000, снята с моделей,
# принятых в сцену. Потолок из аргумента больше не потолок, а верх полосы.
gates = [
    (f"1 треугольников <= {ceiling}", tris <= ceiling, tris),
    ("2 треугольников >= 15000", tris >= 15000, tris),
    ("3 мешей ровно 1", len(meshes) == 1, len(meshes)),
    ("4 материалов ровно 1", len(materials) == 1, len(materials)),
    ("5 текстур ровно 1", textures == 1, textures),
    ("6 текстура 2048", tex_size != "нет" and int(tex_size.split("x")[0]) == 2048, tex_size),
    (f"7 плиты нет (<= {plita_limit})", plita <= plita_limit or chislo_ne_rabotaet, round(plita, 3)),
]

print(f"{src.split('/')[-1]}: треугольников {tris}, мешей {len(meshes)}, "
      f"материалов {len(materials)}, текстур {textures}, текстура {tex_size}, "
      f"габарит {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f}, плита {plita:.2f}")

if chislo_ne_rabotaet:
    print(f"  плита {plita:.2f} НЕ ИНФОРМАТИВНА: максимальное сечение на высоте"
          f" {vysota_maks:.0%} — форма расширяется книзу (полусфера, конус, куча)."
          f" Решать по нижнему рендеру.")

failed = [f"{name} (сейчас {value})" for name, ok, value in gates if not ok]
if failed:
    print("ПРОВАЛ:", "; ".join(failed))
else:
    print("числовые ворота пройдены, остаются глаза: 8-11")
sys.exit(1 if failed else 0)
