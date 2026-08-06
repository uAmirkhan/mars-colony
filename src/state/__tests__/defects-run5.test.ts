/**
 * Доказательства прогона 5. Ничего здесь не чинится — тесты фиксируют
 * расхождение кода с каркасом и красные они намеренно (в гейт не входят).
 *
 * Оба дефекта — в коде, написанном 2026-08-06 в рамках докупки строй-модуля
 * за изотопы (второй канал И-1, находка Н-5, реестр [[spec-prototype-build]]
 * раздел 8 пункт 19) и подъема SAVE_VERSION до 4.
 */

import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CREDITS_START } from '../../domain/config/economy';
import {
  createInitialState,
  SAVE_KEY,
  type SaveBackend,
  saveStorage,
  useGame,
} from '../gameStore';

const T0 = 1_000_000;
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

beforeEach(() => {
  file = new Map();
  vi.spyOn(Date, 'now').mockImplementation(() => T0 * 1000);
  useGame.persist.setOptions({ storage: saveStorage(backend) });
  useGame.setState(createInitialState());
});

async function bootWith(saved: string | null): Promise<void> {
  useGame.setState(createInitialState());
  if (saved === null) backend.removeItem(SAVE_KEY);
  else backend.setItem(SAVE_KEY, saved);
  await useGame.persist.rehydrate();
}

/* ------------------------------------------------------------------------ *
 * Дефект 1: `startConstruction` мутирует общий объект `BuildSlot.purchased`
 * по ссылке — старый снимок состояния молча меняется задним числом.
 *
 * `buyModulesFor` (соседнее действие с тем же полем) сознательно делает
 * ГЛУБОКУЮ копию `purchased` — докстринг там же объясняет зачем: «Мутация
 * такого объекта прошла бы мимо подписчиков — экран не перерисовался бы, а
 * сейв получил бы число, которого игрок на экране не видел». Тот же риск
 * актуален для `startConstruction`: `startBuild` внутри вычитает докупленное
 * из `build.purchased[id]` ПО МЕСТУ (construction.ts). `startConstruction`
 * копирует стройки мелко — `s.construction.builds.map((b) => ({ ...b }))` —
 * и `purchased` остается ОБЩЕЙ ссылкой со старым (уже показанным игроку и,
 * возможно, отрисованным где-то еще) объектом состояния.
 *
 * Наблюдаемое следствие — «тихая порча»: любой код, успевший захватить
 * ссылку на build ДО вызова `startConstruction` (замыкание обработчика,
 * мемоизированный селектор, оверлей подтверждения с уже отрисованными
 * числами), видит, как число докупленных модулей меняется под ним без
 * единого сигнала о том, что состояние вообще изменилось.
 * ------------------------------------------------------------------------ */
describe('Дефект: startConstruction мутирует чужой снимок build.purchased', () => {
  it('старый объект build.purchased, захваченный до старта стройки, не должен измениться', () => {
    useGame.setState({ level: 7, isotopes: 100_000, credits: 100_000 });
    s().tick(T0);

    // Докупаем 1 панель, оставляя остальной рецепт (frame, sealant) на складе —
    // чтобы purchased стал непустым объектом до старта стройки.
    useGame.setState((st) => ({
      construction: {
        ...st.construction,
        stock: { ...st.construction.stock, frame: 5, sealant: 7, panel: 5 },
      },
    }));
    s().buyModulesFor('habitat_block', 'panel');

    const buildBefore = s().construction.builds.find((b) => b.kind === 'habitat_block');
    if (!buildBefore) throw new Error('стройка не найдена');
    const purchasedSnapshot = { ...buildBefore.purchased };
    expect(purchasedSnapshot.panel).toBe(1);

    s().startConstruction('habitat_block');

    // buildBefore — объект, живший в состоянии ДО этого действия. Он не
    // участвует в новом состоянии стора и обязан остаться таким, каким был.
    expect(buildBefore.purchased).toEqual(purchasedSnapshot);
  });
});

/* ------------------------------------------------------------------------ *
 * Дефект 2: проверка версии сейва завязана на `typeof version === 'number'`
 * (zustand/middleware.js). Конверт с нечисловым или отсутствующим `version`
 * не считается «другой версией» и НЕ идет через `migrate` (который у нас
 * всегда возвращает undefined и тем самым отбрасывает чужой сейв целиком) —
 * он идет прямо в `merge`. Единственная оставшаяся защита — проверка формы
 * (`isSave`), а она проверяет только ВЕРХНИЙ уровень ключей и открыто не
 * лезет во вложенные структуры (докстринг: «форма, а не вся модель»).
 *
 * Итог: конверт, у которого поле `version` испорчено или отсутствует, но
 * верхний уровень совпадает с текущей формой (все ключи `SAVED_KEYS` на
 * месте), проходит как «свой», даже если по документу обязан быть выброшен
 * как чужая версия. Комментарий у SAVE_VERSION обещает обратное: «Сейв
 * прошлой версии выбрасывается целиком... тихо принять состояние неизвестной
 * формы хуже потери прогресса».
 * ------------------------------------------------------------------------ */
describe('Дефект: проверка SAVE_VERSION обходится нечисловым/отсутствующим version', () => {
  it('version строкой не считается несовпадением версии — сейв принимается', async () => {
    const alien = JSON.stringify({
      state: { ...createInitialState(), credits: 999_999, level: 12 },
      version: '3',
    });

    await bootWith(alien);

    expect(s().level).toBe(1);
    expect(s().credits).toBe(CREDITS_START);
  });

  it('конверт вовсе без поля version принимается как совместимый', async () => {
    const alien = JSON.stringify({
      state: { ...createInitialState(), credits: 999_999, level: 12 },
    });

    await bootWith(alien);

    expect(s().level).toBe(1);
    expect(s().credits).toBe(CREDITS_START);
  });
});
