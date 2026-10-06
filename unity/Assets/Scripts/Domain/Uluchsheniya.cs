using System.Collections.Generic;
using MarsColony.Domain.Config;

namespace MarsColony.Domain
{
    /// <summary>
    /// Уровни техники колонии (спека Khan 08.09): ангар спецтехники продаёт улучшения машинам,
    /// шаттлу и дронам. Правило домена, не интерфейса: цена и эффект считаются здесь, панель
    /// только показывает числа и зовёт покупку.
    ///
    /// Как у плавильни и добычи, TS-зеркала нет: сама добыча (`Mining`) живёт только в C#-треке,
    /// и правило про её уровни было бы нечему зеркалить. Числа заведены здесь и больше нигде,
    /// чтобы не разъехались.
    ///
    /// Уровень 1 — стартовый, покупок всего четыре (до 5).
    /// Добыча: +1 единица за уровень сверх первого.
    /// Шаттл и дроны: каждый уровень срезает 10 % времени рейса (уровень 5 — 60 % исходного).
    /// Цена: кредиты, железная руда и строй-модуль с шаттла — на КАЖДОМ уровне (решение Khan 08.09).
    /// Модуль привязан к технике по смыслу: фильтр ледосборщикам, буровая головка копателям,
    /// герметик газовой буровой, панель шаттлу, кабель дронам. До этого модули с шаттла было
    /// некуда тратить, а теперь рейс — обязательное условие любой прокачки.
    /// </summary>
    public static class Uluchsheniya
    {
        public const int UROVEN_START = 1, UROVEN_MAX = 5;
        /// <summary>Ключи улучшаемого. Совпадают с ключами `SostoyanieIgry.urovni`.</summary>
        public const string LED = "water_ice", REGOLIT = "regolith", RUDA = "iron_ore", METAN = "methane", SHATTL = "shattl", DRONY = "drony";

        private const int KREDITOV_ZA_UROVEN = 300, RUDY_ZA_UROVEN = 3;

        /// <summary>Какой строй-модуль нужен каждому виду техники.</summary>
        public static readonly Dictionary<string, ModuleId> MODUL_TEHNIKI = new Dictionary<string, ModuleId>
        {
            { LED, ModuleId.filter }, { REGOLIT, ModuleId.drill_head }, { RUDA, ModuleId.drill_head },
            { METAN, ModuleId.sealant }, { SHATTL, ModuleId.panel }, { DRONY, ModuleId.cable },
        };
        private const double SREZ_VREMENI_ZA_UROVEN = 0.10;

        public static readonly string[] VSE = { LED, REGOLIT, RUDA, METAN, SHATTL, DRONY };

        public static bool EstKuda(int uroven) => uroven < UROVEN_MAX;

        // ---- топливо шаттла (спека Khan 08.09) ----
        //
        // Шаттл не взлетает на одном грузе: нужны метан, кислород и энергия. Это замыкает три ветки,
        // которые до сих пор жили сами по себе — газовая буровая, кислородная станция и солнечные плиты.
        // Если топлива нет, шаттл ждёт на площадке с полными отсеками (решение Khan: «ждёт на площадке»).
        // Цена рейса меряется тем же способом, что и весь ребаланс экономики (ТЗ 08.09, раздел 2):
        // долей от того, что производство успевает выдать за ОДИН цикл рейса. Цикл на уровне 9+ —
        // 75 минут. Метановая буровая даёт 4 в час (900 с на единицу) — 5 за цикл; кислородный
        // модуль 6 в час (600 с) при руде 9 в час; энергия копится 65 в час (станция 20 плюс девять
        // плит по 5) — 81 за цикл. Топливо берёт около трети каждого потока: рейс ощутимо стоит,
        // но добыча его покрывает, не останавливая игру. Первые числа (5/3/100) были взяты на глаз
        // и съедали весь метан и больше всей энергии за цикл — шаттл вставал навсегда.
        public const string TOPLIVO_METAN = "methane", TOPLIVO_KISLOROD = "oxygen_tank";
        public const int METANA_NA_REYS = 2, KISLORODA_NA_REYS = 2;
        public const double ENERGII_NA_REYS = 30;

        /// <summary>
        /// Сколько топлива нужно рейсу на уровне игрока. Метан открывается на десятом уровне,
        /// кислородный баллон на восьмом: требовать их раньше — значит запереть шаттл у новичка,
        /// которому эти товары ещё негде взять. Пороги читаются из конфига товаров, а не
        /// переписываются числом, чтобы не разъехаться с ним при следующем ребалансе.
        /// </summary>
        public static void ToplivoReysa(int urovenIgroka, out int metan, out int kislorod, out double energiya)
        {
            metan = urovenIgroka >= Goods.Of(TOPLIVO_METAN).unlock_level ? METANA_NA_REYS : 0;
            kislorod = urovenIgroka >= Goods.Of(TOPLIVO_KISLOROD).unlock_level ? KISLORODA_NA_REYS : 0;
            energiya = ENERGII_NA_REYS;
        }

        /// <summary>Цена следующего уровня: кредиты, руда и строй-модули. Нули — потолок достигнут.</summary>
        public static void Tsena(int uroven, out int kredity, out int ruda, out int moduley)
        {
            if (!EstKuda(uroven)) { kredity = 0; ruda = 0; moduley = 0; return; }
            kredity = KREDITOV_ZA_UROVEN * uroven;
            ruda = RUDY_ZA_UROVEN * uroven;
            moduley = (uroven + 1) / 2;   // 1, 1, 2, 2 — модули редкие, лестница не должна упереться в рейсы
        }

        /// <summary>Сколько единиц ресурса добавляет уровень к одному циклу добычи.</summary>
        public static int PribavkaDobychi(int uroven) => System.Math.Max(0, uroven - UROVEN_START);

        /// <summary>Множитель времени рейса шаттла или дрона: 1.0 на первом уровне, 0.6 на пятом.</summary>
        public static double MnozhitelVremeni(int uroven) =>
            System.Math.Max(0.1, 1.0 - SREZ_VREMENI_ZA_UROVEN * (uroven - UROVEN_START));

        /// <summary>Уровень из состояния; отсутствующий ключ — стартовый уровень.</summary>
        public static int Uroven(Dictionary<string, int> urovni, string klyuch) =>
            urovni != null && urovni.TryGetValue(klyuch, out int v) && v >= UROVEN_START ? v : UROVEN_START;
    }
}
