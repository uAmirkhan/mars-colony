using System.Collections.Generic;
using MarsColony.Domain;
using MarsColony.Domain.Config;

namespace MarsColony.Game
{
    /// <summary>
    /// Теплица сцены (заказчик 06.09, фото «Теплица 1»): у каждой свой уровень, от уровня — число рядов
    /// (1/2/3 по уровню), в ряду до 6 горшков. Горшки докупаются по одному кнопкой «+», когда ряды уровня заполнены —
    /// «+» превращается в улучшение теплицы. Горшок = <see cref="FieldSlot"/> домена (правила посева и сбора
    /// не трогаются), теплица хранит только индексы своих горшков в <see cref="ColonyState.fields"/>.
    /// </summary>
    public sealed class Teplitsa
    {
        public const int MAX_UROVEN = 3, V_RYADU = 6, GORSHKOV_START = 3;
        /// <summary>Порядок культур в лотке — по реальному сроку созревания, от быстрого к медленному (Khan 06.09):
        /// грибы 2–3 недели, капуста ~2,5 мес, томаты ~3, соя ~3,5, картошка ~4, хлопок ~6, кофе — годы.
        /// Цена и время роста в игре должны расти в этом же порядке (перебалансировка отдельно).</summary>
        public static readonly string[] PORYADOK_KULTUR = { "soy", "potato", "mushrooms", "algae", "tomatoes", "cotton", "coffee_beans" };   // лотки по времени роста, от быстрой к долгой (решение Khan 08.09)
        public static int Ryadov(int uroven) => uroven;                     // уровень = число рядов: 1 → 1, 2 → 2, 3 → 3 (Khan 06.09)
        public static int MaxGorshkov(int uroven) => Ryadov(uroven) * V_RYADU;
        /// <summary>Цена следующего горшка: n-й горшок стоит дороже предыдущего.</summary>
        public static int CenaGorshka(int uzheGorshkov) => 40 + 30 * (uzheGorshkov - GORSHKOV_START + 1);
        public static int CenaUluchsheniya(int uroven) => 300 * uroven;

        public int uroven = 1;
        public List<int> gorshki = new List<int>();
    }

    /// <summary>
    /// Состояние колонии на срезе. Тонкая обертка над доменом: хранит и
    /// применяет результат, но ни одного правила не решает сама — ровно тот же
    /// договор, что у `src/state/gameStore.ts` в веб-прототипе.
    ///
    /// `fields` — плоский список ВСЕХ горшков всех теплиц (домен знает только грядки);
    /// `teplitsy` — раскладка горшков по теплицам сцены.
    /// </summary>
    public sealed class ColonyState
    {
        public const int START_LEVEL = 1;

        public int credits;
        public int xp;
        public int level;
        public WarehouseState warehouse;
        public readonly List<FieldSlot> fields = new List<FieldSlot>();
        public readonly List<Teplitsa> teplitsy = new List<Teplitsa>();

        public static ColonyState CreateNew()
        {
            var state = new ColonyState
            {
                credits = Economy.CREDITS_START,
                xp = 0,
                level = START_LEVEL,
                warehouse = Warehouse.Create(),
            };
            for (int i = 0; i < Economy.FIELD_SLOTS_START; i++)
                state.DobavitTeplitsu();
            return state;
        }

        public Teplitsa DobavitTeplitsu()
        {
            var t = new Teplitsa();
            for (int i = 0; i < Teplitsa.GORSHKOV_START; i++) DobavitGorshok(t);
            teplitsy.Add(t);
            return t;
        }

        public FieldSlot DobavitGorshok(Teplitsa t)
        {
            var f = Production.CreateField(fields.Count);
            fields.Add(f);
            t.gorshki.Add(f.idx);
            return f;
        }

        /// <summary>Теплица, которой принадлежит горшок; -1 — ничья.</summary>
        public int TeplitsaGorshka(int pot)
        {
            for (int i = 0; i < teplitsy.Count; i++) if (teplitsy[i].gorshki.Contains(pot)) return i;
            return -1;
        }

        /// <summary>
        /// Горшок, представляющий теплицу целиком (для карточки цели, маркера, тапа по зданию):
        /// готовый — раньше всего, иначе растущий с ближайшим сроком, иначе первый пустой.
        /// </summary>
        public FieldSlot PoleTeplitsy(int t)
        {
            if (t < 0 || t >= teplitsy.Count) return null;
            FieldSlot luchshiy = null;
            foreach (int idx in teplitsy[t].gorshki)
            {
                if (idx < 0 || idx >= fields.Count) continue;
                var f = fields[idx];
                if (luchshiy == null) { luchshiy = f; continue; }
                if (Rang(f) > Rang(luchshiy) || (Rang(f) == Rang(luchshiy) && f.state == FieldState.GROWING && f.ends_at < luchshiy.ends_at)) luchshiy = f;
            }
            return luchshiy;
        }

        /// <summary>
        /// Первая по счёту теплица, где есть СОЗРЕВШИЙ горшок с этой культурой; -1 — такой нет.
        /// Порядок теплиц, а не порядок горшков в плоском списке: цель ведёт игрока по теплицам
        /// одну за другой и пропускает те, где запрошенного урожая нет (Khan 07.09).
        /// </summary>
        public int PervayaTeplitsaSKulturoy(string good)
        {
            if (string.IsNullOrEmpty(good)) return -1;
            for (int t = 0; t < teplitsy.Count; t++)
                foreach (int idx in teplitsy[t].gorshki)
                {
                    if (idx < 0 || idx >= fields.Count) continue;
                    var f = fields[idx];
                    if (f.state == FieldState.READY && f.good_id == good) return t;
                }
            return -1;
        }

        /// <summary>Культура первого созревшего горшка при обходе теплиц по порядку; null — созревших нет.</summary>
        public string PervayaGotovayaKultura()
        {
            for (int t = 0; t < teplitsy.Count; t++)
                foreach (int idx in teplitsy[t].gorshki)
                {
                    if (idx < 0 || idx >= fields.Count) continue;
                    if (fields[idx].state == FieldState.READY) return fields[idx].good_id;
                }
            return null;
        }

        private static int Rang(FieldSlot f) => f.state == FieldState.READY ? 2 : f.state == FieldState.GROWING ? 1 : 0;

        public ProductionContext ContextAt(double now) =>
            new ProductionContext { now = now, warehouse = warehouse, credits = credits, level = level };

        public void Apply(ActionResult result)
        {
            if (!result.ok)
                return;
            credits += result.credits_delta;
            xp += result.xp_gained;
        }
    }
}
