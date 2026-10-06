namespace MarsColony.Domain.Tests
{
    /// <summary>
    /// Детерминированный генератор для тестов — та же идея, что
    /// `mars-colony/src/sim/rng.ts` (`makeRng`): прогон обязан воспроизводиться
    /// по seed. Не побитовая копия JS-версии (разные платформы целочисленной
    /// арифметики), только тот же контракт: один seed — одна и та же
    /// последовательность в диапазоне [0, 1).
    /// </summary>
    public static class TestRng
    {
        public static System.Func<double> Make(uint seed)
        {
            uint a = seed;
            return () =>
            {
                a += 0x6D2B79F5;
                uint t = a;
                t = (t ^ (t >> 15)) * (1 | t);
                t ^= t + (t ^ (t >> 7)) * (61 | t);
                return ((t ^ (t >> 14)) & 0xFFFFFFFF) / 4294967296.0;
            };
        }
    }
}
