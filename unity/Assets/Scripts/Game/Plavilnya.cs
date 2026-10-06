using System;
using MarsColony.Domain;
using MarsColony.Domain.Config;

namespace MarsColony.Game
{
    /// <summary>
    /// Плавильня льда по эталону заказчика 06.09: непрерывный конвертер, а не очередь слотов. Игрок засыпает лёд
    /// в воронку (буфер входа до <see cref="VHOD_MAX"/>), плавильня по одной единице переводит лёд в воду
    /// за время рецепта воды из домена (<see cref="Goods"/>: 1 лёд → 1 вода), готовая вода копится в канистре
    /// (до <see cref="VYHOD_MAX"/>), пока её не заберут. Полная канистра останавливает плавку — как у Township:
    /// производство ждёт игрока, а не пропадает.
    /// </summary>
    public sealed class Plavilnya
    {
        public const int VHOD_MAX = 20, VYHOD_MAX = 5, PORTSIYA_ZAGRUZKI = 5;

        public int vhod;
        public int vyhod;
        /// <summary>Момент готовности текущей единицы (шкала Now); 0 — плавка не идёт.</summary>
        public double gotovoV;

        public static string Vhodnoy => Goods.WATER_ICE;
        public static string Vyhodnoy => Goods.WATER;

        public bool Rabotaet => gotovoV > 0 && vhod > 0 && vyhod < VYHOD_MAX;

        /// <summary>Догоняет время: сколько единиц успело расплавиться с прошлого кадра/сессии.</summary>
        public void Obnovit(double now, double dlitSek)
        {
            if (vhod <= 0 || vyhod >= VYHOD_MAX) { gotovoV = 0; return; }
            if (gotovoV <= 0) { gotovoV = now + dlitSek; return; }
            int guard = 0;
            while (now >= gotovoV && vhod > 0 && vyhod < VYHOD_MAX && guard++ < 1000)
            {
                vhod--; vyhod++;
                gotovoV = (vhod > 0 && vyhod < VYHOD_MAX) ? gotovoV + dlitSek : 0;
            }
        }

        /// <summary>Засыпать лёд со склада; возвращает, сколько реально ушло в воронку.</summary>
        public int Zagruzit(WarehouseState sklad, int skolko)
        {
            int mozhno = Math.Min(skolko, Math.Min(VHOD_MAX - vhod, Warehouse.AvailableOf(sklad, Vhodnoy)));
            if (mozhno <= 0) return 0;
            if (!Warehouse.Consume(sklad, Vhodnoy, mozhno)) return 0;
            vhod += mozhno;
            return mozhno;
        }

        /// <summary>Забрать воду на склад, сколько влезет; возвращает количество.</summary>
        public int Zabrat(WarehouseState sklad)
        {
            int mozhno = Math.Min(vyhod, Warehouse.FreeSpace(sklad));
            if (mozhno <= 0) return 0;
            if (!Warehouse.Deposit(sklad, Vyhodnoy, mozhno)) return 0;
            vyhod -= mozhno;
            return mozhno;
        }

        public double Ostalos(double now) => Rabotaet ? Math.Max(0, gotovoV - now) : 0;
    }
}
