using System;

namespace MarsColony.Domain
{
    /// <summary>
    /// Арифметика, совпадающая с TypeScript-доменом побитово.
    ///
    /// `Math.Round` в C# по умолчанию округляет половину к ЧЕТНОМУ: Round(2.5)=2.
    /// В JavaScript `Math.round(2.5)=3`. Перенос формулы конфига один в один с
    /// подменой округления дает число, отличающееся от каркаса на единицу в
    /// половине случаев, и никакой тест этого не покажет, пока не попадется
    /// ровно та цена, у которой доля дает половину. Поэтому округление здесь
    /// явное, а не унаследованное от платформы.
    /// </summary>
    public static class Numeric
    {
        /// <summary>Аналог JS `Math.round`: половина всегда вверх.</summary>
        public static double RoundHalfUp(double value) => Math.Floor(value + 0.5);

        public static int RoundHalfUpToInt(double value) => (int)RoundHalfUp(value);
    }
}
