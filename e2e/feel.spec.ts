import { expect, type Locator, type Page, test } from '@playwright/test';
import { diffRatio } from './frame-diff';

/**
 * Доказательство ощущения, а не рассказ о нем.
 *
 * Правило проекта: модульные тесты не рендерят, и однажды приложение не
 * отрисовывалось вообще при ста сорока зеленых тестах. Отклик на нажатие —
 * ровно та вещь, про которую «я добавил анимацию» проверить нечем: она либо
 * видна на кадре, либо ее нет.
 *
 * Поэтому здесь три канала доказательства и ни одного рассуждения:
 *   1. сравнение кадров до и после действия (как в mars-unity/check-interaction.mjs);
 *   2. наличие в DOM элемента вылетевшей цифры и плашки отказа;
 *   3. офлайн-рендер звука с измерением пика и RMS — ресурс в этом проекте
 *      уже дважды молча не доезжал до сборки, и звук такой же кандидат.
 */

/** Ниже этого кадры считаем одинаковыми. */
const SAME_MAX = 0.004;
/** Выше этого реакция считается видимой. */
const REACTION_MIN = 0.01;

type Store = {
  getState: () => Record<string, unknown>;
  setState: (patch: Record<string, unknown>) => void;
};

interface SfxSeam {
  played: Array<{ name: string; at: number; audible: boolean }>;
  names: string[];
  render: (name: string) => Promise<{ peak: number; rms: number }>;
  state: () => string;
}

async function openGame(page: Page) {
  await page.goto('/?fresh=1');
  await page.getByRole('button', { name: 'Играть' }).click();
}

interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * Область вокруг элемента, зафиксированная числами.
 *
 * Именно числами, а не локатором: элемент после действия перестает быть собой
 * (грядка теряет `slot-ready` ровно потому, что урожай собран), и снимать «до»
 * и «после» одним локатором нельзя — второй снимок ждал бы элемент, которого
 * больше нет. Сравнивать надо один и тот же кусок экрана.
 */
async function rectOf(page: Page, target: Locator, pad = 44): Promise<Rect> {
  const box = await target.boundingBox();
  if (!box) throw new Error('элемент не на экране');
  const size = page.viewportSize() ?? { width: 1280, height: 720 };
  return {
    x: Math.max(0, box.x - pad),
    y: Math.max(0, box.y - pad),
    width: Math.min(size.width, box.width + pad * 2),
    height: Math.min(size.height, box.height + pad * 2),
  };
}

function shot(page: Page, clip: Rect): Promise<Buffer> {
  return page.screenshot({ clip });
}

function sfx(page: Page) {
  return {
    played: () =>
      page.evaluate(() => (window as unknown as { __sfx: SfxSeam }).__sfx.played.slice()),
    state: () => page.evaluate(() => (window as unknown as { __sfx: SfxSeam }).__sfx.state()),
    clear: () =>
      page.evaluate(() => {
        (window as unknown as { __sfx: SfxSeam }).__sfx.played.length = 0;
      }),
  };
}

test('нажатие отвечает на кадре, а не через триста миллисекунд', async ({ page }) => {
  await openGame(page);
  const button = page.getByRole('button', { name: 'Склад' });
  await expect(button).toBeVisible();

  // Опорный замер: без действий кадр обязан стоять. Иначе любая «реакция»
  // ниже — это просто шум перерисовки.
  const region = await rectOf(page, button);
  const idle_a = await shot(page, region);
  await page.waitForTimeout(320);
  const idle_b = await shot(page, region);
  const idle = await diffRatio(idle_a, idle_b);
  expect(idle, `покой должен быть покоем (${idle.toFixed(4)})`).toBeLessThan(SAME_MAX);

  // Нажатие удерживается: так кадр снимается заведомо внутри отклика, а не
  // после него. Игрок видит ровно это состояние в момент касания.
  const box = await button.boundingBox();
  if (!box) throw new Error('кнопка не на экране');
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  const pressed = await shot(page, region);
  await page.mouse.up();

  const reaction = await diffRatio(idle_b, pressed);
  expect(
    reaction,
    `кнопка обязана видимо просесть под пальцем (${reaction.toFixed(4)} против покоя ${idle.toFixed(4)})`,
  ).toBeGreaterThan(REACTION_MIN);

  // И тот же жест обязан дать звук нажатия.
  const played = await sfx(page).played();
  expect(played.map((p) => p.name)).toContain('press');
});

test('сбор урожая: цифра вылетает из грядки, кадр меняется', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));

  await openGame(page);

  // Созревшая грядка ставится швом: ждать реальный цикл в браузерной проверке
  // нечестно по времени и незачем — проверяется отклик, а не таймер.
  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const now = Math.floor(Date.now() / 1000);
    store.setState({
      now,
      credits: 5000,
      warehouse: { capacity: 500, cells: {} },
      fields: [
        { idx: 0, state: 'READY', good_id: 'algae', ends_at: now - 10 },
        { idx: 1, state: 'EMPTY', good_id: null, ends_at: 0 },
      ],
    });
  });

  const field = page.locator('.slot-ready').first();
  await expect(field).toBeVisible();

  const region = await rectOf(page, field, 70);
  const before = await shot(page, region);
  await field.click();
  // Ждем ПОЯВЛЕНИЯ цифры, а не фиксированные 120 мс.
  //
  // Раньше цифра жила 900 мс и любая задержка укладывалась в ее срок. Теперь
  // спека дает 300 (находка Н-14), и фиксированная пауза стала лотереей: на
  // загруженной машине шесть параллельных браузеров съедали окно целиком, и
  // проверка падала через раз, ничего не сообщая о самой игре. Опрос по кадрам
  // ловит цифру в момент рождения и не зависит от того, сколько живет эффект.
  await page.waitForFunction(() => document.querySelectorAll('.fx-float').length > 0, null, {
    timeout: 5000,
    polling: 16,
  });

  // Снимок эффектов берется ОДНИМ обращением и ПЕРЕД кадром, а не серией
  // ожиданий с ретраями после него. Причина в сроках жизни: точка урожая
  // живет 680 мс, цифра 960 мс, а `toHaveText` ждет до пяти секунд и на
  // загруженной машине опрашивает уже пустой экран. Снимок экрана — самая
  // медленная операция в тесте, и все, что стоит за ним, рискует опоздать.
  // Кадру это не мешает: сбор меняет саму грядку навсегда, а не только на
  // время эффекта.
  const fx = await page.evaluate(() => ({
    floats: [...document.querySelectorAll('.fx-float')].map((e) => e.textContent ?? ''),
    flies: document.querySelectorAll('.fx-fly').length,
  }));
  const after = await shot(page, region);

  const ratio = await diffRatio(before, after);
  expect(ratio, `кадр после сбора обязан отличаться (${ratio.toFixed(4)})`).toBeGreaterThan(
    REACTION_MIN,
  );

  // Цифра существует в DOM и несет число, а не пустую строку. Ищется по форме,
  // а не по «первой в списке»: сбор трогает и опыт, и склад, и наблюдатель
  // рождает свои цифры у счетчиков HUD раньше, чем обертка действия — свою у
  // грядки. «Первая» — это порядок рождения, а проверяется наличие.
  expect(fx.floats, 'над грядкой обязана вылететь цифра прибавки').toEqual(
    expect.arrayContaining([expect.stringMatching(/^\+\d+$/)]),
  );

  // И точки полетели к счетчику склада.
  expect(fx.flies, 'иконки урожая обязаны улететь к счетчику склада').toBeGreaterThan(0);

  const played = await sfx(page).played();
  expect(played.map((p) => p.name)).toContain('success');
  expect(errors).toEqual([]);
});

test('отказ виден и слышен: не хватает кредитов', async ({ page }) => {
  await openGame(page);

  // Отказ надо ЗАСЛУЖИТЬ. Пустой кошелек сам по себе отказа не дает: домен
  // держит И-15 и вытаскивает игрока из безвыходного положения — посев при
  // нуле кредитов, пустом складе и пустых грядках проходит за счет колонии
  // (`isPlantingSoftlocked`). Поэтому рядом ставится растущая грядка: выход
  // у игрока есть, спасать его не от чего, и нехватка кредитов становится
  // настоящим отказом, а не спасением.
  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    const now = Math.floor(Date.now() / 1000);
    store.setState({
      now,
      credits: 0,
      warehouse: { capacity: 500, cells: {} },
      fields: [
        { idx: 0, state: 'EMPTY', good_id: null, ends_at: 0 },
        { idx: 1, state: 'GROWING', good_id: 'algae', ends_at: now + 600 },
      ],
    });
  });

  await page.locator('.slot').first().click();
  const sow = page.getByRole('button', { name: /^Посеять/ }).first();
  await expect(sow).toBeVisible();

  await sfx(page).clear();
  const before = await page.screenshot();
  await sow.click();

  // Плашка с причиной. Это и есть весь канал объяснения отказа: до этой
  // работы отказ не давал ни звука, ни движения, и игрок читал его как
  // поломку игры.
  const toast = page.getByTestId('toast-warn');
  await expect(toast).toBeVisible();
  await expect(toast).toHaveText('Не хватает кредитов');

  await page.waitForTimeout(120);
  const after = await page.screenshot();
  const ratio = await diffRatio(before, after);
  expect(ratio, `отказ обязан быть виден на кадре (${ratio.toFixed(4)})`).toBeGreaterThan(
    0.002,
  );

  const played = await sfx(page).played();
  expect(played.map((p) => p.name), 'у отказа обязан быть свой звук').toContain('deny');
  // Отказ не имеет права звучать успехом.
  expect(played.filter((p) => p.name === 'success')).toHaveLength(0);
});

test('звук доехал до сборки и не является тишиной', async ({ page }) => {
  await openGame(page);

  // Контекст разблокируется только жестом. Проверяем, что жест его открыл.
  await page.getByRole('button', { name: 'Склад' }).click();
  expect(await sfx(page).state()).toBe('running');

  const played = await sfx(page).played();
  expect(
    played.some((p) => p.audible),
    'сигнал обязан реально уйти в звуковой граф, а не только в журнал',
  ).toBe(true);

  // Главное: те же голоса, отрендеренные офлайн, дают ненулевой сигнал.
  // Молчащий синтез — единственный режим отказа, который остался у звука,
  // раз файлов в проекте нет.
  const levels = await page.evaluate(async () => {
    const seam = (window as unknown as { __sfx: SfxSeam }).__sfx;
    const out: Array<{ name: string; peak: number; rms: number }> = [];
    for (const name of seam.names) {
      const level = await seam.render(name);
      out.push({ name, ...level });
    }
    return out;
  });

  expect(levels).toHaveLength(4);
  for (const level of levels) {
    expect(level.peak, `голос «${level.name}» не должен быть тишиной`).toBeGreaterThan(0.01);
    expect(level.rms, `голос «${level.name}» слишком пуст`).toBeGreaterThan(0.001);
    // И не должен быть громким: тихо по умолчанию — часть требования.
    expect(level.peak, `голос «${level.name}» слишком громкий`).toBeLessThan(0.5);
  }
});

test('выключатель звука действительно выключает', async ({ page }) => {
  await openGame(page);
  const toggle = page.getByTestId('sound-toggle');
  await expect(toggle).toBeVisible();

  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-pressed', 'true');

  await sfx(page).clear();
  await page.getByRole('button', { name: 'Склад' }).click();

  const played = await sfx(page).played();
  expect(played.length, 'нажатие все равно фиксируется').toBeGreaterThan(0);
  expect(
    played.every((p) => !p.audible),
    'после выключения ни один сигнал не должен звучать',
  ).toBe(true);
});

test('счетчик докручивается, а не прыгает скачком', async ({ page }) => {
  await openGame(page);

  await page.evaluate(() => {
    const store = (window as unknown as { __game: Store }).__game;
    store.setState({
      credits: 0,
      warehouse: { capacity: 500, cells: { algae: { qty: 60, reserved: 0 } } },
    });
  });

  await page.getByRole('button', { name: 'Склад' }).click();

  // Дожидаемся покоя счетчика. Подстановка состояния сама по себе меняет
  // кредиты (стартовые 50 -> 0), и эта докрутка идет ВНИЗ. Без ожидания
  // сэмплер садился в ее середину и первым замером видел спуск, а не старт
  // продажи — проверка падала на честно работающем счетчике.
  await expect(page.locator('[data-fx-anchor="credits"] span')).toHaveText('0');

  // Сэмплер вешается ДО клика: докрутка длится 340 мс, поймать ее опросом
  // снаружи нельзя — каждый вызов в браузер стоит десятки миллисекунд.
  await page.evaluate(() => {
    const w = window as unknown as { __samples: string[] };
    const el = document.querySelector('[data-fx-anchor="credits"] span');
    // Опорное значение снимается СИНХРОННО, а не первым кадром анимации:
    // между установкой сэмплера и кликом лежит round-trip в браузер, и если
    // кадр за это время не пришел, первый замер оказывается уже внутри
    // докрутки. Тогда проверка падала на «начали не с нуля», хотя счетчик
    // вел себя ровно правильно.
    w.__samples = [el?.textContent ?? ''];
    const tick = () => {
      w.__samples.push(el?.textContent ?? '');
      if (w.__samples.length < 90) requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  });

  await page.getByRole('button', { name: 'Все' }).first().click();
  await page.waitForTimeout(900);

  const samples = await page.evaluate(
    () => (window as unknown as { __samples: string[] }).__samples,
  );
  const distinct = [...new Set(samples)];

  expect(
    distinct.length,
    `счетчик обязан пройти промежуточные значения, а не два (${distinct.join(', ')})`,
  ).toBeGreaterThanOrEqual(4);
  expect(distinct[0]).toBe('0');
});
