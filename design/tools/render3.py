"""Три рендера модели: три четверти сверху, сбоку, снизу. Плюс ноготь 64 px.

Нижний ракурс обязателен на каждой попытке: плиту под объектом с других
ракурсов не видно, а это главный дефект конвейера (ворота 7 гида).

Ноготь нужен для ворот 8 — слепого опознания. Смотреть его надо ДО того, как
посмотрел исходник, иначе узнаешь не объект, а собственную память.

Запуск: python render3.py модель.glb папка_вывода
"""

import os
import sys

os.environ.setdefault("PYOPENGL_PLATFORM", "egl")

import numpy as np
import pyrender
import trimesh
from PIL import Image

RES = 512
BG = (1.0, 1.0, 1.0)  # белый: силуэт на нем читается, ворота 11

# Свет почти весь рассеянный: оценщик смотрит форму и цвет текстуры, а
# контрастный направленный свет красит терракоту в бурый и врет про покраску.
# yaw, pitch, имя. Питч со знаком минус смотрит снизу.
VIEWS = [
    (np.radians(35), np.radians(35), "tri-chetverti"),
    (np.radians(90), np.radians(5), "sboku"),
    (np.radians(35), np.radians(-70), "snizu"),
]


def look_at(yaw, pitch, radius, center):
    eye = center + radius * np.array(
        [np.cos(pitch) * np.sin(yaw), np.sin(pitch), np.cos(pitch) * np.cos(yaw)]
    )
    forward = center - eye
    forward /= np.linalg.norm(forward)
    right = np.cross(forward, [0, 1, 0])
    # У полюсов right вырождается: подпираем другой осью, иначе камера схлопнется.
    if np.linalg.norm(right) < 1e-6:
        right = np.cross(forward, [0, 0, 1])
    right /= np.linalg.norm(right)
    up = np.cross(right, forward)
    pose = np.eye(4)
    pose[:3, 0] = right
    pose[:3, 1] = up
    pose[:3, 2] = -forward
    pose[:3, 3] = eye
    return pose


def main(src, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    name = os.path.splitext(os.path.basename(src))[0]

    mesh = trimesh.load(src)
    scene = pyrender.Scene(bg_color=[*BG, 1.0], ambient_light=[0.9, 0.9, 0.9])
    scene.add(pyrender.Mesh.from_trimesh(mesh, smooth=False) if isinstance(mesh, trimesh.Trimesh)
              else pyrender.Scene.from_trimesh_scene(mesh).meshes.pop())

    bounds = mesh.bounds
    center = (bounds[0] + bounds[1]) / 2
    # Дистанция считается из угла обзора, а не подбирается: объект должен
    # влезать целиком при любом габарите, иначе половина рендеров обрезана.
    extent = float(np.linalg.norm(bounds[1] - bounds[0]))
    radius = extent / (2 * np.tan(np.radians(35) / 2)) * 1.15

    renderer = pyrender.OffscreenRenderer(RES, RES)
    camera = pyrender.PerspectiveCamera(yfov=np.radians(35))
    light = pyrender.DirectionalLight(color=[1, 1, 1], intensity=1.6)

    for yaw, pitch, label in VIEWS:
        pose = look_at(yaw, pitch, radius, center)
        cam_node = scene.add(camera, pose=pose)
        light_node = scene.add(light, pose=pose)
        color, _ = renderer.render(cam_node.camera and scene)
        Image.fromarray(color).save(f"{out_dir}/{name}-{label}.png")
        scene.remove_node(cam_node)
        scene.remove_node(light_node)

    # Ноготь: три четверти, уменьшенные до 64 px.
    big = Image.open(f"{out_dir}/{name}-tri-chetverti.png")
    big.resize((64, 64), Image.LANCZOS).save(f"{out_dir}/{name}-nogot.png")
    print(f"{name}: три рендера + ноготь")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
