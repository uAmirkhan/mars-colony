/**
 * Прогон 6 — доказательства дефектов (ломатель), стор. Красные намеренно, в
 * гейт не входят (имя файла `defects-*.test.ts`).
 *
 * Дефект: `refreshSlotNow` (платный рефреш заказа дрона, `gameStore.ts`)
 * генерирует новый заказ через `makeOrder`, который мутирует `s.deficit_locks`
 * ПО МЕСТУ (тот же прием, что и у склада, см. докстринг `makeOrder`), но
 * итоговый `set()` этого действия не передает свежую ссылку `deficit_locks`,
 * в отличие от ВСЕХ остальных мест, где домен мутирует локи по месту
 * (`tick`, `sendOrderAt`, `discardOrderAt`, `applyDeparture` — каждое из них
 * явно комментирует «стор обязан отдать подписчикам новую ссылку»). Само
 * правило появилось в этом файле не случайно: `buyModulesFor`/`startConstruction`
 * несут отдельный докстринг про ровно тот же класс дефекта на другом поле
 * (`purchased`) — «мутация прошла бы мимо подписчиков, экран не
 * перерисовался бы». Здесь тот же класс дефекта воспроизведен на
 * `deficit_locks`, просто в другом действии.
 *
 * Наблюдаемо: контент `deficit_locks` меняется (лок реально регистрируется —
 * это видно через `useGame.getState()` ПОСЛЕ действия), но ссылка на объект
 * не меняется. Любой будущий подписчик, читающий стор через селектор
 * `s => s.deficit_locks` (как это уже принято для `warehouse`,
 * `construction` и других полей стора), не получит уведомления об изменении:
 * zustand сравнивает результат селектора через `Object.is`.
 */

import { describe, expect, it, vi } from 'vitest';
import { createInitialState, useGame } from '../gameStore';

const s = () => useGame.getState();

describe('Дефект: refreshSlotNow не дает деficit_locks новую ссылку после мутации', () => {
  it('ссылка на deficit_locks не меняется, хотя платный рефреш реально регистрирует новый лок', () => {
    // level20 c пустым складом: часть крупных культур (томаты/хлопок/кофе)
    // структурно небыстрые (>30 мин по И-8) и с высокой вероятностью попадают
    // в заказ как дефицитная позиция — фиксированный rng делает выбор
    // детерминированным для теста.
    vi.spyOn(Math, 'random').mockReturnValue(0.01);
    try {
      useGame.setState({ ...createInitialState(), level: 20, isotopes: 100_000 });
      s().tick(1_000_000);
      s().discardOrderAt(0);

      const before_ref = s().deficit_locks;
      const before_snapshot = JSON.stringify(before_ref); // {} — discardOrderAt уже все освободил

      s().refreshSlotNow(0);

      const after_ref = s().deficit_locks;
      const after_snapshot = JSON.stringify(after_ref);

      // Контент реально изменился — новый заказ зарегистрировал дефицитный лок.
      expect(after_snapshot).not.toBe(before_snapshot);
      // Дефект: это ТА ЖЕ ссылка, что и до действия (мутация по месту без
      // нового `set({ deficit_locks: {...} })`), хотя содержимое поменялось.
      // Правильное поведение — новая ссылка, как у `tick`/`sendOrderAt`/
      // `discardOrderAt`/`applyDeparture`.
      expect(after_ref).not.toBe(before_ref);
    } finally {
      vi.restoreAllMocks();
    }
  });
});
