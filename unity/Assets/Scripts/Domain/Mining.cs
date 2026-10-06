using System;
using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// Добыча льда/реголита партиями с ручным тапом «запустить/забрать». НЕТ в
    /// TS-домене — новый модуль по решению интервью 06.09 (`SPEC-NOCH-3-igra.md`,
    /// «Решения интервью», п.2, и «Что играет», п.3): «таймер партии 10 мин /
    /// число машин... машин три с самого начала (таймер льда 3 мин 20 с)».
    ///
    /// Числа не новые: `prod_time_sec` и `GOOD_BASE_QTY` берутся из уже
    /// перенесенного `Goods` (зеркало каркаса) — здесь только деление на число
    /// машин и партийная выдача, которых в TS-домене нет вовсе (там добыча
    /// смоделирована как обычный фабричный слот на 1 единицу за цикл).
    ///
    /// Покупка машин — концепция ночи 3: параметр `machines` меняется кодом
    /// теста/симулятора, интерфейса покупки нет.
    /// </summary>
    public enum MiningState
    {
        EMPTY,
        WORKING,
        READY,
    }

    public sealed class MiningSite
    {
        /// <summary>'water_ice' | 'regolith' — единственные ресурсы добычи по спеке ночи 3.</summary>
        public string good_id;
        public MiningState state;

        /// <summary>1..3. Покупка — концепция, здесь просто параметр.</summary>
        public int machines;
        public double started_at;
        public double ends_at;
    }

    public static class Mining
    {
        public static MiningSite CreateSite(string good_id, int machines = 3) =>
            new MiningSite
            {
                good_id = good_id,
                state = MiningState.EMPTY,
                machines = machines,
                started_at = 0,
                ends_at = 0,
            };

        /// <summary>Таймер партии = prod_time_sec x time_scale / число машин (решение интервью п.2).</summary>
        public static double BatchDurationSec(string good_id, int machines, Tuning tuning = null)
        {
            tuning ??= TuningDefaults.Default();
            double prod_time = Goods.Of(good_id).prod_time_sec * tuning.time_scale;
            return prod_time / Math.Max(1, machines);
        }

        /// <summary>Тап по площадке: запускает партию. Только из EMPTY.</summary>
        public static ActionResult Start(MiningSite site, double now, Tuning tuning = null)
        {
            if (site.state != MiningState.EMPTY)
                return ActionResult.Fail(ActionReason.slot_busy);

            double duration = BatchDurationSec(site.good_id, site.machines, tuning);
            site.state = MiningState.WORKING;
            site.started_at = now;
            site.ends_at = now + duration;
            return new ActionResult { ok = true };
        }

        /// <summary>READY вычисляется лениво: состояние не хранится, а выводится из времени.</summary>
        public static MiningSite Refresh(MiningSite site, double now)
        {
            if (site.state == MiningState.WORKING && now >= site.ends_at)
                site.state = MiningState.READY;
            return site;
        }

        /// <summary>
        /// Тап «забрать»: партия случайного размера в диапазоне GOOD_BASE_QTY
        /// (`rnd` — детерминированный источник случайности теста/симулятора).
        /// Переполнение склада отклоняет сбор ЦЕЛИКОМ — тот же контракт, что у
        /// грядки и фабрики (`Production.CollectField`/`CollectFactory`):
        /// площадка остается READY, партия не портится и не пропадает.
        /// </summary>
        /// <param name="pribavka">Прибавка от уровня техники (`Uluchsheniya.PribavkaDobychi`), 0 — первый уровень.</param>
        public static ActionResult Collect(MiningSite site, WarehouseState warehouse, Func<double> rnd, double now, int pribavka = 0)
        {
            Refresh(site, now);
            if (site.state != MiningState.READY)
                return ActionResult.Fail(ActionReason.not_ready);

            Good good = Goods.Of(site.good_id);
            Goods.MinMax range = Goods.GOOD_BASE_QTY[site.good_id];
            int span = range.max - range.min + 1;
            int qty = Math.Min(range.max, range.min + (int)Math.Floor(rnd() * span)) + Math.Max(0, pribavka);

            if (!Warehouse.CanAccept(warehouse, qty))
                return ActionResult.Fail(ActionReason.warehouse_full);

            Warehouse.Deposit(warehouse, site.good_id, qty);
            int xp = good.base_xp * qty;

            site.state = MiningState.EMPTY;
            site.started_at = 0;
            site.ends_at = 0;

            return new ActionResult { ok = true, xp_gained = xp };
        }
    }
}
