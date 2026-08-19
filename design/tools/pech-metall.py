# -*- coding: utf-8 -*-
"""Рифлёный настил дорог печём числами, а не генератором.

Почему не генератором: рёбра это строгая периодика в одном направлении.
Генератор её ломает (плитка, смена направления через квадрат), а здесь она
задаётся периодом, который делит сторону текстуры нацело — бесшовность
получается по построению, вклейка краёв не нужна вовсе.
"""
import numpy as np
from PIL import Image

N = 1024          # сторона
PERIOD = 32       # ширина ребра в пикселях, 1024 / 32 = 32 ребра ровно
CEL = (0.769, 0.659, 0.510)   # #C4A882
VYKHOD = r"c:\Ai\Jarvis\mars-colony\design\cut\tayly-poverkhnosti-4sht-chistye\tile_metal.png"

rng = np.random.default_rng(1)


def shum(n, kletka):
    """Периодический шум ячейки kletka с гладкой интерполяцией."""
    k = n // kletka
    z = rng.random((k, k))
    z = np.tile(z, (2, 2))[:k + 1, :k + 1]      # замыкание на края
    z[-1, :] = z[0, :]
    z[:, -1] = z[:, 0]
    im = Image.fromarray((z * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC)
    a = np.asarray(im, dtype=np.float32) / 255.0
    return (a - a.mean()) / (a.std() + 1e-9)


x = np.arange(N, dtype=np.float32)
faza = 2.0 * np.pi * x / PERIOD

# Профиль ребра: скруглённый гребень, не синус — у синуса нет плоского дна,
# а у настила между рёбрами есть ровная полка.
p = np.cos(faza)
rebra = np.sign(p) * np.abs(p) ** 0.6            # приплюснутый гребень
rebra = np.tile(rebra, (N, 1))                   # рёбра вдоль одной оси, все параллельны

# Слои поверх: мелкая пыль и вытертая краска. Оба слабые — текстура должна
# остаться почти заливкой, иначе крупное пятно станет штампом при замощении.
pyl = shum(N, 4) * 0.012
iznos = shum(N, 128) * 0.018

yark = 1.0 + rebra * 0.045 + pyl + iznos

a = np.stack([yark * c for c in CEL], axis=-1)

# Привести средний цвет ровно к цели: слои сдвигают его на доли процента,
# но цвет земли в кадре есть произведение текстуры на тон материала, и
# накопленный сдвиг уедет вместе с ним.
for i in range(3):
    a[..., i] *= CEL[i] / a[..., i].mean()

a = np.clip(a, 0.0, 1.0)
Image.fromarray((a * 255 + 0.5).astype(np.uint8)).save(VYKHOD)

L = a @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
sred = a.reshape(-1, 3).mean(axis=0)
kb = N // 16
nizk = L.reshape(16, kb, 16, kb).mean(axis=(1, 3))


def korr(u, v):
    u = u.ravel() - u.mean(); v = v.ravel() - v.mean()
    return float((u * v).sum() / (np.sqrt((u * u).sum() * (v * v).sum()) + 1e-9))


print(f"средний цвет  #{int(sred[0]*255+.5):02X}{int(sred[1]*255+.5):02X}{int(sred[2]*255+.5):02X}  цель #C4A882")
print(f"пятна         {float(nizk.max()-nizk.min()):.4f}")
print(f"контраст      {float(L.std()):.4f}")
print(f"симметрия     гор {abs(korr(L, L[:, ::-1])):.3f}, вер {abs(korr(L, L[::-1, :])):.3f}")
print(f"шов лево-право {float(np.abs(L[:, :1].mean(1) - L[:, -1:].mean(1)).mean()):.5f}")
print(f"шов верх-низ   {float(np.abs(L[:1, :].mean(0) - L[-1:, :].mean(0)).mean()):.5f}")
print("записан:", VYKHOD)
