using System.Collections.Generic;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Машина, которая едет по замкнутому маршруту и разворачивается по ходу.
    ///
    /// Зачем. Дорожная сеть в сцене построена целиком, а по ней никто не ездит:
    /// оба критика назвали это первым, что убивает ощущение живой колонии
    /// (дефекты C1d и C2 в `BAZA-KRITIKI-2026-09-01.md`). Неподвижная техника на
    /// готовых дорогах читается не как «пауза», а как «игра не запущена».
    ///
    /// Почему свой компонент, а не аниматор. Маршрут задаётся точками в мировых
    /// координатах и правится числом в инспекторе; клип аниматора пришлось бы
    /// перезаписывать при каждом сдвиге дороги. Плюс высота берётся лучом по
    /// рельефу, а не запекается: грунт под дорогами неровный, и машина с
    /// запечённой высотой то ныряет, то висит.
    ///
    /// Компонент НИЧЕГО не решает про экономику: это чистая витрина движения.
    /// </summary>
    public sealed class EzditPoMarshrutu : MonoBehaviour
    {
        /// <summary>Точки маршрута в мировых координатах. Замыкается автоматически.</summary>
        public List<Vector3> tochki = new List<Vector3>();

        /// <summary>Метров в секунду. Казуальная скорость: заметно, но не суетливо.</summary>
        public float skorost = 2.2f;

        /// <summary>Градусов в секунду на развороте.</summary>
        public float skorost_povorota = 120f;

        /// <summary>Насколько близко надо подойти к точке, чтобы взять следующую.</summary>
        public float dopusk = 0.35f;

        /// <summary>
        /// Приподнять над найденной землёй. Ноль означает «поставить пузом в
        /// грунт»: у гусеничной техники пивот обычно в центре корпуса.
        /// </summary>
        public float podnyat = 0.0f;

        /// <summary>Сколько ждать в конечных точках, секунд. Ноль — не ждать.</summary>
        public float pauza_na_kontse = 1.5f;

        private int _tsel;
        private float _zhdu;

        private void Start()
        {
            if (tochki.Count > 1)
            {
                transform.position = PoZemle(tochki[0]);
                _tsel = 1;
            }
        }

        private void Update()
        {
            if (tochki.Count < 2)
                return;

            if (_zhdu > 0f)
            {
                _zhdu -= Time.deltaTime;
                return;
            }

            Vector3 tsel = PoZemle(tochki[_tsel]);
            Vector3 tut = transform.position;
            Vector3 kuda = tsel - tut;
            kuda.y = 0f;

            if (kuda.sqrMagnitude <= dopusk * dopusk)
            {
                // Конечные точки маршрута — это места работы: там машина стоит,
                // иначе она мечется по кругу и читается заводной игрушкой.
                bool konets = _tsel == 0 || _tsel == tochki.Count - 1;
                if (konets && pauza_na_kontse > 0f)
                    _zhdu = pauza_na_kontse;

                _tsel = (_tsel + 1) % tochki.Count;
                return;
            }

            Quaternion nado = Quaternion.LookRotation(kuda.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, nado, skorost_povorota * Time.deltaTime);

            Vector3 shag = transform.forward * skorost * Time.deltaTime;
            Vector3 novo = tut + shag;
            transform.position = PoZemle(novo);
        }

        /// <summary>
        /// Посадить точку на землю лучом сверху. Если под точкой ничего нет —
        /// высота остаётся прежней: молча уронить машину под рельеф хуже, чем
        /// оставить её на месте.
        /// </summary>
        public Vector3 PoZemle(Vector3 tochka)
        {
            RaycastHit hit;
            Vector3 sverhu = new Vector3(tochka.x, tochka.y + 30f, tochka.z);
            if (Physics.Raycast(sverhu, Vector3.down, out hit, 120f))
                return new Vector3(tochka.x, hit.point.y + podnyat, tochka.z);
            return tochka;
        }

        /// <summary>Длина маршрута по замкнутому кольцу, метров.</summary>
        public float Dlina()
        {
            float s = 0f;
            for (int i = 0; i < tochki.Count; i++)
                s += Vector3.Distance(tochki[i], tochki[(i + 1) % tochki.Count]);
            return s;
        }

        private void OnDrawGizmosSelected()
        {
            if (tochki == null || tochki.Count < 2)
                return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < tochki.Count; i++)
            {
                Gizmos.DrawSphere(tochki[i], 0.3f);
                Gizmos.DrawLine(tochki[i], tochki[(i + 1) % tochki.Count]);
            }
        }
    }
}
