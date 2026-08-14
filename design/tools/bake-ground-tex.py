# Земля из наших ассетов, версия 2.
# Источник - рисованные тайлы поверхности с наших листов (design/cut/...):
# у них верхняя грань большая и однородная, стиль тот же по построению.
# Прием: внутренний прямоугольник верхней грани -> зеркальная сшивка 2x2
# (бесшовно по построению) -> поверх кладем ту же карту со сдвигом на
# полпериода через мягкую маску - симметрия глохнет, шов не появляется.
import sys
import os
import bpy
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
cut_dir = os.path.abspath(argv[0])
out_dir = os.path.abspath(argv[1])
os.makedirs(out_dir, exist_ok=True)

SIZE = 1024

# файл, окно внутренней части верхней грани (x0,y0,x1,y1 в долях), выход
JOBS = [
    (os.path.join(cut_dir, 'tayly-poverkhnosti-4sht-chistye', 'tile_sand.png'),
     (0.36, 0.26, 0.64, 0.46),'mars-ground'),
    (os.path.join(cut_dir, 'tayly-poverkhnosti-4sht-chistye', 'tile_ice.png'),
     (0.36, 0.26, 0.64, 0.46),'mars-ice'),
    (os.path.join(cut_dir, 'tayly-poverkhnosti-4sht-chistye', 'tile_rock.png'),
     (0.36, 0.26, 0.64, 0.46),'mars-rock'),
]


def load_rgb(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    return px[::-1, :, :3]   # переворачиваем в экранные координаты


def save_rgb(arr, path):
    h, w = arr.shape[0], arr.shape[1]
    img = bpy.data.images.new('o', w, h, alpha=False)
    rgba = np.concatenate([arr[::-1], np.ones((h, w, 1), dtype=np.float32)], axis=2)
    img.pixels[:] = rgba.ravel()
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()


def resize(arr, size):
    h, w = arr.shape[0], arr.shape[1]
    ys = np.linspace(0, h - 1, size).astype(int)
    xs = np.linspace(0, w - 1, size).astype(int)
    return arr[np.ix_(ys, xs)]


for src, (fx0, fy0, fx1, fy1), name in JOBS:
    rgb = load_rgb(src)
    h, w = rgb.shape[0], rgb.shape[1]
    c = rgb[int(h * fy0):int(h * fy1), int(w * fx0):int(w * fx1)]

    # мягкое приглушение крупных пятен: чуть к среднему цвету
    mean = c.mean(axis=(0, 1), keepdims=True)
    c = c * 0.62 + mean * 0.38

    # зеркальная сшивка 2x2
    top = np.concatenate([c, c[:, ::-1]], axis=1)
    quad = np.concatenate([top, top[::-1, :]], axis=0)
    quad = resize(quad, SIZE)

    # гашение симметрии: та же карта со сдвигом на полпериода, мягкая маска
    rolled = np.rot90(quad, 1).copy()
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) / SIZE
    mask = (0.5 + 0.5 * np.sin(2 * np.pi * (xx + 0.25)) *
            np.sin(2 * np.pi * (yy + 0.25))).astype(np.float32)
    mask = (mask * 0.55)[..., None]
    out = quad * (1 - mask) + rolled * mask

    save_rgb(out.astype(np.float32), os.path.join(out_dir, f'{name}.png'))
    print(f'записана {name}.png')
print('ГОТОВО')
