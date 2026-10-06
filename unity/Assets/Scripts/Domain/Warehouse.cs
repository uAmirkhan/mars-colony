using System.Collections.Generic;
using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// Склад: qty, reserved, капасити, блокировка сбора при переполнении.
    /// Источник истины — [[tz-production-mars]] раздел 3; перенесено из
    /// `mars-colony/src/domain/warehouse.ts` целиком, без сокращений: на этих
    /// функциях стоит вся дальнейшая цепочка (заказ, погрузка, отправка).
    ///
    /// Семантика, которую нельзя перепутать:
    ///   qty       — физически занимает место, независимо от резерва
    ///   reserved  — подмножество qty, положенное в слот заказа, но не отправленное
    ///   available — qty - reserved, то, что можно потратить прямо сейчас
    ///
    /// fill:    reserved += n           (капасити НЕ освобождается)
    /// clear:   reserved -= n           (qty не меняется)
    /// deliver: qty -= n, reserved -= n (капасити освобождается)
    /// </summary>
    public sealed class WarehouseCellState
    {
        public int qty;
        public int reserved;
    }

    public sealed class WarehouseState
    {
        public Dictionary<string, WarehouseCellState> cells =
            new Dictionary<string, WarehouseCellState>();
        public int capacity;
    }

    public static class Warehouse
    {
        public static WarehouseState Create(int capacity = Economy.WAREHOUSE_START_CAPACITY) =>
            new WarehouseState { capacity = capacity };

        public static int QtyOf(WarehouseState w, string good_id) =>
            w.cells.TryGetValue(good_id, out var c) ? c.qty : 0;

        public static int ReservedOf(WarehouseState w, string good_id) =>
            w.cells.TryGetValue(good_id, out var c) ? c.reserved : 0;

        public static int AvailableOf(WarehouseState w, string good_id) =>
            QtyOf(w, good_id) - ReservedOf(w, good_id);

        public static int TotalQty(WarehouseState w)
        {
            int sum = 0;
            foreach (var c in w.cells.Values)
                sum += c.qty;
            return sum;
        }

        public static int FreeSpace(WarehouseState w) => w.capacity - TotalQty(w);

        public static bool IsFull(WarehouseState w) => FreeSpace(w) <= 0;

        /// <summary>Хватает ли места, чтобы принять qty единиц целиком. Частичного приема нет.</summary>
        public static bool CanAccept(WarehouseState w, int qty) => FreeSpace(w) >= qty;

        private static WarehouseCellState Cell(WarehouseState w, string good_id)
        {
            if (!w.cells.TryGetValue(good_id, out var c))
            {
                c = new WarehouseCellState();
                w.cells[good_id] = c;
            }
            return c;
        }

        /// <summary>
        /// Положить товар на склад. Все или ничего: при нехватке места возвращает
        /// false и НЕ кладет ничего. Сервер никогда не обрезает qty до остатка
        /// капасити и не продает излишек автоматически ([[tz-production-mars]] 3.2).
        /// </summary>
        public static bool Deposit(WarehouseState w, string good_id, int qty)
        {
            if (qty <= 0)
                return false;
            if (!CanAccept(w, qty))
                return false;
            Cell(w, good_id).qty += qty;
            return true;
        }

        /// <summary>Потратить свободный товар. Считает по available, не по qty.</summary>
        public static bool Consume(WarehouseState w, string good_id, int qty)
        {
            if (qty <= 0)
                return false;
            if (AvailableOf(w, good_id) < qty)
                return false;
            Cell(w, good_id).qty -= qty;
            return true;
        }

        /// <summary>fill: положить в слот заказа. Товар остается на складе.</summary>
        public static bool Reserve(WarehouseState w, string good_id, int qty)
        {
            if (qty <= 0)
                return false;
            if (AvailableOf(w, good_id) < qty)
                return false;
            Cell(w, good_id).reserved += qty;
            return true;
        }

        /// <summary>clear: вернуть из слота заказа до отправки. qty не меняется.</summary>
        public static bool Unreserve(WarehouseState w, string good_id, int qty)
        {
            if (qty <= 0)
                return false;
            if (ReservedOf(w, good_id) < qty)
                return false;
            Cell(w, good_id).reserved -= qty;
            return true;
        }

        /// <summary>deliver: заказ отправлен, зарезервированное покидает склад.</summary>
        public static bool ShipReserved(WarehouseState w, string good_id, int qty)
        {
            if (qty <= 0)
                return false;
            if (!w.cells.TryGetValue(good_id, out var c))
                return false;
            if (c.reserved < qty || c.qty < qty)
                return false;
            c.qty -= qty;
            c.reserved -= qty;
            return true;
        }

        /// <summary>Цена следующего расширения в кредитах; 0 — потолок достигнут. Зеркало warehouse.ts.</summary>
        public static int UpgradePrice(WarehouseState w) =>
            w.capacity >= Economy.WAREHOUSE_MAX_CAPACITY ? 0 : w.capacity * Economy.WAREHOUSE_UPGRADE_PRICE_PER_CAPACITY;

        /// <summary>Апгрейд склада: +10 к капасити, потолок MVP — 300.</summary>
        public static bool UpgradeCapacity(WarehouseState w)
        {
            if (w.capacity >= Economy.WAREHOUSE_MAX_CAPACITY)
                return false;
            w.capacity = System.Math.Min(
                Economy.WAREHOUSE_MAX_CAPACITY,
                w.capacity + Economy.WAREHOUSE_UPGRADE_STEP
            );
            return true;
        }

        public static List<string> OccupiedGoods(WarehouseState w)
        {
            var result = new List<string>();
            foreach (var pair in w.cells)
                if (pair.Value.qty > 0)
                    result.Add(pair.Key);
            return result;
        }
    }
}
