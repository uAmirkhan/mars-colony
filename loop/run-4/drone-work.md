# T3-1 / T3-5: дрон — частичная погрузка позиции + confirm на выброс

## Задача
- T3-1: OrderPosition несет булев `filled` вместо количества — частичная
  погрузка позиции невозможна. Нужно переделать по образцу шаттла
  (`ShuttleSlot.qty_filled` + `qty_purchased?`), включая UI (пара кнопок
  «Погрузить {stock}» / «Докупить {qty-stock}») и цену докупки по недостаче.
- T3-5: нет confirm-диалога на «Выбросить» (ТЗ 7.2, AC 10/11). Один
  универсальный диалог, текст собирается из состояния заказа.

## План (мелкими шагами, коммит состояния после каждого)
1. [x] Прочитать tz-drone-mars.md (7.2, 9 AC10/11), shuttle.ts (эталон
   qty_filled/qty_purchased/stockedIn/purchasedIn), drone.ts, drone-board.tsx,
   gameStore.ts (drone actions), drone.test.ts, shuttle.test.ts.
2. [x] Проверить blast radius смены типа OrderPosition по всему репо (grep
   `.filled`) — задеты: drone.ts, drone-board.tsx, gameStore.ts (мои), плюс
   drone.test.ts, defects-run3.test.ts, factory-queue-wake.test.ts,
   persistence.test.ts (не мои файлы, но `tsc -b` их проверяет — придётся
   точечно поправить типы, не трогая логику остальных механик).
   defects-run1/2/3 про ShuttleSlot не задеты (другой тип). gameStore.test.ts,
   economy-invariants.property.test.ts, generator-shape.property.test.ts —
   не трогают `.filled` напрямую, только зовут `loadOrderPosition` — не
   должны сломаться.
3. [~] drone.ts: изменить `OrderPosition` (qty_filled, filled_by, qty_purchased?),
   добавить приватные хелперы positionShort/purchasedIn/stockedIn (как в
   shuttle.ts), поправить: fallbackMinimalPositions, generateOrder (push),
   positionCovered, canFulfillNow, loadPosition (частичная погрузка),
   positionBuyoutPrice (цена по недостаче), buyoutPosition (докупка остатка),
   sendOrder (списание по stockedIn), releaseReserved (сброс на выбросе),
   + новая discardImpact(slot) для confirm-диалога (T3-5).
   → прогнать `npm run check` сразу после этого шага.
4. [~] drone-board.tsx: пара кнопок в OrderWindow (Погрузить{stock}/Докупить) —
   сделано. OrderCard — `position.filled` заменен на `done` — сделано.
   DiscardConfirm — сделано, вынесен как overlay-сосед scrim'а OrderWindow
   (не вложен внутрь), чтобы клик по фону confirm не закрывал заодно окно
   заказа. Осталось: проверить синтаксис/отступы правкой руками (три Edit
   подряд рвали indent), прогнать `tsc -b` на весь файл.
   ЗАМЕТКА: программист шаттла отдельно делает переиспользуемый компонент
   подтверждения траты изотопов (Н-7) для стройки/шаттла. Мой DiscardConfirm —
   про выброс заказа (не про трату изотопов), намеренно свой, не жду его.
   Если увижу новый файл подтверждения от него в src/ui — не трогаю, помечаю
   в отчете, что два диалога стоит слить позже.
   СДЕЛАНО: структура (fragment, DiscardConfirm — сосед scrim'а, не вложен),
   `npx tsc -b` чист по drone-board.tsx, `biome check --write` только на этот
   файл (0 ошибок, остались только a11y warnings — тот же паттерн что и в
   остальных scrim-окнах проекта, не блокирует check). Дальше — тесты.
5. [x] gameStore.ts: проверено `npx tsc -b` — loadOrderPosition/buyoutOrderPosition
   не задеты ошибками, правок не требуют (домен уже отдаёт партиал через тот
   же контракт `boolean`). Действия дрона в gameStore.ts НЕ трогал.
6. [x] drone.test.ts: хелпер `pos()` переведён на qty_filled/qty_purchased.
   Тест «без товара погрузка не проходит» переписан на два теста (партиал
   успешен, нулевой склад отклонён). Добавлены describe:
   «Т3-1: докупка позиции — цена и количество по недостаче» (4 теста) и
   «Т3-5: последствия выброса для confirm-диалога» (5 тестов, АС10/АС11 и
   смешанный случай + releaseReserved аннулирует докупленное безвозвратно).
   `npx vitest run src/domain/__tests__/drone.test.ts` → 65/65 зелёные.
   `biome check --write` только на этот файл → 0 ошибок (7 warn noNonNullAssertion,
   уже был такой паттерн в файле, не блокирует check exit=0).
7. [x] Точечно поправить типы в defects-run3.test.ts, factory-queue-wake.test.ts,
   persistence.test.ts — СЛЕДУЮЩИЙ ШАГ. ВАЖНО (сообщение координатора): в этих
   же файлах параллельно работает программист дроп-роллера — DropContext
   переехал (arrivals_without_needed/last_floor_arrival → warehouse_avg_24h +
   constructions[]). Править ТОЛЬКО вхождения ПОЗИЦИИ ЗАКАЗА (filled→qty_filled
   и т.п.), ошибки про DropContext/ArrivalRoll/drop_without_needed — не мои,
   не трогать, точечный Edit с уникальным якорем.
8. [~] Отчитаться. СДЕЛАНО по пути:
   - defects-run3.test.ts: поправлены 3 места OrderPosition (Д-13 order(),
     property-тест премии, Д-13-бис readyOrder) + пересчитан `shipped` в
     property-тесте (было `filled_by==='self' ? +qty`, стало `qty_filled -
     qty_purchased` — старая формула предполагала «позиция либо целиком self,
     либо целиком purchase», что верно было ДО Т3-1, но не после: частичная
     погрузка + докупка остатка теперь оставляет filled_by='purchase' на
     позиции, где часть все равно со склада). Ошибки про DropContext в этом
     файле — не мои, не трогал.
   - factory-queue-wake.test.ts: 1 место, точечно.
   - persistence.test.ts: `.filled` → `.qty_filled` в одном assert.
   - gameStore.ts SAVE_VERSION докстринг: дописан абзац про Т3-1 под версией 4
     (не бампал версию повторно, как просил координатор).
   `npx tsc -b` — ноль ошибок с упоминанием drone/OrderPosition во всем репо.
   Точечный vitest по всем правленным файлам — зелёный (drone.test.ts 65/65,
   factory-queue-wake.test.ts + persistence.test.ts вместе — 101/101,
   defects-run3.test.ts -t "Д-13" — 7/7, -t "премия внутри коридора" — 1/1).

   `npm run check` целиком: gate красный, но 8 ошибок biome — все НЕ мои:
   6 «format» + 2 «assist/organizeImports» в droproller.ts/.test.ts,
   construction.ts, gameStore.ts (блок импорта droproller + drop_floor_guarantee),
   first-goal.tsx, hub-badge.test.ts — везде это правки другого агента
   (droproller/construction в процессе, WIP по `git status`). Один формат-баг
   БЫЛ мой (defects-run3.test.ts, длинная строка в моём же fix `shipped`) —
   починил точечным Edit (перенос в блок), проверил `biome check` на файл —
   0 ошибок. `npx tsc -b` по всему репо — ноль ошибок с упоминанием
   drone/OrderPosition. Оставшиеся ошибки tsc — все про DropContext/ArrivalRoll
   (не мои, чужой WIP).

   ИТОГ: задача закрыта. Отчет дальше.

## Финальная проверка (после фикса своего format-бага)
- `npx tsc -b` по всему репо: 4 ошибки, все `drop_without_needed`/`drop_last_floor`
  в persistence.test.ts (293/301/302) и shuttle-softlock.test.ts (37) — чужой
  WIP droproller-агента, не мои строки. Ноль ошибок с drone/OrderPosition.
- `npx vitest run --exclude "**/*defects-*.test.ts"`: 19 файлов, 569/569 тестов
  зелёные (vitest транспилирует через esbuild и не падает на чужих tsc-ошибках
  типов, в отличие от `tsc -b`).
- `npm run check`: красный на шаге biome (7 ошибок) — все не мои
  (droproller.ts/.test.ts, construction.ts, first-goal.tsx, hub-badge.test.ts,
  gameStore.ts — блок импорта droproller и `drop_floor_guarantee`, не моя
  докстринг-правка). До шага `tsc -b`/vitest цепочка `&&` не доходит из-за
  этого; когда droproller/construction агенты закроют свои WIP, drone-часть
  ничего не добавит в красное — проверено отдельно.
- Задача полностью готова: T3-1 и T3-5 закрыты и в domain, и в UI, тесты
  написаны и проходят.

## Важное / договорённости с собой
- Не трогать shuttle.ts, shuttle-station.tsx, construction.*, droproller.ts.
- Никаких Write по существующим файлам — только Edit.
- Цена докупки = buyoutPrice(good_id, недостача), а не buyoutPrice(good_id, qty).
  Это чинит вторую половину бага "цена по полной величине".
- Confirm показывается только если qty_filled>0 хоть у одной позиции (AC10:
  0 filled → мгновенный выброс без диалога).
- Текст диалога: "Товар со склада ({N} поз.) вернется на склад." (для позиций
  со stockedIn>0) + "Докупленное на {sum} ⚛ сгорит — изотопы не возвращаются
  (И-12)." (для позиций с purchasedIn>0, сумма = buyoutPrice(good_id, qty_purchased)
  пересчитанная — функция чистая, даёт ту же цену что была уплачена).
- Кнопки диалога: «Выбросить» (primary/destructive, в ките нет спец-стиля —
  просто primary) / «Отмена» (secondary).
