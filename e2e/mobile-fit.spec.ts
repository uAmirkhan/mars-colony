import { expect, type Page, test } from '@playwright/test';

/**
 * Ни одна кнопка не имеет права стоять за краем экрана.
 *
 * Дефект Д-23 нашелся случайно: проверка ощущения не смогла нажать «Склад» на
 * телефоне, потому что кнопка стояла на координате x = -149 при ширине экрана
 * 412. Хаб из шести кнопок не влезал в строку, а блок не переносился — левые
 * кнопки уезжали за край, и склад с телефона не открывался вовсе.
 *
 * Почему это не поймали раньше: браузерные проверки нажимают через локатор, а
 * Playwright перед кликом сам прокручивает элемент в видимую область. Для
 * машины кнопка была доступна, для пальца — нет. Поэтому проверка меряет
 * геометрию, а не кликабельность.
 *
 * Проверка идет на обоих экранах: узкий ловит переполнение, широкий держит
 * гарантию, что лечение узкого не сломало обычный вид.
 */

/** Полтора пикселя списываем на округление раскладки, больше — уже за краем. */
const EDGE_TOLERANCE = 1.5;

interface Offender {
  text: string;
  x: number;
  right: number;
  y: number;
  bottom: number;
  width: number;
}

async function offscreenButtons(page: Page): Promise<Offender[]> {
  return page.evaluate((tolerance) => {
    const out: Offender[] = [];

    /**
     * Можно ли доскроллить до кнопки.
     *
     * По вертикали «за краем экрана» само по себе не дефект: панель склада
     * прокручивается внутри себя, и десять кнопок продажи честно лежат ниже
     * окна — до них доезжают пальцем. Первая редакция вертикальной проверки
     * этого не различала и краснела на здоровом складе и здоровой фабрике.
     *
     * Дефект — кнопка, до которой доскроллить НЕЛЬЗЯ. Ровно такой был крестик
     * закрытия панели: он абсолютно спозиционирован выше рамки, ни один
     * предок не прокручивается, и страница тоже.
     */
    const reachable = (el: Element): boolean => {
      for (let node = el.parentElement; node !== null; node = node.parentElement) {
        const overflow = getComputedStyle(node).overflowY;
        const scrolls = /auto|scroll/.test(overflow) && node.scrollHeight > node.clientHeight;
        if (scrolls) return true;
      }
      return document.documentElement.scrollHeight > window.innerHeight;
    };

    for (const el of document.querySelectorAll('button')) {
      const r = el.getBoundingClientRect();
      // Скрытое и схлопнутое не проверяем: это не «за краем», это «не на экране».
      if (r.width === 0 || r.height === 0) continue;
      if (getComputedStyle(el).visibility === 'hidden') continue;
      // Верх и низ проверяются наравне с боками. Первая редакция смотрела
      // только по горизонтали — и пропустила крестик закрытия панели стройки
      // на координате y = -17: кнопка стояла ЗА ВЕРХНИМ краем экрана, панель
      // было нечем закрыть, а проверка честно докладывала «чисто». Дефект
      // нашелся падением чужого теста, а не этой проверкой.
      const off_x = r.left < -tolerance || r.right > window.innerWidth + tolerance;
      const off_y =
        (r.top < -tolerance || r.bottom > window.innerHeight + tolerance) && !reachable(el);
      if (off_x || off_y) {
        out.push({
          text: (el.textContent ?? '').trim().slice(0, 24),
          x: Math.round(r.left),
          right: Math.round(r.right),
          y: Math.round(r.top),
          bottom: Math.round(r.bottom),
          width: Math.round(r.width),
        });
      }
    }
    return out;
  }, EDGE_TOLERANCE);
}

/**
 * Кнопки, накрытые чем-то другим.
 *
 * Проверка «помещается в экран» этого не ловит: элемент может лежать целиком
 * внутри экрана и при этом быть недоступен пальцу, потому что поверх него
 * стоит другой. Ровно так выключатель звука накрыл кнопку «Играть» на узком
 * экране — главный вход в игру был не нажимаем, а все проверки зеленые.
 *
 * Спрашиваем у браузера, кто на самом деле получит касание в центре кнопки.
 * Свой же потомок считается своим: у кнопки внутри есть текст и иконки.
 */
async function coveredButtons(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const out: string[] = [];
    /** Сколько проб из 25 накрыто чужим, чтобы считать это дефектом. */
    const COVERED_MIN = 2;
    /** Что накрывает по замыслу: модалка, затемнение, плашка сообщения. */
    const ALLOWED = '.scrim, .toast, .fx-layer';

    for (const el of document.querySelectorAll('button, .slot')) {
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) continue;
      if (getComputedStyle(el).visibility === 'hidden') continue;

      let covered = 0;
      let by = '';
      // Сетка проб, а не одна точка в центре. Дефект Д-31 накрывал ВЕРХ
      // карточки грядки счетчиком склада, центр оставался свободным, и
      // проверка по центру честно докладывала «чисто». Накрытая наполовину
      // кнопка — такой же дефект, как накрытая целиком.
      for (let i = 1; i <= 5; i++) {
        for (let j = 1; j <= 5; j++) {
          const x = r.left + (r.width * i) / 6;
          const y = r.top + (r.height * j) / 6;
          if (x < 0 || y < 0 || x > window.innerWidth || y > window.innerHeight) continue;
          const hit = document.elementFromPoint(x, y);
          if (hit === null) continue;
          // Свои и родители — не помеха: у кнопки внутри текст, снаружи фон.
          if (el.contains(hit) || hit.contains(el)) continue;
          if (hit.closest(ALLOWED) !== null) continue;
          covered += 1;
          by = `${(hit.textContent ?? '').trim().slice(0, 20)} (${hit.className || hit.tagName})`;
        }
      }

      if (covered >= COVERED_MIN) {
        const label = (el.textContent ?? '').trim().slice(0, 24);
        out.push(`«${label}» накрыта на ${covered} проб из 25: ${by}`);
      }
    }
    return out;
  });
}

async function openGame(page: Page) {
  await page.goto('/?fresh=1');
  await page.getByRole('button', { name: 'Играть' }).click();
}

test('кнопки игры помещаются в экран', async ({ page }) => {
  await openGame(page);
  await expect(page.getByRole('button', { name: 'Склад' })).toBeVisible();

  const bad = await offscreenButtons(page);
  expect(bad, `кнопки за краем экрана: ${JSON.stringify(bad)}`).toEqual([]);
});

test('ни одна кнопка не накрыта другой', async ({ page }) => {
  // Первый экран проверяется до входа в игру: именно там висит «Играть», и
  // именно ее накрывал выключатель звука.
  await page.goto('/?fresh=1');
  await expect(page.getByRole('button', { name: 'Играть' })).toBeVisible();
  expect(await coveredButtons(page), 'на первом экране').toEqual([]);

  await page.getByRole('button', { name: 'Играть' }).click();
  await expect(page.getByRole('button', { name: 'Склад' })).toBeVisible();
  expect(await coveredButtons(page), 'канонический старт').toEqual([]);
});

test('в показе ни одна кнопка не накрыта другой', async ({ page }) => {
  // Показ проверяется ОТДЕЛЬНО, и это не дубль.
  //
  // Дефект Д-31 жил только здесь: канонический старт дает четыре пустые
  // грядки, показ — шесть засеянных, поле выше, и именно оно наезжало на
  // счетчики. Проверка, ходившая по `?fresh=1`, честно докладывала «чисто» —
  // на своем экране она была права. А смотреть будут ровно этот.
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();
  await expect(page.getByTestId('demo-badge')).toBeVisible();
  expect(await coveredButtons(page), 'состояние показа').toEqual([]);
});

test('кнопки каждой панели помещаются в экран', async ({ page }) => {
  await openGame(page);

  // Панели проверяются по очереди: у каждой своя раскладка, и переполнение
  // одной ничего не говорит про остальные.
  for (const name of ['Склад', 'Фабрика', 'Дрон', 'Шаттл', 'Стройка']) {
    await page.getByRole('button', { name, exact: true }).click();
    const bad = await offscreenButtons(page);
    expect(bad, `панель «${name}»: кнопки за краем ${JSON.stringify(bad)}`).toEqual([]);
    await page.getByRole('button', { name: 'Закрыть' }).first().click();
  }
});

/**
 * Состояние, при котором панели максимально высокие.
 *
 * Канонический старт для этой проверки бесполезен: на первом уровне стройки
 * заперты, склад пуст, у заказов дрона нет позиций — панели низкие, и дефект
 * высоты на них не воспроизводится в принципе. Дырой оказалась именно она:
 * проверка ходила по пустым панелям и зеленела, пока настоящая панель стройки
 * не влезала в экран.
 *
 * Девятый уровень открывает обе стройки, а склад модулей закрывает ровно одну
 * из них — вторая тянет за собой три кнопки докупки за изотопы. Это и есть
 * самая высокая панель среза.
 */
async function seedTallPanels(page: Page) {
  await page.evaluate(() => {
    const store = (window as unknown as { __game: { getState: () => unknown; setState: (s: unknown) => void } }).__game;
    const state = store.getState() as { construction: unknown };
    store.setState({
      level: 9,
      credits: 100_000,
      isotopes: 100_000,
      construction: {
        ...(state.construction as object),
        stock: { filter: 6, cable: 6, sealant: 6 },
      },
      now: Math.floor(Date.now() / 1000),
    });
  });
  // Состояния строек пересчитываются шагом времени, а не присвоением уровня.
  await page.waitForTimeout(900);
}

test('в открытой панели ни одна кнопка не накрыта и не за краем', async ({ page }) => {
  // Отдельная проверка, и это не дубль соседних.
  //
  // «Помещается в экран» и «не накрыта» — разные свойства, и панель ломала оба
  // сразу. У стройки к чек-листу добавились кнопки докупки модуля за изотопы,
  // панель переросла экран, и ее крестик закрытия — он по замыслу выступает на
  // 34 точки ВЫШЕ рамки панели — оказался на координате y = -17 на десктопе и
  // под шапкой на телефоне, где та переносится на две строки. Панель стало
  // нечем закрыть.
  //
  // Корень был не в высоте, а в слоях: затемнение модалки лежало ниже шапки и
  // ниже выключателя звука. Модалка, которую перекрывает то, что под ней, — не
  // модалка. Тот же класс, из-за которого выключатель однажды накрыл «Играть».
  await openGame(page);
  await seedTallPanels(page);

  for (const name of ['Склад', 'Фабрика', 'Дрон', 'Шаттл', 'Стройка']) {
    await page.getByRole('button', { name, exact: true }).click();
    await expect(page.getByRole('button', { name: 'Закрыть' }).first()).toBeVisible();

    const covered = await coveredButtons(page);
    expect(covered, `панель «${name}»: накрыто`).toEqual([]);

    const off = await offscreenButtons(page);
    expect(off, `панель «${name}»: за краем ${JSON.stringify(off)}`).toEqual([]);

    await page.getByRole('button', { name: 'Закрыть' }).first().click();
  }
});

test('указатель цели стоит на своей кнопке', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Играть' }).click();
  await expect(page.getByTestId('goal-bar')).toBeVisible();

  // Стрелка — псевдоэлемент, отдельного узла у нее нет. Считаем ее острие по
  // тем же числам, которыми она нарисована: смещение вверх и высота треугольника.
  //
  // Дефект Д-32: хаб на телефоне переносится на две строки, стрелка стояла в
  // промежутке между рядами и читалась указателем на кнопку ВЕРХНЕГО ряда, а
  // кольцо было на нижней. Проверка требует, чтобы острие принадлежало той же
  // кнопке, что и кольцо.
  const verdict = await page.evaluate(() => {
    const ring = document.querySelector('.goal-point');
    if (ring === null) return 'кольца цели нет на экране';

    const style = getComputedStyle(ring, '::after');
    const offset = Math.abs(Number.parseFloat(style.top || '0'));
    const half = Number.parseFloat(style.borderTopWidth || '0');
    const r = ring.getBoundingClientRect();

    const tip_x = r.left + r.width / 2;
    const tip_y = r.top - offset + half * 2;

    const hit = document.elementFromPoint(tip_x, tip_y);
    if (hit === null) return `острие в пустоте: ${Math.round(tip_x)}, ${Math.round(tip_y)}`;
    // Годится только попадание В САМО кольцо или в его содержимое. Попадание в
    // родителя — это промах: острие висит в промежутке между кнопками, и
    // читается оно тем, что стоит рядом. Первая редакция проверки считала
    // родителя своим и не краснела на исходном дефекте.
    if (ring === hit || ring.contains(hit)) return 'ok';
    return `острие попадает в «${(hit.textContent ?? '').trim().slice(0, 20)}», а кольцо на «${(ring.textContent ?? '').trim().slice(0, 20)}»`;
  });

  expect(verdict).toBe('ok');
});
