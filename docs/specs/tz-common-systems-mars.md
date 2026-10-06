---
title: "ТЗ: Общие подсистемы (Mars Colony)"
type: concept
created: 2026-07-28
last_verified: 2026-07-28
confidence: medium
expires_after: 90d
topics: [saas, game, mars-colony, tz, common-systems, economy]
---

# ТЗ: Общие подсистемы (Mars Colony) v1

Источник закона: [[mars-colony-frame]] (валюты, инварианты И-1..И-13, товарный субстрат, модель данных п.9, API-контракты п.10, UX-стандарт п.13, стандарт аналитики п.12). Это ТЗ закрывает находку приемки [[tz-review-2026-07-28]] («общие подсистемы описаны трижды по-разному») — семь систем ниже пишутся **один раз**, [[tz-drone-mars]], [[tz-shuttle-mars]], [[tz-liner-mars]] их только конфигурируют через таблицы раздела 8. Расхождение механики с контрактом этого документа — баг ТЗ механики, не повод переопределить контракт локально.

## 0. Область документа

Документ описывает семь подсистем каркаса (раздел 8): генератор заказов, дроп-роллер, серверный countdown, idempotency-контракт, rush-cost калькулятор, соц-граф, кошелек и транзакции. Не описывает: специфику UI отдельной механики (окна отсека шаттла, палубы лайнера, доска дрона — см. соответствующие ТЗ), товарный субстрат и XP-кривую (каркас разделы 3, 6), стройку и производство ([[tz-production-mars]]).

При ревизии этого документа обнаружено и зафиксировано здесь несколько мест, где tz-shuttle-mars.md и tz-liner-mars.md определяли одну и ту же вещь разными числами (дублирование, которое и находит приемка). Каждое такое место помечено блоком `> [!important] Унификация` — это не новое решение, а выбор одного источника истины из нескольких существующих и фиксация локальных копий как устаревших.

### 0.1 Сквозной принцип: check-then-act запрещен

Находка приемки #6 (`assignLeagueRoom` — race condition на `memberCount`, check-then-act без транзакции) — не локальный баг лайнера, а класс ошибки, который повторяется везде, где сервер сначала читает состояние, потом решает, потом пишет двумя отдельными операциями. Во всех семи подсистемах ниже действует одно правило:

> Любая последовательность «прочитать условие → решить → записать результат» обязана быть одной атомарной операцией (транзакция с блокировкой строки, условный `UPDATE ... WHERE version = ?`, атомарный `INSERT ... ON CONFLICT`) — никогда двумя раздельными вызовами. Если в псевдокоде ниже шаг выглядит как две строки «if / then mutate», конкретная реализация обязана свернуть их в одну атомарную операцию на уровне БД.

Это правило применяется явно к: дроп-роллеру (floor guarantee), idempotency-контракту (конкурентный дубль-тап), rush-cost/кошельку (списание + начисление), соц-графу (лимит друзей, дневной кап помощи), генератору (И-13 лок дефицита).

## 1. Генератор заказов

### 1.1 Назначение

Один движок производит **один заказ/один рейс за вызов**: набор позиций «товар + количество», удовлетворяющий инвариантам И-8 (анти-фрустрация), И-10 (реализуемость), И-13 (изоляция дефицита между механиками). Дрон вызывает движок независимо для каждого из своих до 9 слотов доски; шаттл и лайнер — один раз на рейс, с последующей раскладкой плоского списка позиций в свою форму (отсеки шаттла — плоский список; палубы лайнера — 3 группы по 2-3).

### 1.2 Контракт

**Вход:** `mechanic` (drone|shuttle|liner), `player_state` (уровень, склад с `reserved`, построенные здания, история последнего заказа этой механики, активные дефицит-локи других механик), `config` (таблица раздела 1.4, специфичная для `mechanic`).

**Выход:** `order = { order_id, mechanic, positions: [{good_id, qty, easy: bool}], generated_at, server_seed }`, записанный на сервере атомарно в момент создания (для дрона — в момент освобождения слота доски; для шаттла/лайнера — в момент ухода предыдущего рейса). Клиент **никогда** не генерирует заказ и не видит состав до создания — только читает уже зафиксированный результат. Повторный запрос состояния с тем же `order_id` идемпотентен по построению (чтение, не мутация).

### 1.3 Псевдокод — основной цикл

```
function generateOrder(mechanic, player, config):
    pool = buildGoodsPool(mechanic, player, config)
    if pool.isEmpty():
        return fallbackMinimalOrder(mechanic, player, config)   // см. 1.6, крайний случай

    previous = player.lastOrderGoods(mechanic)                // sequential-фолбэк ниже, если REPEAT_SCOPE=sequential
    positions = []
    deficitCount = 0
    attempts = 0

    while not shapeComplete(positions, mechanic, player.level, config) and attempts < config.GEN_MAX_ATTEMPTS:
        attempts += 1
        good = weightedPickGood(pool, positions, previous, config)
        if good == null: break                                    // пул уникальных кандидатов исчерпан раньше формы заказа — см. 1.6
        stock = availableStock(player, good)                     // qty - reserved, модель каркаса п.9
        qty = computeQty(good, player.level, mechanic, config)    // см. 1.4
        produceMin = productionTimeMinutes(good, qty, player)
        isEasy = (stock >= qty) or (produceMin <= config.EASY_PRODUCE_MAX_MIN[mechanic])

        if not isEasy:
            if deficitCount >= config.MAX_DEFICIT_SLOTS[mechanic]:
                continue                                          // лимит дефицитных позиций исчерпан — ищем другой товар
            if isDeficitLockedByOtherMechanic(good, player, mechanic):
                continue                                          // И-13: позиция уже держится другой механикой, пропускаем товар
            qty = applyPinch(good, stock, qty, config)             // см. 1.4 PINCH_MODE
            deficitCount += 1
            registerDeficitLock(good, player, mechanic, order_ref, ttl=config.DEFICIT_LOCK_TTL)

        positions.append({good: good, qty: qty, easy: isEasy})

    // И-8 покрытие
    if easyRatio(positions) < config.COVERAGE_MIN[mechanic]:
        positions = downgradeHardestDeficitSlot(positions, player, config)

    // И-8 анти-повтор — скоуп сравнения задан параметром движка REPEAT_SCOPE (каркас раздел 8, п.1),
    // а не особенностью одной механики: "board" — попарно против всех одновременно видимых заказов
    // этой механики (дрон, где видны все 9, берется максимум пересечения среди всех пар); "sequential" —
    // только против одного предыдущего заказа той же механики (шаттл, лайнер)
    repeatAgainst = (config.REPEAT_SCOPE[mechanic] == "board")
        ? mostOverlappingVisibleOrder(positions, player.visibleOrders(mechanic))   // возвращает goods-состав заказа с максимальным пересечением
        : previous
    if repeatRatio(positions, repeatAgainst) > config.REPEAT_CAP[mechanic]:
        positions = replaceOverlappingSlots(positions, pool, repeatAgainst, config)

    // И-10 реализуемость (пропускается, если у механики нет дедлайна — см. 1.4 ACHIEVABILITY_CHECK)
    if config.ACHIEVABILITY_CHECK[mechanic]:
        budget = config.window_minutes(mechanic, player.level) * 0.6
        if totalProductionMinutes(positions, player) > budget:
            positions = rebalanceForAchievability(positions, player, budget, config)

    if not isValidOrder(positions, mechanic, config):
        positions = fallbackMinimalOrder(mechanic, player, config)  // финальный предохранитель, см. 1.6

    order = assembleOrderShape(mechanic, positions, config)   // плоский список / отсеки / палубы 3x2-3
    persist(order, server_seed = randomSeed(), generated_at = now())
    return order
```

Приоритет фолбэков при конфликте (наследует явное решение из tz-shuttle-mars.md п.3.2, распространено на все механики): **достижимость (И-10) > покрытие (И-8 coverage) > анти-повтор (И-8 repeat) > изоляция дефицита (И-13)**. И-13 стоит последней не потому что менее важна, а потому что ее фолбэк тривиален и не требует даунгрейда: если дефицитный товар залочен другой механикой, генератор просто пробует другой товар в цикле выше — до 1 дефицитной позиции в заказе, отсутствие дефицитной позиции вообще не нарушает ни один инвариант (И-8 требует «не более 1», не «ровно 1»).

### 1.4 Вспомогательные функции

```
function buildGoodsPool(mechanic, player, config):
    pool = match config.POOL_MODE[mechanic]:
        "built_only"        -> player.unlockedGoods.filter(g -> g.requiredBuilding in player.builtBuildings)
        "unlocked_or_built"  -> player.unlockedGoods.filter(g -> g.unlockLevel <= player.level)
    return pool

function computeQty(good, level, mechanic, config):
    base = config.GOOD_BASE_QTY[good]                         // {min, max}, каркас п.3.1 — общая таблица
    sampled = randRange(base.min, base.max)
    bracket = config.BRACKET_MULT[levelBracket(level)]        // 1.0/1.5/2.0/2.5 по брекетам 1-9/10-14/15-19/20+ (каркас 3.1)
    mech = config.MECHANIC_MULT[mechanic][speedClass(good)]   // дрон/шаттл 1.0 для любого speedClass; лайнер: быстрый кроп (цикл <=15 мин) 6.0, медленный кроп (>15 мин) 3.0, фабричный 2.5 (каркас 3.1)
    raw = sampled * bracket * mech
    return max(base.min, floorToDisplayStep(raw, config.QTY_DISPLAY_STEP))

function speedClass(good):
    // каркас 3.1 делит кропы на быстрые/медленные по времени цикла; фабричные — одна группа
    if good.kind != 'crop': return 'factory'
    return (good.prod_time_sec <= 15 * 60) ? 'crop_fast' : 'crop_slow'

function applyPinch(good, stock, targetQty, config):
    match config.PINCH_MODE[mechanic]:
        "absolute" -> return stock + clamp(targetQty - stock, config.PINCH_MIN, config.PINCH_MAX)      // дрон, шаттл
        "percent"  -> return stock + clamp(targetQty - stock, targetQty * config.PINCH_PCT_MIN, targetQty * config.PINCH_PCT_MAX)  // лайнер

function weightedPickGood(pool, chosenSoFar, previousOrderGoods, config):
    candidates = pool.filter(g -> g not in chosenSoFar)          // товар уникален в пределах одного заказа — жесткое ограничение,
                                                                    // не мягкий вес: одна и та же позиция не встречается в заказе дважды
    if candidates.isEmpty(): return null                          // пул меньше требуемой формы заказа — см. крайний случай 1.6

    weights = {}
    for good in candidates:
        w = 1.0
        if good in previousOrderGoods and nearRepeatCap(chosenSoFar, previousOrderGoods, config): w *= 0.5
        weights[good] = w * config.CATEGORY_WEIGHTS[mechanic][good.kind]
    return weightedRandomPick(candidates, weights)

function shapeComplete(positions, mechanic, level, config):
    match mechanic:
        "drone"   -> len(positions) >= pickPositionCount(level, config)          // 1-6, формула tz-drone-mars 3.2
        "shuttle" -> len(positions) >= pickSlotCount(level, config)              // 3-5, веса по брекету
        "liner"   -> len(positions) >= 3 * pickContainersPerDeck(config)         // 3 палубы x 2-3 (плоско, раскладка в assembleOrderShape)

function assembleOrderShape(mechanic, positions, config):
    match mechanic:
        "drone"   -> { positions: positions }                                    // 1 заказ = 1-6 плоских позиций
        "shuttle" -> { slots: positions }                                        // 1 рейс = 3-5 отсеков
        "liner"   -> { decks: chunkIntoDecks(positions, sizes=[2..3, 2..3, 2..3]) }  // 3 палубы
```

**`productionTimeMinutes` / `totalProductionMinutes`** — от них зависят И-8 (`isEasy`) и И-10 (`ACHIEVABILITY_CHECK`), но нигде не были определены явно. Псевдокод ниже учитывает три вещи, без которых обе оценки лгут: параллельность производственных слотов одного товара (несколько грядок/слотов очереди фабрики сразу), уже идущее производство (нельзя планировать так, будто слот свободен, если в нем уже крутится батч) и склад, зарезервированный под чужие заказы (`Warehouse.reserved`, каркас п.9 — резерв не считается доступным).

```
function productionSlotsFor(good, player):
    // явное определение (находка приемки: функция вызывалась, но нигде не была определена).
    // Два разных источника слотов, ранее не различались (находка приемки: грядка — не Building,
    // join по required_building на пустом фильтре возвращал [] и INFINITY для самой ходовой
    // товарной группы, ломая проверку реализуемости заказа И-8/И-10 у дрона и лайнера):
    if good.required_building == null:
        // культуры — грядка не здание (tz-production-mars 2.1, FieldSlot). Лимит = число
        // ОТКРЫТЫХ слотов грядок игрока (уровневые анлоки + докупленные сверх лимита,
        // tz-production-mars 5.6/7: FIELD_SLOTS_START, FIELD_SLOT_UNLOCK_LEVELS,
        // FIELD_SLOT_PURCHASE_PRICE_ISO), а не построенных зданий — их для грядки не существует
        return player.fields
    // фабрики — слоты очереди ВСЕХ построенных зданий этого типа (join по Building.type ==
    // good.required_building, канон каркаса п.9; на ур.15 у здания может быть 2 экземпляра,
    // tz-production-mars 2.3/5.2 — оба считаются, иначе слоты второго экземпляра теряются);
    // недостроенные (state == "under_construction") в join не участвуют
    buildings = player.buildings.filter(b -> b.type == good.required_building and b.built)
    if buildings.isEmpty(): return []
    // FactorySlot[] всех этих зданий — 2 базовых на здание, докупаемых до 4 (tz-production-mars
    // 2.2/7: FACTORY_QUEUE_BASE_SLOTS, FACTORY_QUEUE_SLOT3_PRICE_ISO, FACTORY_QUEUE_SLOT4_PRICE_ISO).
    // Раньше функция отдавала 1 запись на здание независимо от докупки — время производства для
    // докупленной очереди занижалось в 1.5-2 раза, потому что productionTimeMinutes считает каждый
    // элемент списка отдельным параллельным слотом.
    buildingIds = buildings.map(b -> b.id)
    return player.factorySlots.filter(s -> buildingIds.includes(s.building_id))

function productionTimeMinutes(good, qty, player):
    // сколько минут пройдет до накопления qty штук good, доступных ИМЕННО этой позиции —
    // считаем от availableStock = qty - reserved (каркас п.9), не от сырого Warehouse.qty:
    // товар, зарезервированный под чужой слот/заказ, доступным не считается
    stillNeeded = max(0, qty - availableStock(player, good))
    if stillNeeded == 0: return 0

    slots = productionSlotsFor(good, player)
    if slots.isEmpty(): return INFINITY                  // товар недостижим этим складом/постройками — решает И-10/1.6 выше по стеку

    // слот, где уже идет цикл (FieldSlot.state == GROWING или FactorySlot.state == PRODUCING,
    // tz-production-mars 2.1/2.2), освобождается не сейчас, а в ends_at —
    // "уже идущее производство" учитывается как задержка старта следующего цикла на этом слоте
    freeAt = [slot.state in ["GROWING", "PRODUCING"] ? minutesUntil(slot.ends_at) : 0 for slot in slots]
    cycleMin = good.prod_time_sec / 60
    // выпуск строго поштучно за цикл (было: вызов неопределенной batchSizeOf, подразумевавшей
    // пачки, которых нет ни в одной механике и не сконфигурированы ни для одного из 12 товаров —
    // функция убрана из контракта, не заменена)

    produced = 0
    lastCompletionMin = 0
    while produced < stillNeeded:
        // слоты производят ПАРАЛЛЕЛЬНО: на каждом шаге берем слот, который освободится раньше всех остальных
        // (не суммируем время всех слотов последовательно — иначе оценка завышена в разы при N>1 слотах)
        idx = indexOfMin(freeAt)
        freeAt[idx] += cycleMin                          // этот слот сразу стартует следующий цикл вслед за предыдущим
        lastCompletionMin = freeAt[idx]
        produced += 1                                     // поштучно, без пачек

    return lastCompletionMin

function totalProductionMinutes(positions, player):
    // И-10 сравнивает это с бюджетом окна механики (`budget = window_minutes * 0.6`). Наивная сумма
    // productionTimeMinutes по всем позициям штрафует заказ за здания, которые производят ПАРАЛЛЕЛЬНО
    // друг другу (грядка водорослей и фабрика синт-ткани не делят один слот). Позиции группируются по
    // `required_building` (канон каркаса п.9): внутри одного здания слоты уже учтены как параллельные
    // самой productionTimeMinutes, а сами позиции этого здания встают в очередь друг за другом на общий
    // пул слотов и поэтому суммируются; разные здания друг друга не блокируют.
    byBuilding = groupBy(positions, pos -> pos.good.required_building)
    perBuildingMinutes = [sum(productionTimeMinutes(pos.good, pos.qty, player) for pos in group)
                          for building, group in byBuilding]
    return max(perBuildingMinutes, default=0)            // здания работают параллельно — время заказа = самое долгое здание, не сумма всех
```

**Фолбэки коррекции заказа** — `downgradeHardestDeficitSlot`, `replaceOverlappingSlots`, `rebalanceForAchievability`. Это штатный путь коррекции почти каждого заказа (малый пул товаров на низком уровне гарантирует срабатывание регулярно), не редкий крайний случай — детерминированные, не рекурсивный ретрай генератора целиком; гарантируют, что `GEN_MAX_ATTEMPTS` не исчерпывается без результата.

```
function downgradeHardestDeficitSlot(positions, player, config):
    // срабатывает при easyRatio < COVERAGE_MIN (1.3, И-8 coverage) — превращает самую "тяжелую"
    // дефицитную позицию (дальше всех от порога easy) в easy, остальные позиции не трогает
    deficitPositions = positions.filter(p -> not p.easy)
    if deficitPositions.isEmpty(): return positions              // защита от гонки — сюда не должны попадать без дефицита

    hardest = argmax(deficitPositions,
                      p -> productionTimeMinutes(p.good, p.qty, player) - config.EASY_PRODUCE_MAX_MIN[mechanic])
    floorQty = config.GOOD_BASE_QTY[hardest.good].min
    easyQty = maxQtyWithin(hardest.good, player, config.EASY_PRODUCE_MAX_MIN[mechanic])  // наибольшее qty, покрытое складом ИЛИ производимое в пределах порога easy

    if easyQty >= floorQty:
        hardest.qty = easyQty
        hardest.easy = true                                       // урезали количество, товар не меняли
    else:
        // даже минимум этого товара не easy — меняем сам товар на easy-альтернативу из пула
        replacement = pickEasyGoodFromPool(pool, excluding=positions.goods, player, config)
        if replacement == null: return positions                  // пул не дал альтернативы — решает финальный фолбэк 1.6
        positions.replace(hardest, {good: replacement, qty: config.GOOD_BASE_QTY[replacement].min, easy: true})
    return positions

function replaceOverlappingSlots(positions, pool, repeatAgainst, config):
    // срабатывает при repeatRatio > REPEAT_CAP (1.3, И-8 анти-повтор) — меняет пересекающиеся
    // с repeatAgainst позиции по одной, пересчитывая repeatRatio после каждой замены
    overlapping = positions.filter(p -> p.good in repeatAgainst).sortBy(p -> p.easy ? 0 : 1)  // easy меняем первыми — дешевле для coverage

    for position in overlapping:
        if repeatRatio(positions, repeatAgainst) <= config.REPEAT_CAP[mechanic]: break
        candidates = pool.filter(g -> g not in repeatAgainst and g not in positions.goods)
        if candidates.isEmpty(): break                             // пул исчерпан — оставляем как есть, решает 1.6
        replacement = weightedPickGood(candidates, positions.goods, repeatAgainst, config)
        positions.replace(position, {good: replacement, qty: computeQty(replacement, player.level, mechanic, config),
                                      easy: isEasyGood(replacement, player, config)})
    return positions

function rebalanceForAchievability(positions, player, budget, config):
    // срабатывает при totalProductionMinutes(positions) > budget (И-10). Приоритет фолбэков (1.3) ставит
    // достижимость выше coverage/repeat — эта функция вправе снова ухудшить easyRatio/repeatRatio,
    // если иначе не уложиться в budget
    while totalProductionMinutes(positions, player) > budget:
        target = argmax(positions, p -> productionTimeMinutes(p.good, p.qty, player))   // самая долгая по времени позиция
        floorQty = config.GOOD_BASE_QTY[target.good].min
        if target.qty > floorQty:
            target.qty = max(floorQty, target.qty - qtyReductionStep(target, config))    // урезаем количество ступенчато
            target.easy = (availableStock(player, target.good) >= target.qty)
                          or (productionTimeMinutes(target.good, target.qty, player) <= config.EASY_PRODUCE_MAX_MIN[mechanic])
        else:
            // дальше некуда резать количество этого товара — меняем сам товар на более быструю альтернативу пула
            replacement = fastestProducibleGood(pool, excluding=positions.goods, player, config)
            if replacement == null: break                           // пул не дает более быстрой альтернативы — решает финальный фолбэк 1.6
            positions.replace(target, {good: replacement, qty: config.GOOD_BASE_QTY[replacement].min, easy: true})
    return positions
```

### 1.5 И-13: изоляция дефицита между механиками — контракт лока

```
DeficitLock { player_id, good_id, locked_by_mechanic, order_ref, created_at, expires_at }

function isDeficitLockedByOtherMechanic(good, player, mechanic):
    lock = DeficitLock.get(player.id, good.id)
    return lock != null and lock.locked_by_mechanic != mechanic and lock.expires_at > now()

function registerDeficitLock(good, player, mechanic, order_ref, ttl):
    // атомарный upsert (0.1: check-then-act запрещен) — INSERT ... ON CONFLICT(player_id, good_id) DO NOTHING,
    // конфликт означает "уже залочено кем-то" -> вызывающий код обязан пропустить этот товар и продолжить цикл
    DeficitLock.upsertIfAbsent(player.id, good.id, mechanic, order_ref, expires_at = now() + min(ttl, config.DEFICIT_LOCK_TTL_MAX))

function releaseDeficitLock(good, player, mechanic):
    // вызывается при терминальном событии заказа: отправлен/выброшен/улетел/сдан
    DeficitLock.delete(player.id, good.id, where locked_by_mechanic == mechanic)
```

Лок снимается при любом терминальном переходе заказа-владельца (deliver/discard/depart/deliverContainer последней позиции) — но дрон-заказы могут висеть без таймера сколь угодно долго (каркас раздел 7: «заказы висят»), поэтому лок все равно ограничен потолком `DEFICIT_LOCK_TTL_MAX` (24ч), чтобы забытый заказ дрона не блокировал шаттл/лайнер навсегда.

### 1.6 Крайние случаи и фолбэки

- **Пул товаров пуст** (крайне маловероятно после гейта уровня открытия механики, но возможно для лайнера в первую минуту после ур.12, если игрок ни разу не строил фабрику) — `fallbackMinimalOrder`: одна позиция самого дешевого/быстрого доступного товара (обычно водоросли), количество = `GOOD_BASE_QTY.min`, форма заказа урезается ниже нормального минимума (для шаттла — допустим 1 отсек вместо 3), событие `order_generation_degraded` логируется как алерт для дежурного, а не тихо проглатывается.
- **Все попытки цикла исчерпаны (`GEN_MAX_ATTEMPTS`) без валидного набора** — та же `fallbackMinimalOrder`, приоритет отдается И-10 (заказ обязан быть выполним) над И-8/И-13.
- **И-8 coverage и И-13 изоляция одновременно неразрешимы** (маленький пул, единственный дефицитный товар в пуле уже залочен) — генератор просто не создает дефицитную позицию (deficitCount остается 0), это всегда валидно.
- **Игрок построил здание секунду назад, но снес его до окончания цикла генерации** (race между постройкой/сносом и генерацией) — пул строится один раз атомарно на момент вызова (снимок `player_state`), последующие изменения зданий в этом вызове не учитываются; для уже созданных заказов см. tz-drone-mars AC13 (позиция помечается «недостижима», не перегенерируется).
- **Пул товаров меньше требуемой формы заказа** (после жесткого ограничения уникальности 1.4 — напр. шаттлу нужно 5 отсеков, а игрок открыл всего 4 товара) — `weightedPickGood` возвращает `null`, основной цикл (1.3) выходит из `while` раньше `shapeComplete`, позиции не докручиваются до полной формы; `isValidOrder` считает такой заказ валидным по меньшей форме (не считает его "недособранным" по И-8/И-10), либо, если и меньшая форма не проходит инварианты, срабатывает `fallbackMinimalOrder`.

### 1.7 Конфиг-таблица (общие поля движка, значения по механике — см. раздел 8)

| Параметр | Назначение | Диапазон |
|---|---|---|
| `POOL_MODE[mechanic]` | built_only \| unlocked_or_built | фиксировано по механике |
| `CATEGORY_WEIGHTS[mechanic]` | вес кроп/фабричный при выборе товара | 0.3-0.7 на категорию |
| `GEN_MAX_ATTEMPTS` | предохранитель цикла подбора | 20-100 |
| `EASY_PRODUCE_MAX_MIN[mechanic]` | порог «легко произвести» для И-8 | 20-45 мин (дрон/шаттл); часы для лайнера, см. раздел 8 |
| `COVERAGE_MIN[mechanic]` | доля easy-позиций (И-8) | >=0.60 базовая; лайнер >=0.70 (строже, не нарушает базовый порог, обоснование — И-10 запас 17ч, см. tz-liner-mars 5.1) |
| `MAX_DEFICIT_SLOTS[mechanic]` | максимум дефицитных позиций | 1 (фиксировано И-8, все три механики) |
| `PINCH_MODE[mechanic]` | absolute \| percent | дрон/шаттл absolute; лайнер percent (плоские 1-3 единицы незаметны при qty x6-12) |
| `PINCH_MIN/MAX` \| `PINCH_PCT_MIN/MAX` | размер дефицита сверх склада | 1-3 ед. (absolute); 10-25% (percent) |
| `REPEAT_CAP[mechanic]` | максимум общих товаров с предыдущим заказом (или самым похожим видимым — см. `REPEAT_SCOPE`) | 0.40-0.55 |
| `REPEAT_SCOPE[mechanic]` | board \| sequential — параметр движка (каркас раздел 8, п.1), не особенность одной механики | board (дрон — попарно против всех видимых заказов доски); sequential (шаттл, лайнер — только против одного предыдущего заказа) |
| `ACHIEVABILITY_CHECK[mechanic]` | включена ли проверка И-10 | выкл. для дрона (нет дедлайна); вкл. для шаттла/лайнера |
| `DEFICIT_LOCK_TTL` / `DEFICIT_LOCK_TTL_MAX` | время жизни И-13 лока | до терминального события заказа, потолок 24ч |
| `QTY_DISPLAY_STEP` | шаг округления количества | 1-5 |

> [!important] Унификация — подтверждено по факту, не гипотетически
> `BRACKET_MULT` и `MECHANIC_MULT` — единственный источник масштабирования количества по уровню и механике, определен в каркасе 3.1. На момент первой версии этого документа `tz-shuttle-mars.md` v2 действительно определял собственный `LEVEL_QTY_SCALAR` (1.0/1.5/2.2 по брекетам 5-8/9-14/15+), не совпадавший ни значениями, ни границами брекетов с каркасным `BRACKET_MULT` — ровно то дублирование, которое находит приемка. Параллельный fix-pass довел шаттл до v3: раздел 3-4 tz-shuttle-mars.md v3 уже убрал `LEVEL_QTY_SCALAR`, явно ссылается на `GOOD_BASE_QTY x bracket_mult x mechanic_mult(1.0)` каркаса и не переопределяет значения локально. tz-liner-mars.md (`computeLinerQuantity`, раздел 5.2) использует ту же таблицу с `MECHANIC_MULT[liner]` — каркас 3.1 делит лайнерные кропы на быстрые (цикл <=15 мин: водоросли, соя, грибы, хлопок-синт) 6.0 и медленные (>15 мин: кофе-бобы, томаты-гидро) 3.0, отдельно фабричные 2.5; `computeQty` здесь резолвит нужную группу через `speedClass(good)`, а не плоский `good.kind`, чтобы отразить это трехгруппное деление одной и той же функцией для всех механик.

> [!important] Унификация — И-10 у лайнера реализована на уровне qty, не на уровне заказа
> `tz-liner-mars.md` v2 применяет достижимость (И-10) иначе, чем шаттл: не постфактум-рибаланс суммы по всем позициям (как `rebalanceForAchievability` здесь), а прямое ограничение внутри самой `computeQty` — `computeLinerQuantity` (tz-liner-mars 5.2) урезает `qty` конкретного товара, если его производство превышает 60% дедлайна загрузки (432 мин на 12ч окне), до вызова остального движка. Оба варианта валидны в рамках этого контракта: `ACHIEVABILITY_CHECK` может быть реализована либо как постфактум-рибаланс всего заказа (шаттл), либо встроена в `computeQty` конкретного товара (лайнер) — второе дешевле предотвращает проблему, но требует, чтобы функция количества знала дедлайн механики заранее. Отдельно от этого лайнер держит более строгий порог покрытия всего груза — `COVERAGE_MIN=0.70` в пределах бюджета 17ч (5ч превью + 12ч окно, обоснование tz-liner-mars 5.1) — это уже стандартный `easyRatio`-механизм раздела 1.3, не дублирует per-товарный И-10.
>
> **Имя параметра — не путать с движковым `window_minutes`.** Этот 17ч-бюджет живет в конфиге лайнера под именем `ACHIEVABILITY_BUDGET_MIN = 1020` (мин) — собственный параметр лайнера для порога `COVERAGE_MIN`, а не то же самое, что `window_minutes(mechanic, level)` движка (раздел 1.3): та функция возвращает дедлайн конкретной механики для постфактум-рибаланса (`rebalanceForAchievability`), которым лайнер, как описано выше, не пользуется. Ранее параметр лайнера назывался `achievabilityBudgetHours: 17` (в часах) и был переименован в `window_minutes: 1020` при переводе в минуты — совпадение имени с движковой функцией смешивало константу и функцию под одним именем. Зафиксировано под именем `ACHIEVABILITY_BUDGET_MIN` (tz-liner-mars 5.2, `LINER_CONFIG`) — отдельный параметр лайнера, не поле движка.

> [!important] Унификация — лайнер теперь тоже несет целевую дефицитную позицию
> Каркас v0.2 (раздел 5, И-8) зафиксировал: «Целевой дефицит: ровно 1 позиция (+1..+3) у всех трех механик, включая лайнер». `tz-liner-mars.md` (раздел 5.1-5.2) конфигурирует `MAX_DEFICIT_SLOTS[liner]=1`, `PINCH_MODE[liner]=percent`, `PINCH_PCT_MIN/MAX=0.10/0.25` — те же поля контракта, что и у дрона/шаттла, порог покрытия `COVERAGE_MIN=0.70` не меняется. Регистрация `DeficitLock`/И-13 для лайнера теперь происходит на общих основаниях (раздел 1.5), как и у остальных механик.

### 1.8 Аналитика

> [!important] `order_generated` — каноничное событие (каркас п.12, «владение именами событий»)
> Схема ниже определена ровно здесь и переиспользуется всеми тремя механиками буквально, без добавления/переименования полей. Ни одно ТЗ механики не переопределяет `order_generated` собственным набором полей — это ровно тот сценарий («одно имя с двумя разными наборами полей»), который каркас называет худшим из возможных исходов. Локальный вариант механики, которому нужны дополнительные поля (напр. `premium` дрона), обязан идти под собственным именем с префиксом механики — `drone_order_generated`, `shuttle_order_generated` — и не переиспользовать имя `order_generated`.

| Событие | Параметры | Метрика → решение |
|---|---|---|
| `order_generated` | order_id, mechanic, positions[], deficit_positions, easy_ratio, repeat_ratio, achievability_ratio | распределение покрытия/дефицита по механике → калибровка `COVERAGE_MIN`/`EASY_PRODUCE_MAX_MIN` раздельно на механику |
| `order_generation_degraded` | mechanic, reason: empty_pool\|max_attempts\|unresolvable_invariant, player_level | частота фолбэков → сигнал бага генератора или недостроенного контент-пула на раннем уровне |
| `deficit_lock_conflict` | good_id, blocking_mechanic, requesting_mechanic, player_level | частота конфликтов И-13 → если один и тот же товар систематически конфликтует между шаттлом и лайнером — пересмотреть пул на пересечении уровней открытия |
| `deficit_lock_expired_unused` | good_id, mechanic, held_minutes | лок дожил до `DEFICIT_LOCK_TTL_MAX`, не был снят терминальным событием → сигнал брошенных дрон-заказов, влияющих на другие механики |

### 1.9 Что проверяется юнит-тестом генератора (не только продакшн-наблюдением)

Для каждой механики: сгенерированный заказ на 1000 прогонов с разными снапшотами `player_state` (низкий/средний/высокий уровень, пустой/полный склад, узкий/широкий пул построек) всегда проходит: `easyRatio >= COVERAGE_MIN`, `deficitCount <= MAX_DEFICIT_SLOTS`, `repeatRatio <= REPEAT_CAP` (когда есть предыдущий заказ), `achievability` (когда `ACHIEVABILITY_CHECK` включена), и никогда не завершается ошибкой/пустым результатом — только валидным заказом (в т.ч. через фолбэк 1.6).

## 2. Дроп-роллер

### 2.1 Назначение

Один движок роллит награду из тиражированного пула тиров для любого события выдачи «предмета по весу» в игре: сейчас — контейнер модуля шаттла (1 роллна отсек при отправке рейса) и палубный/сундучный приз лайнера (роллна закрытие палубы и на полную загрузку, каркас 2.3 «роллируется из пула палубных призов»). Дрон роллер не использует — платит детерминированно (кредиты+XP по формуле, tz-drone-mars 3.3).

### 2.2 Контракт

**Вход:** `tier_pool_config` (веса тиров + пул предметов в тире, специфичен для источника — модули шаттла vs призы лайнера), `player_roller_state` (pity-счетчики, скользящее среднее склада, окно floor guarantee), `context` (что для игрока сейчас «активная нужда» — активная стройка для модулей; для призов лайнера floor guarantee не применяется, см. 2.6).

**Выход:** `{item_id, tier, pity_triggered: bool, antistockpile_triggered: bool, floor_guarantee_forced: bool}`, зафиксированный на сервере атомарно в момент роспуска триггера (отправка рейса шаттла / закрытие палубы или полной загрузки лайнера) — не в момент визуального вскрытия контейнера игроком. Клиент получает состав только по явному запросу «собрать» — закрывает реролл через переустановку/смену времени устройства.

### 2.3 Псевдокод

```
function rollReward(source, colony_state, config):
    // source: {kind: shuttle_module | liner_prize, context}
    openTiers = tiersAvailable(colony_state, config)             // гейтовый тир только если гейт открыт
    tier = weightedPick(openTiers, weightsFrom(config.TIER_WEIGHTS[source.kind], openTiers))
    pool = config.POOL[source.kind][tier]

    weights = {}
    for item in pool:
        w = config.BASE_ITEM_WEIGHT[item]                        // равномерно внутри тира, если не задано иное

        // И-7 pity: нужный активной стройке предмет, не выпавший K прибытий подряд
        if config.PITY_ENABLED[source.kind] and isNeededForActiveContext(item, colony_state)
           and colony_state.pity_counter[item] >= config.PITY_K:
            w *= config.PITY_MULTIPLIER

        // И-7 анти-стокпайл: запас выше потребности x THRESHOLD (среднее 24ч) -> вес /FACTOR
        avgStock24h = colony_state.warehouse_avg_24h[item]
        activeNeed = activeContextNeed(item, colony_state)
        if activeNeed > 0 and avgStock24h > activeNeed * config.ANTISTOCKPILE_THRESHOLD:
            w *= config.ANTISTOCKPILE_FACTOR

        weights[item] = w

    return weightedPick(pool, weights)


function applyPityAndStockUpdates(rolledItem, colony_state, config):
    for item in config.ALL_ITEMS[source.kind]:
        if item == rolledItem: colony_state.pity_counter[item] = 0
        elif isNeededForActiveContext(item, colony_state): colony_state.pity_counter[item] += 1
    // анти-стокпайл читает скользящее среднее склада, само pity не мутирует


function enforceFloorGuarantee(rewards, colony_state, config):
    // только для source.kind == shuttle_module (И-11), см. 2.6 антиэксплойт
    if not config.FLOOR_GUARANTEE_ENABLED[colony_state.source_kind]: return rewards
    for construction in colony_state.active_constructions:
        if warehouseCoversConstruction(construction, colony_state): continue     // И-11: не срабатывает, склад уже покрывает стройку
        if constructionTriggeredRecently(construction, config.FLOOR_GUARANTEE_MIN_GAP): continue  // не чаще 1 раза на 5 прибытий
        recentArrivals = lastNArrivals(colony_state, config.FLOOR_GUARANTEE_WINDOW - 1)
        if not any(a contains neededItemFor(construction) for a in recentArrivals)
           and not any(r.item in neededItemsFor(construction) for r in rewards):
            slot = pickLeastImpactfulSlot(rewards)
            slot.item = pickNeededItem(construction, colony_state, allowedTiers=["basic","rare"])  // И-11: не гейтовый, никогда
            markConstructionTriggered(construction, now())
    return rewards
```

### 2.4 Таблица состояния роллера на игрока (`RollerState`)

| Поле | Тип | Назначение | Обновляется |
|---|---|---|---|
| `player_id, source_kind, item_id` | ключ | какой предмет какого источника (модуль шаттла / приз лайнера) | — |
| `pity_counter` | int | число прибытий/событий без выпадения предмета, нужного активному контексту | +1 на каждое событие, где предмет нужен и не выпал; 0 при выпадении |
| `warehouse_avg_24h` | float (кэш) | скользящее среднее запаса за 24ч — читает анти-стокпайл | фоновый job пересчета (каркас раздел 11) |
| `floor_guarantee_window_counter` | int | сколько прибытий подряд без нужного предмета для конкретной стройки | +1 на прибытие без нужного предмета; 0 при выпадении или форс-выдаче |
| `floor_guarantee_last_triggered_at` | timestamp \| null | последнее срабатывание форс-выдачи для этой стройки | при срабатывании `enforceFloorGuarantee` |
| `last_dropped_at` | timestamp | для дебага/аналитики, не участвует в формулах | при каждом роле |

### 2.5 Конфиг-параметры

| Параметр | Значение по умолчанию | Диапазон | Применимо к |
|---|---|---|---|
| `TIER_WEIGHTS[shuttle_module]` | базовый 62%, редкий 33%, гейтовый 5% | каркас раздел 4 (60-65/30-35/~5) | шаттл |
| `TIER_WEIGHTS[liner_prize]` | своя таблица призов, не тиры модулей (тюнится продюсером под контент сундука) | — | лайнер |
| `PITY_K` | 4 | 3-6 | оба источника, если `PITY_ENABLED` |
| `PITY_MULTIPLIER` | x2 | x1.5-x3 | — |
| `ANTISTOCKPILE_THRESHOLD` | x2 (среднее 24ч) | x1.5-x3 | — |
| `ANTISTOCKPILE_FACTOR` | x0.5 | x0.3-x0.7 | — |
| `FLOOR_GUARANTEE_ENABLED` | true (шаттл), false (лайнер) | — | только шаттл, см. 2.6 |
| `FLOOR_GUARANTEE_WINDOW` | 3 прибытия | фиксировано каркасом (И-11) | шаттл |
| `FLOOR_GUARANTEE_MIN_GAP` | 5 прибытий между срабатываниями на одну стройку | фиксировано каркасом (И-11 антиэксплойт) | шаттл |
| `FLOOR_GUARANTEE_ALLOWED_TIERS` | [basic, rare] | фиксировано (гейтовый — никогда) | шаттл |

### 2.6 И-11 floor guarantee — антиэксплойт-ограничения (детально)

Находка приемки #5: «держи стройку голодной 2 прибытия — 3-е гарантированно дает модуль любого тира, вкл. гейтовый 5%» — эксплойт. Зафиксированные контрмеры (все три обязательны одновременно, ослабление любой возвращает эксплойт):

1. **Только базовый/редкий тир.** Гейтовый тир никогда не форсируется — `pickNeededItem` вызывается с `allowedTiers=["basic","rare"]` жестко, не конфигурируемо шире.
2. **Не чаще 1 раза на 5 прибытий на одну стройку.** `FLOOR_GUARANTEE_MIN_GAP` проверяется до срабатывания — считает от `floor_guarantee_last_triggered_at` той же стройки, не глобально по игроку (у игрока может быть 2 активные линии стройки, каркас раздел 6, у каждой свой счетчик).
3. **Не срабатывает, если склад уже покрывает стройку.** `warehouseCoversConstruction` — сравнение `ModuleStock.qty` игрока с оставшейся потребностью рецепта стройки; если запаса достаточно закрыть стройку без дополнительных дропов, форс-выдача не имеет смысла и не начисляется (иначе создает избыточный сток вместо решения фрустрации).

### 2.7 Аналитика

| Событие | Параметры | Метрика → решение |
|---|---|---|
| `reward_rolled` | source_kind, item, tier, pity_triggered, antistockpile_triggered | честность дропа по тирам факт vs `TIER_WEIGHTS` → алерт при системном отклонении |
| `floor_guarantee_forced` | source_kind, construction_id, item, gap_since_last, reason: floor_guarantee\|front_loaded_luck | частота форс-выдачи по каждой причине отдельно → если `floor_guarantee` высок, базовые веса скупы для активных строек (тюнинг `TIER_WEIGHTS`), если форс срабатывает у одного игрока часто — проверка на паттерн «держим стройку голодной»; `front_loaded_luck` считается отдельно и не путается со штатной гарантией — иначе первые три прибытия любого игрока искажают метрику частоты И-11 |
| `pity_triggered` | item, pity_counter_at_trigger | распределение реального ожидания выпадения → калибровка `PITY_K`/`PITY_MULTIPLIER` |
| `antistockpile_triggered` | item, avg_stock_24h, active_need | частота срезания веса → калибровка `ANTISTOCKPILE_THRESHOLD`, проверка что «продал перед прибытием» не читерит (среднее, не мгновенное значение) |

> [!important] `floor_guarantee_forced` — одна схема на два источника форс-выдачи
> Событие каноничное и обязано покрывать оба механизма форсированной выдачи модуля: штатную гарантию И-11 (2.6, не чаще 1 раза на 5 прибытий, только basic/rare) и обучающий форс первых трех прибытий шаттла (каркас п.13, «front-loaded удача» — гарантия поверх И-11, действует до него). Без `reason` два механизма неотличимы в данных: нельзя посчитать частоту штатной гарантии отдельно от гарантированного онбординга, который срабатывает у каждого нового игрока систематически на первых трех прибытиях. Обе реализации эмитят это же событие с соответствующим значением `reason`, не заводят собственное имя.

### 2.8 Крайние случаи

- **Активных строек нет вообще (игрок между стройками).** Pity/floor guarantee не имеют контекста нужды — веса падают к чистым `TIER_WEIGHTS` без модификаторов, это ожидаемо (нет фрустрации без цели).
- **Две активные стройки одновременно** (вторая линия за 300 изотопов, каркас раздел 6) — каждая ведет свой независимый `floor_guarantee_window_counter`/`last_triggered_at`; форс-выдача при срабатывании выбирает `pickLeastImpactfulSlot` относительно предмета, который закрывает менее прогретую из двух нужд первой.
- **Гейтовый тир открылся посреди активного pity-цикла** (игрок построил буровую/энергостанцию) — `tiersAvailable` пересчитывается на каждый ролл, не кэшируется на момент создания заказа; гейтовый предмет сразу участвует в обычных весах, но floor guarantee по-прежнему его не форсирует (2.6, п.1).
- **A/B контроль без pity/анти-стокпайла** (фиче-флаг выключен) — используются чистые `TIER_WEIGHTS` без модификаторов; floor guarantee также выключен в контрольной группе (иначе группа не чистая).

## 3. Серверный countdown

### 3.1 Назначение

Единый контракт таймера для всех сущностей с обратным отсчетом: рейс шаттла, прилет/окно загрузки лайнера, кулдаун доски дрона, кулдаун сбора шаттла, таймер рефреша выброшенного заказа дрона. Один контракт вместо трех разных реализаций «когда что сгорает» — находка приемки #6 (рассинхрон на границах состояний) и #8 (фоновая инфраструктура не упомянута) закрываются здесь.

### 3.2 Контракт

**Вход:** мутирующий вызов, создающий или продлевающий таймер (`generate_order`, `depart`, `arrive`), передает `duration_sec` (из конфига механики) и `ref` (какая сущность — order/rejs/slot).

**Выход/хранение:** сервер пишет **абсолютный** момент `ends_at` (UTC), не относительную длительность. Клиент никогда не хранит и не считает `ends_at` сам — только читает и локально тикает разницу до следующего sync.

### 3.3 Модель данных

```
Timer { id, mechanic, ref_id, ref_kind: order|flight|loading_window|cooldown|refresh,
        starts_at (server UTC), ends_at (server UTC), state: active|expired|resolved }
```

`resolved` — таймер истек И терминальное действие (сгорание/переход состояния) уже применено фоновым job'ом; отличие от `expired` (время вышло, но фоновая обработка еще не прошла — короткое окно, обычно секунды) важно для отладки залипших переходов.

### 3.4 Псевдокод

```
function createTimer(mechanic, ref, refKind, durationSec):
    now = serverNow()
    timer = Timer.insert(mechanic, ref.id, refKind, starts_at=now, ends_at=now + durationSec, state="active")
    return timer

function remainingSeconds(timer):
    return max(0, timer.ends_at - serverNow())          // всегда считается от серверных часов запроса, не от клиентских

function isExpired(timer):
    return serverNow() >= timer.ends_at                  // единственное место, где решается "истек ли таймер" — сервер

// клиент: локальный тик между sync-точками
function clientTick(timer, lastSyncServerTime, lastSyncDeviceTime):
    offset = lastSyncServerTime - lastSyncDeviceTime      // оффсет считается один раз на sync, не пересчитывается по ходу
    estimatedServerNow = deviceNow() + offset
    return max(0, timer.ends_at - estimatedServerNow)     // косметический локальный countdown между sync

// фоновый sweep — гарантирует переход состояния БЕЗ визита игрока (каркас раздел 11: обязательные фоновые джобы)
function deadlineSweepJob():
    for timer in Timer.where(state="active", ends_at <= serverNow()):
        // атомарная транзакция: пометить resolved + применить терминальный переход (0.1 check-then-act запрещен)
        withLock(timer.ref_id):
            applyTerminalTransition(timer)                // shuttle_departed / liner_departed / drone_slot_refreshed / cooldown_ended
            timer.state = "resolved"
```

### 3.5 Реконсиляция после оффлайна

При возврате в сеть клиент вызывает `GET /state` (каркас п.10) — сервер отдает **актуальный** `remainingSeconds` для каждого активного таймера игрока, посчитанный на момент запроса. Клиент отбрасывает любой локально тикавший countdown и перерисовывает с нуля от свежего значения; если таймер уже `resolved` фоновым sweep (игрок был оффлайн дольше таймера — типовой случай лайнера через ночь), `GET /state` возвращает уже новое состояние (следующий рейс/новый заказ), не «истекший старый».

### 3.6 Защита от перевода часов устройства

Ни один расчет истечения не использует клиентские часы как источник истины. Клиентский `Date.now()` участвует только в вычислении косметического `offset` для локального тика (3.4) — если игрок переводит системное время вперед или назад, `offset` при следующем sync пересчитывается заново от реального сервера, счетчик не искажается накопительно. Любой мутирующий запрос (`speedup`, `deliverContainer`, `discard`) валидируется по `remainingSeconds` из БД на момент обработки запроса сервером — клиентское «осталось 0:00» не принимается как аргумент.

### 3.7 Конфиг-параметры

| Параметр | Назначение | Значение/диапазон |
|---|---|---|
| `DEADLINE_SWEEP_INTERVAL_SEC` | частота фонового job'а сканирования истекших таймеров | 15-60 сек (компромисс нагрузка/задержка перехода) |
| `CLIENT_RESYNC_ON_FOREGROUND` | пересчет offset при возврате приложения на передний план | всегда true |
| `CLIENT_RESYNC_INTERVAL_MIN` | периодический пересинк во время активной сессии | 5-15 мин |
| `TIMER_KIND_PER_MECHANIC` | какие виды таймеров есть у механики | дрон: refresh, cooldown; шаттл: flight, cooldown; лайнер: flight (in_transit), loading_window |

### 3.8 Аналитика

| Событие | Параметры | Метрика → решение |
|---|---|---|
| `timer_resolved_by_sweep` | mechanic, ref_kind, delay_since_ends_at | задержка фонового sweep → SLA инфраструктуры, если задержка систематически большая — учащать `DEADLINE_SWEEP_INTERVAL_SEC` |
| `client_clock_drift_detected` | device_offset_sec, mechanic | частота/величина рассинхрона клиентских часов у аудитории → нужен ли более частый ресинк по умолчанию |
| `deadline_action_rejected_expired` | mechanic, ref_id, action | попытка мутации после истечения (гонка клиента с sweep) → корректность UI-отката (см. AC раздела 11) |

### 3.9 Крайние случаи

- **Игрок открывает экран ровно в момент истечения** (как liner AC5) — сервер авторитетно переводит состояние по `deadlineSweepJob` или по проверке `isExpired` внутри самого мутирующего запроса (какой сработает раньше); в обоих случаях клиентский запрос на действие получает явный код отказа (`ORDER_EXPIRED`/`LINER_DEPARTED`), не тихую ошибку — UI обязан откатить оптимистичное состояние.
- **Оффлайн дольше нескольких циклов таймера подряд** (лайнер: игрок не заходил 3 дня, рейс должен был смениться дважды) — `deadlineSweepJob` обязан быть идемпотентным по цепочке переходов (обработать первый резолв → создать следующий таймер → на следующей итерации sweep обработать и его), а не полагаться на визит игрока как триггер продвижения цепочки.
- **Часы сервера сами дрейфуют** (NTP-рассинхрон между нодами) — вне скоупа игровой логики, но контракт явно указывает: `serverNow()` обязан быть единой синхронизированной точкой (монотонные часы кластера/БД), не `Date.now()` отдельного апп-сервера.

## 4. Idempotency-контракт

### 4.1 Назначение

Все мутирующие действия каркаса (п.10, п.11: «Идемпотентность всех платных действий») — единый контракт, а не три похожих, но не идентичных реализации «дубль-тап игнорируется» (дрон использовал UUID-key с TTL 24ч по тексту, шаттл — только декларацию без деталей TTL, лайнер — «TTL >= окна ретрая, минимум 24ч»). Здесь контракт фиксируется полностью один раз.

### 4.2 Формат ключа

`Idempotency-Key` — заголовок каждого мутирующего `POST` (список действий — каркас п.10). Значение — client-generated UUID v4, сгенерированный заново на каждое НОВОЕ пользовательское намерение (не на каждый HTTP-запрос: если клиент ретраит один и тот же неудавшийся запрос по сети, key переиспользуется; если игрок заново нажимает кнопку после явного успеха или явной отмены — генерируется новый key).

Область уникальности ключа — пара `(player_id, endpoint)`, не глобально: `idempotency_log` хранит `(player_id, endpoint, idempotency_key) -> response_snapshot`. Это исключает случайную коллизию UUID между разными действиями одного игрока и упрощает шардирование лога по игроку.

### 4.3 TTL

`IDEMPOTENCY_TTL = 24ч` (минимум; выбрано с запасом на порядки выше любого реалистичного окна ретрая мобильного клиента — секунды/минуты, не часы). После истечения TTL запись в логе удаляется/архивируется; повторный запрос с тем же ключом после истечения TTL обрабатывается как НОВОЕ действие (риск редупликации при экстремально позднем ретрае признан и осознанно принят — 24ч перекрывает весь реалистичный диапазон retry-политик клиента).

### 4.4 Поведение при ретрае — псевдокод

```
function handleMutatingRequest(player_id, endpoint, idempotency_key, payload):
    // атомарная операция целиком (0.1: check-then-act запрещен) — уникальный индекс на (player_id, endpoint, idempotency_key)
    existing = IdempotencyLog.tryInsertOrGetExisting(player_id, endpoint, idempotency_key, payload_hash=hash(payload))

    if existing.wasAlreadyPresent:
        if existing.payload_hash != hash(payload):
            return error("IDEMPOTENCY_KEY_CONFLICT")           // тот же ключ, другой payload — баг клиента либо переиспользование key, не ретрай
        if existing.status == "in_progress":
            waitForCompletion(existing, timeout=REQUEST_TIMEOUT)  // конкурентный дубль-тап пришел ДО завершения первого — ждем, не выполняем параллельно
            return existing.cached_response
        return existing.cached_response                          // штатный повтор — отдаем закешированный результат, ничего не выполняем заново

    // первый визит этого ключа — выполняем действие
    result = executeAction(endpoint, player_id, payload)          // сама мутация (кошелек, склад, генератор и т.д.)
    IdempotencyLog.markCompleted(player_id, endpoint, idempotency_key, response_snapshot=result)
    return result
```

Ключевой момент — атомарный `tryInsertOrGetExisting` (уникальный констрейнт БД), не «прочитать лог → если пусто, выполнить → записать». Два конкурентных запроса с одним ключом (дребезг сети, двойной тап в 500мс) обязаны сериализоваться на уровне БД: один вставляет и выполняет, второй получает `existing` и либо ждет, либо читает уже готовый кэш.

### 4.5 Что возвращает сервер

- **Успех, первый визит:** новое состояние затронутых сущностей (заказ/слот/склад/таймер) + дельта кошелька (каркас п.10) + `idempotency_key` эхом.
- **Повтор того же ключа/того же payload:** идентичный ответ (тот же снапшот, тот же HTTP-статус) — клиент не может отличить первый визит от повтора по форме ответа, только по факту, что состояние не изменилось дважды.
- **Конфликт ключа (`IDEMPOTENCY_KEY_CONFLICT`):** явная ошибка 409, клиент обязан сгенерировать новый ключ и решить, было ли первое действие с этим ключом успешным (запросить состояние через `GET /state`), прежде чем повторять попытку.
- **Отказ по бизнес-правилу** (недостаточно изотопов, заказ уже улетел, дефицит-лок и т.д.) — тоже кэшируется под тем же ключом: повторный дубль-тап на заведомо неудачное действие возвращает тот же отказ, не пытается выполнить действие заново.

### 4.6 Как это тестировать

| Тест | Ожидание |
|---|---|
| Двойной тап, оба запроса дошли за <500мс с одним ключом | Ровно одно списание/начисление; второй ответ идентичен первому |
| Тот же ключ, конкурентно (два запроса буквально одновременно) | Ровно одно выполнение действия; оба HTTP-ответа приходят успешными и идентичными (второй ждет первого, не выполняется параллельно) |
| Тот же ключ, другой payload | 409 `IDEMPOTENCY_KEY_CONFLICT`, действие НЕ выполняется повторно с новыми параметрами |
| Ретрай через 1 минуту с тем же ключом (в пределах TTL) | Кэшированный ответ, без повторной мутации |
| Ретрай через 25ч с тем же ключом (после TTL) | Обрабатывается как новое действие — задокументированный, осознанный риск (4.3) |
| Ключ переиспользован на другом `endpoint` тем же игроком | Не конфликтует — область уникальности `(player_id, endpoint, key)`, не глобальная |
| Действие завершилось отказом по бизнес-правилу | Повтор с тем же ключом отдает тот же отказ, не пытается выполнить действие заново |

### 4.7 Конфиг-параметры

| Параметр | Значение | Диапазон |
|---|---|---|
| `IDEMPOTENCY_TTL` | 24ч | 12-48ч |
| `IDEMPOTENCY_WAIT_TIMEOUT` | сколько конкурентный дубль ждет завершения первого | 5-15 сек, после — 503 retry-able |
| `IDEMPOTENCY_KEY_SCOPE` | `(player_id, endpoint)` | фиксировано |

### 4.8 Аналитика

| Событие | Параметры | Метрика → решение |
|---|---|---|
| `idempotent_replay_served` | endpoint, mechanic, delay_since_original | частота реальных дублей-тапов/ретраев по эндпоинту → индикатор проблем сети/UI (частые повторы на конкретной кнопке — UX-сигнал добавить лоадер) |
| `idempotency_key_conflict` | endpoint, player_id | частота конфликтов → почти всегда баг клиента (переиспользование key) — алерт, не продуктовая метрика |
| `idempotent_wait_timeout` | endpoint | конкурентный дубль не дождался первого выполнения → сигнал деградации бэкенда под нагрузкой |

### 4.9 Крайние случаи

- **Клиент теряет сгенерированный key до получения ответа** (краш приложения между отправкой и рендером результата) — при перезапуске клиент обязан сначала запросить `GET /state`, не генерировать новую попытку вслепую; если бизнес-логика конкретного действия допускает безопасный ретрай без учета старого key (например, повторное чтение состояния), это не проблема идемпотентности.
- **Два устройства одного аккаунта** отправляют одно и то же намерение с разными ключами почти одновременно (напр. игрок залогинен на телефоне и вебе) — идемпотентность НЕ защищает от этого (это два разных легитимных действия с точки зрения контракта); защита — обычная бизнес-валидация состояния внутри `executeAction` (напр. «слот уже заполнен» отклонит второе намерение по существу, не по ключу).

## 5. Rush-cost калькулятор

### 5.1 Назначение

Одна функция для двух потребителей: И-4 (докупка в слот заказа = rush-cost x 1.2) и любой другой «витрины ускорения» в игре — ускорение производства грядки/фабрики напрямую (ставки И-5), не только докупка готового товара в заказ. До этого документа rush-cost был описан только внутри tz-shuttle-mars (раздел 8, для одного товара за раз) — здесь он обобщен на произвольную цепочку производства (товар с входами, которые сами требуют входов).

### 5.2 Контракт

**Вход:** `good_id`, `qty`, `player_state` (склад с `reserved`, скорость построек).

**Выход:** `rush_cost` (в изотопах, до наценки) — сумма стоимости ускорения всех **недостающих** звеньев цепочки производства до количества `qty`, с учетом того, что уже есть на складе на каждом уровне цепочки. Витринная цена по месту вызова = `roundToDisplayNumber(rush_cost * margin)`, где `margin` зависит от контекста вызова (И-4 докупка — 1.2; прямое ускорение производства — margin=1.0, наценки нет, это не «докупка», а обычный speedup, см. 5.5).

### 5.3 Псевдокод — обход цепочки

```
function rushCost(good, qty, player, config):
    chain = expandProductionChain(good, qty, player, visited={})
    total = 0
    for link in chain:
        owned = availableStock(player, link.good)
        missingQty = max(0, link.qtyNeeded - owned)
        if missingQty == 0: continue
        rate = config.SPEEDUP_RATE_ISOTOPES_PER_MIN[link.kind]     // И-5: грядки ~5/мин, фабрики ~6/мин (floor 10 на короткий остаток); link.kind — crop|factory, каркас п.9
        minutes = missingQty * link.minutesPerUnit
        total += max(minutes * rate, config.SPEEDUP_FLOOR_ISOTOPES[link.kind])
    return total

function expandProductionChain(good, qty, player, visited):
    // рекурсивный обход BOM-графа (Good.inputs[] из модели данных каркаса п.9)
    if good.id in visited: return []                                        // защита от циклов в графе рецептов (не должно быть по контенту, но защита обязательна)
    visited = visited + {good.id}

    ownedOfGood = availableStock(player, good)
    neededOfGood = max(0, qty - ownedOfGood)
    links = [{good: good, qtyNeeded: qty, kind: good.kind, minutesPerUnit: good.prod_time_sec / 60}]

    if neededOfGood > 0:
        for input in good.inputs:                                          // канон каркаса п.9: {good_id, qty}
            inputQtyNeeded = neededOfGood * input.qty
            links += expandProductionChain(input.good, inputQtyNeeded, player, visited)

    return links
```

Ключевое отличие от исходной версии tz-shuttle-mars: цепочка обходится **рекурсивно** (Комбинезон требует Ткань-синт x2, которая требует Хлопок-синт x2 — три уровня), а не только «сумма минут по прямым звеньям одного товара». Для товаров без входов (кропы) рекурсия останавливается на первом уровне.

### 5.4 Округление к витринным числам

```
function roundToDisplayNumber(x):
    if x < 50:   return roundToStep(x, 5)
    if x < 200:  return roundToStep(x, 10)
    if x < 1000: return roundToStep(x, 50)
    return roundToStep(x, 100)

// PRICE_DISPLAY_STEPS — производная лестница, не отдельный источник конфига:
// [5,10,...45, 50,60,...190, 200,250,...950, 1000,1100,...]
```

### 5.5 Правило самосогласованности витрины (применяется к любому вызову, не только докупке)

Наследуется из экономического обоснования tz-shuttle-mars (раздел 8) и обобщается на все витрины ускорения: цена ускорения по этой функции **не должна быть дешевле** суммы честных поштучных ускорений отдельных звеньев цепочки (иначе витрина каннибализирует обычные speedup-кнопки производства) и **не должна быть дороже x1.5** от чистого `rush_cost` (иначе опция неактивна экономически, никто не покупает). И-4 фиксирует наценку докупки в слот заказа на 1.2 — внутри этого коридора. Любая новая витрина ускорения, добавляемая в будущем (напр. ускорение постройки), обязана попадать в тот же коридор `[1.0x, 1.5x]` от `rush_cost`, а не изобретать собственную наценку.

### 5.6 Конфиг-параметры

| Параметр | Значение | Диапазон | Источник |
|---|---|---|---|
| `SPEEDUP_RATE_ISOTOPES_PER_MIN[crop]` | 5 | 4-7 | И-5 |
| `SPEEDUP_RATE_ISOTOPES_PER_MIN[factory]` | 6 | 5-8 | И-5 |
| `SPEEDUP_FLOOR_ISOTOPES[factory]` | 10 | 8-15 (короткие остатки) | И-5 |
| `SPEEDUP_RATE_ISOTOPES_PER_MIN[shuttle_slot]` | 2.3-3.9 (зависит от числа отсеков) | И-6 формула каркаса; не реализован в коде как rate/мин — фактический параметр шаттла ниже | И-6 |
| `SPEEDUP_TARIFF_ISOTOPES_PER_SLOT[shuttle]` | 70 | 55-85, держит ~15-20% запас над третью EV отсека (tz-shuttle-mars п.9) | И-6; разрешенное расширение канона — собственный параметр шаттла (плоский тариф от доли остатка таймера, был `SKIP_TARIFF_PV_PER_SLOT`, переименован под канон этой правкой; не совпадает по форме со строкой выше) |
| `SPEEDUP_RATE_ISOTOPES_PER_MIN[liner_arrival]` | floor 15, линейно | аналог шаттла | tz-liner-mars |
| `PURCHASE_MARGIN` (И-4 докупка в слот) | 1.2 | 1.10-1.25 | И-4 |
| `SPEEDUP_MARGIN_CEILING` | 1.5x rush-cost | фиксировано (5.5) | обоснование tz-shuttle-mars 8 |

> [!important] Унификация — дополнение к API-контракту каркаса
> Каркас п.10 перечисляет `POST /order/{id}/slot/{idx}/buy` (докупка в слот) и `POST /order/{id}/speedup` (ускорение таймера рейса/окна), но **не перечисляет** прямое ускорение производства в грядке/фабрике, хотя И-5 явно задает ставки для этого случая («грядки ~5 изотопов/мин, фабрики ~6/мин»). Это пробел контракта, не новая механика — добавляется действие `POST /production/{building}/speedup` (принимает `idempotency_key`, как все мутирующие вызовы), использующее эту же функцию `rushCost` с `margin=1.0` для одного товара (без цепочки — ускоряется конкретная стоящая в очереди партия, а не весь путь до сырья). Полноценный `rushCost` с рекурсивным обходом BOM-графа нужен именно для докупки готового товара, которого еще нет ни на одном уровне цепочки.

### 5.7 Аналитика

| Событие | Параметры | Метрика → решение |
|---|---|---|
| `rush_cost_computed` | good_id, qty, chain_depth, total_isotopes, context: order_buy\|production_speedup | распределение цен докупки по товарам/глубине цепочки → находит «болезненные» товары (дорогие в докупке из-за глубокой цепочки) для тюнинга контента |
| `purchase_missing_goods` | slot_id, good_id, qty, price, margin_used | выручка докупки, конверсия по механике → сегментация payer/non-payer (стандарт каркаса п.12) |
| `speedup_used` | context: field\|factory\|construction\|shuttle_flight\|liner_arrival, target_id, remaining_sec, price, balance_before | выручка ускорения по контексту → какой канал монетизации сильнее на каком типе таймера |

> [!important] `speedup_used` — каноничное событие, единая схема на все виды ускорения таймеров. Владелец схемы — этот документ, и только он.
> Заменяет прежнее локальное `production_speedup_used` (покрывало только грядку/фабрику). Дискриминатор `context` обязателен и различает пять источников ускорения каркаса: `field` (грядка), `factory` (фабрика) — оба через прямое ускорение производства (5.6, `POST /production/{building}/speedup`); `construction` — ускорение стройки; `shuttle_flight` — скип таймера рейса шаттла (И-6); `liner_arrival` — ускорение прилета лайнера. `target_id` — полиморфная ссылка на объект ускорения (building_id для field/factory/construction, order_id/flight_id для shuttle_flight, room_id для liner_arrival). `remaining_sec` — остаток таймера на момент ускорения в секундах (не в минутах — короткие шаттл-таймеры требуют секундной точности). `balance_before` — баланс валюты игрока до списания `price`, нужен для сегментации платежеспособности. До этой правки событие было объявлено канонично дважды — здесь (поля `ref_id`/`remaining_min`) и в `tz-production-mars` (поля `target_id`/`remaining_sec`/`balance_before`, без `context`); ревью дважды называло это худшим из возможных дефектов схемы аналитики. Единственный канон — набор полей выше, зафиксирован ровно в этом документе; `tz-production-mars` и любая другая механика обязаны переиспользовать его буквально и не объявлять собственную схему этого имени (см. сверочную таблицу 5.9). Механики не заводят собственное имя события для своего вида ускорения — только используют это событие с соответствующим значением `context`.

### 5.8 Крайние случаи

- **Рецепт с циклом в графе входов** (контентная ошибка, не должно проходить ревью баланса) — `expandProductionChain` обязана детектировать через `visited` и прерывать рекурсию, а не зависать; событие-алерт при обнаружении цикла в проде.
- **Часть цепочки уже полностью на складе, часть — нет** (напр. хлопок-синт есть, но ткань-синт кончилась) — рекурсия учитывает `availableStock` на каждом уровне отдельно, не только на верхнем товаре; докупка платит только за реально недостающие уровни.
- **Товар без входов, но с здание, которого нет** (крайний случай гонки: постройка снесена между генерацией заказа и докупкой) — `rushCost` не проверяет наличие здания (это ответственность генератора/валидации слота выше), функция считает чистую экономику цепочки; вызывающий код обязан отдельно проверить `producible`, иначе вернуть явную ошибку до вызова калькулятора.

### 5.9 Сверочная таблица сквозных событий (канон против реализации)

Требование каркаса, раздел 12. Смысл: декларация владения именем недоказуема на ревью без сверки. Нарушение контракта видно только тому проверяющему, кто его специально ищет, и этот класс ошибки воспроизводился в проекте три круга подряд — сначала на именах полей модели данных, потом на `speedup_used`. Таблица переводит контракт из обещания в проверяемый факт.

Правило чтения: «буквально» означает, что механика эмитит событие ровно с каноничными полями. Любое отличие обязано быть в этой таблице явно и с обоснованием, иначе это баг ТЗ.

| Событие | Каноничные поля | Производство | Дрон | Шаттл | Лайнер |
|---|---|---|---|---|---|
| `goods_consumed` | `consumer_mechanic, good_id, qty, stock_before, stock_after` | буквально | буквально | буквально | буквально |
| `speedup_used` | `context, target_id, remaining_sec, price, balance_before` | буквально (`context`: field, factory, construction) | не эмитит | `context = shuttle_flight` + разрешенное расширение `slot_count` (без него тариф скипа не восстановить и инвариант И-6 не проверить по данным) | `context = liner_arrival` |
| `order_generated` | `order_id, mechanic, positions[], deficit_positions, easy_ratio` | не эмитит | буквально (`mechanic = drone`) | буквально (`mechanic = shuttle`) | буквально (`mechanic = liner`) |
| `floor_guarantee_forced` | `source_kind, construction_id, item, gap_since_last, reason` | не эмитит | не эмитит | буквально; `reason` различает штатную гарантию И-11 и обучающий форс первых трех прибытий | не эмитит (гарантия выключена) |
| `help_slot_opened` | `mechanic, host_id, slot_id` | не эмитит | не эмитит | буквально | буквально |
| `help_container_delivered` | `mechanic, host_id, helper_id, capped` | не эмитит | не эмитит | буквально; поле `capped` обязательно — иначе срабатывание дневного капа не видно в данных | буквально |
| `purchase_missing_goods` | `slot_id, good_id, qty, price, margin_used` | не эмитит | буквально | буквально | буквально |

**Собственные события механик** живут вне этой таблицы и обязаны нести префикс механики: `drone_ftue_step_completed`, `production_ftue_step_completed`, `shuttle_ftue_step_completed`, `liner_league_points_earned` и подобные. Префикс — не украшение: он гарантирует, что локальное событие никогда не попадет в одну таблицу хранилища со сквозным и не испортит агрегацию.

## 6. Соц-граф (пишется с нуля; post-MVP, в прототипе мокается NPC-пулом)

### 6.1 Назначение

Находка приемки #4: «соц-графа не существует, кооп шаттла и лайнера ссылаются на систему друзей, которой нет ни в одном документе (лайнер ссылается на список друзей дрона — у дрона его нет)». Этот раздел — единственный источник истины про друзей/союзников, заявки, лимиты, поиск и антиабьюз. Оба текущих потребителя обязаны переиспользовать этот контракт целиком, не изобретать свой:

- **Шаттл** (роль по каркасу раздел 7: «1 отсек помощи союзника») — в текущем tz-shuttle-mars этот кооп явно вынесен за скоуп v2 («ждет социальной инфраструктуры колонии», раздел 7/14) — с этим документом инфраструктура готова, шаттлу остается только включить `HELP_SLOT_ENABLED` в своем конфиге (раздел 8).
- **Лайнер** (роль: «до 3 контейнеров помощи, очки за помощь») — уже специфицировал `help_requested`-флаг и антиабьюз локально (tz-liner-mars разделы 4, 6, 8, 10) — здесь эта логика обобщается на любую механику, значения лайнера остаются его конфигом.

### 6.2 Контракт данных

Расширение `Friendship` из модели данных каркаса (п.9: `{player_id, friend_id, state: pending|active, created_at}`) — добавлены поля, без которых направление заявки и повторные попытки не определены:

```
Friendship { player_id, friend_id, state: pending|active|declined,
             initiator_id, created_at, responded_at }

HelpSlot   { order_id|cargo_id, slot_idx, mechanic, host_player_id,
             help_open: bool, opened_at, opened_by: manual|auto,
             helped_by: player_id|null, helped_at }

HelpLog    { id, mechanic, host_player_id, helper_player_id, good_id, qty,
             reward_to_helper: {credits, xp, league_points},
             capped: bool, server_ts }
```

`HelpLog` — append-only журнал каждого акта помощи (аналог `TxLog`, но для помощи, т.к. одно событие затрагивает двух игроков и не сводится к одной денежной дельте) — единственный источник для дневного капа, лимита пары и anomaly-флага (6.5).

### 6.3 Друзья: заявки, принятие, лимит, поиск

```
function sendFriendRequest(fromPlayer, toPlayer):
    if fromPlayer.friendCount() >= config.MAX_FRIENDS: return error("FRIEND_LIMIT_REACHED")
    existing = Friendship.get(fromPlayer.id, toPlayer.id)
    if existing and existing.state == "declined" and recentlyDeclined(existing, config.DECLINE_COOLDOWN):
        return error("RECENTLY_DECLINED")                          // антиспам повторных заявок после отказа
    Friendship.upsert(fromPlayer.id, toPlayer.id, state="pending", initiator_id=fromPlayer.id)
    return ok()

function respondFriendRequest(player, requesterId, accept: bool):
    // атомарно: чтение состояния заявки + лимит обеих сторон + запись — одна транзакция (0.1)
    withLock(player.id, requesterId):
        request = Friendship.get(requesterId, player.id, state="pending")
        if not request: return error("REQUEST_NOT_FOUND")
        if not accept:
            request.state = "declined"; request.responded_at = now()
            return ok()
        if player.friendCount() >= config.MAX_FRIENDS or requester.friendCount() >= config.MAX_FRIENDS:
            return error("FRIEND_LIMIT_REACHED")                    // лимит проверяется на ОБЕИХ сторонах на момент принятия
        request.state = "active"; request.responded_at = now()
        return ok()

function searchPlayers(query, requestingPlayer):
    // публичный профиль only: ник, уровень колонии, миниатюра колонии/аватар — без email/платежных данных
    return PlayerIndex.searchByNameOrId(query).exclude(requestingPlayer.id).map(toPublicProfile)

function leagueRoomSuggestions(player):
    // соседи по Лиге как источник связей (лайнер) — те, с кем игрок уже соревновался
    room = LeagueRoom.currentFor(player)
    if not room: return []
    return room.member_ids.exclude(player.id).exclude(player.friendIds()).map(toPublicProfile)
```

### 6.4 Помощь: открытие слота, заполнение, вознаграждение

Помощь — механика-агностичный слой поверх любого `OrderSlot`/контейнера: любая позиция заказа/рейса/палубы может быть помечена `help_open`, видна друзьям, заполняется товаром помощника (не хозяина).

```
function requestHelp(hostPlayer, order, slotIdx, mode: manual|auto):
    slot = order.slots[slotIdx]
    if slot.state == "loaded": return error("SLOT_ALREADY_FILLED")
    if not config.HELP_SLOT_ENABLED[order.mechanic]: return error("HELP_NOT_AVAILABLE")
    if countOpenHelpSlots(order) >= config.MAX_HELP_SLOTS_PER_ORDER[order.mechanic]: return error("HELP_SLOT_LIMIT")
    cooldownHours = config.HELP_SLOT_COOLDOWN_HOURS[order.mechanic]           // получение помощи гейтится механикой (каркас раздел 7/8): шаттл 6ч, лайнер — не задан (гейт через MAX_HELP_SLOTS_PER_ORDER=3/рейс)
    if cooldownHours:
        lastOpenedAt = HelpSlot.lastOpenedAt(hostPlayer.id, order.mechanic)   // последний opened_at этого игрока по этой механике, любой слот
        if lastOpenedAt and hoursSince(lastOpenedAt) < cooldownHours: return error("HELP_SLOT_COOLDOWN")
    HelpSlot.upsert(order.id, slotIdx, order.mechanic, hostPlayer.id, help_open=true, opened_by=mode)
    return ok()

function deliverHelpContainer(helperPlayer, order, slotIdx, idempotency_key):
    // весь блок — одна атомарная операция (0.1): проверка открытости + проверка капов + списание товара +
    // начисление награды + запись HelpLog + закрытие слота
    withLock(order.id, slotIdx):
        slot = order.slots[slotIdx]
        helpSlot = HelpSlot.get(order.id, slotIdx)
        if not helpSlot or not helpSlot.help_open: return error("HELP_SLOT_NOT_OPEN")
        if helperPlayer.id == helpSlot.host_player_id: return error("CANNOT_HELP_SELF")
        if not areFriends(helperPlayer, helpSlot.host_player_id): return error("NOT_FRIENDS")

        capResult = checkHelpCaps(helperPlayer.id, helpSlot.host_player_id, order.mechanic, config)  // см. 6.5, атомарно внутри этой же транзакции

        owned = availableStock(helperPlayer, slot.good_id)
        if owned < slot.qty_required: return error("INSUFFICIENT_STOCK")     // реальный товар обязателен (6.5, п.3)
        debitWarehouse(helperPlayer, slot.good_id, slot.qty_required)         // склад помощника, не хозяина

        slot.qty_filled = slot.qty_required
        slot.filled_by = "ally"
        helpSlot.helped_by = helperPlayer.id; helpSlot.helped_at = now()

        reward = computeHelpReward(order.mechanic, slot, capResult, config)   // см. ниже — учитывает дневной кап И диминишинг внутри капа
        applyWallet(helperPlayer.id, reward, reason="help_" + order.mechanic, idempotency_key)
        HelpLog.insert(order.mechanic, helpSlot.host_player_id, helperPlayer.id, slot.good_id, slot.qty_required, reward, capResult.capped, now())

        // награда хозяину — обычная логика механики (кредиты+XP лайнера / модуль шаттла), не меняется помощью
        return ok(slot, reward, capResult.capped)

function computeHelpReward(mechanic, slot, capResult, config):
    base = config.HELP_REWARD_TABLE[mechanic]                     // {credits, xp, league_points} — механика решает, чем платит (И-1)
    if capResult.capped:
        return { credits: 0, xp: 0, league_points: 0 }   // сверх дневного капа: товар засчитан, ВСЯ награда помощника обнуляется —
                                                           // не только league_points; шаттл платит помощнику исключительно XP
                                                           // (HELP_REWARD_TABLE[shuttle]), обнуление одного league_points оставляло
                                                           // XP-канал шаттла без капа вообще

    reward = { credits: base.credits, xp: base.xp, league_points: base.league_points }
    if config.HELP_DIMINISHING_ENABLED[mechanic]:
        // диминишинг внутри капа (лайнер, tz-liner-mars 4.4): N-й засчитанный акт помощи за сутки
        // дает round(BASE / N), не флэт — гасит доминирование фарм-петли над штатной механикой
        reward.league_points = round(base.league_points / capResult.daily_count)
    return reward
```

### 6.5 Антиабьюз (обязателен для обеих текущих механик-потребителей; post-MVP, в прототипе мокается NPC-пулом)

Обобщение анти-абьюз блока tz-liner-mars (раздел 4.4) — там же он был специфичен к очкам Лиги, здесь распространен на любую механику, потому что угроза («два аккаунта фармят взаимопомощь ради вознаграждения без реальной игровой активности») не зависит от того, платит помощь очками Лиги, XP или чем-то еще.

1. **Дневной кап засчитанных актов помощи.** `DAILY_HELP_CAP` считается **по игроку суммарно по всем механикам** (не отдельно на шаттл и отдельно на лайнер) — иначе после включения кооп-шаттла игрок фармит 5/день на лайнере + 5/день на шаттле, обходя лимит удвоением поверхности. Сверх лимита: товар списывается и слот закрывается нормально (помощь физически состоялась), но вся награда помощника (`credits`/`xp`/`league_points` — все валюты `HELP_REWARD_TABLE[mechanic]`, не только `league_points`) не начисляется, клиент предупреждает до подтверждения.
2. **Лимит пары за эпоху.** `PAIR_HELP_CAP` — не более N засчитанных (не капнутых) актов помощи от одного `helper_player_id` одному `host_player_id`, тоже суммарно по механикам. Эпоха = сезон Лиги (14 дней, тот же серверный якорь 00:00 UTC понедельника) — переиспользуется как общий счетчик времени для анти-абьюза даже для механик без собственного сезона (шаттл сезонов не имеет).
3. **Списание товара обязательно, всегда.** Ни один help-контейнер не бесплатен для помощника (`INSUFFICIENT_STOCK` блокирует иначе) — фарм-петля упирается в реальную стоимость производства с обеих сторон.
4. **Server-side rate anomaly flag.** Пара аккаунтов, дающая >80% всех своих актов помощи друг другу за 7 дней подряд, — помечается для ревью (не авто-бан), считается по `HelpLog` за скользящее окно.
5. **Атомарность капов.** `checkHelpCaps` обязан быть частью той же транзакции, что списание и начисление (6.4) — два конкурентных `deliverHelpContainer` от одного helper'а не должны оба проскочить проверку капа до того, как счетчик обновился (тот же класс гонки, что и `assignLeagueRoom`, принцип 0.1).

```
function checkHelpCaps(helperId, hostId, mechanic, config):
    // атомарный инкремент-с-проверкой (UPSERT ... SET count = count + 1 WHERE count < cap, или инкремент затем сверка)
    dailyCount = HelpCounter.incrementAndGet(helperId, window="daily")
    pairCount = HelpCounter.incrementAndGet((helperId, hostId), window="season")
    capped = dailyCount > config.DAILY_HELP_CAP or pairCount > config.PAIR_HELP_CAP
    if pairShareOfHelper(helperId, hostId, window="7d") > config.ANOMALY_PAIR_SHARE_THRESHOLD:
        flagForReview(helperId, hostId)
    return { capped: capped, daily_count: dailyCount, pair_count: pairCount }
```

### 6.6 Конфиг-параметры

| Параметр | Значение по умолчанию | Диапазон |
|---|---|---|
| `MAX_FRIENDS` | 100 | 50-200 |
| `DECLINE_COOLDOWN` | 7 дней | 3-14 дней |
| `MAX_HELP_SLOTS_PER_ORDER[mechanic]` | шаттл 1; лайнер 3 (1 на палубу) | фиксировано ролью механики (каркас раздел 7) |
| `HELP_SLOT_COOLDOWN_HOURS[mechanic]` | шаттл 6ч; лайнер — не задан | фиксировано ролью механики (каркас раздел 7: «не чаще 1 раза в 6ч»; раздел 8, п.6: «получение помощи гейтится каждой механикой отдельно») — новый отсек-кандидат на помощь у шаттла не открывается чаще раза в 6ч на игрока, независимо от числа сменившихся заказов; лайнер гейтится счетом контейнеров на рейс, отдельного кулдауна не требует |
| `HELP_SLOT_ENABLED[mechanic]` | шаттл true, лайнер true — обе механики специфицируют кооп в полной версии ТЗ (tz-shuttle-mars v3 п.2.2, tz-liner-mars v2 раздел 4); на MVP-прототипе список союзников мокается пулом NPC для обеих (раздел 10), это ограничение прототипа, не выключенная фича | — |
| `HELP_AUTO_FLAG_DELAY[mechanic]` | лайнер 8ч из 12ч окна | 6-10ч; для шаттла — н/д, отсеки без дедлайна, авто-флаг не применим |
| `HELP_REWARD_TABLE[shuttle]` | `{credits: 0, xp: good_xp x K_shuttle x qty, league_points: 0}` | шаттл не касается очков Лиги (И-1) |
| `HELP_REWARD_TABLE[liner]` | `{credits: 0, xp: good_xp x K_liner x qty, league_points: 15}` — базовая ставка ДО диминишинга | каркас/tz-liner-mars |
| `HELP_DIMINISHING_ENABLED[mechanic]` | лайнер true, шаттл false | лайнер: N-й акт/сутки платит `round(15/N)` (tz-liner-mars 4.4) — 15/8/5/4/3 очков на N=1..5; шаттл платит XP флэтом, диминишинг не нужен (XP не относится к дефицитной соревновательной валюте, И-1) |
| `DAILY_HELP_CAP` | 5/сутки, суммарно по всем механикам | 3-8 |
| `PAIR_HELP_CAP` | 20 актов/сезон (14 дней), суммарно по всем механикам | 15-30 |
| `ANOMALY_PAIR_SHARE_THRESHOLD` | 80% за 7 дней | 70-90% |

> [!important] Унификация
> Награда помощнику различается по механике не произвольно, а по границе И-1 (изоляция валют каркаса): лайнер — единственный канал очков Лиги, поэтому только он платит ими за помощь; шаттл никогда не касается кредитов и очков Лиги, поэтому его помощник получает только XP. Это не два независимых решения, а одно правило `HELP_REWARD_TABLE[mechanic] ⊆ currencies_allowed(mechanic)` (каркас раздел 2, И-1), которое будущие механики обязаны соблюдать при включении кооп-слота.

### 6.7 Экраны

По UX-стандарту каркаса (раздел 13): единый глагол закрытия позиции «Погрузить» уже занят основным потоком заказа — помощь использует отдельный явный CTA «Помочь», чтобы не путать «свою» загрузку с чужой. Тап-таргеты >=44pt, критичные действия не ближе 8pt к краю.

**Экран 1 — Список друзей** (точка входа: иконка в хабе внимания, бейдж = число новых заявок + число открытых слотов помощи у друзей)
- Верх: поисковая строка «Найти по имени/ID», под ней — кнопка-вкладка «Заявки» (с бейджем-числом непрочитанных).
- Список карточек друга: аватар/миниатюра колонии, ник, уровень колонии, статусная плашка — приоритет сортировки: «есть открытый слот помощи» (подсвечено, кнопка «Помочь» прямо на карточке) → активные онлайн/недавно заходившие → остальные по алфавиту.
- Пустое состояние (0 друзей): иллюстрация + текст «Пока нет союзников» + два CTA: «Найти по имени» и «Соседи по Лиге» (список 6.3 `leagueRoomSuggestions`, доступен с ур.12, когда игрок уже был хоть в одной комнате Лиги; до этого — скрыт).
- Долгий тап/кнопка «...» на карточке друга: «Удалить из друзей» (с подтверждением — необратимая потеря связи, единственное confirm-действие экрана без траты изотопов, но с потерей соц-связи — обоснованно тем же принципом каркаса «подтверждение для необратимых действий»).
- Арт-промпт: теплый «уютный космос» тон (каркас раздел 1) — портреты-аватары в скафандрах разных цветов куполов, не реалистичные лица; список — карточки с легким скруглением, как окно отсека шаттла (визуальная согласованность компонентов).

**Экран 2 — Заявки** (вкладка из экрана 1)
- Два таба: «Входящие» (с кнопками «Принять»/«Отклонить» на карточке) и «Исходящие» (статус «Ожидает ответа», без действий, кроме «Отменить заявку»).
- Карточка заявки: аватар, ник, уровень колонии, для входящих — обе кнопки на одной строке (44pt каждая, не ближе 8pt друг к другу и к краю).
- Пустое состояние по каждому табу отдельно: «Нет новых заявок» / «Вы никому не отправляли заявок».
- Edge: `FRIEND_LIMIT_REACHED` при попытке принять — модалка «Список друзей заполнен (100/100)», без скрытого отказа.

**Экран 3 — Экран помощи** (доступен из хаба внимания отдельной иконкой ИЛИ как раздел внутри экрана 1)
- Верх: своя секция «Ваши открытые слоты» — если у игрока сейчас есть `help_open` слот (свой заказ/рейс), краткая карточка с текстом «Друзья видят: нужен {товар} x{qty}» + кнопка «Закрыть запрос» (снять флаг вручную).
- Ниже: лента «Друзьям нужна помощь» — карточки открытых `HelpSlot` друзей: аватар/ник хозяина, товар+количество, механика-иконка (шаттл/лайнер), кнопка «Помочь».
- Тап «Помочь» → модалка подтверждения: точный товар/количество, «спишется с вашего склада», превью награды («+15 очков Лиги +XX XP» либо «+XX XP» для шаттла) — если помощь под дневным капом, модалка честно показывает «Лимит наград за помощь на сегодня исчерпан — товар все равно засчитается» (не скрытый silent-cap, тот же принцип, что AC10 лайнера).
- Пустая лента: «Пока никому не нужна помощь» — не блокирующий, некарательный тон (каркас раздел 1, «теплый оптимистичный космос»).
- Arт-детализация для вайрфрейма: карточка помощи визуально роднится с карточкой заказа соответствующей механики (иконка контейнера шаттла / иконка палубы лайнера), чтобы игрок сразу считывал «это тот же тип задачи, что у меня, только у друга».

### 6.8 Аналитика

| Событие | Параметры | Метрика → решение |
|---|---|---|
| `friend_request_sent` / `friend_request_responded` | from, to, accepted: bool | конверсия заявка→принятие → здоровье соц-графа, сигнал звать ли активнее (пуш на новую заявку) |
| `friend_limit_reached` | player_id, side: sender\|receiver | частота упора в `MAX_FRIENDS` → тюнинг лимита вверх, если частое |
| `league_room_suggestion_shown` / `_friend_added` | player_id, room_id | конверсия «соседи по Лиге → друг» → ценность источника связей относительно ручного поиска |
| `help_slot_opened` | mechanic, order_id, mode: manual\|auto | доля авто-флага vs ручного запроса → тюнинг `HELP_AUTO_FLAG_DELAY` (если авто доминирует — окно коротко) |
| `help_container_delivered` | mechanic, host_id, helper_id, capped | доля контейнеров, закрытых помощью, по механике → баланс кооп-петли, конверсия капа |
| `help_daily_cap_hit` / `help_pair_cap_hit` | helper_id, mechanic | частота упора в капы → калибровка `DAILY_HELP_CAP`/`PAIR_HELP_CAP` |
| `help_anomaly_flagged` | helper_id, host_id, share_7d | вход в антифрод-дашборд, не продуктовая метрика роста |

### 6.9 Крайние случаи

- **Хозяин снимает `help_open` флаг вручную в момент, когда помощник уже открыл модалку подтверждения** — `deliverHelpContainer` проверяет `helpSlot.help_open` внутри той же атомарной транзакции (6.4), поздний помощник получает явную ошибку `HELP_SLOT_NOT_OPEN`, не тихий сбой; UI откатывает модалку с сообщением «Хозяин уже закрыл этот запрос».
- **Слот заполнен обычным путем (хозяин сам донес товар) одновременно с попыткой помощи** — тот же механизм: `slot.state == "loaded"` проверяется внутри транзакции, второе действие (кто бы ни пришел вторым — хозяин или помощник) отклоняется атомарно, не задваивает награду.
- **Игрок удаляет друга, у которого есть незакрытый открытый слот помощи в сторону этого игрока** — `HelpSlot` не требует активной дружбы для уже открытого слота (иначе удаление друга посреди помощи создает гонку), но новые запросы помощи между экс-друзьями недоступны (`areFriends` проверяется на момент `deliverHelpContainer`, не на момент открытия слота) — то есть уже открытый слот все еще можно закрыть, но новый уже нет.
- **Оба потребителя одновременно включены** (шаттл + лайнер) и один и тот же друг открыл help-слоты в обеих механиках — `DAILY_HELP_CAP`/`PAIR_HELP_CAP` считаются суммарно (6.5, п.1-2), помощник физически может закрыть оба (товар спишется в обоих), но очки/бонус получит только в пределах общего капа.

## 7. Кошелек и транзакции

### 7.1 Назначение

Единая точка списания/начисления для 4 скалярных валют каркаса (кредиты, изотопы, XP, очки Лиги — раздел 2). Строй-модули и товары склада — итемизированные стеки (`ModuleStock`, `Warehouse`), не скалярные валюты; они следуют тому же принципу атомарности и идемпотентности (разделы 0.1, 4), но не проходят через этот кошелек — у них уже есть свой контракт (`Warehouse.reserved`, каркас п.9). Здесь — только credits/isotopes/xp/league_points.

### 7.2 Контракт

**Вход:** `applyWallet(player_id, {currency: delta}, reason, idempotency_key)` — единственная разрешенная точка входа для мутации `Player.credits/isotopes/xp/league_points`. Ни один другой код не имеет права писать в эти поля напрямую.

**Выход:** новое значение баланса по каждой затронутой валюте + запись в `TxLog` (каркас п.9: `{id, player_id, currency, delta, reason, idempotency_key, server_ts}`).

### 7.3 Псевдокод

```
function applyWallet(playerId, deltas, reason, idempotencyKey):
    // весь блок — одна транзакция с блокировкой строки игрока (0.1: check-then-act запрещен)
    withLock(playerId):
        for currency, delta in deltas:
            if not isAllowedCurrency(currency, reason):                 // И-1 изоляция каналов
                return error("CURRENCY_ISOLATION_VIOLATION")
            if ALLOWED_DELTA_SIGN[currency] == "positive_only" and delta < 0:
                return error("INVALID_DELTA_SIGN")                      // XP и очки Лиги не тратятся обычными транзакциями

        player = Player.get(playerId)
        for currency, delta in deltas:
            newBalance = player[currency] + delta
            if newBalance < 0:
                return error("INSUFFICIENT_BALANCE")                    // отрицательный баланс невозможен — отказ ДО записи, не откат ПОСЛЕ
        // все валюты проверены — теперь применяем все дельты одним коммитом (все-или-ничего)
        for currency, delta in deltas:
            player[currency] += delta
            TxLog.insert(playerId, currency, delta, reason, idempotencyKey, server_ts=now())
        player.save()
        return ok(player.balances())

function isAllowedCurrency(currency, reason):
    // И-1: строй-модули не покупаются за кредиты; очки Лиги не покупаются ничем; дрон не платит модулями; шаттл не платит кредитами
    return currency in CURRENCY_WHITELIST_BY_REASON[reason]
```

Проверка достаточности баланса выполняется **для всех валют транзакции до применения любой из них** — многовалютная операция (напр. докупка модуля: списание изотопов + начисление модуля идет через `ModuleStock`, не через кошелек, но если бы это была многовалютная кошелек-операция) не может списать одну валюту и провалиться на второй, оставив баланс в промежуточном состоянии.

### 7.4 Откат (rollback)

Кошелек — append-only leдger (`TxLog` никогда не редактируется и не удаляется, аналогично принципу append-only `wiki/log.md` этого проекта, только для игровой экономики). Откат — это **новая компенсирующая запись**, не мутация/удаление старой:

```
function rollbackTransaction(originalTxId, reason_suffix):
    original = TxLog.get(originalTxId)
    return applyWallet(original.player_id, {original.currency: -original.delta},
                        reason="rollback:" + original.reason + ":" + reason_suffix,
                        idempotency_key=newKey())                        // новый ключ — это новое действие, не ретрай оригинала
```

Практический источник отката — не игровые действия (они атомарны по построению, 7.3), а внешняя асинхронность: покупка пака изотопов за реальные деньги (И-9) — платеж у провайдера подтвержден, но `applyWallet` начисления не выполнился (сбой в моменте между вебхуком и записью). Реконсиляция: фоновый job сверяет журнал платежного провайдера с `TxLog` по `idempotency_key = payment_provider_transaction_id`; недостающее начисление применяется задним числом с тем же ключом (естественно идемпотентно — если начисление уже прошло, повтор ничего не меняет, раздел 4).

### 7.5 Отрицательный баланс невозможен — гарантия

Гарантия обеспечивается на двух уровнях одновременно (defense in depth):
1. **Прикладной уровень** — проверка `newBalance < 0` внутри `applyWallet` до коммита (7.3).
2. **Уровень БД** — `CHECK (credits >= 0 AND isotopes >= 0 AND xp >= 0 AND league_points >= 0)` констрейнт на таблице `Player` — защита от бага в прикладном коде, который обошел `applyWallet` напрямую (не должно случиться по контракту 7.2, но констрейнт — последняя линия обороны).

Особый случай — очки Лиги на границе сезона: обнуление в `rolloverSeason` (tz-liner-mars 3.2) — это не обычная транзакция «потратил», а административная операция с `reason="season_rollover"`, которая **разрешена** писать отрицательную дельту именно для `league_points` (единственное исключение из `ALLOWED_DELTA_SIGN=positive_only`), логируется в `TxLog` как обычно для полной прослеживаемости.

### 7.6 Конфиг-параметры

| Параметр | Значение |
|---|---|
| `ALLOWED_DELTA_SIGN[credits]` / `[isotopes]` | any (можно списывать и начислять) |
| `ALLOWED_DELTA_SIGN[xp]` / `[league_points]` | positive_only (искл. `season_rollover` для league_points) |
| `CURRENCY_WHITELIST_BY_REASON` | таблица reason→разрешенные валюты, выводится из И-1 построчно |
| `PAYMENT_RECONCILIATION_INTERVAL` | частота фонового job'а сверки платежей | 5-15 мин |

### 7.7 Аналитика

| Событие | Параметры | Метрика → решение |
|---|---|---|
| `wallet_tx_applied` | player_id, currency, delta, reason | стандартный источник для всех финансовых дашбордов (ARPU, ARPPU, sink/source баланс по валюте) — сквозной для всего проекта |
| `wallet_insufficient_balance` | player_id, currency, attempted_delta | попытка потратить больше баланса → UX-сигнал (кнопка должна быть неактивна раньше, если это происходит часто — баг клиентской валидации, не угроза целостности) |
| `wallet_currency_isolation_violation` | reason, currency | попытка нарушить И-1 → всегда баг кода, алерт разработке, не продуктовая метрика |
| `payment_reconciled_late` | payment_provider_tx_id, delay_sec | частота/задержка ручной реконсиляции IAP → SLA платежного пайплайна |

### 7.8 Крайние случаи

- **Многовалютная операция, где одна валюта прошла бы, а другая — нет** (гипотетически) — 7.3 проверяет достаточность по всем валютам до применения любой; частично проваленных мутаций не бывает по контракту.
- **Два конкурентных списания изотопов** (напр. докупка в двух разных заказах почти одновременно) на баланс, которого хватает ровно на одно — `withLock(playerId)` сериализует оба запроса; второй увидит уже уменьшенный баланс и получит `INSUFFICIENT_BALANCE`, не гонку с отрицательным итогом.
- **Rollback запускается повторно** (ретрай реконсиляции) — `rollbackTransaction` генерирует новый `idempotency_key` каждый раз, поэтому сам механизм отката не идемпотентен по построению; защита — реконсиляционный job обязан сначала проверить, не был ли уже применен компенсирующий `TxLog` с тем же `original.reason` за тот же исходный `originalTxId` (доп. индекс по `reason`, содержащему исходный `originalTxId`), прежде чем создавать новую компенсацию.

## 8. Что конфигурирует каждая механика

Сводная таблица — источник истины для конфиг-секций tz-drone-mars, tz-shuttle-mars, tz-liner-mars. Любое числовое значение внутри этих ТЗ, противоречащее строке ниже, — баг ТЗ механики, а не альтернативная валидная конфигурация.

| Параметр | Дрон | Шаттл | Лайнер |
|---|---|---|---|
| Форма заказа | 1 заказ = 1-6 позиций, до 9 заказов на доске одновременно | 1 рейс = 3-5 отсеков | 1 рейс = 3 палубы x 2-3 контейнера |
| `POOL_MODE` | built_only | built_only | unlocked_or_built |
| `CATEGORY_WEIGHTS` (кроп/фабричный) | 0.5/0.5 (не специфицировано отдельно — дефолт) | 0.5/0.5 (дефолт) | 0.4/0.6 (tz-liner-mars 5.2) |
| `EASY_PRODUCE_MAX_MIN` | 30 мин | 30 мин | н/д — заменено бюджетом 17ч (`ACHIEVABILITY_CHECK`) |
| `COVERAGE_MIN` | 0.60 | 0.60 | 0.70 |
| `MAX_DEFICIT_SLOTS` | 1 | 1 | 1 |
| `PINCH_MODE` | absolute (1-3 ед.) | absolute (1-3 ед.) | percent (10-25%) |
| `REPEAT_CAP` | <=0.50 | <=0.50 | <=0.50 |
| `REPEAT_SCOPE` | board (попарно против всех видимых заказов доски) | sequential (только предыдущий рейс) | sequential (только предыдущий рейс) |
| `ACHIEVABILITY_CHECK` (И-10) | выкл. (нет дедлайна) | вкл., постфактум-рибаланс: сумма по всем отсекам <= 60% таймера рейса | вкл., два независимых уровня: (a) `computeLinerQuantity` режет qty ДО сборки заказа, если один контейнер один сам по себе требует >432 мин (60% из 12ч окна загрузки); (b) `COVERAGE_MIN=0.70` покрытия всего груза в пределах 17ч бюджета (`ACHIEVABILITY_BUDGET_MIN=1020` мин = 5ч превью + 12ч окно) |
| `MECHANIC_MULT` (каркас 3.1) | 1.0 | 1.0 | быстрый кроп (цикл <=15 мин) 6.0 / медленный кроп (>15 мин) 3.0 / фабричный 2.5 |
| Валюты, которыми платит | кредиты + XP (K=2) | строй-модули + XP (K=8) | кредиты + XP (K=8, кэп) + очки Лиги + сундук |
| Дроп-роллер | не используется (детерминированная награда) | да — модули по тирам, pity, анти-стокпайл, floor guarantee | да — палубные призы/сундук, без floor guarantee (2.6) |
| `FLOOR_GUARANTEE_ENABLED` | — | true | false |
| Countdown-таймеры | refresh слота (30 мин), кулдаун доски | flight (60-90 мин), collect cooldown (5 мин) | in_transit (5ч), loading_window (12ч) |
| `HELP_SLOT_ENABLED` | нет (роль дрона не соревновательная/кооп) | true (tz-shuttle-mars v3 п.2.2, MVP мокает список союзников) | true |
| `MAX_HELP_SLOTS_PER_ORDER` | — | 1 | 3 (1/палуба) |
| `HELP_SLOT_COOLDOWN_HOURS` | — | 6 (получение помощи гейтится раз в 6ч, каркас раздел 7/8) | — (гейтится счетом `MAX_HELP_SLOTS_PER_ORDER=3`/рейс, отдельный кулдаун не нужен) |
| `HELP_REWARD_TABLE` | — | credits:0, xp:да, league_points:0 (флэт) | credits:0, xp:да, league_points: `round(15/N)` по N-му акту/сутки (диминишинг) |
| `HELP_DIMINISHING_ENABLED` | — | false (флэтовая XP-ставка; дневной кап помощи уже ограничивает фарм) | true (`round(15/N)`, см. 4.4 tz-liner-mars) |
| Rush-cost калькулятор | докупка позиции доски (если включена — сейчас нет в v0.1 дрона) | докупка в отсек (И-4) | докупка в контейнер (И-4) |
| Idempotency | send/discard/refresh_slot | fill/buy/clear/deliver/speedup | deliverContainer/buyMissingGoods/deliverHelpContainer/earlyDeparture |

## 9. A/B-контракт

### 9.1 Назначение

Поле `ab_bucket` объявлено в event envelope (каркас п.12) для каждого события, но до этого документа не подключено ни к одному механизму — ни один псевдокод трех ТЗ механик не читает его при выборе значения конфига, поэтому все заявленные в них гипотезы непроверяемы экспериментально: единственный доступный метод — сравнение метрики до/после релиза, где эффект неотделим от сезонности и самого факта выката новой версии (каркас п.12). Раздел закрывает пробел: функция назначения бакета, реестр экспериментов, механизм переопределения конфига по варианту.

### 9.2 assignBucket — детерминированное назначение

```
function assignBucket(player_id, experiment_id):
    experiment = ExperimentRegistry.get(experiment_id)
    if experiment == null or experiment.status != "running":
        return experiment?.default_variant or "control"           // эксперимент не идет — override ниже по цепочке не применяется

    hashInput = player_id + ":" + experiment_id                    // конкатенация с experiment_id обязательна: один и тот же player_id
                                                                     // не должен попадать в один и тот же бакет во всех экспериментах разом,
                                                                     // иначе эффекты разных экспериментов на одном игроке коррелируют
    bucketValue = stableHash32(hashInput) % 10000                  // детерминированная хэш-функция (FNV-1a/MurmurHash3) — НЕ Math.random,
                                                                     // НЕ зависит от device_id/install_id/времени вызова
    return resolveVariantByTraffic(bucketValue, experiment.traffic_split)   // кумулятивные интервалы долей трафика, см. 9.3
```

**Стабильность при переустановке.** `player_id` — идентификатор `Player` на сервере (модель данных каркаса п.9), присваивается один раз при создании аккаунта и переживает переустановку клиента через логин/авторизацию — это не client-side install id, генерируемый заново при каждой установке. `assignBucket` — чистая функция без обязательного сохраняемого состояния: одинаковый вход (`player_id`, `experiment_id`) всегда дает одинаковый результат, поэтому переустановка, смена устройства или повторный вызов через полгода дают тот же бакет, пока жив аккаунт и активен эксперимент (кэширование результата допустимо ради производительности, но не требуется для корректности).

### 9.3 Реестр экспериментов

```
Experiment { id,                             -- машинное имя, UPPER_SNAKE_CASE, напр. SHUTTLE_SKIP_PRICE_20260801
             variants: [string],              -- напр. ["control", "variant_a"]
             traffic_split: {variant: pct},   -- сумма = 100, кумулятивные интервалы для resolveVariantByTraffic
             target_params: [{mechanic, param_name}],   -- какие конфиг-параметры переопределяет, см. 9.4
             overrides: {variant: {param_name: value}},
             start_at, end_at,
             status: draft | running | stopped,
             success_metric,                  -- событие/агрегат каркаса п.12 («метрика → решение»), которое эксперимент обязан сдвинуть
             default_variant }                -- что возвращает assignBucket вне running-окна (обычно "control")
```

Правило на день один: **не более одного активного (`status: running`) эксперимента на одну пару `(mechanic, param_name)` одновременно** — иначе два эксперимента разом переопределяют один параметр, и эффект каждого неразличим (тот же класс проблемы, который сам A/B-контракт должен устранять). Проверка выполняется при переводе эксперимента `draft -> running`, не в рантайме — под принцип 0.1 не подпадает.

### 9.4 Override конфига по варианту

```
function resolveConfig(mechanic, param_name, player, staticConfig):
    baseValue = staticConfig[mechanic][param_name]                  // обычное значение — таблицы разделов 1.7/2.5/5.6/6.6/8
    experiment = ExperimentRegistry.findActiveFor(mechanic, param_name)   // 9.3: не более одного по правилу выше
    if experiment == null:
        return baseValue
    variant = assignBucket(player.id, experiment.id)
    if param_name in experiment.overrides[variant]:
        return experiment.overrides[variant][param_name]            // override побеждает статический конфиг для этого игрока
    return baseValue                                                  // вариант не трогает именно этот параметр — частичный оверрайд допустим
```

Любой конфиг-параметр, который псевдокод этого документа или ТЗ механик читает как `config.PARAM[mechanic]`, обязан фактически резолвиться через `resolveConfig(mechanic, "PARAM", player, staticConfig)` — иначе оверрайд объявлен в реестре, но не долетает до логики.

**Параметры, обязанные поддерживать override на день один** (минимальный набор — без него нельзя проверить ни одну уже заявленную в ТЗ механик гипотезу):

| Параметр | Механика | Источник |
|---|---|---|
| Цена скипа шаттла (`SPEEDUP_TARIFF_ISOTOPES_PER_SLOT[shuttle]`) | shuttle | раздел 5.6, И-6 |
| Коэффициент премии дрона (`premium`, базовое значение 1.48) | drone | tz-drone-mars 4.x |
| `HELP_DIMINISHING_ENABLED[mechanic]` | shuttle, liner | раздел 6.4/6.6 |

## 10. MVP-срез для прототипа

По каркасу (раздел 11: стек прототипа — веб, localStorage-эмуляция сервера-заглушки) каждая подсистема входит в MVP в следующем объеме:

- **Генератор заказов** — реализуется полностью настоящей логикой (раздел 1), это ядро баланса и не вырезается; уровневые брекеты можно тестировать на одном статичном срезе уровня (как уже решено в tz-shuttle-mars и tz-drone-mars).
- **Дроп-роллер** — псевдокод раздела 2 реализуется полностью для шаттла (это ядро его механики); floor guarantee и pity — тоже реальные, не заглушка. Гейтовый тир тестируется с гейтом всегда закрытым.
- **Серверный countdown** — контракт `ends_at`/`serverNow()` реализуется по-настоящему даже на сервере-заглушке (иначе вся защита от перевода часов непроверяема); фоновый `deadlineSweepJob` может быть упрощен до периодического клиентского опроса заглушки вместо настоящего cron, но модель данных `Timer` — реальная.
- **Idempotency-контракт** — на MVP допустима упрощенная версия (дубль-тап в течение 500мс игнорируется на клиенте, как решено в tz-shuttle-mars 7), полноценная серверная дедупликация с БД-констрейнтом — за скоупом прототипа, но интерфейс вызовов уже принимает `idempotency_key` везде.
- **Rush-cost калькулятор** — реализуется полностью (это чистая функция без внешних зависимостей, дешево реализовать честно даже в прототипе).
- **Соц-граф** — самая урезанная часть на MVP: список друзей и заявки не имеют смысла без реальных других игроков на localStorage-стенде, поэтому (аналогично уже принятому решению tz-liner-mars 12) друзья/помощь на MVP — фиксированный пул NPC-профилей с синтетическими открытыми слотами помощи; UI трех экранов (6.7) собирается и тестируется на этом пуле полностью, серверная логика лимитов/капов (6.5) — реализуется как настоящий код (не заглушка), просто данные для него синтетические.
- **Кошелек и транзакции** — реализуется полностью (`applyWallet`, `TxLog`) даже на заглушке, это единственная точка правды для баланса и должна тестироваться реальным кодом с первого дня — иначе прототип не докажет главный тезис документа (единая точка вместо разбросанных списаний).

## 11. Acceptance criteria

1. **Given** генератор собирает заказ для любой из трех механик, **when** сборка завершена, **then** доля easy-позиций не ниже `COVERAGE_MIN` этой механики, дефицитных позиций не больше `MAX_DEFICIT_SLOTS`, повтор с предыдущим заказом не выше `REPEAT_CAP` — все три проверяются одним юнит-тестом на 1000 случайных снапшотов состояния игрока (1.9).
2. **Given** дефицитная позиция уже залочена под шаттл (`DeficitLock`), **when** генератор лайнера в том же окне пытается выбрать тот же товар как дефицитный, **then** он пропускает этот товар и ищет другой — тот же товар не становится дефицитным одновременно в двух механиках (И-13).
3. **Given** дрон-заказ с дефицит-локом на товар остается неотправленным дольше `DEFICIT_LOCK_TTL_MAX` (24ч), **when** истекает потолок, **then** лок снимается автоматически, даже если исходный заказ так и не был отправлен/выброшен — другая механика может снова запросить этот товар в дефицит.
4. **Given** пул доступных товаров для генерации пуст (крайний случай), **when** вызывается генератор, **then** возвращается валидный `fallbackMinimalOrder` (не ошибка, не пустой заказ), событие `order_generation_degraded` логируется.
5. **Given** активная стройка не получала нужный модуль `FLOOR_GUARANTEE_WINDOW - 1` прибытий подряд, **when** склад модулей игрока уже покрывает оставшуюся потребность стройки, **then** floor guarantee НЕ форсирует выдачу (антиэксплойт-условие «склад покрывает» из 2.6, п.3), несмотря на то что окно исчерпано.
6. **Given** floor guarantee уже сработал для конкретной стройки, **when** проходит меньше `FLOOR_GUARANTEE_MIN_GAP` (5) прибытий с момента предыдущего срабатывания, **then** повторное форсирование для этой же стройки не происходит, даже если формальное окно (3 прибытия без нужного модуля) снова истекло.
7. **Given** запас модуля > потребности x `ANTISTOCKPILE_THRESHOLD`, **when** игрок продает весь склад этого модуля прямо перед прибытием, **then** вес дропа не меняется мгновенно — используется скользящее среднее за 24ч (`warehouse_avg_24h`), не мгновенное значение остатка.
8. **Given** таймер лайнера (`loading_window`) истекает ровно в момент, когда клиент отправляет `deliverContainer`, **when** оба события происходят одновременно, **then** сервер решает по `isExpired`/`deadlineSweepJob` авторитетно; если таймер уже resolved — запрос отклоняется с явным кодом (`LINER_DEPARTED`), клиент откатывает оптимистичное состояние, не показывает тихую ошибку.
9. **Given** игрок был оффлайн дольше, чем длится 2+ последовательных цикла таймера лайнера, **when** он возвращается в сеть, **then** `deadlineSweepJob` идемпотентно провел все переходы состояния без визита игрока, `GET /state` отдает уже актуальный (не устаревший) рейс.
10. **Given** игрок переводит системные часы устройства вперед на 20 часов, **when** клиент отправляет любой мутирующий запрос с таймером (speedup, deliver, discard), **then** сервер валидирует `remainingSeconds` по собственным часам на момент обработки запроса, клиентское время не влияет на результат.
11. **Given** двойной тап одного и того же платного действия происходит **конкурентно** (оба запроса доходят до сервера практически одновременно, не последовательно), **when** оба обрабатываются, **then** ровно одно выполнение действия (атомарный `tryInsertOrGetExisting`), второй запрос либо ждет и получает идентичный кэшированный ответ, либо (если выполнение уже завершилось) сразу получает его — списание/начисление происходит один раз.
12. **Given** клиент отправляет тот же `idempotency_key` с ИЗМЕНЕННЫМ payload (баг клиента или переиспользование ключа), **when** сервер сравнивает хэш payload с сохраненным, **then** возвращается `IDEMPOTENCY_KEY_CONFLICT` (409), действие с новыми параметрами не выполняется.
13. **Given** докупка товара с цепочкой производства в 3 звена (напр. Комбинезон ← Ткань-синт ← Хлопок-синт), где часть звеньев частично на складе, **when** вызывается `rushCost`, **then** сумма считается только по реально недостающим количествам на каждом уровне цепочки, не по полной стоимости от нуля.
14. **Given** любая витрина ускорения (докупка в слот, прямое ускорение производства), **when** вычисляется ее цена, **then** итоговая цена лежит в коридоре `[1.0x, 1.5x]` от чистого `rush_cost` — ни одна витрина не дешевле честного поштучного ускорения и не дороже верхней границы коридора.
15. **Given** игрок A отправляет заявку в друзья игроку B, у которого уже `MAX_FRIENDS` (100) друзей, **when** B пытается принять заявку, **then** принятие отклоняется с `FRIEND_LIMIT_REACHED`, заявка остается в `pending` (не автоматически отклоняется — B может сначала удалить кого-то и повторить принятие).
16. **Given** помощник заполняет `HelpSlot` друга, **when** на складе помощника недостаточно требуемого товара, **then** запрос отклоняется с `INSUFFICIENT_STOCK` — бесплатных (без реального товара) контейнеров помощи не существует ни для одной механики.
17. **Given** игрок уже совершил `DAILY_HELP_CAP` (5) засчитанных актов помощи за сутки суммарно по шаттлу и лайнеру вместе, **when** он помогает в 6-й раз (в любой из двух механик), **then** товар списывается и слот закрывается нормально, но `league_points`/бонусная часть награды не начисляется, клиент предупреждает об этом до подтверждения действия.
18. **Given** хозяин закрывает свой `help_open` слот вручную (или заполняет сам) в момент, когда помощник уже отправил подтверждение помощи, **when** оба запроса обрабатываются, **then** атомарная проверка внутри `deliverHelpContainer` гарантирует, что только один из двух путей закрытия слота проходит — второй получает явную ошибку (`HELP_SLOT_NOT_OPEN` или `SLOT_ALREADY_FILLED`), награда не задваивается.
19. **Given** многовалютная операция кошелька (гипотетически затрагивающая 2+ валюты одной транзакцией), **when** баланса хватает на одну валюту, но не хватает на другую, **then** ни одна из валют не списывается/начисляется — операция отклоняется целиком (`INSUFFICIENT_BALANCE`), промежуточного состояния не возникает.
20. **Given** реконсиляция платежа (IAP) обнаруживает подтвержденный платеж без соответствующего начисления изотопов в `TxLog`, **when** фоновый job применяет компенсирующее начисление с `idempotency_key = payment_provider_transaction_id`, **then** повторный запуск того же job'а на тот же платеж не начисляет изотопы дважды (естественная идемпотентность по ключу платежа).

## 12. Статус и связанные страницы

- [[mars-colony-frame]] — каркас-закон: валюты, инварианты И-1..И-13, модель данных п.9, API-контракты п.10 (дополнены здесь — `POST /production/{building}/speedup`, см. 5.6), стандарты аналитики/UX пп.12-13.
- [[tz-review-2026-07-28]] — приемка, закрывшая находки #4 (соц-граф), #5 (floor guarantee эксплойт), #6 (race condition, обобщено в 0.1), #8 (фоновая инфраструктура, раздел 3).
- [[tz-drone-mars]], [[tz-shuttle-mars]], [[tz-liner-mars]] — потребители: конфигурируют движки этого документа по таблице раздела 8, не переопределяют их. `LEVEL_QTY_SCALAR` (шаттл) уже убран в tz-shuttle-mars.md v3 (см. Унификация, раздел 1.4); локальные версии idempotency/rush-cost описаний также заменены ссылками сюда.
- Готово к консилиуму (gameplay-разработчик, гейм-аналитик, economy designer, UX, продюсер) — каркас раздел 14, гейт >=7/10.

