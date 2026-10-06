using System;

namespace MarsColony.Domain
{
    /// <summary>
    /// Куда домен пишет алерты (деградация генератора заказов). Домен не знает про Unity: игра
    /// подставляет Debug.LogWarning, тесты и консоль — ничего (тогда Console.WriteLine).
    /// </summary>
    public static class DomainLog
    {
        public static Action<string> Sink;
        public static void Warn(string msg) { if (Sink != null) Sink(msg); else Console.WriteLine(msg); }
    }
}
