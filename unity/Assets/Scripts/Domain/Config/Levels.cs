using System;
using System.Collections.Generic;

namespace MarsColony.Domain.Config
{
    /// <summary>
    /// XP-кривая, разблокировки, награды за левелап. Источник истины —
    /// [[mars-colony-frame]] раздел 6; перенесено из
    /// `mars-colony/src/domain/config/levels.ts`.
    ///
    /// НЕ путать с <see cref="Progress"/>: тот — отдельная Unity-track система
    /// уровней (BAZA/SHAG), заведенная до этого переноса и явно объявляющая
    /// себя НЕ зеркалом каркаса (см. её заголовок). Этот файл — честное зеркало
    /// TS-домена, требуемое спекой ночи 3 («уровень и XP считаются... по ТЗ
    /// 5.1/5.3»). Два файла существуют параллельно: `Progress` остается на
    /// местах, где уже используется существующим кодом Game/, `Levels` — новый
    /// источник для домена/тестов.
    /// </summary>
    public static class Levels
    {
        public const int MAX_LEVEL_MVP = 21;

        public const double XP_CURVE_BASE_COEF = 120;
        public const double XP_CURVE_EXPONENT = 1.35;
        public const int XP_CURVE_ROUND_STEP = 10;

        /// <summary>XP_to_next(N) = round(120 x N^1.35 / 10) x 10.</summary>
        public static int XpToNext(
            int level,
            double base_coef = XP_CURVE_BASE_COEF,
            double exponent = XP_CURVE_EXPONENT
        )
        {
            double raw = base_coef * Math.Pow(level, exponent);
            return Numeric.RoundHalfUpToInt(raw / XP_CURVE_ROUND_STEP) * XP_CURVE_ROUND_STEP;
        }

        /// <summary>Накопленный XP, нужный чтобы дойти с 1-го уровня до указанного.</summary>
        public static int CumulativeXpToReach(int level)
        {
            int total = 0;
            for (int n = 1; n < level; n++)
                total += XpToNext(n);
            return total;
        }

        /// <summary>Уровни без нового контента: выдают изотопы и бесплатное расширение купола.</summary>
        public static readonly int[] EMPTY_LEVELS = { 4, 14, 16, 18, 19, 21 };

        public const int LEVEL_UP_ISOTOPES_BASE = 20;
        public const int LEVEL_UP_ISOTOPES_EMPTY_LEVEL_BONUS = 25;

        public sealed class LevelUpRewardInfo
        {
            public int credits;
            public int isotopes;
            public bool free_dome_expansion;
        }

        public const double LEVEL_UP_CREDITS_COEF = 100;
        public const double LEVEL_UP_CREDITS_EXPONENT = 1.2;
        public const int LEVEL_UP_CREDITS_ROUND_STEP = 10;

        /// <summary>
        /// Награда за достижение уровня N: 100 x N^1.2 кредитов, округление к
        /// десяткам. Показатель 1.2 ниже показателя стока расширений 1.5 —
        /// иначе кредиты обесценятся к двадцатому уровню (каркас, раздел 6).
        /// </summary>
        public static LevelUpRewardInfo LevelUpReward(
            int level,
            double credits_coef = LEVEL_UP_CREDITS_COEF,
            double credits_exponent = LEVEL_UP_CREDITS_EXPONENT
        )
        {
            bool is_empty = Array.IndexOf(EMPTY_LEVELS, level) >= 0;
            double raw_credits = credits_coef * Math.Pow(level, credits_exponent);
            return new LevelUpRewardInfo
            {
                credits = Numeric.RoundHalfUpToInt(raw_credits / LEVEL_UP_CREDITS_ROUND_STEP)
                    * LEVEL_UP_CREDITS_ROUND_STEP,
                isotopes = LEVEL_UP_ISOTOPES_BASE
                    + (is_empty ? LEVEL_UP_ISOTOPES_EMPTY_LEVEL_BONUS : 0),
                free_dome_expansion = is_empty,
            };
        }

        /// <summary>Пожизненный бюджет изотопов неплатящего игрока за MVP-прогрессию. Каркас обещает 550.</summary>
        public static int FreeIsotopeBudget(int max_level = MAX_LEVEL_MVP)
        {
            int total = 0;
            for (int n = 2; n <= max_level; n++)
                total += LevelUpReward(n).isotopes;
            return total;
        }

        /// <summary>Уровни открытия механик трио.</summary>
        public static readonly Dictionary<Mechanic, int> MECHANIC_UNLOCK_LEVEL = new Dictionary<
            Mechanic,
            int
        >
        {
            { Mechanic.drone, 2 },
            { Mechanic.shuttle, 5 },
            { Mechanic.liner, 12 },
        };
    }
}
