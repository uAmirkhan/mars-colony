using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Живые машины колонии (заказчик, 05.09 вечер, несколько уточнений подряд).
    ///
    /// Ледосборщики: «подъезжают к глыбе, пока не коснутся буром, около 5 секунд добывают
    /// (глыба шатается, летят льдинки), отъезжают на своё место, стоят около 3 секунд и снова
    /// подъезжают; у каждой машины свой такт, несинхронно; глыба не падает и не кренится —
    /// просто шатается; бур — тонкий конец машины, он должен смотреть в лёд». Касание считается
    /// лучом по коллайдеру глыбы на высоте кончика бура: лёд низкополигональный, по вершинам
    /// кромку не поймать, а по габариту машина вставала с зазором в метр-полтора.
    ///
    /// Реголитосборщик: «в яме, за буровой 02 (в дальней части), ездит челноком туда-обратно,
    /// на буровую не заезжает, следа не оставляет, камни на пути убирает; корпус едет прямо,
    /// крутится только буровая насадка (дочерний узел nasadka, отделён от корпуса)». Линия
    /// челнока считается от контура дна ямы и северного края буровой на уровне дна.
    ///
    /// Осколки льда — рантайм-объекты, в сцену не сохраняются. Коллайдеры грунтовым патчам,
    /// дорогам и льду добавляются на старте на слой Ignore Raycast: без них луч рельефа видел
    /// только общую землю, техника висела над ледяной площадкой, а колонист шёл под дорогой.
    /// </summary>
    public sealed class MashinyDobychi : MonoBehaviour
    {
        [System.Serializable]
        public struct Zadanie
        {
            public string mashina;
            public string tsel;
        }

        [Tooltip("Ледосборщик -> его льдина. Заказчик: 02 и 03 к глыбе 13, 01 к гребню 11.")]
        [SerializeField]
        private Zadanie[] _ledosbory =
        {
            new Zadanie { mashina = "mashina-ledosbor-02", tsel = "gen-glyba-13" },
            new Zadanie { mashina = "mashina-ledosbor-03", tsel = "gen-glyba-13" },
            new Zadanie { mashina = "mashina-ledosbor-01", tsel = "gen-greben-11" },
        };

        [SerializeField] private string _regolitosbor = "mashina-regolitosbor";
        [SerializeField] private string _burovaya = "burovaya-02";
        [Tooltip("С какой стороны буровой ездит челнок: +1 — север (дальняя от камеры часть ямы, как на рисунке заказчика), -1 — юг.")]
        [SerializeField] private int _storonaChelnoka = 1;
        [Tooltip("Челнок вне ямы: машина ездит по поверхности вдоль оси Z от своего места, яму и буровую не ищет. Khan 08.09: в яме машина всё время пряталась за вышкой.")]
        [SerializeField] private bool _vneYamy = false;

        [Header("Скорости, м/с")]
        [SerializeField] private float _skorostPodezda = 2.2f;
        [SerializeField] private float _skorostChelnoka = 2.0f;

        /// <summary>Зазор между кончиком бура и кромкой льда, м.</summary>
        private const float ZAZOR_DO_LDA = 0.05f;
        /// <summary>Добыча около 5 с и отдых около 3 с — у каждой машины свои.</summary>
        private const float T_DOBYCHI_MIN = 4.5f, T_DOBYCHI_MAX = 6f;
        private const float T_OTDYHA_MIN = 2.5f, T_OTDYHA_MAX = 3.8f;
        /// <summary>Сдвиг глыбы при бурении, м.</summary>
        private const float AMPLITUDA_TRYASKI = 0.05f;
        private const float OSKOLKOV_V_SEKUNDU = 3.5f;
        /// <summary>Обороты бура-шнека ледосборщика при добыче, град/с. Знак минус — по часовой стрелке,
        /// если смотреть сзади машины вперёд (в Unity положительный поворот виден по часовой с конца оси).</summary>
        private const float SKOROST_BURA = -220f;
        private const float T_ZHIZNI_OSKOLKA = 0.9f;
        /// <summary>Челнок: отступ концов линии от стенок ямы и от края буровой, м.</summary>
        private const float OTSTUP_OT_STENKI = 3.0f, ZAZOR_OT_BUROVOY = 2.5f;
        /// <summary>Тот же зазор, когда полоса узкая: лучше проехать в притирку, чем уйти на другую сторону ямы.</summary>
        private const float ZAZOR_VPRITYK = 1.2f;
        /// <summary>Длина хода челнока, м: короткий ход у правого края ямы, где машину видно и можно кликнуть.</summary>
        private const float DLINA_CHELNOKA = 3.5f;
        /// <summary>Ход челнока вне ямы, м: на поверхности места больше (Khan 08.09 расчистил край карьера).</summary>
        private const float DLINA_VNE_YAMY = 7f;
        /// <summary>Скорость разворота челнока на месте, град/с.</summary>
        private const float SKOROST_RAZVOROTA = 110f;
        /// <summary>Радиус, в котором реголитосборщик убирает камни, м.</summary>
        private const float RADIUS_UBORKI = 3.2f;
        /// <summary>Обороты буровой насадки реголитосборщика, град/с.</summary>
        private const float SKOROST_NASADKI = 240f;
        /// <summary>Зазор между низом габарита и грунтом при посадке.</summary>
        private const float OTSTUP_OT_GRUNTA = 0.03f;

        private static readonly Dictionary<Transform, int> _burilshchiki = new Dictionary<Transform, int>();

        /// <summary>Игровой режим (ночь 3): добыча идёт только по тапу игрока — флаги ставит IgraKolonii по
        /// состоянию площадок. Без игры оба истинны, и техника живёт как декорация.</summary>
        public static bool DobychaLdaIdet = true, DobychaRegolitaIdet = true;

        private void OnEnable()
        {
            ObespechitKollayderyGrunta();
            ObespechitKollayderyLda();
            foreach (var z in _ledosbory)
            {
                var m = Nayti(z.mashina);
                var l = Nayti(z.tsel);
                if (m == null || l == null)
                {
                    Debug.LogWarning($"[машины] не найдены '{z.mashina}' или '{z.tsel}' — бурение не запущено");
                    continue;
                }
                StartCoroutine(RabotaLedosbora(m, l));
            }

            var reg = Nayti(_regolitosbor);
            var bur = NaytiPoPrefiksu(_burovaya);
            if (reg == null || bur == null) Debug.LogWarning($"[машины] не найдены '{_regolitosbor}' или '{_burovaya}*' — челнок не запущен");
            else StartCoroutine(ChelnokVYame(reg, bur));
        }

        private void OnDisable() => StopAllCoroutines();

        // ---------------------------------------------------------------- лёд

        /// <summary>Один ледосборщик: подъехать до касания бура с глыбой, добывать несколько секунд,
        /// отъехать задним ходом на стоянку, передохнуть и снова. Такты у машин свои.</summary>
        private IEnumerator RabotaLedosbora(Transform mashina, Transform led)
        {
            var rMash = mashina.GetComponentInChildren<Renderer>();
            var rLed = led.GetComponentInChildren<Renderer>();
            if (rMash == null || rLed == null) yield break;

            Vector3 startPoza = mashina.position;
            Quaternion startPovorot = mashina.rotation;
            Vector3 pered = PeredLedosbora(mashina);            // тонкий конец длинной оси — там бур
            Transform bur = NaytiRebenka(mashina, "bur");        // шнек отделён от корпуса в дочерний узел, крутится отдельно
            Vector3 osBuraLok = Vector3.forward;   // узел bur повёрнут так, что его Z — ось шнека (3D-PCA меша), пивот на этой оси
            if (bur == null) Debug.LogWarning($"[машины] у {mashina.name} нет дочернего узла bur — шнек не крутится");
            float podem = startPoza.y - rMash.bounds.min.y + OTSTUP_OT_GRUNTA;

            Vector3 centrLda = rLed.bounds.center; centrLda.y = 0f;
            Vector3 otMashiny = startPoza; otMashiny.y = 0f;
            Vector3 kLdu = centrLda - otMashiny; kLdu.y = 0f;
            float distanciya = kLdu.magnitude;
            kLdu /= Mathf.Max(distanciya, 0.001f);

            // Кончик бура: самая дальняя вперёд вершина машины; кромка льда — лучом на его высоте.
            float vyletBura = VyletVperyod(mashina, pered, out float yKonchika);
            float kromka = KromkaLuchom(led, new Vector3(startPoza.x, yKonchika, startPoza.z), kLdu);
            string sposob = "лучом";
            if (kromka <= 0f) { kromka = distanciya - RadiusVNapravlenii(rLed.bounds, kLdu); sposob = "по габариту (луч не попал)"; }
            float put = Mathf.Max(kromka - vyletBura - ZAZOR_DO_LDA, 0.5f);   // сколько ехать от стоянки до касания
            Vector3 tochkaKasaniya = startPoza + kLdu * put;
            Vector3 kasanie = otMashiny + kLdu * kromka; kasanie.y = yKonchika;

            Debug.Log($"[машины] {mashina.name} -> {led.name}: до кромки {kromka:0.00} м ({sposob}), вылет бура {vyletBura:0.00} м "
                    + $"на высоте {yKonchika:0.0}, ход до касания {put:0.00} м, касание {kasanie:F1}, перёд {pered:F2}");

            yield return new WaitForSeconds(Random.Range(0f, 4f));   // каждый в своём такте, не строем
            while (true)
            {
                while (!DobychaLdaIdet) yield return null;   // ждём тапа игрока по площадке
                yield return Proehat(mashina, rMash, startPovorot, mashina.position, tochkaKasaniya, pered, kLdu, _skorostPodezda, podem);
                yield return Dobycha(led, kasanie, kLdu, Random.Range(T_DOBYCHI_MIN, T_DOBYCHI_MAX), bur, osBuraLok);
                // Задним ходом: курс остаётся к льду, машина не разворачивается.
                yield return Proehat(mashina, rMash, startPovorot, mashina.position, startPoza, pered, kLdu, _skorostPodezda, podem);
                yield return new WaitForSeconds(Random.Range(T_OTDYHA_MIN, T_OTDYHA_MAX));
            }
        }

        /// <summary>Добыча: пока бур в глыбе, она трясётся (одна тряска на глыбу, сколько бы машин
        /// её ни бурили) и из точки касания летят льдинки.</summary>
        private IEnumerator Dobycha(Transform led, Vector3 kasanie, Vector3 otMashiny, float dlitelnost, Transform bur, Vector3 osBuraLok)
        {
            if (_burilshchiki.TryGetValue(led, out int n) && n > 0) _burilshchiki[led] = n + 1;
            else { _burilshchiki[led] = 1; StartCoroutine(TryaskaGlyby(led)); }
            var matOskolka = MaterialLda(led);
            float t = 0f, sledushchiy = 0f;
            while (t < dlitelnost)
            {
                t += Time.deltaTime;
                if (t >= sledushchiy) { sledushchiy += 1f / OSKOLKOV_V_SEKUNDU; StartCoroutine(Oskolok(kasanie, otMashiny, matOskolka)); }
                if (bur != null) bur.Rotate(osBuraLok, SKOROST_BURA * Time.deltaTime, Space.Self);
                yield return null;
            }
            if (_burilshchiki.TryGetValue(led, out n)) _burilshchiki[led] = Mathf.Max(0, n - 1);
        }

        /// <summary>Одна тряска на глыбу: мелкий сдвиг по трём осям и едва заметный поворот вокруг
        /// вертикали. Крена нет по построению — наклонных осей в тряске не бывает.</summary>
        private IEnumerator TryaskaGlyby(Transform led)
        {
            Vector3 pokoyPoz = led.position; Quaternion pokoyRot = led.rotation;
            // Своя фаза и частота на каждую глыбу: заказчик — «глыба 13 и гребень 11 не должны трястись в такт».
            float faza = Random.Range(0f, 100f), chastota = Random.Range(0.8f, 1.25f);
            while (_burilshchiki.TryGetValue(led, out int n) && n > 0)
            {
                float t = Time.time * chastota + faza;
                Vector3 sdvig = new Vector3(Mathf.Sin(t * 37f), Mathf.Sin(t * 53f + 1f) * 0.5f, Mathf.Cos(t * 41f)) * AMPLITUDA_TRYASKI;
                led.position = pokoyPoz + sdvig;
                led.rotation = Quaternion.AngleAxis(Mathf.Sin(t * 29f) * 0.35f, Vector3.up) * pokoyRot;
                yield return null;
            }
            led.position = pokoyPoz; led.rotation = pokoyRot;
            _burilshchiki.Remove(led);
        }

        /// <summary>Кусочек льда: вылетает из точки касания к машине, падает, исчезает.</summary>
        private IEnumerator Oskolok(Vector3 tochka, Vector3 otMashiny, Material mat)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "oskolok-lda";
            var kol = g.GetComponent<Collider>(); if (kol != null) Destroy(kol);
            float razmer = Random.Range(0.12f, 0.28f);
            g.transform.localScale = Vector3.one * razmer;
            g.transform.position = tochka + Random.insideUnitSphere * 0.25f;
            g.transform.rotation = Random.rotation;
            var rend = g.GetComponent<MeshRenderer>();
            if (mat != null) rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vector3 skorost = -otMashiny * Random.Range(1.2f, 2.6f)
                            + Vector3.up * Random.Range(1.6f, 3.0f)
                            + Vector3.Cross(Vector3.up, otMashiny) * Random.Range(-1.2f, 1.2f);
            Vector3 vrashchenie = Random.insideUnitSphere * 260f;
            float t = 0f;
            while (t < T_ZHIZNI_OSKOLKA)
            {
                t += Time.deltaTime;
                skorost += Vector3.down * 9.8f * Time.deltaTime;
                g.transform.position += skorost * Time.deltaTime;
                g.transform.Rotate(vrashchenie * Time.deltaTime, Space.World);
                float k = 1f - t / T_ZHIZNI_OSKOLKA;
                g.transform.localScale = Vector3.one * razmer * Mathf.Clamp01(k * 1.4f);
                yield return null;
            }
            Destroy(g);
        }

        private static Material MaterialLda(Transform led)
        {
            var r = led.GetComponentInChildren<Renderer>();
            return r != null ? r.sharedMaterial : null;
        }

        // ------------------------------------------------------- реголит

        /// <summary>Реголитосборщик в яме: челнок туда-обратно по прямой в дальней части ямы за
        /// буровой, между её краем и стенкой. Следа не оставляет, камни на пути убирает, корпус
        /// едет прямо, на концах разворачивается на месте, крутится только насадка.</summary>
        private IEnumerator ChelnokVYame(Transform mashina, Transform burovaya)
        {
            var rMash = mashina.GetComponentInChildren<Renderer>();
            if (rMash == null) yield break;
            var yama = _vneYamy ? (Bounds?)new Bounds(mashina.position, Vector3.one) : NaytiYamu(burovaya);
            if (!yama.HasValue) { Debug.LogWarning("[машины] яма вокруг буровой не найдена — челнок не запущен"); yield break; }

            Bounds bYama = yama.Value;
            Quaternion ishodnyy = mashina.rotation;
            Transform nasadka = NaytiRebenka(mashina, "nasadka");
            Vector3 pered = PeredRegolitosbora(mashina, nasadka);
            float podem = mashina.position.y - rMash.bounds.min.y + OTSTUP_OT_GRUNTA;
            float poluShirina = PoluShirina(mashina, pered);           // поперёк хода, по вершинам: AABB повёрнутой машины почти квадратный
            float poluDlina = Mathf.Max(VyletVperyod(mashina, pered, out _), VyletNazad(mashina, pered)) + 0.3f;   // на концах к стенке обращена корма — она длиннее носа
            if (nasadka == null) Debug.LogWarning("[машины] у реголитосборщика нет дочернего узла nasadka — насадка не вращается");

            // Сначала сторона из настройки, затем противоположная: с одной стороны буровой полосы может
            // не быть вовсе, и лучше ездить с другой, чем сквозь вышку.
            // Сторона из настройки с нормальным зазором, она же впритык, и только потом другая сторона:
            // заказчик просил дальнюю часть ямы, менять её стоит лишь когда машина туда физически не влезает.
            Vector3 a = Vector3.zero, b = Vector3.zero;
            int storona = 0; float zazor = 0f;
            if (_vneYamy)
            {
                // Поверхность у края карьера: линия вдоль Z от места стоянки, яму и буровую не ищем.
                Vector3 p0 = mashina.position; p0.y = 0f;
                a = p0 + Vector3.back * (DLINA_VNE_YAMY * 0.5f); b = p0 + Vector3.forward * (DLINA_VNE_YAMY * 0.5f);
                storona = 1; zazor = ZAZOR_OT_BUROVOY;
            }
            else foreach (var popytka in new[] { (_storonaChelnoka, ZAZOR_OT_BUROVOY), (_storonaChelnoka, ZAZOR_VPRITYK), (-_storonaChelnoka, ZAZOR_OT_BUROVOY), (-_storonaChelnoka, ZAZOR_VPRITYK) })
            {
                if (!LiniyaChelnoka(bYama, burovaya, popytka.Item1, poluShirina, poluDlina, rMash.bounds.size.y, popytka.Item2, out a, out b)) continue;
                storona = popytka.Item1; zazor = popytka.Item2; break;
            }
            if (storona == 0)
            {
                Debug.LogWarning("[машины] ни с одной стороны буровой нет полосы для челнока — реголитосборщик стоит");
                yield break;
            }
            if (storona != _storonaChelnoka || zazor != ZAZOR_OT_BUROVOY)
                Debug.Log($"[машины] челнок пущен со стороны {storona} с зазором {zazor:0.0} м (просили сторону {_storonaChelnoka})");
            Debug.Log($"[машины] {mashina.name} челнок в яме: дно {bYama.min.x:0.0}..{bYama.max.x:0.0} x {bYama.min.z:0.0}..{bYama.max.z:0.0}, "
                    + $"линия {a:F1} -> {b:F1} ({Vector3.Distance(a, b):0.0} м), подъём пивота {podem:0.00} м");

            // Сначала к ближнему концу линии.
            Vector3 tekushchaya = mashina.position; tekushchaya.y = 0f;
            Vector3 ot = Vector3.Distance(tekushchaya, a) <= Vector3.Distance(tekushchaya, b) ? a : b;
            Vector3 do_ = ot == a ? b : a;
            Vector3 kNachalu = ot - tekushchaya;
            if (kNachalu.magnitude > 0.1f)
            {
                yield return Razvernutsya(mashina, ishodnyy, pered, kNachalu.normalized, nasadka);
                yield return Proehat(mashina, rMash, ishodnyy, mashina.position, ot, pered, kNachalu.normalized, _skorostPodezda, podem, nasadka, true);
            }

            while (true)
            {
                while (!DobychaRegolitaIdet) { if (nasadka != null) { } yield return null; }
                Vector3 kurs = (do_ - ot).normalized;
                yield return Razvernutsya(mashina, ishodnyy, pered, kurs, nasadka);
                yield return Proehat(mashina, rMash, ishodnyy, mashina.position, do_, pered, kurs, _skorostChelnoka, podem, nasadka, true);
                yield return new WaitForSeconds(0.5f);
                var t = ot; ot = do_; do_ = t;
            }
        }

        /// <summary>Линия челнока: вдоль X, посередине между краем буровой (на уровне дна) и стенкой
        /// ямы с заданной стороны, концы отступают от стенок. Ложь — полосы шириной в машину нет.</summary>
        private static bool LiniyaChelnoka(Bounds bYama, Transform burovaya, int storona, float poluShirina, float poluDlina, float vysotaMashiny, float zazorOtBurovoy, out Vector3 a, out Vector3 b)
        {
            a = b = Vector3.zero;
            float dno = bYama.min.y;
            // Край буровой на уровне земли: вершины ниже крыши машины — то, во что она может упереться.
            // Верх вышки не в счёт, но и близко к ней вставать нельзя: в изометрии машина, проехавшая
            // впритирку, читается как едущая внутри буровой (Khan 07.09) — за это отвечает зазор.
            float krayBur = storona > 0 ? float.MinValue : float.MaxValue;
            var rBur = burovaya.GetComponentsInChildren<Renderer>();
            if (rBur.Length == 0) return false;
            Bounds bBur = rBur[0].bounds;
            foreach (var r in rBur) bBur.Encapsulate(r.bounds);
            foreach (var mf in burovaya.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = mf.transform.TransformPoint(v);
                    if (w.y > dno + vysotaMashiny) continue;
                    if (storona > 0) krayBur = Mathf.Max(krayBur, w.z); else krayBur = Mathf.Min(krayBur, w.z);
                }
            }
            // Меши бывают нечитаемыми — тогда габарит целиком, он заведомо не меньше подошвы.
            if (krayBur == float.MinValue || krayBur == float.MaxValue) krayBur = storona > 0 ? bBur.max.z : bBur.min.z;
            float stenka = storona > 0 ? bYama.max.z : bYama.min.z;
            float svobodno = Mathf.Abs(stenka - krayBur);
            // Ровная полоса дна между краем буровой и стенкой: край платформы буровой поднят на метр,
            // а стенка пологая, поэтому середину ищем по рельефу на трёх продольных линиях (центр ямы и ±2.5 м).
            float xc = bYama.center.x, z0 = Mathf.Min(krayBur, stenka), z1 = Mathf.Max(krayBur, stenka);
            float[] sdvigi = { -2.5f, 0f, 2.5f };
            float dnoPolosy = float.MaxValue;
            for (float zz = z0; zz <= z1; zz += 0.25f) foreach (float dx in sdvigi) dnoPolosy = Mathf.Min(dnoPolosy, VysotaRelyefa(xc + dx, zz));
            float luchA = float.NaN, luchB = float.NaN, tekA = float.NaN;
            for (float zz = z0; zz <= z1 + 0.3f; zz += 0.25f)
            {
                bool rovno = zz <= z1;
                if (rovno) foreach (float dx in sdvigi) if (VysotaRelyefa(xc + dx, zz) > dnoPolosy + 0.35f) rovno = false;
                if (rovno && float.IsNaN(tekA)) tekA = zz;
                if (!rovno && !float.IsNaN(tekA)) { if (float.IsNaN(luchA) || zz - tekA > luchB - luchA) { luchA = tekA; luchB = zz - 0.25f; } tekA = float.NaN; }
            }
            float z;
            if (!float.IsNaN(luchA) && luchB - luchA >= poluShirina * 2f + 0.3f)
            {
                z = (luchA + luchB) * 0.5f;
                // Ровная полоса считается по рельефу и про саму буровую ничего не знает: без этой
                // проверки челнок ездил прямо сквозь вышку (Khan 07.09).
                float bortBur = krayBur + storona * (zazorOtBurovoy + poluShirina);
                if (storona > 0) z = Mathf.Max(z, bortBur); else z = Mathf.Min(z, bortBur);
                float predelStenki = stenka - storona * poluShirina;
                if (storona > 0 ? z > predelStenki : z < predelStenki) return false;   // между буровой и стенкой машина не помещается
            }
            else
            {
                z = (krayBur + stenka) * 0.5f;
                float minOtBur = krayBur + storona * (zazorOtBurovoy + poluShirina);
                float maxOtStenki = stenka - storona * OTSTUP_OT_STENKI;
                if (storona > 0) z = Mathf.Clamp(z, minOtBur, maxOtStenki); else z = Mathf.Clamp(z, maxOtStenki, minOtBur);
                if (svobodno < poluShirina * 2f + zazorOtBurovoy + 0.6f) return false;
            }
            // Концы — по ровному дну: стенки ямы пологие, и отступ от контура в метрах их не ловит.
            float xMin = bYama.min.x, xMax = bYama.max.x, dnoLinii = float.MaxValue;
            for (float x = xMin; x <= xMax; x += 0.25f) dnoLinii = Mathf.Min(dnoLinii, VysotaRelyefa(x, z));
            float xA = float.NaN, xB = float.NaN;
            for (float x = xMin; x <= xMax; x += 0.25f)
            {
                bool rovno = VysotaRelyefa(x, z) < dnoLinii + 0.35f;
                if (rovno && float.IsNaN(xA)) xA = x;
                if (rovno) xB = x;
            }
            if (float.IsNaN(xA)) { xA = xMin + OTSTUP_OT_STENKI; xB = xMax - OTSTUP_OT_STENKI; }
            xA += poluDlina; xB -= poluDlina;
            // Линия жмётся к правому (по камере: +x) краю ямы и не длиннее DLINA_CHELNOKA: с левого края
            // машину закрывала вышка, и по ней нельзя было попасть кликом (Khan 07.09).
            xA = Mathf.Max(xA, xB - DLINA_CHELNOKA);
            a = new Vector3(xA, 0f, z);
            b = new Vector3(xB, 0f, z);
            Debug.Log($"[машины] край буровой на уровне дна z={krayBur:0.0}, стенка z={stenka:0.0}, свободно {svobodno:0.0} м (машина {poluShirina * 2f:0.0} м поперёк), линия на z={z:0.0} (ровная полоса z={luchA:0.0}..{luchB:0.0}), ровное дно x={xA:0.0}..{xB:0.0}");
            return Vector3.Distance(a, b) > 3f;
        }

        /// <summary>Яма вокруг буровой: самый тесный контур дна (yama/vystilka) рядом с ней.</summary>
        private static Bounds? NaytiYamu(Transform burovaya)
        {
            var bBur = burovaya.GetComponentInChildren<Renderer>().bounds;
            Bounds? luchshaya = null;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                string n = r.gameObject.name.ToLower();
                if (!(n.StartsWith("yama") || n.StartsWith("vystilka"))) continue;
                var b = r.bounds;
                if (Mathf.Abs(b.center.x - bBur.center.x) > 12f || Mathf.Abs(b.center.z - bBur.center.z) > 12f) continue;
                if (!luchshaya.HasValue || b.size.x * b.size.z < luchshaya.Value.size.x * luchshaya.Value.size.z) luchshaya = b;
            }
            return luchshaya;
        }

        /// <summary>Разворот на месте лицом по курсу; насадка крутится и во время разворота.</summary>
        private static IEnumerator Razvernutsya(Transform mashina, Quaternion ishodnyy, Vector3 pered, Vector3 kurs, Transform nasadka)
        {
            Quaternion cel = PovorotPoKursu(ishodnyy, pered, kurs);
            while (Quaternion.Angle(mashina.rotation, cel) > 0.5f)
            {
                mashina.rotation = Quaternion.RotateTowards(mashina.rotation, cel, SKOROST_RAZVOROTA * Time.deltaTime);
                if (nasadka != null) nasadka.Rotate(Vector3.up, SKOROST_NASADKI * Time.deltaTime, Space.World);
                yield return null;
            }
            mashina.rotation = cel;
        }

        /// <summary>Камни на пути машина убирает: они гаснут, когда она подходит вплотную.</summary>
        private void UbratKamniRyadom(Vector3 tochka)
        {
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var go = r.gameObject;
                string n = go.name.ToLower();
                if (!(n.StartsWith("kamen") || n.StartsWith("valun") || n.StartsWith("gen-kamen") || n.StartsWith("stone"))) continue;
                var b = r.bounds;
                if (b.size.x > 6f || b.size.z > 6f) continue;              // крупные скалы не трогаем
                Vector3 d = b.center - tochka; d.y = 0f;
                if (d.sqrMagnitude > RADIUS_UBORKI * RADIUS_UBORKI) continue;
                StartCoroutine(UbratKamen(go));
            }
        }

        private IEnumerator UbratKamen(GameObject kamen)
        {
            if (!kamen.activeSelf) yield break;
            Vector3 masshtab = kamen.transform.localScale;
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                kamen.transform.localScale = masshtab * Mathf.Clamp01(1f - t / 0.35f);
                yield return null;
            }
            kamen.SetActive(false);
            kamen.transform.localScale = masshtab;   // масштаб возвращаем: объект просто скрыт, а не испорчен
            Debug.Log($"[машины] реголитосборщик убрал камень {kamen.name}");
        }

        // ------------------------------------------------------- общее

        /// <summary>Проехать по прямой между двумя точками (XZ), сажая машину на грунт по низу
        /// габарита; перёд доводится к курсу. Насадка, если дана, крутится; камни, если надо, убираются.</summary>
        private IEnumerator Proehat(Transform mashina, Renderer rMash, Quaternion ishodnyy, Vector3 ot, Vector3 do_, Vector3 osMashiny, Vector3 kurs,
                                    float skorost, float podem, Transform nasadka = null, bool ubiratKamni = false)
        {
            Vector3 a = new Vector3(ot.x, 0f, ot.z), b = new Vector3(do_.x, 0f, do_.z);
            float dlina = Vector3.Distance(a, b);
            if (dlina < 0.05f) yield break;
            Quaternion povorot = PovorotPoKursu(ishodnyy, osMashiny, kurs);
            float vsego = dlina / Mathf.Max(skorost, 0.1f), t = 0f;
            while (t < vsego)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / vsego));
                Vector3 p = Vector3.Lerp(a, b, u);
                mashina.position = new Vector3(p.x, VysotaPivota(rMash, p.x, p.z, podem), p.z);
                mashina.rotation = Quaternion.Slerp(mashina.rotation, povorot, Time.deltaTime * 3f);
                if (nasadka != null) nasadka.Rotate(Vector3.up, SKOROST_NASADKI * Time.deltaTime, Space.World);
                if (ubiratKamni) UbratKamniRyadom(p);
                yield return null;
            }
            mashina.rotation = povorot;
        }

        /// <summary>Поворот вокруг вертикали так, чтобы перёд машины смотрел по курсу. Ось «перёд» измерена
        /// в исходной позе, поэтому и поворот считается от исходной позы, а не от текущей.</summary>
        private static Quaternion PovorotPoKursu(Quaternion ishodnyy, Vector3 osMashinyIsh, Vector3 kurs)
        {
            Vector3 a = osMashinyIsh; a.y = 0f;
            Vector3 b = kurs; b.y = 0f;
            if (a.sqrMagnitude < 1e-4f || b.sqrMagnitude < 1e-4f) return ishodnyy;
            return Quaternion.AngleAxis(Vector3.SignedAngle(a.normalized, b.normalized, Vector3.up), Vector3.up) * ishodnyy;
        }

        /// <summary>Перёд ледосборщика: сторона, где шнек (дочерний узел bur). Раньше брался тонкий конец
        /// длинной оси, но после отделения шнека корпус стал тонким с кормы, и эвристика перевернула машины
        /// кормой к льду. Без узла bur — старая эвристика по всем мешам.</summary>
        private static Vector3 PeredLedosbora(Transform obekt)
        {
            var bur = NaytiRebenka(obekt, "bur");
            var rKorpus = obekt.GetComponent<Renderer>();
            var rBur = bur != null ? bur.GetComponent<Renderer>() : null;
            if (rKorpus != null && rBur != null)
            {
                Vector3 d = rBur.bounds.center - rKorpus.bounds.center; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) return d.normalized;
            }
            Vector3 os = DlinnayaOs(obekt);
            var w = VseVershinyMir(obekt); int n = w.Count;
            if (n < 8) return os;
            var pr = new float[n]; float pmin = float.MaxValue, pmax = float.MinValue;
            for (int i = 0; i < n; i++) { pr[i] = w[i].x * os.x + w[i].z * os.z; pmin = Mathf.Min(pmin, pr[i]); pmax = Mathf.Max(pmax, pr[i]); }
            System.Array.Sort(pr);
            return (pmin + pmax) * 0.5f - pr[n / 2] >= 0f ? os : -os;
        }

        /// <summary>Все вершины всех мешей объекта и его детей в мировых координатах: корпус и отделённые части вместе.</summary>
        private static List<Vector3> VseVershinyMir(Transform obekt)
        {
            var spisok = new List<Vector3>();
            foreach (var mf in obekt.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var tr = mf.transform;
                foreach (var v in mf.sharedMesh.vertices) spisok.Add(tr.TransformPoint(v));
            }
            return spisok;
        }

        /// <summary>Перёд реголитосборщика: сторона, где висит буровая насадка (дочерний узел nasadka).</summary>
        private static Vector3 PeredRegolitosbora(Transform mashina, Transform nasadka)
        {
            var rMash = mashina.GetComponent<Renderer>(); var rNas = nasadka != null ? nasadka.GetComponent<Renderer>() : null;
            if (rMash == null || rNas == null) return DlinnayaOs(mashina);
            Vector3 d = rNas.bounds.center - rMash.bounds.center; d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : DlinnayaOs(mashina);
        }

        private static Transform NaytiRebenka(Transform koren, string imya)
        {
            foreach (var t in koren.GetComponentsInChildren<Transform>(true)) if (t != koren && t.name == imya) return t;
            return null;
        }

        /// <summary>Длинная горизонтальная ось всех мешей объекта (PCA по вершинам): у техники это направление хода.
        /// Оси трансформа у ассетов Tripo произвольные, поэтому считаем по геометрии.</summary>
        private static Vector3 DlinnayaOs(Transform obekt)
        {
            var w = VseVershinyMir(obekt); int n = w.Count;
            if (n < 8) return obekt.forward;
            double sx = 0, sz = 0;
            for (int i = 0; i < n; i++) { sx += w[i].x; sz += w[i].z; }
            double mx = sx / n, mz = sz / n, cxx = 0, cxz = 0, czz = 0;
            for (int i = 0; i < n; i++) { double dx = w[i].x - mx, dz = w[i].z - mz; cxx += dx * dx; cxz += dx * dz; czz += dz * dz; }
            double theta = 0.5 * System.Math.Atan2(2 * cxz, cxx - czz);
            return new Vector3((float)System.Math.Cos(theta), 0f, (float)System.Math.Sin(theta));
        }

        /// <summary>Полуширина всех мешей поперёк оси «перёд», м.</summary>
        private static float PoluShirina(Transform obekt, Vector3 pered)
        {
            Vector3 bok = new Vector3(-pered.z, 0f, pered.x).normalized;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var w in VseVershinyMir(obekt)) { float pr = w.x * bok.x + w.z * bok.z; min = Mathf.Min(min, pr); max = Mathf.Max(max, pr); }
            return min < max ? (max - min) * 0.5f : 1.2f;
        }

        /// <summary>Вылет всех мешей вперёд от пивота вдоль оси «перёд», м, и высота самой дальней точки (кончика шнека).</summary>
        private static float VyletVperyod(Transform obekt, Vector3 pered, out float yKonchika)
        {
            yKonchika = obekt.position.y;
            Vector3 p = new Vector3(pered.x, 0f, pered.z).normalized; Vector3 piv = obekt.position;
            float max = float.MinValue;
            foreach (var w in VseVershinyMir(obekt))
            {
                float pr = (w.x - piv.x) * p.x + (w.z - piv.z) * p.z;
                if (pr > max) { max = pr; yKonchika = w.y; }
            }
            return max > float.MinValue ? Mathf.Max(max, 0f) : 2f;
        }

        /// <summary>Вылет всех мешей назад от пивота (против оси «перёд»), м.</summary>
        private static float VyletNazad(Transform obekt, Vector3 pered)
        {
            Vector3 p = new Vector3(pered.x, 0f, pered.z).normalized; Vector3 piv = obekt.position;
            float min = float.MaxValue;
            foreach (var w in VseVershinyMir(obekt)) min = Mathf.Min(min, (w.x - piv.x) * p.x + (w.z - piv.z) * p.z);
            return min < float.MaxValue ? Mathf.Max(-min, 0f) : 2f;
        }

        /// <summary>Кромка глыбы лучом: из точки ot вдоль napr (и с боковым сдвигом ±0.5 м, и чуть выше)
        /// до коллайдера этой глыбы. Меньше нуля — луч в глыбу не попал.</summary>
        private static float KromkaLuchom(Transform led, Vector3 ot, Vector3 napr)
        {
            Vector3 bok = new Vector3(-napr.z, 0f, napr.x);
            float min = float.MaxValue;
            foreach (float sdvig in new[] { 0f, 0.5f, -0.5f })
                foreach (float vverh in new[] { 0f, 0.4f, -0.3f })
                {
                    Vector3 o = ot + bok * sdvig + Vector3.up * vverh;
                    foreach (var h in Physics.RaycastAll(o, napr, 40f, ~0))
                    {
                        if (!h.collider.transform.IsChildOf(led) && h.collider.transform != led) continue;
                        float pr = (h.point.x - ot.x) * napr.x + (h.point.z - ot.z) * napr.z;
                        if (pr > 0f && pr < min) min = pr;
                    }
                }
            return min < float.MaxValue ? min : -1f;
        }

        /// <summary>Радиус габарита в горизонтальном направлении: эллипс по полуосям.</summary>
        private static float RadiusVNapravlenii(Bounds b, Vector3 napravlenie)
        {
            Vector3 d = napravlenie; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return Mathf.Max(b.extents.x, b.extents.z);
            d.Normalize();
            float ex = Mathf.Max(b.extents.x, 0.05f), ez = Mathf.Max(b.extents.z, 0.05f);
            float a = d.x / ex, c = d.z / ez;
            return 1f / Mathf.Sqrt(a * a + c * c);
        }

        /// <summary>Грунтовые патчи, дороги и бортики идут без коллайдеров, и луч рельефа видел под ними
        /// только общую землю: техника, стартовавшая на ледяной площадке, висела в воздухе, а колонист
        /// шёл под поверхностью дороги. Даём им меш-коллайдеры на слое Ignore Raycast, чтобы клики и
        /// прочая физика игры их не заметили.</summary>
        public static void ObespechitKollayderyGrunta()
        {
            int dobavleno = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                string n = r.gameObject.name.ToLower();
                if (!(n.StartsWith("biom") || n.StartsWith("grunt") || n.StartsWith("yama") || n.StartsWith("vystilka") || n.StartsWith("pandus")
                      || n.StartsWith("set-") || n.StartsWith("dorog") || n.StartsWith("bortik"))) continue;
                if (DobavitKollayder(r)) dobavleno++;
            }
            if (dobavleno > 0) Debug.Log($"[машины] грунту, патчам и дорогам добавлено коллайдеров: {dobavleno} (слой Ignore Raycast)");
        }

        /// <summary>Коллайдеры льду — только для горизонтального луча «где кромка»; для рельефа лёд не считается.</summary>
        public static void ObespechitKollayderyLda()
        {
            int dobavleno = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (EtoLed(r.gameObject.name.ToLower()) && DobavitKollayder(r)) dobavleno++;
            if (dobavleno > 0) Debug.Log($"[машины] льду добавлено коллайдеров: {dobavleno} (слой Ignore Raycast)");
        }

        private static bool DobavitKollayder(Renderer r)
        {
            if (r.GetComponent<Collider>() != null) return false;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            var mc = r.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            r.gameObject.layer = 2;   // Ignore Raycast
            return true;
        }

        private static bool EtoLed(string n) => n.StartsWith("gen-glyba") || n.StartsWith("gen-greben") || n.StartsWith("led-");

        /// <summary>Чьи коллайдеры не считаются рельефом: люди, техника, дроны, лёд, осколки.</summary>
        public static bool NeGrunt(string imya)
        {
            string n = imya.ToLower();
            return n.StartsWith("mashina") || n.StartsWith("kosmonavt") || n.StartsWith("dron") || n.StartsWith("nasadka")
                || n.StartsWith("oskolok") || EtoLed(n);
        }

        /// <summary>Высота пивота, при которой низ габарита лежит на грунте: рельеф усредняется по центру
        /// и четырём точкам пятна, чтобы на склоне машина не висела одним углом и не тонула другим.</summary>
        private static float VysotaPivota(Renderer rMash, float x, float z, float podem)
        {
            // Пятно проб — внутри гусениц (0.4 меньшей полуоси габарита), а не по всему AABB с мачтой:
            // иначе на краю ямы пробы цепляют склон, и машина приподнимается над дном.
            float r = Mathf.Min(rMash.bounds.extents.x, rMash.bounds.extents.z) * 0.4f;
            Vector3 e = new Vector3(r, 0f, r);
            float summa = VysotaRelyefa(x, z) + VysotaRelyefa(x - e.x, z - e.z) + VysotaRelyefa(x + e.x, z - e.z) + VysotaRelyefa(x - e.x, z + e.z) + VysotaRelyefa(x + e.x, z + e.z);
            return summa / 5f + podem;
        }

        private static float VysotaRelyefa(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 90f, z), Vector3.down, 200f, ~0);   // ~0: и слой Ignore Raycast
            float verh = -1000f;
            foreach (var h in hits)
            {
                if (NeGrunt(h.collider.gameObject.name)) continue;
                if (h.point.y > verh) verh = h.point.y;
            }
            return verh > -999f ? verh : 0f;
        }

        private static Transform Nayti(string imya)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == imya) return t;
            return null;
        }

        private static Transform NaytiPoPrefiksu(string prefiks)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name.StartsWith(prefiks)) return t;
            return null;
        }
    }
}
