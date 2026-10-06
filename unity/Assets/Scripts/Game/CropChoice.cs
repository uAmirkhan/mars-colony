using MarsColony.Domain;
using MarsColony.Domain.Config;

namespace MarsColony.Game
{
    /// <summary>
    /// Какую культуру сеет клик по грядке.
    ///
    /// РЕШЕНИЕ СРЕЗА, НЕ ПРАВИЛО ИГРЫ. ТЗ производства (раздел 2.1) дает игроку
    /// выбор культуры отдельным экраном; экрана выбора в этом срезе нет, а клику
    /// нужна однозначная культура. Берется самая дешевая из разблокированных —
    /// тот же критерий, которым И-15 определяет тупик посева, так что срез не
    /// вводит второго правила: он пользуется существующим.
    ///
    /// Когда появится экран выбора, этот класс уходит целиком, а `Plant`
    /// начинает получать `good_id` от интерфейса.
    /// </summary>
    public static class CropChoice
    {
        public static string CheapestUnlocked(int level)
        {
            string best = null;
            int best_cost = 0;
            foreach (var good in Goods.GOODS.Values)
            {
                if (good.kind != GoodKind.crop || good.unlock_level > level)
                    continue;
                int cost = Economy.PlantingCost(good.price);
                if (best == null || cost < best_cost)
                {
                    best = good.id;
                    best_cost = cost;
                }
            }
            return best;
        }
    }
}
