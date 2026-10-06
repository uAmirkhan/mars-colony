namespace MarsColony.Domain.Config
{
    /// <summary>
    /// Рост уровня колонии по опыту.
    ///
    /// ВНИМАНИЕ, ПРОИСХОЖДЕНИЕ ЧИСЕЛ. Как и <see cref="Power"/>, этот файл НЕ
    /// зеркало TypeScript-каркаса: правила уровня там нет вовсе. В состоянии
    /// колонии `level` заводится значением `START_LEVEL` и не меняется никогда,
    /// а `xp` копится отдельно и ни на что не влияет. Игрок видит два числа,
    /// между которыми нет связи, и роста не чувствует
    /// (дефект B11 в `BAZA-KRITIKI-2026-09-01.md`).
    ///
    /// Числа заведены в Unity-треке решением от 2026-09-01 и здесь являются
    /// источником истины.
    ///
    /// ПОЧЕМУ ИМЕННО ТАКОЙ ПОРОГ. Уровни в этой игре не абстрактный счётчик:
    /// на них завязана разблокировка товаров (`unlock_level` в <see cref="Goods"/>).
    /// Соя открывается на втором, батончик на третьем, грибы на четвёртом.
    /// Значит первые уровни обязаны браться быстро — иначе игрок сидит на одних
    /// водорослях и не видит, ради чего копит. Отсюда мягкий старт и ускоряющийся
    /// рост: 12 опыта до второго уровня, дальше шаг растёт в полтора раза.
    ///
    /// Сбор одной грядки даёт опыт равным числу товаров (K = 1 в
    /// <see cref="Economy.PRODUCTION_XP_K"/>), поэтому 12 — это примерно
    /// три-четыре сбора. Первый уровень берётся за минуту, и это намеренно.
    /// </summary>
    public static class Progress
    {
        /// <summary>Опыт, нужный чтобы уйти со второго уровня и дальше.</summary>
        public const int BAZA = 12;

        /// <summary>Во сколько раз каждый следующий уровень дороже предыдущего.</summary>
        public const double SHAG = 1.5;

        /// <summary>
        /// Сколько опыта нужно НАКОПИТЬ ВСЕГО, чтобы достичь уровня `uroven`.
        /// Для первого уровня — ноль: игра начинается уже на нём.
        /// </summary>
        public static int VsegoDlyaUrovnya(int uroven)
        {
            if (uroven <= 1)
                return 0;
            double summa = 0;
            double shag = BAZA;
            for (int i = 1; i < uroven; i++)
            {
                summa += shag;
                shag *= SHAG;
            }
            return (int)summa;
        }

        /// <summary>Какой уровень соответствует накопленному опыту.</summary>
        public static int UrovenPoOpytu(int xp)
        {
            int uroven = 1;
            while (uroven < 99 && xp >= VsegoDlyaUrovnya(uroven + 1))
                uroven++;
            return uroven;
        }

        /// <summary>
        /// Доля пройденного до следующего уровня, от нуля до единицы. На
        /// последнем уровне возвращает единицу: полоса не должна выглядеть
        /// незаполненной там, где расти больше некуда.
        /// </summary>
        public static float DolyaDoSleduyushchego(int xp)
        {
            int uroven = UrovenPoOpytu(xp);
            int nizhniy = VsegoDlyaUrovnya(uroven);
            int verhniy = VsegoDlyaUrovnya(uroven + 1);
            if (verhniy <= nizhniy)
                return 1f;
            float dolya = (xp - nizhniy) / (float)(verhniy - nizhniy);
            return dolya < 0f ? 0f : dolya > 1f ? 1f : dolya;
        }

        /// <summary>Сколько опыта осталось до следующего уровня.</summary>
        public static int OstalosDoSleduyushchego(int xp)
        {
            int uroven = UrovenPoOpytu(xp);
            int verhniy = VsegoDlyaUrovnya(uroven + 1);
            int ostatok = verhniy - xp;
            return ostatok < 0 ? 0 : ostatok;
        }
    }
}
