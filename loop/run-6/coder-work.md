# Прогон 6 — работа кодера

Задача: закрыть три находки судьи (run-5/judge.md) — И-13 (минус 30), И-10
(минус 20), property-тест докупки модуля (минус 10). Правила: тест сначала,
конфиг не трогать, комментарии по-русски без «ё».

## План (от дешевого к дорогому, порядок координатора)

1. [ ] Property-тест докупки строй-модуля за изотопы
   - Файл: `src/state/__tests__/economy-invariants.property.test.ts`
   - Добавить Action `buy_module` (kind_idx стройки, module_idx) → `s.buyModulesFor(...)`
   - Добавить инвариант: `purchased[module_id]` никогда не отрицателен и
     `stock[id] + purchased[id]` никогда не превышает рецепт стройки (нет
     переплаты/овербая через buyModules/modulePurchasePrice/startBuild).
   - Красный тест сначала: временно проверить, что без правки harness тест не
     покрывает buyModules вовсе (баг = отсутствие вызова, не логики) —
     значит "красный до правки" тут не в смысле падающего теста, а в смысле
     отсутствия покрытия. Подтверждаю через grep, что buy_module нигде не
     фигурирует до правки.

2. [ ] И-10 реализуемость заказа (ORDER_FEASIBILITY_DEADLINE_SHARE)
   - Добавить `totalProductionMinutes` в `src/domain/rushcost.ts` (группировка
     по required_building, максимум по группам — канон 1.4).
   - Добавить `ACHIEVABILITY_CHECK: Record<Mechanic, boolean>` в
     `config/economy.ts` (drone:false, shuttle:true, liner:true) — имя из
     канона 1.7.
   - `shuttle.ts`: после сборки slots — если ACHIEVABILITY_CHECK.shuttle и не
     is_first_trip (FTUE — отдельное решение, см. ниже), считать бюджет
     `flightTimerMin(level) * ORDER_FEASIBILITY_DEADLINE_SHARE` и звать
     `rebalanceForAchievability` (урезание qty до floor, потом замена товара).
   - **Решение по FTUE, требует записи в реестр**: achievability не
     применяется к is_first_trip. Причина: FTUE обязателен «ровно 3 отсека,
     все easy», пул на 5 уровне — ровно 3 кропа (algae/soy/mushrooms), их
     суммарное время по канону (группировка по required_building=null, все
     кропы — одна группа) на пустом складе ~35 мин против бюджета FTUE-таймера
     12*0.6=7.2 мин — недостижимо структурно, менять нечем (пул исчерпан).
     Записать в spec-prototype-build.md раздел 8, пункт 24.
   - Тесты: домен-тест на totalProductionMinutes (группировка, параллельность
     зданий) + тест на генератор шаттла (сценарий, где без ребаланса бюджет
     нарушен, после — либо укладывается, либо строго уменьшился и товары
     остались на floor).

3. [ ] И-13 изоляция дефицита (DeficitLock)
   - Новый файл `src/domain/deficitlock.ts`: DeficitLock, DeficitLockState
     (Record<GoodId, DeficitLock>), isDeficitLockedByOtherMechanic,
     registerDeficitLock, releaseDeficitLock. Константы
     DEFICIT_LOCK_TTL/DEFICIT_LOCK_TTL_MAX в config/economy.ts.
   - drone.ts: generateOrder принимает ctx.deficit_locks?/ctx.now? (опционально,
     дефолт {} и 0 — обратная совместимость со старыми тестами). При выборе
     дефицитной позиции — если locked другой механикой, downgrade как при
     исчерпанном бюджете дефицита. После финального выбора positions — для
     каждой !easy позиции звать registerDeficitLock. Экспорт
     releaseOrderDeficitLocks(slot, locks, now) для стора (terminal: send/discard).
   - shuttle.ts: то же самое для generateTrip/slots, release — внутри depart()
     (терминальное событие шаттла — сам факт отправки).
   - gameStore.ts: новое поле `deficit_locks: DeficitLockState` в GameState,
     createInitialState, SAVED_OBJECT_KEYS, isSave, SAVE_VERSION 4→5 (новое
     поле формы сейва). Прокинуть в makeOrder/makeTrip ctx, release в
     sendOrderAt/discardOrderAt и внутри applyDeparture/loadSlot(shuttle).
   - spec-drift.test.ts: сузить группу `^(DEFICIT|SKIP|TIMER|ROLL)_` — убрать
     DEFICIT (теперь реализовано), оставить SKIP_/TIMER_/ROLL_/SLOT_ALREADY_.
     Сузить `^ACHIEVABILITY_` до `^ACHIEVABILITY_BUDGET` (ACHIEVABILITY_CHECK
     теперь реализован, ACHIEVABILITY_BUDGET_MIN — лайнер, все еще отложен).
   - economy-invariants.property.test.ts: добавить `deficit_locks: {}` в
     reset().
   - Тесты: unit на domain/deficitlock.ts (register/release/expiry/TTL_MAX
     clamp), интеграционный тест — драка дрона и шаттла за один дефицитный
     товар (drone заказ держит лок → шаттл не берет тот же товар в дефицит,
     после release шаттл может).

## Если И-13 не влезет целиком
Останавливаюсь и докладываю координатору, что сделано, что нет — не
имитирую целое частичной реализацией.

## Лог по узлам (заполняется по ходу)

### Узел 1 — property-тест докупки модуля — СДЕЛАНО
- Файл: `src/state/__tests__/economy-invariants.property.test.ts`.
- Добавлен Action `buy_module` (kind_idx стройки x module_idx), вызывает
  `s.buyModulesFor`.
- Добавлен инвариант в `checkInvariants`: `build.purchased[id]` не
  отрицателен и не превышает `recipeFor(build.kind)[id]`. Сравнение
  СТРОГО с рецептом, не с `stock+purchased` — первая версия сравнивала с
  суммой остатка склада (общего на все стройки) и докупки, и была неверной:
  склад модулей общий, вторая стройка может законно поднять его выше
  потребности первой, это не переплата. Поймано до коммита, не тестом.
- Добавлена веха `module_purchased` в milestones + в список обязательных
  вех «прогон дошел до значимого состояния» — без нее свойство могло бы
  зеленеть, ни разу не вызвав докупку.
- `npx vitest run src/state/__tests__/economy-invariants.property.test.ts`
  — 3/3 зелено (buyModules уже был реализован верно per судья, тест это
  подтверждает как регрессию, а не как фикс).

### Узел 2 — И-10 реализуемость заказа — В РАБОТЕ
