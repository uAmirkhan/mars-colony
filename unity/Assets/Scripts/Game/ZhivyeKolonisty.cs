using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Три колониста с маршрутами по объектам сцены. Заказчик 05.09: «убери анимацию со всех,
    /// кто двигается; двигаться должны: космонавт 08 (несёт) — от своего места у ящиков 3 до
    /// конца склада к ящикам 4 (6) и обратно, подходит вплотную, поворачивается к ящикам,
    /// разворачивается и идёт к другому; космонавт 00 (несёт) — берёт ящики у ангара, идёт по
    /// улице Восток (1), в конце поворачивает налево на улицу Главную, доходит до ящиков
    /// zamena-0 (1), поворачивается к ним; kosmonavt-dvor-otdyh-1 — подходит к краю улицы Юг,
    /// смотрит лицом на шаттл, разворачивается, идёт к улице Север и останавливается, не
    /// доходя до её начала». Он же: «космонавты ходят задом» — лицо фигуры (визор) совпадает с
    /// осью forward трансформа, проверено рендером с четырёх сторон; поворот делается вокруг
    /// центра меша, а не пивота: у dvor-otdyh-1 пивот в пяти метрах от фигуры, и разворот
    /// вокруг него швырял фигуру дугой.
    ///
    /// Ящики yashchiki-4 (6) и yashchiki-angar переименованы в площадки дронов
    /// (mesto-vzleta-dron-2 и mesto-vzleta-dron-1), маршруты ссылаются на новые имена.
    /// </summary>
    public sealed class ZhivyeKolonisty : MonoBehaviour
    {
        [Tooltip("Скорость шага, м/с.")]
        [SerializeField] private float _skorost = 0.85f;
        [Tooltip("Пауза у ящика или на точке осмотра, секунды (диапазон).")]
        [SerializeField] private float _pauzaMin = 1.5f, _pauzaMax = 3f;

        /// <summary>Подъём стопы на шаге, м; частота шагов, Гц; покачивание корпуса, градусы.</summary>
        private const float SHAG_VYSOTA = 0.05f, SHAG_CHASTOTA = 1.8f, SHAG_KREN = 3f;
        /// <summary>Скорость поворота на месте, град/с.</summary>
        private const float SKOROST_POVOROTA = 110f;
        /// <summary>Зазор между фигурой и ящиком, к которому она подходит вплотную, м.</summary>
        private const float ZAZOR_DO_YASHCHIKA = 0.12f;
        /// <summary>Отступ от кромки дороги, к которой подходит фигура, м.</summary>
        private const float OTSTUP_OT_KROMKI = 0.5f;
        /// <summary>Сколько не доходить до улицы Север, м.</summary>
        private const float NE_DOHODYA = 2f;
        /// <summary>Вынос дуги обхода от стены склада на восток, м.</summary>
        private const float DUGA_OT_STENY = 2.2f;

        private void OnEnable()
        {
            MashinyDobychi.ObespechitKollayderyGrunta();
            Zapustit(MarshrutSklad());
            Zapustit(MarshrutAngar());
            Zapustit(MarshrutShattl());
        }

        private void OnDisable() => StopAllCoroutines();

        // ------------------------------------------------------------------ маршруты

        /// <summary>Шаг маршрута: дойти до точки (по центру меша), повернуться лицом к цели, постоять.</summary>
        private struct Shag
        {
            public Vector3 tochka;        // куда встать центром меша (XZ)
            public Vector3? smotret;      // куда смотреть, встав; null — по ходу
            public bool pauza;
        }

        private sealed class Marshrut
        {
            public string figura;
            public List<Shag> shagi = new List<Shag>();
            public bool tudaObratno = true;   // после конца идти тем же путём назад
            public string sinhronSDronom;      // дрон, за круг которого фигура должна дойти до дальней точки
        }

        /// <summary>Космонавт 08: ящики 3 (своё место) ↔ ящики у конца склада (mesto-vzleta-dron-2).</summary>
        private Marshrut MarshrutSklad()
        {
            var m = new Marshrut { figura = "kosmonavt-08-figura-neset" };
            var fig = Gabarit("kosmonavt-08-figura-neset");
            var y3 = Gabarit("yashchiki-3");
            var y46 = Gabarit("mesto-vzleta-dron-2");
            if (!fig.HasValue || !y3.HasValue || !y46.HasValue) return m;
            // Прямая от места фигуры к дальним ящикам идёт внутри стены склада (замер лучом: стена в 1.3 м
            // к западу от линии, на восток свободно 9 м). Заказчик: «обходи по дуге, пошире» — дуга на восток
            // и подход к ящикам с восточной стороны, лицом к ним.
            Vector3 duga = new Vector3(fig.Value.center.x + DUGA_OT_STENY, 0f, (fig.Value.center.z + y46.Value.center.z) * 0.5f);
            m.shagi.Add(new Shag { tochka = duga });
            m.shagi.Add(Podhod(new Vector3(y46.Value.center.x + 3f, 0f, y46.Value.center.z), fig.Value.size, y46.Value));
            m.shagi.Add(new Shag { tochka = duga });
            m.shagi.Add(Podhod(new Vector3(y3.Value.center.x + 0.5f, 0f, y3.Value.center.z + 3f), fig.Value.size, y3.Value));
            m.tudaObratno = false;   // маршрут уже замкнут: дуга туда, дуга обратно
            return m;
        }

        /// <summary>Космонавт 00: ящики у ангара → улица Восток (1) → налево на Главную → ящики zamena-0 (1).</summary>
        private Marshrut MarshrutAngar()
        {
            var m = new Marshrut { figura = "kosmonavt-00-figura-neset", sinhronSDronom = "dron-1" };   // дрон-1 взлетает с тех же ящиков у ангара
            var fig = Gabarit("kosmonavt-00-figura-neset");
            var yAngar = Gabarit("mesto-vzleta-dron-1");
            var vostok = Gabarit("set-ul-vostok (1)");
            var glavnaya = Gabarit("set-ul-glavnaya");
            var yZamena = Gabarit("yashchiki-zamena-0 (1)");
            if (!fig.HasValue || !yAngar.HasValue || !vostok.HasValue || !glavnaya.HasValue || !yZamena.HasValue) return m;
            float zVostok = vostok.Value.center.z;                 // осевая улицы Восток (идёт вдоль X)
            float xGlavnaya = glavnaya.Value.center.x;             // осевая Главной (идёт вдоль Z)
            m.shagi.Add(Podhod(fig.Value, yAngar.Value));                                                  // взять ящики
            m.shagi.Add(new Shag { tochka = new Vector3(Mathf.Min(yAngar.Value.center.x - 1.5f, vostok.Value.max.x - 1f), 0f, zVostok) });   // выйти на улицу Восток
            m.shagi.Add(new Shag { tochka = new Vector3(xGlavnaya, 0f, zVostok) });                          // до конца Восток — угол с Главной
            m.shagi.Add(new Shag { tochka = new Vector3(xGlavnaya, 0f, yZamena.Value.center.z) });           // налево, по Главной до ящиков
            m.shagi.Add(Podhod(new Vector3(xGlavnaya, 0f, yZamena.Value.center.z), fig.Value.size, yZamena.Value));   // к ящикам, лицом к ним
            return m;
        }

        /// <summary>dvor-otdyh-1: к кромке улицы Юг, лицом на шаттл; развернуться и к улице Север, не доходя.</summary>
        private Marshrut MarshrutShattl()
        {
            var m = new Marshrut { figura = "kosmonavt-dvor-otdyh-1", sinhronSDronom = "dron-2" };
            var fig = Gabarit("kosmonavt-dvor-otdyh-1");
            var yug = Gabarit("set-ul-yug");
            var sever = Gabarit("set-ul-sever");
            var shattl = Gabarit("shattl-zakrytyy") ?? Gabarit("ploshchadka-shattla");
            if (!fig.HasValue || !yug.HasValue || !sever.HasValue || !shattl.HasValue) return m;
            float x = fig.Value.center.x;
            Vector3 uYuga = new Vector3(x, 0f, yug.Value.max.z + OTSTUP_OT_KROMKI + fig.Value.extents.z);   // северная кромка Юга
            Vector3 uSevera = new Vector3(x, 0f, sever.Value.min.z - NE_DOHODYA);                            // не доходя до Севера
            m.shagi.Add(new Shag { tochka = uYuga, smotret = shattl.Value.center, pauza = true });
            m.shagi.Add(new Shag { tochka = uSevera, pauza = true });
            return m;
        }

        /// <summary>Шаг «подойти вплотную к ящику»: точка на линии от фигуры к ящику на расстоянии
        /// полуширины ящика плюс полуширины фигуры плюс зазор, взгляд на ящик, пауза.</summary>
        private static Shag Podhod(Bounds figura, Bounds yashchik) => Podhod(figura.center, figura.size, yashchik);

        private static Shag Podhod(Vector3 otkuda, Vector3 razmerFigury, Bounds yashchik)
        {
            Vector3 c = yashchik.center; c.y = 0f;
            Vector3 ot = otkuda; ot.y = 0f;
            Vector3 k = c - ot; k.y = 0f;
            if (k.sqrMagnitude < 1e-4f) k = Vector3.forward;
            k.Normalize();
            float radiusYashchika = Mathf.Abs(k.x) * yashchik.extents.x + Mathf.Abs(k.z) * yashchik.extents.z;
            float radiusFigury = Mathf.Max(razmerFigury.x, razmerFigury.z) * 0.5f;
            Vector3 tochka = c - k * (radiusYashchika + radiusFigury + ZAZOR_DO_YASHCHIKA);
            // Если на этой стороне ящика кто-то стоит (другая фигура, бочка), берём ближайшую свободную сторону.
            if (!SvobodnoOtMelochi(tochka, radiusFigury, null))
            {
                for (int shag = 1; shag <= 7; shag++)
                {
                    float ugol = (shag % 2 == 1 ? 1f : -1f) * 45f * ((shag + 1) / 2);
                    Vector3 k2 = Quaternion.AngleAxis(ugol, Vector3.up) * k;
                    float r2 = Mathf.Abs(k2.x) * yashchik.extents.x + Mathf.Abs(k2.z) * yashchik.extents.z;
                    Vector3 t2 = c - k2 * (r2 + radiusFigury + ZAZOR_DO_YASHCHIKA);
                    if (SvobodnoOtMelochi(t2, radiusFigury, null)) { tochka = t2; break; }
                }
            }
            return new Shag { tochka = tochka, smotret = c, pauza = true };
        }

        // ------------------------------------------------------------------ исполнение

        private void Zapustit(Marshrut m)
        {
            var figura = NaytiUzel(m.figura);
            if (figura == null || m.shagi.Count == 0)
            {
                Debug.LogWarning($"[колонисты] {m.figura}: фигура или опорные объекты маршрута не найдены — стоит на месте");
                return;
            }
            StartCoroutine(Zhizn(figura, m));
        }

        private IEnumerator Zhizn(Transform figura, Marshrut m)
        {
            var rs = figura.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) yield break;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            // Смещение центра меша от пивота в ЛОКАЛЬНЫХ осях фигуры: поворачиваем вокруг центра меша.
            Vector3 smeshchenieLok = Quaternion.Inverse(figura.rotation) * (b.center - figura.position);
            float podem = b.center.y - b.min.y;   // центр меша над стопами

            var sb = new System.Text.StringBuilder($"[колонисты] {figura.name}: старт {b.center:F1}, шаги: ");
            foreach (var s in m.shagi) sb.Append($"{s.tochka:F1}{(s.smotret.HasValue ? " (смотреть " + s.smotret.Value.ToString("F1") + ")" : "")}; ");
            Debug.Log(sb.ToString());

            // Прямой порядок шагов, затем обратный до исходного места — и так по кругу.
            var vperyod = ObvestiPrepyatstviya(figura, b.center, m.shagi, Mathf.Max(b.extents.x, b.extents.z));
            var nazad = new List<Shag>();
            for (int i = vperyod.Count - 2; i >= 0; i--) nazad.Add(vperyod[i]);
            nazad.Add(new Shag { tochka = b.center, smotret = null, pauza = true });

            yield return new WaitForSeconds(Random.Range(0.3f, 2f));
            // Скорость: по кругу «своего» дрона — дойти до дальней точки за время, пока дрон улетит и вернётся.
            // Из круга вычтены паузы на точках и время разворотов, остаток — чистый ход.
            float skorost = _skorost;
            if (!string.IsNullOrEmpty(m.sinhronSDronom) && MarshrutyDronov.DlitelnostKruga(m.sinhronSDronom, out float krug) && krug > 5f)
            {
                float dlina = 0f; int pauz = 0; Vector3 pred = new Vector3(b.center.x, 0f, b.center.z);
                foreach (var s in vperyod) { Vector3 t = new Vector3(s.tochka.x, 0f, s.tochka.z); dlina += Vector3.Distance(pred, t); pred = t; if (s.pauza) pauz++; }
                float hod = Mathf.Max(krug - pauz * (_pauzaMin + _pauzaMax) * 0.5f - vperyod.Count * 0.8f, 5f);
                skorost = Mathf.Clamp(dlina / hod, 0.4f, 2.5f);
                Debug.Log($"[колонисты] {figura.name}: до дальней точки {dlina:0.0} м, круг {m.sinhronSDronom} {krug:0.0} с, чистый ход {hod:0.0} с, скорость {skorost:0.00} м/с");
            }
            while (true)
            {
                foreach (var s in vperyod) yield return Vypolnit(figura, s, smeshchenieLok, podem, skorost);
                if (!m.tudaObratno) continue;   // замкнутый маршрут — снова с начала
                foreach (var s in nazad) yield return Vypolnit(figura, s, smeshchenieLok, podem, skorost);
            }
        }

        private IEnumerator Vypolnit(Transform figura, Shag s, Vector3 smeshchenieLok, float podem, float skorost)
        {
            Vector3 tekushchiy = CentrMesha(figura, smeshchenieLok);
            Vector3 kurs = s.tochka - tekushchiy; kurs.y = 0f;
            if (kurs.magnitude > 0.05f)
            {
                yield return Povernutsya(figura, kurs, smeshchenieLok, podem);          // сначала лицом по ходу
                yield return Proyti(figura, tekushchiy, s.tochka, smeshchenieLok, podem, skorost);
            }
            if (s.smotret.HasValue)
            {
                Vector3 vzglyad = s.smotret.Value - CentrMesha(figura, smeshchenieLok); vzglyad.y = 0f;
                yield return Povernutsya(figura, vzglyad, smeshchenieLok, podem);
            }
            if (s.pauza) yield return new WaitForSeconds(Random.Range(_pauzaMin, _pauzaMax));
        }

        /// <summary>Идти по прямой между двумя точками центра меша, лицом по ходу, с шагом и покачиванием.</summary>
        private IEnumerator Proyti(Transform figura, Vector3 ot, Vector3 do_, Vector3 smeshchenieLok, float podem, float skorost)
        {
            Vector3 a = new Vector3(ot.x, 0f, ot.z), c = new Vector3(do_.x, 0f, do_.z);
            float dlina = Vector3.Distance(a, c);
            if (dlina < 0.02f) yield break;
            Quaternion litsom = Quaternion.LookRotation((c - a).normalized, Vector3.up);
            float vsego = dlina / Mathf.Max(skorost, 0.05f), t = 0f;
            while (t < vsego)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / vsego));
                Vector3 p = Vector3.Lerp(a, c, u);
                float faza = t * SHAG_CHASTOTA * Mathf.PI * 2f;
                float y = VysotaRelyefa(p.x, p.z) + podem + Mathf.Abs(Mathf.Sin(faza)) * SHAG_VYSOTA;
                Quaternion rot = litsom * Quaternion.AngleAxis(Mathf.Sin(faza * 0.5f) * SHAG_KREN, Vector3.forward);
                Postavit(figura, new Vector3(p.x, y, p.z), rot, smeshchenieLok);
                yield return null;
            }
            Postavit(figura, new Vector3(c.x, VysotaRelyefa(c.x, c.z) + podem, c.z), litsom, smeshchenieLok);
        }

        /// <summary>Повернуться на месте лицом в направлении napr, вращаясь вокруг центра меша.</summary>
        private IEnumerator Povernutsya(Transform figura, Vector3 napr, Vector3 smeshchenieLok, float podem)
        {
            napr.y = 0f;
            if (napr.sqrMagnitude < 1e-4f) yield break;
            Quaternion cel = Quaternion.LookRotation(napr.normalized, Vector3.up);
            Vector3 centr = CentrMesha(figura, smeshchenieLok);
            centr.y = VysotaRelyefa(centr.x, centr.z) + podem;
            while (Quaternion.Angle(figura.rotation, cel) > 0.5f)
            {
                Quaternion rot = Quaternion.RotateTowards(figura.rotation, cel, SKOROST_POVOROTA * Time.deltaTime);
                Postavit(figura, centr, rot, smeshchenieLok);
                yield return null;
            }
            Postavit(figura, centr, cel, smeshchenieLok);
        }

        /// <summary>Мелкие препятствия на отрезках маршрута (другие фигуры, ящики, бочки, столбы — всё до 3 м
        /// в плане) обходятся дугой: перед препятствием и за ним вставляются точки, сдвинутые вбок на
        /// ширину препятствия плюс фигуру плюс запас. Заказчик: «космонавт 08 проходит через текстуру —
        /// обходи по дуге, пошире». Постройки крупнее 3 м не объезжаются: маршруты заданы заказчиком.</summary>
        private static List<Shag> ObvestiPrepyatstviya(Transform figura, Vector3 start, List<Shag> shagi, float radiusFigury)
        {
            var itog = new List<Shag>();
            Vector3 ot = start; ot.y = 0f;
            foreach (var s in shagi)
            {
                Vector3 do_ = s.tochka; do_ = new Vector3(do_.x, 0f, do_.z);
                itog.AddRange(Obhod(figura, ot, do_, radiusFigury));
                itog.Add(s);
                ot = do_;
            }
            return itog;
        }

        private static List<Shag> Obhod(Transform figura, Vector3 ot, Vector3 do_, float radiusFigury)
        {
            var via = new List<Shag>();
            Vector3 os = do_ - ot; float dlina = os.magnitude;
            if (dlina < 0.5f) return via;
            os /= dlina; Vector3 bok = new Vector3(-os.z, 0f, os.x);
            float luchAlong = float.MaxValue; Bounds pomeha = default; bool est = false;
            foreach (var b in Meloch(figura))
            {
                Vector3 c = b.center - ot; c.y = 0f;
                float along = Vector3.Dot(c, os), lat = Vector3.Dot(c, bok);
                float radius = Mathf.Max(b.extents.x, b.extents.z);
                if (along < radius || along > dlina - radius) continue;                         // концы отрезка — точки стоянки, их не трогаем
                if (Mathf.Abs(lat) > radius + radiusFigury + 0.15f) continue;                 // не на пути
                if (along < luchAlong) { luchAlong = along; pomeha = b; est = true; }
            }
            if (!est) return via;
            Vector3 cp = pomeha.center - ot; cp.y = 0f;
            float latP = Vector3.Dot(cp, bok);
            float radiusP = Mathf.Max(pomeha.extents.x, pomeha.extents.z);
            float sdvig = radiusP + radiusFigury + 0.9f;                                    // «пошире»: почти метр запаса
            float storona = latP <= 0f ? 1f : -1f;                                          // обходим с той стороны, где препятствие дальше от линии
            Vector3 p1 = ot + os * Mathf.Max(luchAlong - sdvig, 0.3f) + bok * storona * sdvig;
            Vector3 p2 = ot + os * Mathf.Min(luchAlong + sdvig, dlina - 0.3f) + bok * storona * sdvig;
            if (!SvobodnoOtMelochi(p1, radiusFigury, figura) || !SvobodnoOtMelochi(p2, radiusFigury, figura))
            {
                storona = -storona;
                p1 = ot + os * Mathf.Max(luchAlong - sdvig, 0.3f) + bok * storona * sdvig;
                p2 = ot + os * Mathf.Min(luchAlong + sdvig, dlina - 0.3f) + bok * storona * sdvig;
            }
            via.Add(new Shag { tochka = p1 });
            via.Add(new Shag { tochka = p2 });
            Debug.Log($"[колонисты] {figura.name}: на отрезке {ot:F1}->{do_:F1} препятствие {pomeha.center:F1} ({radiusP * 2f:0.0} м), дуга через {p1:F1} и {p2:F1}");
            return via;
        }

        /// <summary>Мелкие препятствия сцены: всё выше 0.3 м и не шире 3 м в плане, кроме грунта, дорог и самой фигуры.</summary>
        private static List<Bounds> Meloch(Transform figura)
        {
            var spisok = new List<Bounds>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (figura != null && r.transform.IsChildOf(figura)) continue;
                var b = r.bounds; string n = r.gameObject.name.ToLower();
                if (b.size.y < 0.3f || b.size.x > 3f || b.size.z > 3f) continue;
                if (n.StartsWith("set-") || n.StartsWith("sled") || n.StartsWith("biom") || n.StartsWith("grunt") || n.StartsWith("zemlya") || n.StartsWith("oskolok") || n.StartsWith("dron")) continue;
                spisok.Add(b);
            }
            return spisok;
        }

        private static bool SvobodnoOtMelochi(Vector3 p, float radiusFigury, Transform figura)
        {
            foreach (var b in Meloch(figura))
            {
                Vector3 q = b.ClosestPoint(new Vector3(p.x, b.center.y, p.z));
                if (new Vector2(q.x - p.x, q.z - p.z).magnitude < radiusFigury + 0.1f) return false;
            }
            return true;
        }

        /// <summary>Поставить фигуру так, чтобы центр её меша оказался в centr при повороте rot.</summary>
        private static void Postavit(Transform figura, Vector3 centr, Quaternion rot, Vector3 smeshchenieLok)
        {
            figura.rotation = rot;
            figura.position = centr - rot * smeshchenieLok;
        }

        private static Vector3 CentrMesha(Transform figura, Vector3 smeshchenieLok) => figura.position + figura.rotation * smeshchenieLok;

        // ------------------------------------------------------------------ сцена

        private static Bounds? Gabarit(string imya)
        {
            var t = NaytiUzel(imya);
            if (t == null) return null;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return null;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static Transform NaytiUzel(string imya)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == imya) return t;
            return null;
        }

        private static float VysotaRelyefa(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 90f, z), Vector3.down, 200f, ~0);
            float verh = -1000f;
            foreach (var h in hits)
            {
                if (MashinyDobychi.NeGrunt(h.collider.gameObject.name)) continue;
                if (h.point.y > verh) verh = h.point.y;
            }
            return verh > -999f ? verh : 0f;
        }
    }
}
