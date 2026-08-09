"""Резак текстур: 4096 -> заданный размер, с отчетом по весу.

Зачем. Генератор печет текстуру 4096, и вес файла несет именно она, а не
геометрия: shuttle на 1955 треугольниках весит 25 МБ. Резка поликаунта такой
файл не облегчает вовсе, а веб-плеер платит за каждый мегабайт при загрузке.

Сырье с 4096 не трогается и остается на машине генератора: перепечь текстуру
заново из него дешевле, чем перегенерировать модель.

Запуск: python shrink-texture.py размер выход_папка вход.glb [вход.glb ...]
"""

import sys
from pathlib import Path

import trimesh
from PIL import Image

size = int(sys.argv[1])
out_dir = Path(sys.argv[2])
out_dir.mkdir(parents=True, exist_ok=True)

total_before = total_after = 0

for src in sys.argv[3:]:
    src = Path(src)
    scene = trimesh.load(src)

    # У PBR-материала карт несколько и лежат они в своих полях, а не в общем
    # `image`: цвет, металличность с шероховатостью, нормали, свечение. Резать
    # надо все, иначе вес утащит любая одна пропущенная.
    MAPS = (
        "image",
        "baseColorTexture",
        "metallicRoughnessTexture",
        "normalTexture",
        "emissiveTexture",
        "occlusionTexture",
    )

    shrunk = []
    for mesh in scene.geometry.values():
        material = getattr(mesh.visual, "material", None)
        if material is None:
            continue
        for field in MAPS:
            image = getattr(material, field, None)
            if not isinstance(image, Image.Image) or max(image.size) <= size:
                continue
            was = image.size[0]
            # LANCZOS: панельные линии тонкие, на билинейном они мылятся в кашу.
            setattr(material, field, image.resize((size, size), Image.LANCZOS))
            shrunk.append(f"{field} {was}->{size}")

    dst = out_dir / src.name
    scene.export(dst)

    before, after = src.stat().st_size, dst.stat().st_size
    total_before += before
    total_after += after
    print(
        f"{src.name}: {before / 1e6:.1f} -> {after / 1e6:.1f} МБ"
        f" ({', '.join(shrunk) or 'текстур не найдено'})"
    )

if total_before:
    print(
        f"\nвсего: {total_before / 1e6:.0f} -> {total_after / 1e6:.0f} МБ,"
        f" в {total_before / max(total_after, 1):.1f} раза легче"
    )
