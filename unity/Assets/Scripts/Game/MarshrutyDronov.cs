using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Курьерские дроны между площадками-ящиками. Заказчик 05.09: «дроны не просто летают —
    /// они взлетают с ящиков и садятся на другие: дрон 2 взлетает с mesto-vzleta-dron-2 и
    /// садится на mesto-posadki-dron-2, потом улетает обратно; дрон 1 — с mesto-vzleta-dron-1
    /// на mesto-posadki-dron-1 и назад». Он же ранее: «лицо дрона — камера спереди, летит
    /// объективом вперёд».
    ///
    /// Рейс: вертикальный взлёт с разворотом по курсу, прямой крейсер на постоянной высоте,
    /// вертикальная посадка на верх ящика. Высота крейсера считается по фактическим постройкам
    /// в коридоре маршрута (верх самой высокой плюс запас), а не берётся на глаз.
    ///
    /// Разведение: дрон-1 идёт выше дрона-2 на другом эшелоне; пока шаттл в полёте
    /// (<see cref="PolyotShattla.VPolete"/>), дроны в крейсере не продвигаются.
    ///
    /// Маршруты запускаются в OnEnable: отключение компонента убивает корутины, обратное
    /// включение их перезапускает.
    /// </summary>
    public sealed class MarshrutyDronov : MonoBehaviour
    {
        [Tooltip("Скорость крейсера, м/с.")]
        [SerializeField] private float _skorost = 5f;
        [Tooltip("Пауза на площадке перед следующим рейсом, секунды (диапазон).")]
        [SerializeField] private float _pauzaMin = 2.5f, _pauzaMax = 4.5f;
        [Tooltip("Ось МОДЕЛИ дрона, смотрящая в сторону объектива камеры (лицо), в осях дочернего узла меша. " +
                 "У этого меша Tripo тело повёрнуто на 45° внутри узла, поэтому ось диагональная.")]
        [SerializeField] private Vector3 _litsoModeli = new Vector3(-0.66f, -0.75f, 0f);
        [Tooltip("Площадки дрона-1: откуда взлетает и куда садится (объекты сцены).")]
        [SerializeField] private string _dron1Vzlet = "mesto-vzleta-dron-1", _dron1Posadka = "mesto-posadki-dron-1";
        [Tooltip("Площадки дрона-2.")]
        [SerializeField] private string _dron2Vzlet = "mesto-vzleta-dron-2", _dron2Posadka = "mesto-posadki-dron-2";

        /// <summary>Эшелоны крейсера над верхом площадок, м: дрон-2 ниже, дрон-1 выше.</summary>
        private const float ESHELON_NIZ = 8f, ESHELON_VERH = 13f;
        /// <summary>Запас над самой высокой постройкой в коридоре маршрута, м.</summary>
        private const float ZAPAS_NAD_POSTROYKAMI = 3f;
        /// <summary>Скорость взлёта и посадки, м/с.</summary>
        private const float SKOROST_VERTIKALNAYA = 2.5f;
        /// <summary>Покачивание в крейсере, м / период, с.</summary>
        private const float KACHANIE = 0.12f, PERIOD_KACHANIYA = 2.2f;
        /// <summary>Зазор между днищем дрона и верхом ящика, м.</summary>
        private const float POSADOCHNYY_ZAZOR = 0.03f;

        private PolyotShattla _shattl;

        /// <summary>Расчётная длительность круга каждого дрона (туда, пауза, обратно, пауза), с. Заказчик:
        /// «скорость космонавтов такая, чтобы дойти до дальней точки за то время, пока дроны не вернутся
        /// к точке взлёта» — колонисты читают это значение и подбирают шаг.</summary>
        private static readonly Dictionary<string, float> _dlitelnostKruga = new Dictionary<string, float>();

        public static bool DlitelnostKruga(string imyaDrona, out float sekund) => _dlitelnostKruga.TryGetValue(imyaDrona, out sekund);

        /// <summary>Хореография с шаттлом (заказчик 05.09): дроны взлетают после посадки шаттла, садятся на точки
        /// прибытия — это «погрузка», шаттл закрывается и взлетает; дроны ждут его ухода и летят назад.</summary>
        private static readonly HashSet<string> _zapushcheny = new HashSet<string>(), _pribyli = new HashSet<string>();
        public static bool EstDrony => _zapushcheny.Count > 0;

        // ---- игровой режим (ночь 3): дрон-курьер летает только по заказу игрока ----
        public static MarshrutyDronov Ekz { get; private set; }
        /// <summary>Ставит IgraKolonii в Awake: дроны сидят на своих ящиках и ждут «Отправить».</summary>
        public static bool IgrovoyRezhim;
        private sealed class Kurer { public Transform dron; public Vector3 pad; public Quaternion ishodnaya; public Vector3 litso; public float podem, radius; public bool zanyat; }
        private readonly Dictionary<string, Kurer> _kurery = new Dictionary<string, Kurer>();

        public bool EstKurer(string imya) => _kurery.ContainsKey(imya);
        public bool Svoboden(string imya) => _kurery.TryGetValue(imya, out var k) && !k.zanyat;
        public Vector3 PadMir(string imya) => _kurery.TryGetValue(imya, out var k) ? k.pad : Vector3.zero;

        /// <summary>Рейс курьера: взлёт со своего ящика, крейсер к цели, зависание над ней (сброс груза —
        /// вызов naCeli), возврат, посадка (vernulsya). Ложь — дрон занят или не найден.</summary>
        /// <summary>Множитель скорости от уровня дронов в ангаре (спека 08.09): уровень режет время рейса,
        /// то есть поднимает скорость. Ставит игра при загрузке и после покупки уровня.</summary>
        public static float MnozhitelSkorosti = 1f;   // статическое: методы движения статические, а множитель один на всех курьеров

        public bool Poletet(string imya, Vector3 celMir, System.Action naCeli, System.Action vernulsya)
        {
            if (!_kurery.TryGetValue(imya, out var k) || k.zanyat) return false;
            k.zanyat = true;
            StartCoroutine(ReysKurera(k, celMir, naCeli, vernulsya));
            return true;
        }

        private IEnumerator ReysKurera(Kurer k, Vector3 celMir, System.Action naCeli, System.Action vernulsya)
        {
            Vector3 nadCelyu = new Vector3(celMir.x, celMir.y + 3f + k.podem, celMir.z);
            float vysota = VysotaKreysera(k.pad, nadCelyu, ESHELON_NIZ, k.radius, k.podem);
            yield return Perelet(k.dron, k.ishodnaya, k.litso, k.pad, nadCelyu, vysota);
            naCeli?.Invoke();
            yield return new WaitForSeconds(1.2f);
            yield return Perelet(k.dron, k.ishodnaya, k.litso, nadCelyu, k.pad, vysota);
            k.zanyat = false;
            vernulsya?.Invoke();
        }
        public static bool DronyPribyli => _zapushcheny.Count > 0 && _pribyli.Count >= _zapushcheny.Count;

        private void OnEnable()
        {
            Ekz = this;
            _shattl = FindFirstObjectByType<PolyotShattla>(FindObjectsInactive.Include);
            _zapushcheny.Clear(); _pribyli.Clear();
            Zapustit("dron-1", _dron1Vzlet, _dron1Posadka, ESHELON_VERH);
            Zapustit("dron-2", _dron2Vzlet, _dron2Posadka, ESHELON_NIZ);
        }

        private void OnDisable() => StopAllCoroutines();

        private void Zapustit(string imyaDrona, string vzlet, string posadka, float eshelon)
        {
            var dron = NaytiUzel(imyaDrona);
            var a = NaytiUzel(vzlet);
            var b = NaytiUzel(posadka);
            if (dron == null || a == null || b == null)
            {
                Debug.LogWarning($"[дроны] {imyaDrona}: не найдены дрон или площадки '{vzlet}' / '{posadka}' — рейс не запущен");
                return;
            }
            StartCoroutine(Reys(dron, a, b, eshelon));
        }

        /// <summary>Один дрон: с площадки A на площадку B, пауза, обратно, пауза, и так по кругу.</summary>
        private IEnumerator Reys(Transform dron, Transform ploshchadkaA, Transform ploshchadkaB, float eshelon)
        {
            var r = dron.GetComponentInChildren<Renderer>();
            float podem = r != null ? dron.position.y - r.bounds.min.y : 0f;          // пивот над днищем
            float radius = r != null ? Mathf.Max(r.bounds.extents.x, r.bounds.extents.z) : 1f;
            Quaternion ishodnaya = dron.rotation;
            Vector3 litsoIshodnoe = LitsoVMire(dron);
            Vector3 a = TochkaPosadki(ploshchadkaA, podem), b = TochkaPosadki(ploshchadkaB, podem);
            float vysota = VysotaKreysera(a, b, eshelon, radius, podem);
            float krug = (2f * (vysota - a.y) + 2f * (vysota - b.y)) / (SKOROST_VERTIKALNAYA * MnozhitelSkorosti)
                + 2f * Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z)) / Mathf.Max(_skorost * MnozhitelSkorosti, 0.1f)
                + (_pauzaMin + _pauzaMax);
            _dlitelnostKruga[dron.name] = krug;
            Debug.Log($"[дроны] {dron.name}: расчётный круг {krug:0.0} с");
            Debug.Log($"[дроны] {dron.name}: {ploshchadkaA.name} {a:F1} -> {ploshchadkaB.name} {b:F1}, "
                    + $"крейсер y={vysota:0.0}, лицо модели в мире {litsoIshodnoe:F2}");

            dron.position = a;
            dron.rotation = PovorotLitsom(ishodnaya, litsoIshodnoe, b - a);
            _kurery[dron.name] = new Kurer { dron = dron, pad = a, ishodnaya = ishodnaya, litso = litsoIshodnoe, podem = podem, radius = radius };
            yield return null;   // IgraKolonii выставляет игровой режим в своём Awake; решаем после первого кадра
            if (IgrovoyRezhim) { Debug.Log($"[дроны] {dron.name}: игровой режим — сидит на {ploshchadkaA.name}, ждёт заказа"); yield break; }
            _zapushcheny.Add(dron.name);
            // Первый рейс — по хореографии с шаттлом: ждать его посадки; сесть на точку прибытия (погрузка);
            // ждать, пока шаттл улетит; вернуться. Без шаттла в сцене — сразу обычный цикл.
            while (_shattl != null && !_shattl.NaPloshchadke && !_shattl.Uletel) yield return null;
            yield return new WaitForSeconds(Random.Range(0.5f, 2f));
            yield return Perelet(dron, ishodnaya, litsoIshodnoe, a, b, vysota);
            _pribyli.Add(dron.name);
            float tPribytiya = Time.time;
            while (_shattl != null && !_shattl.Uletel && Time.time - tPribytiya < 150f) yield return null;
            yield return new WaitForSeconds(Random.Range(0.5f, 1.5f));
            yield return Perelet(dron, ishodnaya, litsoIshodnoe, b, a, vysota);
            yield return new WaitForSeconds(Random.Range(_pauzaMin, _pauzaMax));

            while (true)
            {
                yield return Perelet(dron, ishodnaya, litsoIshodnoe, a, b, vysota);
                yield return new WaitForSeconds(Random.Range(_pauzaMin, _pauzaMax));
                yield return Perelet(dron, ishodnaya, litsoIshodnoe, b, a, vysota);
                yield return new WaitForSeconds(Random.Range(_pauzaMin, _pauzaMax));
            }
        }

        private IEnumerator Perelet(Transform dron, Quaternion ishodnaya, Vector3 litsoIshodnoe, Vector3 ot, Vector3 do_, float vysota)
        {
            Quaternion povorot = PovorotLitsom(ishodnaya, litsoIshodnoe, do_ - ot);
            yield return Vertikal(dron, ot, ot.y, vysota, povorot);                    // взлёт с разворотом по курсу

            Vector3 v0 = new Vector3(ot.x, vysota, ot.z), v1 = new Vector3(do_.x, vysota, do_.z);
            float dlina = Vector3.Distance(v0, v1);
            float vsego = dlina / Mathf.Max(_skorost * MnozhitelSkorosti, 0.1f), t = 0f;
            while (t < vsego)
            {
                if (!PropuskatShattl()) t += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / vsego));
                Vector3 p = Vector3.Lerp(v0, v1, u);
                p.y += Mathf.Sin(Time.time / PERIOD_KACHANIYA * Mathf.PI * 2f) * KACHANIE;
                dron.position = p;
                dron.rotation = povorot;
                yield return null;
            }

            yield return Vertikal(dron, do_, vysota, do_.y, povorot);                  // посадка
            dron.position = do_;
        }

        /// <summary>Вертикальный участок над точкой xz: от высоты yOt до yDo, поворот доводится к povorot.</summary>
        private static IEnumerator Vertikal(Transform dron, Vector3 xz, float yOt, float yDo, Quaternion povorot)
        {
            float vsego = Mathf.Abs(yDo - yOt) / (SKOROST_VERTIKALNAYA * MnozhitelSkorosti), t = 0f;
            while (t < vsego)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / vsego));
                dron.position = new Vector3(xz.x, Mathf.Lerp(yOt, yDo, u), xz.z);
                dron.rotation = Quaternion.Slerp(dron.rotation, povorot, Time.deltaTime * 2.5f);
                yield return null;
            }
            dron.rotation = povorot;
        }

        /// <summary>Точка стоянки пивота над площадкой: центр её габарита по XZ, верх плюс зазор плюс подъём пивота.</summary>
        private static Vector3 TochkaPosadki(Transform ploshchadka, float podem)
        {
            var rs = ploshchadka.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return ploshchadka.position + Vector3.up * podem;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return new Vector3(b.center.x, b.max.y + POSADOCHNYY_ZAZOR + podem, b.center.z);
        }

        /// <summary>Высота крейсера: эшелон над верхней из площадок, но не ниже самой высокой постройки
        /// в коридоре маршрута плюс запас. Коридор — полоса шириной в дрон вокруг отрезка A-B.</summary>
        private static float VysotaKreysera(Vector3 a, Vector3 b, float eshelon, float radius, float podem)
        {
            float vysota = Mathf.Max(a.y, b.y) + eshelon;
            Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z);
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var bb = r.bounds;
                if (bb.size.x > 60f || bb.size.z > 60f) continue;             // земля и небо
                string n = r.gameObject.name.ToLower();
                if (n.StartsWith("dron")) continue;
                Vector2 c = new Vector2(bb.center.x, bb.center.z);
                float rast = RasstoyanieDoOtrezka(c, a2, b2) - Mathf.Max(bb.extents.x, bb.extents.z);
                if (rast > radius + 1f) continue;
                vysota = Mathf.Max(vysota, bb.max.y + ZAPAS_NAD_POSTROYKAMI + podem);
            }
            return vysota;
        }

        private static float RasstoyanieDoOtrezka(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Поворот, при котором лицо модели смотрит по курсу; считается от исходной позы,
        /// в которой измерено лицо, а не от текущей.</summary>
        private static Quaternion PovorotLitsom(Quaternion ishodnaya, Vector3 litsoIshodnoe, Vector3 kurs)
        {
            kurs.y = 0f;
            if (kurs.sqrMagnitude < 1e-6f) return ishodnaya;
            float ugol = Vector3.SignedAngle(litsoIshodnoe, kurs.normalized, Vector3.up);
            return Quaternion.AngleAxis(ugol, Vector3.up) * ishodnaya;
        }

        /// <summary>Мировое направление «лица» модели в текущей позе, спроецированное на горизонт.</summary>
        private Vector3 LitsoVMire(Transform dron)
        {
            var uzel = dron.GetComponentInChildren<MeshFilter>();
            Vector3 lok = _litsoModeli.sqrMagnitude < 0.01f ? Vector3.forward : _litsoModeli.normalized;
            Vector3 mir = uzel != null
                ? uzel.transform.right * lok.x + uzel.transform.up * lok.y + uzel.transform.forward * lok.z
                : dron.rotation * lok;
            mir.y = 0f;
            return mir.sqrMagnitude > 0.0001f ? mir.normalized : Vector3.forward;
        }

        /// <summary>Пока шаттл в полёте, дроны в крейсере стоят: линия его ухода идёт через колонию.</summary>
        private bool PropuskatShattl() => _shattl != null && _shattl.VPolete && !_shattl.NaPloshchadke;   // стоит только пока шаттл реально движется: на стоянке VPolete тоже true

        private static Transform NaytiUzel(string imya)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == imya) return t;
            return null;
        }
    }
}
