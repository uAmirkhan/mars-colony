# Droproller — 3 инварианта, рабочий лог

Задача: анти-стокпайл на avg24h + guard activeNeed>0; двухэтапный выбор тир→предмет;
floor guarantee (И-11) на стройку, не на игрока.

## Статус: реализация домена готова, правлю тесты

### Сделано в droproller.ts (переписан целиком, Write)
- `WarehouseAvgState` + `createWarehouseAvg`/`advanceWarehouseAvg` — EMA с окном
  `WAREHOUSE_AVG_WINDOW_SEC` (новая константа в economy.ts, 24*60*60).
- `moduleWeight` теперь весит ТОЛЬКО внутри тира (`baseItemWeight = 1/pool.length`,
  без множителя TIER_WEIGHTS) — тир выбирается отдельно в `pickTier`. Антистокпайл
  переключен на `ctx.warehouse_avg_24h`, guard `needed > 0` добавлен и туда же.
- `DropContext.need` (агрегат) остался как было — используется pity/antistockpile.
  `DropContext.constructions: ConstructionNeed[]` — новое поле, по одной записи на
  AVAILABLE-стройку, с полями `deficit` (не `need`!), `arrivals_without_needed`,
  `last_floor_arrival`. Скаляры `arrivals_without_needed`/`last_floor_arrival` на
  верхнем уровне DropContext УДАЛЕНЫ.
- `floorGuaranteeAllowed` переименован в `floorGuaranteeAllowedFor(construction, ctx, rolled)`.
- `ArrivalRoll.next_arrivals_without_needed`/`next_last_floor_arrival` заменены на
  `next_constructions: ConstructionNeed[]`.
- `deficit` в `ConstructionNeed` считается снаружи (gameStore) через `missingFor(build, stock)`
  — то есть УЖЕ учитывает `purchased` (реестр spec-prototype-build.md 8.19, находка Н-5).
  Ссылку на пункт 19 нужно дописать в комментарий (TODO ниже).

### Сделано в economy.ts
- Добавлена `WAREHOUSE_AVG_WINDOW_SEC = 24*60*60`.

### Сделано в gameStore.ts (точечные Edit)
- SAVE_VERSION поднят с 3 (чужая правка, purchased) до 4, докстринг дополнен
  пунктом "4 —" рядом с существующими 2/3, не тронул чужой текст.
- SAVED_NUMBER_KEYS: убраны drop_without_needed/drop_last_floor.
- SAVED_OBJECT_KEYS: добавлены drop_floor_guarantee, warehouse_avg.
- GameState: drop_without_needed/drop_last_floor удалены, добавлены
  drop_floor_guarantee: FloorGuaranteeByConstruction, warehouse_avg: WarehouseAvgState.
- createInitialState обновлен.
- dropCtx() строит constructions[] из s.construction.builds.filter(AVAILABLE),
  deficit = missingFor(b, stock), счетчики из s.drop_floor_guarantee[b.kind].
- applyDeparture() пишет next_constructions обратно в drop_floor_guarantee по ключу.
- tick() продвигает warehouse_avg через advanceWarehouseAvg.

### TODO (в процессе, буду дописывать по ходу)
1. [x] Добавить ссылку на spec-prototype-build.md 8.19 в комментарий dropCtx()
       и в ConstructionNeed.deficit — сделано, решение "докупленное = покрытие
       И-11, need (агрегат/pity) остается валовым" как предложил координатор.
2. [x] droproller.test.ts переписан по блокам, Edit (не Write). 35/35 зелено
       (`npx vitest run src/domain/__tests__/droproller.test.ts`). Добавлены
       property-тесты fast-check на все три инварианта:
       - "И-7: анти-стокпайл > свойство: решение зависит только от warehouse_avg_24h"
       - "Веса тиров > свойство: pity/анти-стокпайл ОДНОГО предмета не двигают долю тира"
       - "И-11 > свойство: окно И-11 одной стройки не зависит от присутствия другой"
       Один живой краш по ходу: тест "форс-выдача не выдает модуль, которого на
       складе уже хватает" ловил случайное совпадение (естественный ролл при
       rng=0.5 давал 'panel' САМ, форсить было нечего) — не баг кода, баг теста;
       зафиксировал rng=0.9 (естественный ролл падает в rare, не пересекается
       с дефицитом), задокументировал причину в теле теста.
3. [x] defects-run1/2/3/4.test.ts, shuttle.test.ts — все свои `drop()`-хелперы
       поправлены точечным Edit (искал по уникальному якорю rng-seed, где
       хелперов несколько в одном файле — defects-run3.test.ts). Д-24/Д-25 в
       defects-run4.test.ts переписаны на `floorGuaranteeAllowedFor` +
       `missingFor(build, stock)` (build теперь ищется через
       `construction.builds.find`, не строка kind — это чужая правка Н-5,
       которую я только читаю). Все 5 файлов зелены по отдельности
       (`npx vitest run <файл>`), drone-агентские ошибки про `filled` в
       defects-run3.test.ts не мои и не трогал — на момент проверки уже сами
       исчезли (drone-агент успел докатить свою правку).
4. [x] state/__tests__ поправлены: economy-invariants.property.test.ts,
       shuttle-softlock.test.ts — drop_without_needed/drop_last_floor заменены
       на drop_floor_guarantee: {} + warehouse_avg: createWarehouseAvg({}, NOW),
       импорт createWarehouseAvg добавлен. persistence.test.ts — тест "счетчики
       дроп-роллера переживают перезагрузку" переписан на drop_floor_guarantee
       (ключ 'habitat_block') + warehouse_avg (avg не съезжает, т.к. reload в
       тесте не двигает мокнутые часы, elapsed=0). Все 3 файла зелены
       (40/40, `npx vitest run` по трем файлам разом).
5. [x] Гейт прогнан по частям (полный `npm run check` красный ЦЕЛИКОМ из-за
       чужих файлов — construction.ts, ui/first-goal.tsx, ui/__tests__/hub-badge.test.ts,
       ни один из них я не трогал, подтверждено `git diff --stat`, это не мое):
       - `npx tsc -b` — чисто, 0 ошибок.
       - `npx vitest run --coverage --exclude "**/*defects-*.test.ts"` — 19 файлов,
         569/569 зелено, покрытие 95.7% stmt / 89.2% branch / 98.5% func / 97.8% line.
       - `npx vitest run defects-` — 6 файлов, 47/47 зелено.
       - `npx biome check --write` применен ТОЧЕЧНО только к своим трем файлам
         (droproller.ts, droproller.test.ts, gameStore.ts) — только форматирование
         и сортировка импортов, семантика не менялась (перепроверено прогоном
         тестов после форматирования).

## ГОТОВО. Три инварианта реализованы, все тесты (включая мои новые
property-тесты) зелены. Полный отчет — в финальном сообщении оркестратору.

### Важно помнить после обрыва связи
- НЕ трогать drone.ts, drone-board.tsx, shuttle.ts, shuttle-station.tsx, construction.*
  (там ошибки тайпчека от других агентов — OrderPosition.filled и т.п., не мои,
  не чинить).
- construction.ts уже содержит `missingFor(build: BuildSlot, stock)` (не `kind`!) —
  использовать её, не изобретать свою функцию дефицита.
