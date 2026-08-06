/**
 * Доказательства прогона 4, уровень стора и точки входа.
 *
 * Хранилище и `window` здесь свои: `entry.ts` читает адрес страницы и стирает
 * ключ сейва, а без подставного окна обе ветки в node просто не выполняются.
 * Сейв поднимается настоящим `persist.rehydrate()`, а не присваиванием в стор, —
 * иначе доказательство проверяло бы не тот путь, которым игра открывается.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CREDITS_START } from '../../domain/config/economy';
import { reservedOf } from '../../domain/warehouse';
import { DEMO_LEVEL } from '../demo';
import { chooseEntryState } from '../entry';
import {
  createInitialState,
  SAVE_KEY,
  SAVE_VERSION,
  type SaveBackend,
  saveStorage,
  useGame,
} from '../gameStore';

const T0 = 1_000_000;
const PLAYER_LEVEL = 12;
const PLAYER_CREDITS = 9999;

let clock = T0;
let file = new Map<string, string>();

const backend: SaveBackend = {
  getItem: (key) => file.get(key) ?? null,
  setItem: (key, value) => {
    file.set(key, value);
  },
  removeItem: (key) => {
    file.delete(key);
  },
};

const s = () => useGame.getState();

/** Подставное окно: адрес страницы и `history.replaceState`, больше ничего. */
function openAt(search: string): void {
  const href = `http://localhost/${search}`;
  (globalThis as unknown as { window: unknown }).window = {
    location: { search, href },
    history: {
      replaceState: (_a: unknown, _b: unknown, url: string) => {
        const next = new URL(url);
        const w = (
          globalThis as unknown as { window: { location: { search: string; href: string } } }
        ).window;
        w.location.search = next.search;
        w.location.href = url;
      },
    },
  };
  // `window.history.replaceState` зовется через глобальный `window`, а не через
  // `globalThis.history` — обе ссылки должны вести в один объект.
  const w = (globalThis as unknown as { window: { history: unknown } }).window;
  (globalThis as unknown as { history: unknown }).history = w.history;
}

function currentSearch(): string {
  return (globalThis as unknown as { window: { location: { search: string } } }).window.location
    .search;
}

/** Сейв человека, который уже играл. Пишется в ключ до загрузки страницы. */
function playerSave(): string {
  return JSON.stringify({
    state: {
      ...createInitialState(),
      level: PLAYER_LEVEL,
      credits: PLAYER_CREDITS,
      isotopes: 321,
    },
    version: SAVE_VERSION,
  });
}

/** Загрузка страницы: стор поднимает то, что лежит в ключе. */
async function boot(saved: string | null): Promise<void> {
  useGame.setState(createInitialState());
  if (saved === null) backend.removeItem(SAVE_KEY);
  else backend.setItem(SAVE_KEY, saved);
  await useGame.persist.rehydrate();
}

function savedState(): Record<string, unknown> {
  const raw = file.get(SAVE_KEY);
  expect(raw, 'сейв обязан быть в хранилище').toBeDefined();
  return (JSON.parse(raw as string) as { state: Record<string, unknown> }).state;
}

beforeEach(() => {
  file = new Map();
  clock = T0;
  vi.spyOn(Date, 'now').mockImplementation(() => clock * 1000);
  useGame.persist.setOptions({ storage: saveStorage(backend) });
  useGame.setState(createInitialState());
});

afterEach(() => {
  vi.restoreAllMocks();
  (globalThis as unknown as { window?: unknown }).window = undefined;
});

/**
 * Д-26. `?fresh=1` не начинает игру заново, если сейв уже поднят.
 *
 * Что видит игрок: открывает ссылку `.../?fresh=1` — документированный способ
 * начать с нуля («хочет с нуля -> `?fresh=1` стирает сейв и начинает
 * канонический старт», заголовок `entry.ts`) — и получает свою же старую
 * колонию. Метка при этом снимается с адреса, подпись показа не рисуется, и
 * человек остается уверен, что сброс произошел.
 *
 * Где ломается: `chooseEntryState` чистит ХРАНИЛИЩЕ (`persist.clearStorage`), но
 * не трогает СОСТОЯНИЕ. К этому моменту сейв уже поднят синхронно при импорте
 * стора, поэтому следующая строка (`setState({ now })`) записывает поднятый
 * прогресс обратно в только что очищенный ключ. Сброс отменяет сам себя.
 *
 * Почему кнопка «Начать с нуля» при этом работает: `startFresh` успевает удалить
 * ключ ДО перезагрузки, то есть прикрывает дефект снаружи. Любой другой путь к
 * тому же адресу — ссылка, закладка, второй `goto('/?fresh=1')` в браузерной
 * проверке — падает в дыру. Весь набор e2e стоит на этом адресе.
 */
describe('Д-26: ?fresh=1 не сбрасывает уже поднятый сейв', () => {
  it('канонический старт вместо старой колонии', async () => {
    await boot(playerSave());
    // Предпосылка: сейв действительно поднялся, иначе проверять нечего.
    expect(s().level, 'сейв игрока обязан подняться').toBe(PLAYER_LEVEL);

    openAt('?fresh=1');
    const mode = chooseEntryState();

    expect(mode).toBe('fresh');
    expect(s().level, '?fresh=1 обязан начать канонический старт').toBe(1);
    expect(s().credits, 'кредиты канонического старта').toBe(CREDITS_START);
  });

  it('в хранилище после сброса лежит канонический старт, а не прежний прогресс', async () => {
    await boot(playerSave());
    expect(s().level).toBe(PLAYER_LEVEL);

    openAt('?fresh=1');
    chooseEntryState();

    expect(savedState().level, 'сброшенный сейв не имеет права воскреснуть на F5').toBe(1);
  });
});

/**
 * Д-27. `?demo=1` остается в адресе, и каждая перезагрузка пересобирает показ
 * поверх всего, что человек успел сделать.
 *
 * Что видит игрок: открыл ссылку с показом, потыкал колонию — собрал урожай,
 * отправил дрона, скипнул рейс. Нажал F5 (или вернулся во вкладку, которую
 * браузер выгрузил) — и все действия исчезли, колония снова та же самая.
 * Собственный сейв, если он был, к этому моменту уже перезаписан показом.
 *
 * Где ломается: ветка `fresh` снимает свою метку с адреса немедленно и
 * объясняет почему — «Иначе обновление страницы стирало бы прогресс каждый раз».
 * Ветка `demo` метку не снимает, хотя стирает прогресс ровно так же и вдобавок
 * пишет показ в ключ сейва.
 */
describe('Д-27: метка показа не снимается с адреса', () => {
  it('метка ?demo=1 обязана исчезнуть из адреса, как исчезает ?fresh=1', async () => {
    await boot(null);
    openAt('?demo=1');

    expect(chooseEntryState()).toBe('demo');
    expect(
      new URLSearchParams(currentSearch()).has('demo'),
      'метка показа обязана сниматься с адреса — иначе F5 стирает прогресс',
    ).toBe(false);
  });

  it('перезагрузка по тому же адресу не имеет права стирать сделанное', async () => {
    await boot(null);
    openAt('?demo=1');
    chooseEntryState();

    // Человек поиграл в показе: продал излишки, набрал кредитов.
    useGame.setState({ credits: 424_242 });

    // F5 по тому же адресу — второй заход в тот же документ.
    chooseEntryState();

    expect(s().credits, 'показ не имеет права пересобираться поверх сделанного').toBe(424_242);
  });

  it('сейв игрока переживает заход по ссылке с показом', async () => {
    await boot(playerSave());
    expect(s().level).toBe(PLAYER_LEVEL);

    openAt('?demo=1');
    chooseEntryState();

    // Показ на экране — законно, его попросили меткой. Но собственный прогресс
    // человека обязан остаться, к нему должно быть куда вернуться.
    expect(savedState().level, 'показ не имеет права затирать сейв игрока').toBe(PLAYER_LEVEL);
    expect(DEMO_LEVEL).not.toBe(PLAYER_LEVEL);
  });
});

/**
 * Д-28. Сейв, записанный до появления `qty_purchased`, принимается как свой и
 * запирает товар на складе навсегда.
 *
 * Что видит игрок: до обновления он нажал «Погрузить 2», потом «Докупить 1» и
 * закрыл вкладку, не добив последний отсек. После обновления он добивает рейс —
 * шаттл улетает, а две водоросли остаются на складе намертво зарезервированными:
 * отсека больше нет, снять резерв нечем, вместимость потеряна до конца игры.
 * Это в точности Д-1 и Д-18, вернувшиеся через файл сейва.
 *
 * Где ломается: `ShuttleSlot.qty_purchased` объявлено необязательным с
 * объяснением «сейв, записанный до этой правки, его не несет. Отсутствие
 * читается как ноль — то есть прежнее поведение обычной погрузки». Для отсека,
 * закрытого докупкой, прежнее поведение прямо противоположно: там докупка БЫЛА.
 * `stockedIn` насчитывает уехать больше, чем лежит в `reserved`, `shipReserved`
 * молча отказывает целиком, и не уезжает ничего.
 *
 * `SAVE_VERSION` при этом остался единицей — вопреки собственному правилу стора:
 * «Растет на любой правке формы состояния, после которой старый файл больше не
 * описывает игру целиком».
 *
 * Проверяется здесь не версия, а последствие: после отправки рейса на складе не
 * должно оставаться резерва без владельца. Оба честных лечения — поднять версию
 * (старый сейв выбрасывается, рейса нет, резерва нет) или считать складскую
 * часть по факту резерва — дают зеленый.
 */
describe('Д-28: сейв старого формата запирает резерв навсегда', () => {
  /** Рейс в ORDER: отсек 0 закрыт «погрузить + докупить», отсек 1 открыт. */
  function legacySave(): string {
    const base = createInitialState();
    return JSON.stringify({
      state: {
        ...base,
        level: 7,
        shuttle_arrivals: 4,
        warehouse: {
          capacity: 50,
          cells: {
            // Две водоросли легли в отсек погрузкой и держат резерв.
            algae: { qty: 2, reserved: 2 },
            soy: { qty: 5, reserved: 0 },
          },
        },
        shuttle: {
          state: 'ORDER',
          trip_min: 60,
          departed_at: 0,
          arrives_at: 0,
          cooldown_until: 0,
          is_first_trip: false,
          arrival_no: 5,
          slots: [
            {
              idx: 0,
              good_id: 'algae',
              qty_required: 3,
              qty_filled: 3,
              // Поля `qty_purchased` в старом формате не существовало.
              filled_by: 'purchase',
              reward: null,
              collected: false,
              floor_forced: false,
            },
            {
              idx: 1,
              good_id: 'soy',
              qty_required: 5,
              qty_filled: 0,
              filled_by: null,
              reward: null,
              collected: false,
              floor_forced: false,
            },
          ],
        },
      },
      version: SAVE_VERSION,
    });
  }

  it('после отправки на складе не остается резерва без владельца', async () => {
    await boot(legacySave());

    // Добиваем последний отсек — рейс уходит сам (ТЗ шаттла 2.3).
    s().loadShuttleSlot(1);

    const w = s().warehouse;
    expect(reservedOf(w, 'algae'), 'рейса больше нет — снять этот резерв игроку нечем').toBe(0);
  });
});
