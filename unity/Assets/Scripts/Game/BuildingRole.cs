using MarsColony.Domain;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Роль объекта сцены в экономике: какой тип из спеки он изображает.
    ///
    /// Зачем нужен. Сцена собиралась как витрина, и её объекты назывались по
    /// внешнему виду: `kupol-geodezicheskiy`, `zavod-pishchevoy`, `kupol-grib`.
    /// Домен же оперирует типами каркаса (<see cref="BuildingType"/>). Пока
    /// связи не было, экономика и картинка жили порознь: интерфейс показывал
    /// ресурсы, которых нет в коде, а здания не значили ничего
    /// (дефект A5 в `BAZA-KRITIKI-2026-09-01.md`).
    ///
    /// Почему компонентом, а не таблицей имён в статическом классе. Таблица имён
    /// молча разъезжается при первом же переименовании объекта: имя меняют в
    /// сцене, таблица остаётся, ошибки нет — просто здание перестаёт считаться.
    /// Компонент едет вместе с объектом и виден в инспекторе.
    ///
    /// Роль НЕ двигает и не меняет объект: расстановка — константа заказчика.
    /// </summary>
    public sealed class BuildingRole : MonoBehaviour
    {
        /// <summary>Тип из каркаса. Ставится сборщиком ролей.</summary>
        public BuildingType type;

        /// <summary>
        /// Особые роли, которых в перечислении каркаса нет, потому что их нет
        /// и в самом каркасе: энергия заведена в Unity-треке (см. `Power`).
        /// Пусто — значит роль описывается полем <see cref="type"/>.
        /// </summary>
        public string special;

        /// <summary>Потребление энергии, кВт. Ноль у источников и у декора.</summary>
        public int consumption_kw;

        /// <summary>Выработка энергии, кВт. Ноль у потребителей.</summary>
        public int generation_kw;

        public bool IsSpecial => !string.IsNullOrEmpty(special);

        public override string ToString()
        {
            string role = IsSpecial ? special : type.ToString();
            // Роль может и вырабатывать, и потреблять: у особых ролей раньше
            // печаталась только выработка, и добывающие вышки выглядели
            // источниками энергии, которыми они не являются.
            string power = generation_kw > 0 ? $"+{generation_kw} кВт"
                         : consumption_kw > 0 ? $"-{consumption_kw} кВт"
                         : "энергонейтрален";
            return $"{name}: {role} ({power})";
        }
    }
}
