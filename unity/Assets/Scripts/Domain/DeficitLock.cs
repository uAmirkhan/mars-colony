using System.Collections.Generic;
using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// И-13 (изоляция дефицита между механиками) — контракт лока. Источник
    /// истины — [[tz-common-systems-mars]] раздел 1.5, [[mars-colony-frame]]
    /// строка И-13; перенесено из `mars-colony/src/domain/deficitlock.ts`.
    ///
    /// Смысл: одна и та же позиция целевого дефицита (И-8, MAX_DEFICIT_SLOTS=1)
    /// не выдается ОДНОВРЕМЕННО двумя механиками.
    ///
    /// Упрощение относительно канона, как и в TS: канон держит лок на пару
    /// (player_id, good_id) — здесь состояние принадлежит одному игроку,
    /// поэтому лок — просто good_id -> DeficitLock.
    /// </summary>
    public sealed class DeficitLockEntry
    {
        public string good_id;
        public Mechanic locked_by_mechanic;

        /// <summary>
        /// Заказы СВОЕЙ механики, все еще держащие этот товар как дефицитную
        /// позицию. Список, а не единственная ссылка: канон 1.5 разрешает
        /// нескольким заказам одной механики законно делить один дефицитный товар.
        /// </summary>
        public List<string> owners = new List<string>();
        public double created_at;
        public double expires_at;
    }

    /// <summary>Состояние лока на игрока: товар -> действующий лок. Персистентно (сейв).</summary>
    public sealed class DeficitLockState
    {
        public Dictionary<string, DeficitLockEntry> locks = new Dictionary<string, DeficitLockEntry>();
    }

    public static class DeficitLock
    {
        public static DeficitLockState Create() => new DeficitLockState();

        /// <summary>Лок «жив» — не истек по TTL.</summary>
        private static bool IsAlive(DeficitLockEntry lock_, double now) => lock_.expires_at > now;

        /// <summary>
        /// Канон 1.5: товар залочен ДРУГОЙ механикой прямо сейчас — своя механика
        /// конфликта не создает.
        /// </summary>
        public static bool IsDeficitLockedByOtherMechanic(
            DeficitLockState locks,
            string good_id,
            Mechanic mechanic,
            double now
        )
        {
            if (!locks.locks.TryGetValue(good_id, out var lock_))
                return false;
            return lock_.locked_by_mechanic != mechanic && IsAlive(lock_, now);
        }

        /// <summary>
        /// Канон 1.5: атомарный upsert-if-absent. Живой лок другой механики НЕ
        /// перезаписывается (конфликт — молчаливый no-op). Живой лок ТОЙ ЖЕ
        /// механики принимает НОВОГО владельца. Истекший лок перезаписывается целиком.
        /// </summary>
        public static void RegisterDeficitLock(
            DeficitLockState locks,
            string good_id,
            Mechanic mechanic,
            string order_ref,
            double now,
            double ttl = Economy.DEFICIT_LOCK_TTL
        )
        {
            if (locks.locks.TryGetValue(good_id, out var existing) && IsAlive(existing, now))
            {
                if (existing.locked_by_mechanic != mechanic)
                    return; // конфликт — DO NOTHING
                if (!existing.owners.Contains(order_ref))
                    existing.owners.Add(order_ref);
                return;
            }

            locks.locks[good_id] = new DeficitLockEntry
            {
                good_id = good_id,
                locked_by_mechanic = mechanic,
                owners = new List<string> { order_ref },
                created_at = now,
                expires_at = now + System.Math.Min(ttl, Economy.DEFICIT_LOCK_TTL_MAX),
            };
        }

        /// <summary>
        /// Канон 1.5: вызывается при терминальном событии заказа-владельца.
        /// Снимает лок ТОЛЬКО если он принадлежит вызывающей механике и снимает
        /// ровно ОДНОГО владельца. Объект-лок удаляется целиком, только когда
        /// владельцев не осталось.
        /// </summary>
        public static void ReleaseDeficitLock(
            DeficitLockState locks,
            string good_id,
            Mechanic mechanic,
            string order_ref
        )
        {
            if (!locks.locks.TryGetValue(good_id, out var lock_))
                return;
            if (lock_.locked_by_mechanic != mechanic)
                return;

            lock_.owners.RemoveAll(r => r == order_ref);
            if (lock_.owners.Count == 0)
                locks.locks.Remove(good_id);
        }
    }
}
