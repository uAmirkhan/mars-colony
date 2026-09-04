"""Генератор материалов земли для сцены Mars Colony.

Почему процедурно, а не генератором картинок:
  - бесшовность гарантирована конструкцией (весь шум периодический), а не
    подгонкой после;
  - тон задается числом, значит палитру можно двигать под замер нормы;
  - вид выходит рисованный, с жесткими краями и плоскими заливками, а не
    фотографический мелкий шум. Township рисованный, и земля там тоже.

Замысел по цвету: Марс монохромный, поэтому цвет приходит не с грунта, а с
того, что привезли люди - разметка, настил, панели, зелень, знаки опасности.

Запуск:
    python zemlya.py [--out ПАПКА] [--size 1024]
    python zemlya.py --self-test
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

RNG = np.random.default_rng(20260824)


# ---------------------------------------------------------------- шум на торе

def _lattice(n, freq, rng):
    """Периодическая решетка случайных значений freq на freq."""
    return rng.random((freq, freq))


def value_noise(n, freq, rng):
    """Значимый шум с периодом ровно n. Решетка замыкается сама на себя,
    поэтому плитка бесшовна по построению, а не по подгонке."""
    g = _lattice(n, freq, rng)
    g = np.vstack([g, g[:1]])
    g = np.hstack([g, g[:, :1]])          # замыкание
    ys = np.linspace(0, freq, n, endpoint=False)
    xs = np.linspace(0, freq, n, endpoint=False)
    y0 = ys.astype(int); x0 = xs.astype(int)
    fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
    # сглаживание Перлина 6t^5-15t^4+10t^3, иначе видна решетка
    sy = fy * fy * fy * (fy * (fy * 6 - 15) + 10)
    sx = fx * fx * fx * (fx * (fx * 6 - 15) + 10)
    a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x0 + 1)]
    c = g[np.ix_(y0 + 1, x0)]; d = g[np.ix_(y0 + 1, x0 + 1)]
    return (a * (1 - sx) * (1 - sy) + b * sx * (1 - sy)
            + c * (1 - sx) * sy + d * sx * sy)


def fbm(n, freq, octaves, rng, gain=0.5):
    out = np.zeros((n, n)); amp = 1.0; norm = 0.0
    for o in range(octaves):
        out += amp * value_noise(n, freq * (2 ** o), rng)
        norm += amp; amp *= gain
    return out / norm


def worley(n, cells, rng, kind="f1"):
    """Клеточный шум на торе. Дает камни, плиты, трещины."""
    pts = (rng.random((cells * cells, 2)) + np.stack(
        np.meshgrid(np.arange(cells), np.arange(cells)), -1).reshape(-1, 2)) / cells
    ys, xs = np.mgrid[0:n, 0:n] / n
    p = np.stack([ys, xs], -1).reshape(-1, 2)
    best1 = np.full(n * n, 9.0); best2 = np.full(n * n, 9.0)
    idx = np.zeros(n * n, int)
    for i, q in enumerate(pts):
        d = p - q
        d -= np.round(d)                   # тор: ближайшая копия
        dist = np.sqrt((d * d).sum(-1))
        m = dist < best1
        best2 = np.where(m, best1, np.minimum(best2, dist))
        idx = np.where(m, i, idx)
        best1 = np.where(m, dist, best1)
    if kind == "f2f1":
        return ((best2 - best1) / (best2 + 1e-9)).reshape(n, n)
    if kind == "id":
        return idx.reshape(n, n)
    return best1.reshape(n, n)


# ---------------------------------------------------------------- краски

def mix(c1, c2, t):
    c1 = np.asarray(c1, float); c2 = np.asarray(c2, float)
    return c1[None, None, :] * (1 - t[..., None]) + c2[None, None, :] * t[..., None]


def posterize(t, steps):
    """Плоские заливки вместо плавного градиента - главный признак рисованного."""
    return np.floor(t * steps) / max(steps - 1, 1)


def save(rgb, path):
    a = np.clip(rgb, 0, 255).astype(np.uint8)
    Image.fromarray(a).save(path)
    return path


# ---------------------------------------------------------------- материалы

def regolit(n, rng):
    """База. Охра, крупные плоские пятна, редкая мелкая крошка.
    Плоские ступени вместо шума: так земля читается рисованной."""
    big = fbm(n, 3, 3, rng)
    big = posterize((big - big.min()) / np.ptp(big), 5)
    fine = fbm(n, 24, 2, rng)
    t = np.clip(big * 0.85 + fine * 0.15, 0, 1)
    rgb = mix((196, 104, 66), (232, 148, 100), t)
    # крошка
    kr = worley(n, 26, rng, "f1")
    rgb += ((kr < 0.012)[..., None] * np.array([-26, -18, -12]))
    return rgb


def plita(n, rng):
    """Технологическая плита. Светлая, холодноватая, со швами панелей.

    Это главный вклад в ахроматику: по норме Township она около 0.16, у нас
    было 0.048. Самый дешевый пункт таблицы и берется именно тут."""
    cell = n // 8
    ys, xs = np.mgrid[0:n, 0:n]
    # Сетку сдвигаем на полклетки: иначе линия шва ложится ровно на край
    # плитки, и при укладке два шва встают вплотную. Поймано самопроверкой.
    off = cell // 2
    seam = (((ys + off) % cell < 3) | ((xs + off) % cell < 3)).astype(float)
    wear = fbm(n, 8, 3, rng)
    wear = posterize((wear - wear.min()) / np.ptp(wear), 4)
    rgb = mix((176, 172, 166), (208, 205, 199), wear)
    rgb = rgb * (1 - 0.22 * seam[..., None])          # шов темнее
    # потертости и следы, чуть теплее
    sled = (fbm(n, 5, 2, rng) > 0.62).astype(float)
    rgb = rgb * (1 - 0.10 * sled[..., None]) + sled[..., None] * np.array([12, 6, 0])
    return rgb


def nastil(n, rng):
    """Металлический настил. Темный и холодный - вклад в темное и синее,
    противовес охре под постройками."""
    cell = n // 16
    ys, xs = np.mgrid[0:n, 0:n]
    off = cell // 2
    rebro = ((((ys + off) % cell) < cell * 0.30)
             ^ (((xs + off) % cell) < cell * 0.30)).astype(float)
    base = fbm(n, 10, 2, rng)
    rgb = mix((58, 66, 76), (86, 96, 108), posterize(base, 3))
    rgb += rebro[..., None] * np.array([16, 18, 20])
    # заклепки по узлам
    zaklep = worley(n, 16, rng, "f1")
    rgb += ((zaklep < 0.010)[..., None] * np.array([40, 42, 46]))
    return rgb


def delyanka(n, rng):
    """Возделанная делянка под куполом. Темная почва и ряды зелени.
    Единственный носитель зеленого семейства в земле."""
    ys, xs = np.mgrid[0:n, 0:n]
    ryad = np.sin(ys / n * np.pi * 2 * 14) * 0.5 + 0.5    # период целый, шов сойдется
    ryad = posterize(ryad, 3)
    grunt = fbm(n, 12, 2, rng)
    rgb = mix((62, 44, 34), (92, 68, 52), posterize(grunt, 3))
    zelen = (ryad > 0.66).astype(float)
    kust = fbm(n, 30, 2, rng)
    zelen = zelen * (kust > 0.42)
    rgb = rgb * (1 - zelen[..., None]) + zelen[..., None] * np.array([88, 138, 62])
    # блики на листе
    blik = zelen * (fbm(n, 46, 1, rng) > 0.62)
    rgb += blik[..., None] * np.array([34, 40, 18])
    return rgb


def led(n, rng):
    """Лед. Настоящий холодный, а не серый: у старого mars-ice насыщенность
    была 0.061, то есть ахроматика, и как холодный акцент он не работал."""
    tresh = worley(n, 9, rng, "f2f1")
    glub = fbm(n, 6, 3, rng)
    rgb = mix((96, 156, 192), (168, 214, 236), posterize(glub, 4))
    kromka = np.clip((0.10 - tresh) / 0.10, 0, 1)
    rgb = rgb * (1 - 0.45 * kromka[..., None]) + \
        kromka[..., None] * np.array([28, 60, 88])
    blik = (fbm(n, 14, 2, rng) > 0.70).astype(float)
    rgb += blik[..., None] * np.array([26, 22, 14])
    return rgb


def doroga(n, rng):
    """Дорога с колеей. Две накатанные полосы, между ними взрыхленное.
    Старая mars-road читалась песком, а не дорогой: не было структуры."""
    xs = np.mgrid[0:n, 0:n][1] / n
    d = np.minimum(np.abs(xs - 0.30), np.abs(xs - 0.70))
    koleya = np.clip(1 - d / 0.085, 0, 1)
    koleya = posterize(koleya, 3)
    grunt = fbm(n, 10, 3, rng)
    rgb = mix((186, 112, 78), (214, 140, 104), posterize(grunt, 4))
    # накатанное темнее и глаже
    rgb = rgb * (1 - 0.30 * koleya[..., None])
    rgb += koleya[..., None] * np.array([8, 10, 12])
    # выброс по обочине
    obochina = ((koleya < 0.05) & (fbm(n, 28, 2, rng) > 0.60)).astype(float)
    rgb += obochina[..., None] * np.array([18, 12, 8])
    return rgb


def kamen(n, rng):
    """Каменная мостовая. Рисованные плиты с явной кромкой."""
    f2 = worley(n, 11, rng, "f2f1")
    idx = worley(n, 11, rng, "id")
    ton = (idx * 2654435761 % 1000) / 1000.0
    rgb = mix((150, 92, 70), (196, 130, 100), posterize(ton, 4))
    shov = np.clip((0.13 - f2) / 0.13, 0, 1)
    rgb = rgb * (1 - 0.55 * shov[..., None]) + shov[..., None] * np.array([44, 26, 20])
    return rgb


MATERIALY = {
    "z-regolit": regolit,
    "z-plita": plita,
    "z-nastil": nastil,
    "z-delyanka": delyanka,
    "z-led": led,
    "z-doroga": doroga,
    "z-kamen": kamen,
}


# ---------------------------------------------------------------- наклейки

def nakleyki(n, out):
    """Наклейки поверх земли: разметка, круг площадки, пятна.

    Разметка НЕ вплавляется в плитку: тайлить разметку неверно, она должна
    лежать в конкретном месте. Поэтому отдельные картинки с альфой.
    """
    made = []

    # знаки опасности, косые полосы, желтое с черным
    m = n // 2
    ys, xs = np.mgrid[0:m, 0:m]
    pol = (((xs + ys) // (m // 10)) % 2).astype(float)
    rgb = np.where(pol[..., None] > 0.5,
                   np.array([226, 186, 46]), np.array([44, 40, 36])).astype(float)
    a = np.full((m, m), 255.0)
    a[:, :4] = a[:, -4:] = a[:4] = a[-4:] = 0      # мягкий край
    made.append(_rgba(rgb, a, os.path.join(out, "d-opasnost.png")))

    # круг посадочной площадки
    k = n
    ys, xs = np.mgrid[0:k, 0:k]
    r = np.sqrt((ys - k / 2) ** 2 + (xs - k / 2) ** 2) / (k / 2)
    kolco = ((r > 0.72) & (r < 0.82)).astype(float)
    krest = (((np.abs(ys - k / 2) < k * 0.02) | (np.abs(xs - k / 2) < k * 0.02))
             & (r < 0.55)).astype(float)
    mark = np.clip(kolco + krest, 0, 1)
    rgb = np.zeros((k, k, 3)) + np.array([236, 232, 224])
    made.append(_rgba(rgb, mark * 235, os.path.join(out, "d-ploshchadka.png")))

    # Пятна пролива. ЧЕТЫРЕ разные формы, а не одна на семь мест.
    #
    # Смотрящий нашел прежнюю версию инородной: один и тот же контур, плоская
    # заливка, жесткий пиксельный край. Жесткий край - следствие выреза по
    # альфе, у него альфа только 0 или 1. Растворяется стипплом: у границы
    # часть текселей выбивается в дырки, и на экране это читается растушевкой,
    # потому что тексель мельче пикселя.
    rng = np.random.default_rng(7)
    k = n // 2
    ys, xs = np.mgrid[0:k, 0:k]
    for nomer in range(4):
        f = fbm(k, 3 + nomer, 3, rng)
        # форма гуляет по углу, а не круг со случайным радиусом
        ug = np.arctan2(ys - k / 2, xs - k / 2)
        r = np.sqrt((ys - k / 2) ** 2 + (xs - k / 2) ** 2) / (k / 2)
        volna = (1.0
                 + 0.30 * np.sin(ug * (2 + nomer) + nomer * 1.7)
                 + 0.16 * np.sin(ug * (5 + nomer) + nomer * 0.9))
        # Ядро СПЛОШНОЕ. Шум пускается только по кромке: если гнать его по всей
        # площади, он прогрызает дыры в середине, и пролив читается кольцом.
        kromka = 0.62 * volna + (f - 0.5) * 0.18
        telo = r < kromka
        # стиппл по краю: чем ближе к кромке, тем реже тексели
        kraya = np.clip((r - 0.34 * volna) / (0.34 * volna), 0, 1)
        stipl = rng.random((k, k)) > kraya ** 1.6
        a = (telo & stipl).astype(float) * 210
        rgb = np.zeros((k, k, 3)) + np.array([88, 50, 36])
        made.append(_rgba(rgb, a, os.path.join(out, f"d-pyatno-{nomer + 1}.png")))

    return made


def _rgba(rgb, a, path):
    arr = np.dstack([np.clip(rgb, 0, 255), np.clip(a, 0, 255)]).astype(np.uint8)
    Image.fromarray(arr, "RGBA").save(path)
    return path


# ---------------------------------------------------------------- самопроверка

def seam_error(a):
    l, r = a[:, 0].astype(float), a[:, -1].astype(float)
    t, b = a[0].astype(float), a[-1].astype(float)
    gx = np.abs(l - r).mean(); gy = np.abs(t - b).mean()
    ix = np.abs(a[:, 1:].astype(float) - a[:, :-1].astype(float)).mean()
    iy = np.abs(a[1:].astype(float) - a[:-1].astype(float)).mean()
    return gx, gy, (ix + iy) / 2


def self_test(n=256):
    """Плитка обязана сходиться сама с собой. Шов на земле виден сразу и
    читается как техническая ошибка, а не как стиль."""
    ok = True
    print("Стык плитки: край к краю против внутреннего перепада\n")
    print(f"{'материал':14} {'стык X':>8} {'стык Y':>8} {'внутри':>8}  вердикт")
    for name, fn in MATERIALY.items():
        rgb = fn(n, np.random.default_rng(1))
        gx, gy, inner = seam_error(np.clip(rgb, 0, 255).astype(np.uint8))
        good = gx <= inner * 2.0 and gy <= inner * 2.0
        ok &= good
        print(f"{name:14} {gx:8.2f} {gy:8.2f} {inner:8.2f}  "
              f"{'бесшовная' if good else 'ШОВ ВИДЕН'}")
    print("\nСамопроверка:", "пройдена" if ok else "ПРОВАЛЕНА")
    return 0 if ok else 1


def check_dir(d):
    """Приемка сгенерированных плиток. Два числа: сходится ли стык и в какое
    цветовое семейство попадает. Оба против той же нормы, что и кадры."""
    import glob as _g
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from kadr import measure
    files = sorted(_g.glob(os.path.join(d, "*.png")) + _g.glob(os.path.join(d, "*.jpg")))
    if not files:
        print("нечего проверять:", d); return 1
    print(f"{'файл':22} {'размер':11} {'стыкX':>7} {'стыкY':>7} {'внутри':>7} "
          f"{'вердикт':<11} {'насыщ':>6} {'семья':<12}")
    bad = 0
    for f in files:
        im = Image.open(f).convert("RGB")
        arr = np.asarray(im)
        gx, gy, inner = seam_error(arr)
        ok = gx <= inner * 2.0 and gy <= inner * 2.0
        if not ok: bad += 1
        m = measure(f)
        fam = max(m["semyi_ves"], key=m["semyi_ves"].get)
        print(f"{os.path.basename(f):22} {str(im.size):11} {gx:7.2f} {gy:7.2f} "
              f"{inner:7.2f} {'бесшовная' if ok else 'ШОВ ВИДЕН':<11} "
              f"{m['nasyshchennost']:6.3f} {fam:<12}")
    print()
    print(f"со швом: {bad} из {len(files)}")
    return 0 if bad == 0 else 1


# ---------------------------------------------------------------- запуск

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="C:/Ai/Jarvis/mars-unity/Assets/OurAssets/ground2")
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--check", help="проверить чужие плитки в папке: стык и палитра")
    a = ap.parse_args()

    if a.self_test:
        sys.exit(self_test())

    if a.check:
        sys.exit(check_dir(a.check))

    os.makedirs(a.out, exist_ok=True)
    for name, fn in MATERIALY.items():
        rgb = fn(a.size, np.random.default_rng(abs(hash(name)) % (2 ** 31)))
        p = save(rgb, os.path.join(a.out, name + ".png"))
        gx, gy, inner = seam_error(np.clip(rgb, 0, 255).astype(np.uint8))
        print(f"{name:14} стык {gx:5.2f}/{gy:5.2f} при внутреннем {inner:5.2f}  -> {p}")
    for p in nakleyki(a.size, a.out):
        print(f"{'наклейка':14} -> {p}")


if __name__ == "__main__":
    main()
