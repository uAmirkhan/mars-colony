"""Генерация игровой модели из картинки. Рычаги гида разведены по флагам.

    python gen-asset.py вход.jpg выход.glb --potolok 2500 [--setka 512] [--seed 42]

--setka  рычаг 2: грубость объёмной структуры ДО извлечения меша (512/1024/1536).
         Главный рычаг веса. Чем ниже, тем легче меш рождается, формы остаются
         гладкими, дыр не появляется.
--potolok рычаг 3: сколько треугольников оставить при извлечении. Работает
         вместе с remesh, то есть поверхность перестраивается заново, а не
         схлопывается рёбрами — именно схлопывание давало решето в Blender.

Текстура 2048 (ворота 6), лишняя карта металличности снимается: сцена требует
ровно одну текстуру (ворота 5).
"""

import argparse
import os

os.environ["OPENCV_IO_ENABLE_OPENEXR"] = "1"
os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"

from PIL import Image

import o_voxel
from trellis2.pipelines import Trellis2ImageTo3DPipeline

p = argparse.ArgumentParser()
p.add_argument("src")
p.add_argument("dst")
p.add_argument("--potolok", type=int, required=True)
p.add_argument("--setka", default="1024", choices=["512", "1024", "1024_cascade", "1536_cascade"])
p.add_argument("--shagi", type=int, default=25)
p.add_argument("--seed", type=int, default=42)
p.add_argument("--tekstura", type=int, default=2048)
args = p.parse_args()

image = Image.open(args.src)
if image.mode != "RGBA":
    image = image.convert("RGB")  # белый фон снимет встроенный удалятель фона
print(f"вход {args.src} {image.size}, сетка {args.setka}, потолок {args.potolok},"
      f" seed {args.seed}", flush=True)

pipeline = Trellis2ImageTo3DPipeline.from_pretrained("microsoft/TRELLIS.2-4B")
pipeline.cuda()

mesh = pipeline.run(
    image,
    seed=args.seed,
    pipeline_type=args.setka,
    sparse_structure_sampler_params={"steps": args.shagi},
    shape_slat_sampler_params={"steps": args.shagi},
)[0]

glb = o_voxel.postprocess.to_glb(
    vertices=mesh.vertices,
    faces=mesh.faces,
    attr_volume=mesh.attrs,
    coords=mesh.coords,
    attr_layout=mesh.layout,
    voxel_size=mesh.voxel_size,
    aabb=[[-0.5, -0.5, -0.5], [0.5, 0.5, 0.5]],
    decimation_target=args.potolok,
    texture_size=args.tekstura,
    remesh=True,
    remesh_band=1,
    remesh_project=0,
    verbose=False,
)

# Ворота 5: текстура ровно одна. Генератор печёт две — цвет и металличность с
# шероховатостью. Вторая в нашей сцене не используется вовсе: свет там плоский,
# стилизованный. Снимаем ссылку, а не пересобираем атлас.
for m in glb.geometry.values() if hasattr(glb, "geometry") else [glb]:
    mat = getattr(m.visual, "material", None)
    if mat is not None and getattr(mat, "metallicRoughnessTexture", None) is not None:
        mat.metallicRoughnessTexture = None

glb.export(args.dst)
print(f"готово {args.dst}", flush=True)
