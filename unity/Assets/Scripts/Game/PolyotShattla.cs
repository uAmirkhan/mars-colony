using System.Collections;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Прилёт / посадка / стоянка / взлёт шаттла на "ploshchadka-shattla".
    /// План движения: `loop/ui/plan-animatsii-shattla.md` (mars-colony). Числа
    /// фаз (длительности, ключи кривых, радиус пыли) взяты оттуда буквально —
    /// расхождение с планом в этом файле является багом файла, а не плана.
    ///
    /// Компонент вешается на "shattl-zakrytyy" (второй корпус, "shattl-otkrytyy",
    /// по доктрине соседства не анимируется и не трогается — план, раздел
    /// "Источник данных").
    ///
    /// Два разных потребителя одной и той же математики позы:
    /// 1) <see cref="Zapustit"/> — живая корутина в Play, реальное время,
    ///    случайная длительность стоянки (фаза 5).
    /// 2) <see cref="VystavitVremya"/> — детерминированная выставка позы по
    ///    числу секунд от начала фазы 2 (снижение), для батч-кадров
    ///    приёмки (см. `InterfeysBuilder.KadrShattl`). Обе точки входа зовут
    ///    один и тот же <see cref="Poza"/>, чтобы они не могли разойтись.
    ///
    /// Допущение 1 плана (знак вектора "вглубь кадра" F) снято без хардкода
    /// азимута: F берётся напрямую из `Camera.main.transform.forward`,
    /// спроецированного на землю. Это математически то же самое, что
    /// формула `ColonyOursBuilder.VpisatKadr` использует для "наземного следа
    /// экранной вертикали" ((-cos(az), -sin(az)) в мировых X,Z) — направление
    /// от камеры к цели лежит в основе обеих формул, поэтому дублировать
    /// числа az/el здесь не нужно и опасно (они меняются между сборками,
    /// см. комментарий у `ElevKadra` в `ColonyOursBuilder.Build`).
    ///
    /// Допущение 2 плана (перевод 6 px просадки в метры) считается заново на
    /// каждом запуске через `Camera.WorldToScreenPoint` (см.
    /// <see cref="ObespechitInfrastrukturu"/>) — план прямо запрещает брать
    /// константу 0.06 м на веру.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PolyotShattla : MonoBehaviour
    {
        // ---- длительности фаз, секунды (план, раздел 1) ----
        public const float T_SNIZHENIE = 3.0f;         // фаза 2: заход глиссадой слева по курсу носа (было 1.6 при заходе сверху)
        public const float T_ZAVISANIE = 0.15f;        // фаза 3: короткое выравнивание над площадкой (было 0.3 зависания на 1.8 м)
        public const float T_POSADKA = 0.45f;          // фаза 4
        public const float T_ZADERZHKA_VZLETA = 0.5f;  // фаза 6
        public const float T_VZLET = 4.2f;             // фаза 7: разгон носом вперёд через всю карту (было 1.4 вертикального ухода)

        /// <summary>Глобальное t (от начала фазы 2) в момент максимальной просадки — план, §5, п.2.</summary>
        public const float T_KASANIE = T_SNIZHENIE + T_ZAVISANIE + 0.75f * T_POSADKA;

        /// <summary>Глобальное t начала фазы 7 (после фаз 2-4-6).</summary>
        public const float T_VZLET_START = T_SNIZHENIE + T_ZAVISANIE + T_POSADKA + T_ZADERZHKA_VZLETA;

        /// <summary>Три контрольных момента батч-приёмки (план, §5).</summary>
        public const float T_TEST_40_SNIZHENIYA = 0.4f * T_SNIZHENIE;
        public const float T_TEST_KASANIE = T_KASANIE + 0.3f;   // +0.3 с: клубы успевают раскрыться, в момент контакта пыль ещё точечная
        public const float T_TEST_60_VZLETA = T_VZLET_START + 0.6f * T_VZLET;

        // Курс (замечание заказчика 05.09): двигатели сзади, вертикального взлёта нет.
        // Заход — с левого края кадра по курсу носа глиссадой в позу покоя; уход — носом вперёд
        // через всю карту с набором высоты к правому верхнему углу. Нижние границы дистанций и
        // высот; фактические значения считаются в ObespechitInfrastrukturu по камере и рельефу.
        private const float DIST_ZAHODA_MIN = 36f;      // м за левым краем кадра (24 px/м на глубине площадки)
        private const float VYSOTA_ZAHODA_MIN = 9f;     // м над уровнем покоя в точке появления
        private const float DIST_UHODA_MIN = 80f;       // м до правого края кадра
        private const float VYSOTA_UHODA_MIN = 28f;     // м набора к выходу из кадра (правый верхний угол)
        private const float ZAPAS_NAD_RELYEFOM = 2.5f;  // м между низом меша и рельефом на глиссаде
        private const float ZAPAS_NAD_OBEKTAMI = 3f;    // м над постройками под линией ухода
        private const float PROFIL_ZAHODA = 1f;        // h ~ ost^k: 1 = прямая глиссада (было 2 — слишком ранний сброс высоты)
        private const float PROFIL_UHODA = 1.1f;       // h ~ u^k на уходе: ранний набор высоты
        private const float ZAPAS_SILUETA_PX = 14f;    // экранный зазор между низом шаттла и верхом ближнего пропа

        /// <summary>Высота выравнивания перед касанием, метры над Y0 (конец глиссады, начало просадки).</summary>
        private const float VYSOTA_ZAVISANIYA = 0.4f;

        /// <summary>Просадка в пикселях экрана — величина из ТЗ, не мировая (план, §1, конец).</summary>
        private const float PROSADKA_PX = 6f;

        [Header("Стоянка (фаза 5), секунды")]
        [SerializeField] private float _stoyankaMin = 6f;
        [SerializeField] private float _stoyankaMax = 10f;

        [Header("Отладка")]
        [Tooltip("Клавиша ручного запуска вылета — точка входа отладчика, не игрока.")]
        [SerializeField] private KeyCode _klavishaOtladki = KeyCode.L;
        [Tooltip("Таймер автозапуска в Play, секунды. 0 — выключен.")]
        [SerializeField] private float _periodAvtozapuska = 90f;

        // ---- состояние полёта ----
        private bool _letit;
        private bool _ochered;
        /// <summary>Домен прилетел, пока анимация ещё улетала (ускорение за изотопы): после конца улёта сразу зайти на посадку снова.</summary>
        private bool _snovaPosleUlyota;
        private float _avtozapuskTaymer;

        // ---- поза покоя, снятая живьём с transform при первом включении ----
        private bool _pozaZapomnena;
        private Vector3 _pozitsiyaPokoya;
        private Quaternion _rotatsiyaPokoya;

        // ---- геометрия полёта, посчитанная один раз (план: "не хардкодить") ----
        private bool _infrastrukturaGotova;
        private float _y0;
        private Vector3 _nos = new Vector3(1f, 0f, 0f);  // курс: горизонтальная продольная ось меша, нос вперёд
        private float _distZahoda = DIST_ZAHODA_MIN, _vysotaZahoda = VYSOTA_ZAHODA_MIN;
        private float _distUhoda = DIST_UHODA_MIN, _vysotaUhoda = VYSOTA_UHODA_MIN;

        // ---- второй ассет заказчика: шаттл с открытым грузовым отсеком ("shattl-otkrytyy") ----
        // Оба ассета — цельные меши Tripo, дверей отдельными частями нет, поэтому «открывание» —
        // подмена модели: закрытый летает, на стоянке через паузу показывается открытый, перед
        // взлётом снова закрытый. Открытый выровнен в редакторе по курсу и низу меша закрытого;
        // смещение снимается один раз в Awake и переносится вслед за позой покоя.
        private GameObject _otkrytyy;
        private Vector3 _otkrytyySmeshchenie;
        private Quaternion _otkrytyyPovorot = Quaternion.identity;
        private const float T_OTKRYTIE_POSLE_KASANIYA = 0.6f;   // с: пыль и тряска успевают отыграть
        private float _prosadkaMetry = 0.06f;         // фолбэк-оценка плана, перезаписывается фактом

        private ParticleSystem _pyl;

        // ---- огонь двигателей (заказчик 05.09: «сзади, где двигатели, огонь; при посадке уменьшается,
        // сел — прекращается; закрылся и готов взлетать — огонь появляется резким напором») ----
        private Transform[] _fakely;            // по два конуса на сопло: внешний оранжевый и внутренний жёлто-белый
        private Renderer[] _fakelRend;
        private Vector3[] _soplaLok;             // сопла в локальных координатах шаттла
        private Vector3 _strujaLok;              // направление струи (назад и чуть вниз) в локальных координатах
        private const float OGON_DLINA = 1.3f, OGON_SHIRINA = 0.45f, OGON_NAKLON_GRAD = 12f;
        /// <summary>Заказчик: «улетел — возвращать не надо, его нет на карте». После ухода рейсы не запускаются.</summary>
        private bool _uletel;
        /// <summary>Заказчик: «шаттла изначально нет на карте: он прилетает, садится, открывает люки, грузится,
        /// закрывается и улетает». Первый и единственный прилёт — через эту задержку после старта сцены.</summary>
        [SerializeField] private float _zaderzhkaPervogoPrileta = 1.5f;
        private bool _startoval;
        /// <summary>Стоит на площадке (сел, ещё не взлетает). Дроны взлетают только после этого.</summary>
        public bool NaPloshchadke { get; private set; }
        public bool Uletel => _uletel;
        /// <summary>Игровой режим (ночь 3): стоянка длится, пока игрок не заполнит последний отсек
        /// (`ZagruzkaZavershena`), а после ухода шаттл возвращается новым рейсом по таймеру
        /// (`PriletetSnova`). Без игры — прежняя хореография с дронами.</summary>
        public static bool IgrovoyRezhim;
        public bool ZagruzkaZavershena;
        /// <summary>Верхняя граница ожидания погрузки (дронов), с.</summary>
        private const float STOYANKA_MAX = 90f;

        // ---- P12: контактная тень-декаль под летящим шаттлом ----
        // Реальная тень от солнца уезжает вбок на десятки метров и не читается как
        // «шаттл над площадкой»; мягкий эллипс строго под центром меша по XZ даёт
        // глазу высоту (Township: любой летающий объект). Квад создаётся в рантайме,
        // в сцену не пишется.
        private GameObject _ten;
        private MeshRenderer _tenRend;
        private Material _matTen;
        private Vector3 _smeshchenieMesha;                    // центр рендерера минус пивот (пивот уехал на ~24 м)
        private Vector3 _ekstentyMesha = new Vector3(4f, 2f, 4f);
        private const float TEN_ALFA_NA_ZEMLE = 0.85f;        // приёмка UI-35: при 0.55 перепад к грунту ~2 ед. против шума текстуры 20-40
        private const float TEN_ZAZOR = 0.06f;                // над поверхностью, против z-fighting с площадкой
        private const float TEN_ROST_NA_VYSOTE = 0.35f;       // эллипс шире на 35% на высоте появления
        private const float TEN_DIAMETR_K = 2.0f;             // плотное пятно по габариту корпуса читается лучше широкого блёклого
        private const float TEN_VYSOTA_ISCHEZANIYA = 12f;     // м подъёма, на которых тень бледнеет до четверти             // квад к полуоси меша: видимая часть спада ~0.75 квада ≈ габарит корпуса

        /// <summary>Запасной вариант пыли под флагом (задание UI-23, попытка 3):
        /// true переключает эмиссию с ParticleSystem (ObespechitPyl) на клубы-сферы
        /// (SozdatKlubyPyliEsliNado). Публичный static bool специально, чтобы
        /// оркестратор мог переключить сравнение вариантов без перекомпиляции.</summary>
        public static bool PylMeshami = false;

        /// <summary>Попытка 4 (сферы и клубы-сферы забракованы): пыль спрайт-листом —
        /// билборды-квады с флипбуком по <see cref="pylList"/>. true (по умолчанию)
        /// отключает и ParticleSystem (ObespechitPyl), и клубы-сферы (PylMeshami) —
        /// они не создаются вовсе, работает только этот вариант.</summary>
        public static bool PylSpraytami = true;

        /// <summary>Спрайт-лист пыли 5x5 (`Assets/UI/Effekty/pyl-klub-list.png`).
        /// Поле сделано public, а не private+[SerializeField]: назначается кодом
        /// редактора (`InterfeysBuilder.ObespechitPylList`), а Editor-сборка и
        /// сборка Assets/Scripts — разные ассембли без общего доступа к private-
        /// полям без reflection. AssetDatabase.LoadAssetAtPath в рантайме не
        /// работает (нет UnityEditor.dll в билде), поэтому ссылка кладётся в
        /// сцену один раз редакторским кодом, а не читается на лету.</summary>
        public Texture2D pylList;

        private Material _matSprayty;

        private void Awake()
        {
            ObespechitInfrastrukturu();
            // До первого прилёта шаттла на карте нет: рендеры выключены, включаются при появлении за кадром.
            if (Application.isPlaying) foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            // TODO(поставки): реальный триггер — таймер/событие рейса шаттла в
            // домене web-игры, `mars-colony/src/domain/shuttle.ts`
            // (`refreshTrip`/`skipFlight`/`startCooldown`) и оркестрация в
            // `mars-colony/src/state/gameStore.ts`. Мост в Unity ещё не
            // существует для 3D-township (`BrowserBridge.cs` носит только
            // 2D-состояние `ColonyGame`/`FieldView`) — когда он появится,
            // событие "рейс прибыл/убыл" должно звать `Zapustit()` отсюда,
            // а таймер и клавиша ниже остаются debug-путём, не заменяют его.
            // Один прилёт через задержку после старта; периодический автозапуск отключён: улетевший не возвращается.
            if (!_startoval && !_uletel && Time.timeSinceLevelLoad >= _zaderzhkaPervogoPrileta)
            {
                _startoval = true;
                _avtozapuskTaymer = 0f;
                Zapustit();
            }

            if (_klavishaOtladki != KeyCode.None && Input.GetKeyDown(_klavishaOtladki))
                Zapustit();
        }

        /// <summary>Снимает поза-покоя и геометрию полёта живьём, один раз. Безопасно звать многократно.</summary>
        private void ObespechitInfrastrukturu()
        {
            if (!_pozaZapomnena)
            {
                _pozitsiyaPokoya = transform.position;
                _rotatsiyaPokoya = transform.rotation;
                _y0 = _pozitsiyaPokoya.y;
                var r0 = GetComponentInChildren<Renderer>();
                if (r0 != null)
                {
                    _smeshchenieMesha = r0.bounds.center - transform.position;
                    _ekstentyMesha = r0.bounds.extents;
                }
                _pozaZapomnena = true;

                foreach (var tr in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (tr.name == "shattl-otkrytyy" && tr.parent == null) { _otkrytyy = tr.gameObject; break; }
                if (_otkrytyy != null)
                {
                    _otkrytyySmeshchenie = _otkrytyy.transform.position - _pozitsiyaPokoya;
                    _otkrytyyPovorot = Quaternion.Inverse(_rotatsiyaPokoya) * _otkrytyy.transform.rotation;
                    _otkrytyy.SetActive(false);
                }
            }

            if (_infrastrukturaGotova) return;

            var cam = Camera.main;
            if (cam == null)
            {
                var kam = GameObject.Find("kamera");
                if (kam != null) cam = kam.GetComponent<Camera>();
            }
            if (cam == null)
            {
                Debug.LogWarning("[шаттл] камера не найдена — F и просадка остаются на фолбэке "
                        + "до следующего вызова (не хардкодим, план запрещает)");
                return;
            }

            // Допущение 2 плана: просадка 6 px переводится в метры ФАКТОМ через
            // WorldToScreenPoint у пода, а не константой 0.06 м.
            Vector3 p0 = _pozitsiyaPokoya;
            Vector3 p1 = p0 + Vector3.up * 0.5f;
            Vector3 s0 = cam.WorldToScreenPoint(p0);
            Vector3 s1 = cam.WorldToScreenPoint(p1);
            float pxPerMetr = Mathf.Abs(s1.y - s0.y) / 0.5f;
            if (pxPerMetr > 0.01f)
            {
                _prosadkaMetry = PROSADKA_PX / pxPerMetr;
                Debug.Log($"[шаттл] допущение 2 проверено фактом: {pxPerMetr:0.0} px/м у пода, "
                        + $"просадка {PROSADKA_PX} px = {_prosadkaMetry:0.000} м "
                        + "(оценка плана была 0.03-0.04 м/px, то есть 0.18-0.24 общей просадки при "
                        + "другом множителе — здесь считанное фактом число, не оценка)");
            }
            else
            {
                Debug.LogWarning("[шаттл] pxPerMetr вышел вырожденным — просадка на фолбэке 0.06 м");
            }

            // Курс и геометрия захода/ухода. Нос — продольная ось меша: PCA вершин дал (0.829, 0, 0.560),
            // это -up дочернего узла Tripo после поворота X=270 (совпадение 0.2°); тонкий конец (нос) — по +оси.
            var rendNos = GetComponentInChildren<Renderer>();
            Vector3 nos = rendNos != null ? -rendNos.transform.up : transform.forward;
            nos.y = 0f;
            _nos = nos.sqrMagnitude > 0.0001f ? nos.normalized : new Vector3(1f, 0f, 0f);

            Vector3 centrPokoya = _pozitsiyaPokoya + _smeshchenieMesha;          // центр меша (пивот уехал на ~24 м)
            float nizPokoya = centrPokoya.y - _ekstentyMesha.y;                 // низ меша на площадке
            float kray = 0.06f * Screen.width;

            // Заход: старт за левым краем кадра (с запасом на корпус), глиссада с запасом над рельефом.
            _vysotaZahoda = VYSOTA_ZAHODA_MIN;
            _distZahoda = DIST_ZAHODA_MIN;
            while (_distZahoda < 120f && cam.WorldToScreenPoint(centrPokoya - _nos * _distZahoda + Vector3.up * _vysotaZahoda).x > -kray)
                _distZahoda += 2f;
            for (int i = 1; i <= 8; i++)
            {
                float u = i / 10f, ost = 1f - u;
                float sHor = _distZahoda * Mathf.Pow(ost, 1.5f);
                Vector3 p = centrPokoya - _nos * sHor;
                float relyef = VysotaRelyefa(p.x, p.z);
                float nuzhno = relyef + ZAPAS_NAD_RELYEFOM - nizPokoya;       // нужная высота низа над уровнем покоя
                float profil = Mathf.Pow(ost, PROFIL_ZAHODA);                   // доля (H - VZ) в этой точке глиссады
                if (nuzhno > VYSOTA_ZAVISANIYA && profil > 0.05f)
                    _vysotaZahoda = Mathf.Max(_vysotaZahoda, VYSOTA_ZAVISANIYA + (nuzhno - VYSOTA_ZAVISANIYA) / profil);
            }
            _vysotaZahoda = Mathf.Min(_vysotaZahoda, 25f);

            // Уход: носом вперёд до выхода за правый/верхний край, набор высоты с запасом над постройками.
            _vysotaUhoda = VYSOTA_UHODA_MIN;
            _distUhoda = DIST_UHODA_MIN;
            while (_distUhoda < 200f)
            {
                var s = cam.WorldToScreenPoint(centrPokoya + _nos * _distUhoda + Vector3.up * _vysotaUhoda);
                if (s.x > Screen.width + kray || s.y > Screen.height + kray) break;
                _distUhoda += 2f;
            }
            float verhPomehi = 0f; string pomeha = "";
            Vector3 poperechnaya = new Vector3(-_nos.z, 0f, _nos.x);
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(transform)) continue;
                var b = r.bounds;
                if (b.size.x > 60f || b.size.z > 60f) continue;                 // земля, небо, слои теней
                Vector3 otn = b.center - centrPokoya; otn.y = 0f;
                float vdol = Vector3.Dot(otn, _nos);
                if (vdol < 15f || vdol > _distUhoda) continue;                  // ближе 15 м шаттл проходит на взлётной высоте
                if (Mathf.Abs(Vector3.Dot(otn, poperechnaya)) > 6f + Mathf.Max(b.extents.x, b.extents.z)) continue;
                float nuzhno = b.max.y + ZAPAS_NAD_OBEKTAMI - nizPokoya;
                float u = Mathf.Pow(vdol / _distUhoda, 1f / 1.7f);
                float profil = Mathf.Pow(u, PROFIL_UHODA);
                if (profil > 0.02f && nuzhno / profil > _vysotaUhoda) { _vysotaUhoda = nuzhno / profil; verhPomehi = b.max.y; pomeha = r.gameObject.name; }
            }
            _vysotaUhoda = Mathf.Min(_vysotaUhoda, 45f);
            Debug.Log($"[шаттл] курс {_nos:F2}; заход {_distZahoda:0} м с высоты {_vysotaZahoda:0.0}; уход {_distUhoda:0} м на {_vysotaUhoda:0.0} м"
                + (pomeha.Length > 0 ? $" (высоту поднял объект {pomeha}, верх {verhPomehi:0.0})" : ""));

            // Приёмка UI-35: мира по высоте мало — важен ЭКРАН. Проп, который ближе к камере,
            // перекрывает корпус, даже когда шаттл выше его по Y (камера смотрит сверху под 43°).
            // Поднимаем обе траектории, пока низ корпуса не встанет выше верха таких пропов.
            _vysotaZahoda = ChistyySiluet(cam, centrPokoya, nizPokoya, true, _vysotaZahoda);
            _vysotaUhoda = ChistyySiluet(cam, centrPokoya, nizPokoya, false, _vysotaUhoda);

            _infrastrukturaGotova = true;
        }

        /// <summary>Запускает вылет. Повторный запуск во время фаз 2/3/4/6/7
        /// не прерывает текущий полёт (план, §4): вместо этого встаёт в очередь
        /// и досрочно обрывает фазу 5 (стоянку), как только она наступит.</summary>
        /// <summary>Игровой режим: новый рейс прилетает по таймеру домена — снимаем «улетел» и запускаем заход.</summary>
        /// <summary>Сейв говорит «рейс в пути»: шаттл на карте не показывать, первую посадку не играть — сядет, когда домен прилетит.</summary>
        public void SchitatUletevshim()
        {
            StopAllCoroutines();
            _letit = false; _ochered = false; _snovaPosleUlyota = false; _startoval = true; _uletel = true; NaPloshchadke = false; ZagruzkaZavershena = false;
            SkrytTen(); ObnovitOgon(0f);
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (!r.name.StartsWith("ogon-")) r.enabled = false;
            Debug.Log("[шаттл] по сейву рейс в пути — шаттл считается улетевшим, сядет по прилёту");
        }

        public void PriletetSnova()
        {
            if (_letit && NaPloshchadke)
            {
                // Уже стоит на площадке (домен прилетел во время стоянки): новый рейс грузится здесь, повторный запуск не нужен —
                // иначе Zapustit ставил «очередь», стоянка обрывалась и шаттл улетал с пустым заказом (проверка п.1, 07.09)
                Debug.Log("[шаттл] домен прилетел, шаттл уже на площадке — остаёмся");
                return;
            }
            if (_letit && !NaPloshchadke)
            {
                // Ещё улетает: Zapustit() поставил бы в очередь «сократить стоянку», и шаттл остался бы улетевшим
                // (Khan 06.09: «ускорил, а он не возвращается»). Дожидаемся конца улёта и заходим на посадку заново.
                _snovaPosleUlyota = true;
                Debug.Log("[шаттл] домен прилетел раньше конца улёта — посадка сразу после улёта");
                return;
            }
            _uletel = false;
            _startoval = true;
            ZagruzkaZavershena = false;
            Zapustit();
        }

        public void Zapustit()
        {
            ObespechitInfrastrukturu();
            if (_uletel) { Debug.Log("[шаттл] улетел, возврата нет — запуск отклонён"); return; }
            if (_letit)
            {
                _ochered = true;
                Debug.Log("[шаттл] уже в полёте — повторный запуск встал в очередь, "
                        + "сократит текущую стоянку до немедленного взлёта");
                return;
            }
            StartCoroutine(Let());
        }

        /// <summary>Play-only: запускает рейс и снимает три честных кадра окна Game
        /// через ScreenCapture.CaptureScreenshot (40% снижения, T_KASANIE+0.3с,
        /// 60% взлёта) — попытка обойти то, что Camera.Render() в RenderTexture не
        /// рендерит частицы (см. loop/ui/OTCHET-nochi-2.md, UI-23).</summary>
        public void ZapustitISnyat()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[шаттл] ZapustitISnyat работает только в Play, вызов проигнорирован");
                return;
            }
            Zapustit();
            StartCoroutine(SnyatKadryPoleta());
        }

        private IEnumerator SnyatKadryPoleta()
        {
            float start = Time.time;
            float[] momenty = { T_TEST_40_SNIZHENIYA, T_TEST_KASANIE, T_TEST_60_VZLETA };
            float[] snyato = new float[momenty.Length];
            for (int i = 0; i < momenty.Length; i++)
            {
                float tsel = start + momenty[i];
                while (Time.time < tsel) yield return null;
                string put = $"C:/Ai/Jarvis/mars-colony/loop/ui/noch2-shattl-play-{i + 1}.png";
                yield return new WaitForEndOfFrame();   // модуля ScreenCapture в проекте нет: читаем бэкбуфер окна Game, частицы в нём есть
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                System.IO.File.WriteAllBytes(put, tex.EncodeToPNG()); Destroy(tex);
                snyato[i] = Time.time;
                yield return null; // дать кадру дописаться на диск до следующего ожидания
            }
            Debug.Log($"[шаттл] play-кадры сняты: 1={snyato[0]:0.000}с, 2={snyato[1]:0.000}с, 3={snyato[2]:0.000}с");
        }

        private IEnumerator Let()
        {
            _letit = true;
            _ochered = false;

            var tryaska = ObespechitTryaskuKamery();
            // При PylSpraytami (по умолчанию) ParticleSystem вообще не создаётся —
            // задание прямо требует, чтобы при этом флаге ни сферы, ни частицы не
            // возникали в сцене.
            var pyl = PylSpraytami ? null : ObespechitPyl();

            // Фаза 1: телепорт на старт, вне фрустума.
            transform.position = Poza(0f);
            transform.rotation = _rotatsiyaPokoya;
            ObnovitTen();

            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (!r.name.StartsWith("ogon-")) r.enabled = true;   // появился за кадром
            pyl?.Play(true); // ноль локального времени системы совпадает с t=0 ниже
            ObespechitOgon();
            float h0 = Mathf.Max(Poza(0f).y - _pozitsiyaPokoya.y, 0.5f);   // высота появления: огонь тем слабее, чем ниже

            float konetsPosadki = T_SNIZHENIE + T_ZAVISANIE + T_POSADKA;
            float t = 0f;
            while (t < konetsPosadki)
            {
                t += Time.deltaTime;
                transform.position = Poza(Mathf.Min(t, konetsPosadki));
                ObnovitOgon(0.85f * Mathf.Clamp01((transform.position.y - _pozitsiyaPokoya.y) / h0));
                ObnovitTen();
                yield return null;
            }

            // Касание — гарантированно ровно поза покоя по X/Y/Z (план, §4:
            // "transform обязан численно совпасть с этой сохранённой позой").
            transform.position = _pozitsiyaPokoya;
            transform.rotation = _rotatsiyaPokoya;
            ObnovitTen();
            ObnovitOgon(0f);   // сел — огонь прекращается
            NaPloshchadke = true;

            if (tryaska != null)
                tryaska.Vstryakhnut(transform.position, 2f, 0.12f);

            if (PylSpraytami) SozdatPyliSpraytamiEsliNado();
            else SozdatKlubyPyliEsliNado();

            // TODO(звук): щелчок касания шасси. AudioSource.PlayOneShot(clip)
            // здесь, клип не назначен (план, §3).

            // Фаза 5: стоянка. Реальная случайная длительность (не совпадает с
            // компрессированным таймлайном VystavitVremya — там фаза 5 занимает
            // 0 секунд намеренно, см. комментарий у Poza).
            float postoyanka = Random.Range(_stoyankaMin, _stoyankaMax);
            float t5 = 0f;
            bool otkryt = false;
            // Стоянка: минимум случайная, дальше — до погрузки (дроны сели на точки прибытия), но не дольше STOYANKA_MAX.
            while (!_ochered && (IgrovoyRezhim
                       ? !ZagruzkaZavershena || t5 < T_OTKRYTIE_POSLE_KASANIYA + 0.8f
                       : t5 < STOYANKA_MAX && !(t5 >= postoyanka && (MarshrutyDronov.DronyPribyli || !MarshrutyDronov.EstDrony))))
            {
                t5 += Time.deltaTime;
                if (!otkryt && t5 >= T_OTKRYTIE_POSLE_KASANIYA) { PokazatOtkrytyy(true); otkryt = true; }
                yield return null;
            }
            if (otkryt) PokazatOtkrytyy(false);   // перед взлётом отсек закрыт
            NaPloshchadke = false;

            // Фаза 6-7: взлёт.
            if (PylSpraytami) SozdatPyliSpraytamiEsliNado();
            else SozdatKlubyPyliEsliNado();
            float tVzlet = 0f;
            float konetsVzleta = T_ZADERZHKA_VZLETA + T_VZLET;
            while (tVzlet < konetsVzleta)
            {
                tVzlet += Time.deltaTime;
                transform.position = Poza(konetsPosadki + Mathf.Min(tVzlet, konetsVzleta));
                // Напор: за 0.3 с от нуля до 1.3 (длиннее крейсерского), держится задержку и первые 1.5 с разгона, дальше спадает к 0.6.
                float ogon = tVzlet < 0.3f ? Mathf.Lerp(0f, 1.3f, tVzlet / 0.3f)
                    : tVzlet < T_ZADERZHKA_VZLETA + 1.5f ? 1.3f
                    : Mathf.Lerp(1.3f, 0.6f, Mathf.Clamp01((tVzlet - T_ZADERZHKA_VZLETA - 1.5f) / Mathf.Max(T_VZLET - 1.5f, 0.1f)));
                ObnovitOgon(ogon);
                ObnovitTen();
                yield return null;
            }

            // TODO(звук): гул отрыва — PlayOneShot на начале фазы 7 (план, §3).
            // Не проставлен по месту внутри цикла выше, чтобы не плодить флаг
            // "сыграно ли уже": AudioSource с oneshot безопасно звать раз в t=0
            // цикла, здесь оставлен как единая заметка.

            // Фаза 8: скрыть и вернуть трансформ в позу покоя для следующего цикла.
            // Фаза 8: улетел. Заказчик: «если улетел, возвращать не надо — его нет на карте».
            SkrytTen();
            ObnovitOgon(0f);
            var rend = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rend) r.enabled = false;
            _uletel = true;
            _letit = false;
            _ochered = false;
            ZagruzkaZavershena = false;
            Debug.Log(IgrovoyRezhim ? "[шаттл] улетел, вернётся следующим рейсом" : "[шаттл] улетел, возврата нет");
            if (_snovaPosleUlyota) { _snovaPosleUlyota = false; PriletetSnova(); }
        }

        /// <summary>Факелы двигателей: пять сопел по кормовому срезу — три сверху, два снизу, как на меше
        /// (в Play вершины Tripo-меша без Read/Write недоступны, а мировой AABB у диагонально стоящего корпуса
        /// квадратный и уносит корму на 2 м назад; поэтому срез — по локальному боксу меша, он читается всегда).
        /// Геометрия — вытянутые сферы кодом, материал URP Unlit аддитивный, очередь 3001. В сцену не пишутся.</summary>
        /// <summary>Снести факелы и построить заново (после правки кода в Play: при перезагрузке домена Unity
        /// сохраняет и приватные массивы ссылок, так что старые сферы переживают reload).</summary>
        public void PerestroitOgon()
        {
            _fakely = null; _fakelRend = null; _soplaLok = null;
            ObespechitOgon();
        }

        private void ObespechitOgon()
        {
            if (_fakely != null) return;
            for (int i = transform.childCount - 1; i >= 0; i--)                 // сироты после перезагрузки домена в Play
                if (transform.GetChild(i).name.StartsWith("ogon-")) Destroy(transform.GetChild(i).gameObject);
            var mf = GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null) { Debug.LogWarning("[шаттл] огонь: у корпуса нет меша — факелы не построены"); return; }
            Transform uz = mf.transform;
            Bounds b = mf.sharedMesh.bounds;                                   // локальный бокс меша, всегда доступен
            float s = Mathf.Max(uz.lossyScale.x, 1e-4f);                       // локальные единицы → метры
            Vector3 nosLok = uz.InverseTransformDirection(_nos).normalized;
            Vector3 verhLok = uz.InverseTransformDirection(Vector3.up).normalized;
            Vector3 bokLok = Vector3.Cross(verhLok, nosLok).normalized;
            float poluDlina = Mathf.Abs(nosLok.x) * b.extents.x + Mathf.Abs(nosLok.y) * b.extents.y + Mathf.Abs(nosLok.z) * b.extents.z;
            Vector3 srezLok = b.center - nosLok * (poluDlina * 0.97f);        // кормовой срез по локальному боксу
            // сопла (м) относительно центра среза: (вбок, вверх) — верхний ряд из трёх, нижний из двух
            var smesh = new[] { new Vector2(0.15f, 0.5f), new Vector2(0.6f, 0.5f), new Vector2(-0.3f, 0.5f), new Vector2(0.4f, 0f), new Vector2(-0.1f, 0f) };   // по кадру 60: шаг колоколов 0.45 м, ряды через 0.5 м, кластер смещён на 0.15 м к правому борту
            _soplaLok = new Vector3[smesh.Length];
            for (int i = 0; i < smesh.Length; i++)
            {
                Vector3 lok = srezLok + (bokLok * smesh[i].x + verhLok * smesh[i].y) / s;
                _soplaLok[i] = transform.InverseTransformPoint(uz.TransformPoint(lok));
            }
            Vector3 strujaMir = (-_nos * Mathf.Cos(OGON_NAKLON_GRAD * Mathf.Deg2Rad) - Vector3.up * Mathf.Sin(OGON_NAKLON_GRAD * Mathf.Deg2Rad)).normalized;
            _strujaLok = transform.InverseTransformDirection(strujaMir).normalized;
            var matVnesh = MaterialOgnya(new Color(1f, 0.45f, 0.1f, 0.75f));
            var matVnutr = MaterialOgnya(new Color(1f, 0.85f, 0.45f, 0.95f));
            int n = smesh.Length * 2;
            _fakely = new Transform[n]; _fakelRend = new Renderer[n];
            for (int i = 0; i < n; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                g.name = (i % 2 == 0 ? "ogon-vneshniy-" : "ogon-vnutrenniy-") + (i / 2);
                var kol = g.GetComponent<Collider>(); if (kol != null) Destroy(kol);
                g.transform.SetParent(transform, false);
                var r = g.GetComponent<MeshRenderer>();
                r.sharedMaterial = i % 2 == 0 ? matVnesh : matVnutr;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.enabled = false;
                _fakely[i] = g.transform; _fakelRend[i] = r;
            }
            Debug.Log($"[шаттл] огонь двигателей: {smesh.Length} сопел, срез кормы {uz.TransformPoint(srezLok):F1}, корпус по боксу {poluDlina * 2f * s:0.0} м, струя {strujaMir:F2}");
        }

        /// <summary>Интенсивность 0..1.3: длина факела OGON_DLINA × I, ширина OGON_SHIRINA × sqrt(I), мерцание шумом Перлина; 0 — факелы выключены.</summary>
        private void ObnovitOgon(float intensivnost)
        {
            if (_fakely == null) ObespechitOgon();
            else if (_fakely.Length == 0 || _fakely[0] == null) { Debug.LogWarning("[шаттл] факелы уничтожены — строю заново"); PerestroitOgon(); }   // Khan 06.09: «у шаттла пропал огонь»
            if (_fakely == null) return;
            bool goryat = intensivnost > 0.02f;
            for (int i = 0; i < _fakely.Length; i++)
            {
                if (_fakely[i] == null) continue;
                _fakelRend[i].enabled = goryat;
                if (!goryat) continue;
                int soplo = i / 2; bool vnutr = i % 2 == 1;
                float shum = 0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 9f + soplo * 3.1f, soplo * 7.7f + (vnutr ? 2f : 0f));
                float L = OGON_DLINA * intensivnost * shum * (vnutr ? 0.7f : 1f);
                float W = OGON_SHIRINA * Mathf.Sqrt(intensivnost) * (0.9f + 0.2f * shum) * (vnutr ? 0.55f : 1f);
                float s = Mathf.Max(transform.lossyScale.x, 1e-4f);
                var t = _fakely[i];
                t.localRotation = Quaternion.LookRotation(_strujaLok, transform.InverseTransformDirection(Vector3.up));
                t.localPosition = _soplaLok[soplo] + _strujaLok * (L * 0.5f / s);
                t.localScale = new Vector3(W, W, L) / s;
            }
        }

        private static Material MaterialOgnya(Color cvet)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", cvet);
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 2f);   // прозрачный, аддитивный: огонь светится, а не закрашивает
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3001;
            return mat;
        }
        /// <summary>
        /// Поза шаттла по глобальному t (секунды от начала захода). Общая функция для живой
        /// корутины и <see cref="VystavitVremya"/>. Заход и уход — по курсу носа (замечание
        /// заказчика 05.09: двигатели сзади, вертикального взлёта нет): заход с левого края кадра
        /// глиссадой к площадке, короткое выравнивание, просадка-баунс в позу покоя; уход — разгон
        /// носом вперёд через карту с набором высоты к правому верхнему углу. Поза покоя —
        /// константа: и точка касания, и точка старта.
        ///
        /// Фаза 5 (стоянка) в ЭТОЙ функции имеет длительность ровно 0 — таймлайн для
        /// детерминированной выставки/сэмплинга сшивает фазу 4 сразу с фазой 6.
        /// </summary>
        private Vector3 Poza(float t)
        {
            Vector3 pokoy = _pozitsiyaPokoya;
            if (t <= 0f)
                return pokoy - _nos * _distZahoda + Vector3.up * _vysotaZahoda;

            if (t < T_SNIZHENIE)
            {
                float u = t / T_SNIZHENIE;
                float ost = 1f - u;
                float sHor = _distZahoda * Mathf.Pow(ost, 1.5f);                                 // торможение к площадке
                float h = VYSOTA_ZAVISANIYA + (_vysotaZahoda - VYSOTA_ZAVISANIYA) * Mathf.Pow(ost, PROFIL_ZAHODA);   // приёмка UI-35: было ost^2 — на 11 м до площадки шаттл шёл на 2.2 м и уходил за ящики у ангара
                return pokoy - _nos * sHor + Vector3.up * h;
            }

            float t3 = t - T_SNIZHENIE;
            if (t3 < T_ZAVISANIE)
            {
                float shum = Mathf.Sin(t3 * 40f) * 0.03f;
                return pokoy + Vector3.up * (VYSOTA_ZAVISANIYA + shum);
            }

            float t4 = t3 - T_ZAVISANIE;
            if (t4 < T_POSADKA)
            {
                float u = t4 / T_POSADKA;
                return new Vector3(pokoy.x, KrivayaPosadki(u), pokoy.z);
            }

            float t6 = t4 - T_POSADKA;
            if (t6 < T_ZADERZHKA_VZLETA)
            {
                float shum = Mathf.Sin(t6 * 60f) * 0.03f;
                return pokoy + Vector3.up * shum;
            }

            float t7 = t6 - T_ZADERZHKA_VZLETA;
            if (t7 <= T_VZLET)
            {
                float u = Mathf.Clamp01(t7 / T_VZLET);
                float sHor = _distUhoda * Mathf.Pow(u, 1.7f);      // разгон
                float h = _vysotaUhoda * Mathf.Pow(u, PROFIL_UHODA);    // приёмка UI-35: было u^1.8 — шаттл проходил за куполом теплицы
                return pokoy + _nos * sHor + Vector3.up * h;
            }

            return pokoy + _nos * _distUhoda + Vector3.up * _vysotaUhoda;
        }

        private float KrivayaPosadki(float u)
        {
            // Ключи плана: (0, Y0+1.8), (0.75, Y0-просадка, овершут), (1.0, Y0).
            // Минимум строго в k1 (плоские касательные) — низ отдачи, откуда
            // кривая идёт обратно вверх до Y0 к t=1: это и есть "овершут-баунс".
            var k0 = new Keyframe(0f, _y0 + VYSOTA_ZAVISANIYA) { outTangent = -2.5f * (VYSOTA_ZAVISANIYA + _prosadkaMetry) / 0.75f }; // 2.5 средней крутизны: без провала ниже k1 при малой высоте выравнивания
            var k1 = new Keyframe(0.75f, _y0 - _prosadkaMetry) { inTangent = 0f, outTangent = 0f };
            var k2 = new Keyframe(1f, _y0) { inTangent = 0f };
            var kriv = new AnimationCurve(k0, k1, k2);
            return kriv.Evaluate(u);
        }

        /// <summary>
        /// Детерминированная выставка позы для батч-кадров приёмки
        /// (`InterfeysBuilder.KadrShattl`). Не запускает корутину, не трогает
        /// тряску камеры (план, §5, п.2: тряску для скриншот-теста надо уметь
        /// выключать — здесь она просто никогда не вызывается этим путём) и
        /// не проигрывает звук-заглушки. Синхронизирует пыль тем же t через
        /// <see cref="ParticleSystem.Simulate"/>, чтобы кольцо было на месте
        /// на кадрах касания/взлёта и отсутствовало на кадре снижения.
        /// </summary>
        public void VystavitVremya(float t)
        {
            ObespechitInfrastrukturu();
            transform.position = Poza(t);
            transform.rotation = _rotatsiyaPokoya;
            ObnovitTen();

            // Спрайт-квады живут только через StartCoroutine из Let() (Play), сюда,
            // в детерминированную выставку позы для батч-кадров, не заведены —
            // проверка кадра 2 идёт через ZapustitISnyat (см. её XML-комментарий).
            var pyl = PylSpraytami ? null : ObespechitPyl();
            if (pyl != null)
                pyl.Simulate(Mathf.Max(0f, t), true, true);
        }

        /// <summary>Возвращает transform в записанную позу покоя. Печатает
        /// позицию до/после — приёмка плана требует явной проверки (§, задание
        /// mars-unity-coder п.4).</summary>
        public void VosstanovitPokoy()
        {
            Vector3 do_ = transform.position;
            transform.position = _pozitsiyaPokoya;
            transform.rotation = _rotatsiyaPokoya;
            var pyl = PylSpraytami ? null : ObespechitPyl();
            pyl?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            SkrytTen();
            Debug.Log($"[шаттл] поза восстановлена: до ({do_.x:0.000}, {do_.y:0.000}, {do_.z:0.000}), "
                    + $"после ({transform.position.x:0.000}, {transform.position.y:0.000}, {transform.position.z:0.000}), "
                    + $"поза покоя ({_pozitsiyaPokoya.x:0.000}, {_pozitsiyaPokoya.y:0.000}, {_pozitsiyaPokoya.z:0.000})");
        }

        /// <summary>Подмена закрытого шаттла открытым (и обратно) на стоянке. Открытый ставится
        /// по позе покоя со смещением, снятым в Awake, поэтому переживает перенос площадки.</summary>
        private void PokazatOtkrytyy(bool otkryt)
        {
            if (_otkrytyy == null) return;
            if (otkryt)
            {
                _otkrytyy.transform.position = _pozitsiyaPokoya + _otkrytyySmeshchenie;
                _otkrytyy.transform.rotation = _rotatsiyaPokoya * _otkrytyyPovorot;
            }
            _otkrytyy.SetActive(otkryt);
            foreach (var r in GetComponentsInChildren<Renderer>(true)) if (!r.name.StartsWith("ogon-")) r.enabled = !otkryt;   // факелы живут своей логикой
        }

        /// <summary>Минимальная высота траектории, при которой корпус не перекрывают пропы,
        /// стоящие ближе к камере. Считается экраном: 24 пробы вдоль пути, кандидаты — рендереры
        /// в 30 м от коридора; высота поднимается шагами по 12%, пока есть перекрытия (или до 45 м).</summary>
        private float ChistyySiluet(Camera cam, Vector3 centrPokoya, float nizPokoya, bool zahod, float vysota)
        {
            Vector3 poperechnaya = new Vector3(-_nos.z, 0f, _nos.x);
            float dist = zahod ? _distZahoda : _distUhoda;
            var kandidaty = new System.Collections.Generic.List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(transform)) continue;
                var b = r.bounds;
                if (b.size.x > 25f || b.size.z > 25f) continue;                       // земля, небо, слой теней и крупные постройки
                if (r.GetComponentInParent<BuildingClickTarget>() != null) continue;   // над постройками шаттл летит по замыслу: их крыша в кадре — не дефект, а перспектива
                float podnyatie = b.max.y - VysotaRelyefa(b.center.x, b.center.z);
                if (podnyatie < 1f || podnyatie > 9f) continue;                       // плоские дороги/бортики не перекрывают, высокие башни — отдельный случай
                Vector3 otn = b.center - centrPokoya; otn.y = 0f;
                float vdol = Vector3.Dot(otn, _nos) * (zahod ? -1f : 1f);
                if (vdol < 2f || vdol > dist) continue;
                if (Mathf.Abs(Vector3.Dot(otn, poperechnaya)) > 14f) continue;        // только то, что стоит вплотную к коридору
                kandidaty.Add(r);
            }

            float shattlPolvysoty = _ekstentyMesha.y;
            float potolok = vysota * 1.6f;   // выше — глиссада перестаёт быть пологой, а заказчик просил именно пологую
            for (int iter = 0; iter < 10 && vysota < potolok; iter++)
            {
                int meshaet = 0; string kto = "";
                for (int i = 1; i <= 24; i++)
                {
                    float u = zahod ? (0.45f + 0.5f * i / 24f) : (0.05f + 0.55f * i / 24f);   // низкий участок пути: там и перекрывают
                    float sHor, h;
                    if (zahod)
                    {
                        float ost = 1f - u;
                        sHor = -dist * Mathf.Pow(ost, 1.5f);
                        h = VYSOTA_ZAVISANIYA + (vysota - VYSOTA_ZAVISANIYA) * Mathf.Pow(ost, PROFIL_ZAHODA);
                    }
                    else
                    {
                        sHor = dist * Mathf.Pow(u, 1.7f);
                        h = vysota * Mathf.Pow(u, PROFIL_UHODA);
                    }
                    Vector3 centr = centrPokoya + _nos * sHor + Vector3.up * h;
                    Vector3 sNiz = cam.WorldToScreenPoint(centr - Vector3.up * shattlPolvysoty);
                    if (sNiz.z <= 0.1f) continue;
                    Vector3 sLevo = cam.WorldToScreenPoint(centr - _nos * _ekstentyMesha.x);
                    Vector3 sPravo = cam.WorldToScreenPoint(centr + _nos * _ekstentyMesha.x);
                    float xMin = Mathf.Min(sLevo.x, sPravo.x), xMax = Mathf.Max(sLevo.x, sPravo.x);
                    float glubinaShattla = sNiz.z;
                    foreach (var r in kandidaty)
                    {
                        var b = r.bounds;
                        float glubinaPropa = cam.WorldToScreenPoint(b.center).z - Mathf.Max(b.extents.x, b.extents.z);
                        if (glubinaPropa >= glubinaShattla) continue;                       // проп дальше — перекрыть не может
                        float pxMin = 1e9f, pxMax = -1e9f, pyMax = -1e9f;
                        for (int c = 0; c < 8; c++)
                        {
                            Vector3 p = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                            Vector3 sp = cam.WorldToScreenPoint(p);
                            pxMin = Mathf.Min(pxMin, sp.x); pxMax = Mathf.Max(pxMax, sp.x); pyMax = Mathf.Max(pyMax, sp.y);
                        }
                        if (pxMax < xMin || pxMin > xMax) continue;                         // не под корпусом по горизонтали
                        if (pyMax > sNiz.y - ZAPAS_SILUETA_PX) { meshaet++; if (kto.Length == 0) kto = r.gameObject.name; }
                    }
                }
                if (meshaet == 0)
                {
                    Debug.Log($"[шаттл] силуэт {(zahod ? "захода" : "ухода")} чист на высоте {vysota:0.0} м (итераций {iter})");
                    return vysota;
                }
                if (iter == 0)
                    Debug.Log($"[шаттл] силуэт {(zahod ? "захода" : "ухода")}: {meshaet} перекрытий на {vysota:0.0} м, первый — {kto}; поднимаю");
                vysota *= 1.08f;
            }
            Debug.LogWarning($"[шаттл] силуэт {(zahod ? "захода" : "ухода")}: перекрытия остались, но подъём упёрся в потолок {potolok:0.0} м — оставляю {vysota:0.0} м");
            return vysota;
        }

        /// <summary>Верхняя точка коллайдеров под (x,z): земля и постройки. -100, если под точкой пусто.</summary>
        private static float VysotaRelyefa(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 80f, z), Vector3.down, 200f);
            float top = -100f;
            foreach (var h in hits) if (h.point.y > top) top = h.point.y;
            return top;
        }

        private void ObespechitTen()
        {
            if (_ten != null) return;
            _ten = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _ten.name = "ten-shattla";
            var kol = _ten.GetComponent<Collider>();
            if (kol != null) Destroy(kol);
            _ten.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // нормаль квада (-Z) смотрит вверх
            _tenRend = _ten.GetComponent<MeshRenderer>();
            _tenRend.sharedMaterial = MaterialTeni();   // цвет/альфа правятся прямо в материале: он у декали свой
            _ten.SetActive(false);
        }

        /// <summary>Ставит декаль под текущий центр меша на уровень поверхности покоя;
        /// с высотой эллипс растёт и бледнеет. Зовётся после каждой смены позы.</summary>
        private void ObnovitTen()
        {
            ObespechitTen();
            Vector3 centr = transform.position + _smeshchenieMesha;
            float zemlyaY = _pozitsiyaPokoya.y + _smeshchenieMesha.y - _ekstentyMesha.y;
            float h = Mathf.Max(0f, centr.y - _ekstentyMesha.y - zemlyaY);
            float k = Mathf.Clamp01(h / TEN_VYSOTA_ISCHEZANIYA);
            float rost = 1f + TEN_ROST_NA_VYSOTE * k;
            _ten.transform.position = new Vector3(centr.x, zemlyaY + TEN_ZAZOR, centr.z);
            _ten.transform.localScale = new Vector3(_ekstentyMesha.x * TEN_DIAMETR_K * rost, _ekstentyMesha.z * TEN_DIAMETR_K * rost, 1f);
            _matTen.SetColor("_BaseColor", new Color(0f, 0f, 0f, TEN_ALFA_NA_ZEMLE * (1f - 0.55f * k)));
            if (!_ten.activeSelf) _ten.SetActive(true);
        }

        private void SkrytTen()
        {
            if (_ten != null) _ten.SetActive(false);
        }

        private Material MaterialTeni()
        {
            if (_matTen != null) return _matTen;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            mat.SetTexture("_BaseMap", TeksturaTeni(64));
            mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, TEN_ALFA_NA_ZEMLE));
            // Та же схема прозрачности URP, что у квадов пыли (проверена тест-квадами, c44e316).
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = 3001; // как у проверенных тест-квадов; вариант с MPB и очередью 2999 в кадр не попадал (серия ten1)
            _matTen = mat;
            return mat;
        }

        /// <summary>Мягкий радиальный спад: белый цвет, альфа (1-r)^1.6 — ядро плотное, кромка растворяется.</summary>
        private static Texture2D TeksturaTeni(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 0.9f);   // ядро плотное почти до кромки: мягкий спад 1.6 давал пятно ниже шума грунта
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        public bool VPolete => _letit;

        private TryaskaKamery ObespechitTryaskuKamery()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var kam = GameObject.Find("kamera");
                if (kam != null) cam = kam.GetComponent<Camera>();
            }
            if (cam == null) return null;

            var t = cam.GetComponent<TryaskaKamery>();
            if (t == null) t = cam.gameObject.AddComponent<TryaskaKamery>();
            return t;
        }

        /// <summary>
        /// Строит ParticleSystem пыли кодом (доктрина сборки: не руками) один
        /// раз, кэширует. Кольцо по кромке "ploshchadka-shattla" радиусом
        /// 3.6-4.0 м (план, §2). Импульсы касания/взлёта — Bursts модуля
        /// Emission с абсолютным локальным временем системы, поэтому
        /// <see cref="ParticleSystem.Simulate"/> и обычный <c>Play()</c>
        /// дают ОДНУ и ту же картину: тестовый сэмплинг не требует отдельной
        /// копии логики эмиссии.
        /// </summary>
        /// <summary>Запускает запасной вариант пыли (клубы-сферы), если
        /// <see cref="PylMeshami"/> включён. Ничего не делает при выключенном
        /// флаге — тогда работает обычный ObespechitPyl/ParticleSystem.</summary>
        private void SozdatKlubyPyliEsliNado()
        {
            if (!PylMeshami) return;
            StartCoroutine(KlubyPyli());
        }

        // ---- попытка 4: билборды-квады с флипбуком по спрайт-листу 5x5 ----
        private const int SETKA_SPRAYTA = 5;                 // кадров по стороне листа
        private const int KADROV_SPRAYTA = SETKA_SPRAYTA * SETKA_SPRAYTA; // 25
        private const float T_ZHIZN_SPRAYTA = 1.4f;          // все 25 кадров за 1.4с (план п.4)

        /// <summary>Запускает вариант "спрайт-лист", если <see cref="PylSpraytami"/> включён.</summary>
        private void SozdatPyliSpraytamiEsliNado()
        {
            if (!PylSpraytami) return;
            StartCoroutine(PyliSpraytami());
        }

        /// <summary>Центр видимой геометрии шаттла (не пивот — см. комментарий у
        /// ObespechitPyl про смещение пивота на ~24 м), низ меша.</summary>
        private Vector3 CentrMeshaShattla()
        {
            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                var b = rend.bounds;
                return new Vector3(b.center.x, b.min.y, b.center.z);
            }
            return new Vector3(_pozitsiyaPokoya.x, _y0, _pozitsiyaPokoya.z);
        }

        private IEnumerator PyliSpraytami()
        {
            var mat = ObespechitMaterialSprayta();
            if (mat == null) yield break; // pylList не назначен редакторским кодом — пропускаем без падения

            Vector3 centr = CentrMeshaShattla();
            float R = RadiusShattla();   // seriya shattl3: kolco 1.5-3 m lezhalo POD korpusom (shattl ~10 m), kvady zakryty samim shattlom
            float baseY = centr.y + 0.3f; // "на уровне площадки +0.3 м" (задание)

            int kolichestvo = Random.Range(6, 9); // 6-8 квадов, задание
            for (int i = 0; i < kolichestvo; i++)
            {
                float ugol = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float radius = Random.Range(R * 1.0f, R * 1.4f); // kolco ot kromki silueta naruzhu (bylo 1.5-3 m po zadaniyu - pod korpusom)
                Vector3 napravlenie = new Vector3(Mathf.Cos(ugol), 0f, Mathf.Sin(ugol));
                Vector3 start = new Vector3(centr.x, baseY, centr.z) + napravlenie * radius;
                StartCoroutine(OdinSprayt(start, napravlenie, mat, R));
            }
        }

        /// <summary>Polovina bolshey storony gabarita shattla po XZ (m): kolco pyli i razmer klubov schitayutsya ot neyo.</summary>
        private float RadiusShattla()
        {
            var rend = GetComponentInChildren<Renderer>();
            if (rend == null) return 3f;
            var e = rend.bounds.extents;
            return Mathf.Max(e.x, e.z, 1.5f);
        }

        /// <summary>Один билборд-квад: 1.4с флипбук 25 кадров (ease-out), масштаб
        /// 1.2->2.4 м, дрейф от центра 0.8 м, разворот на камеру "kamera" каждый
        /// кадр. UV-окно листа задаётся через MaterialPropertyBlock (_BaseMap_ST),
        /// чтобы все квады шаттла делили один материал-кэш.</summary>
        private IEnumerator OdinSprayt(Vector3 start, Vector3 napravlenie, Material mat, float R)
        {
            var kvad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            kvad.name = "sprayt-pyli-shattla";
            var kollayder = kvad.GetComponent<Collider>();
            if (kollayder != null) Destroy(kollayder);
            var rend = kvad.GetComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            kvad.transform.position = start;
            // Okno kadra lista - cherez UV samogo mesha, ne cherez _BaseMap_ST v MaterialPropertyBlock:
            // Particles/Unlit v URP ignoriroval ST iz bloka i risoval VES list 5x5 na kvade (seriya shattl5: setka tochek vmesto kluba).
            var mf = kvad.GetComponent<MeshFilter>();
            var mesh = Instantiate(mf.sharedMesh);
            mf.mesh = mesh;
            var uvIsh = mesh.uv;
            var uv = new Vector2[uvIsh.Length];

            var cam = Camera.main;
            if (cam == null)
            {
                var kam = GameObject.Find("kamera");
                if (kam != null) cam = kam.GetComponent<Camera>();
            }

            var blok = new MaterialPropertyBlock();
            var tint = new Color(0xE8 / 255f, 0xB4 / 255f, 0x8F / 255f, 1f) /* bylo D9895A a0.65 (zadanie): na 75 m ot kamery davalo effektivnuyu alfu ~0.2, pyl ne chitalas (seriya shattl6, diff_heat) */; // #D9895A, альфа 0.65 — задание
            float shag = 1f / SETKA_SPRAYTA;

            float t = 0f;
            while (t < T_ZHIZN_SPRAYTA)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / T_ZHIZN_SPRAYTA);
                float euOut = 1f - (1f - u) * (1f - u); // ease-out — задание, п.4

                int kadr = Mathf.Clamp(Mathf.FloorToInt(euOut * KADROV_SPRAYTA), 0, KADROV_SPRAYTA - 1);
                int kolonka = kadr % SETKA_SPRAYTA;
                int ryad = kadr / SETKA_SPRAYTA;
                // Лист читается слева-направо, сверху-вниз (задание); V=0 у низа
                // текстуры, поэтому верхнему ряду (ryad=0) соответствует offsetY,
                // близкий к 1.
                float ox = kolonka * shag, oy = 1f - (ryad + 1) * shag;
                for (int vi = 0; vi < uvIsh.Length; vi++) uv[vi] = new Vector2(ox + uvIsh[vi].x * shag, oy + uvIsh[vi].y * shag);
                mesh.uv = uv;
                blok.SetColor("_BaseColor", tint);
                rend.SetPropertyBlock(blok);

                kvad.transform.localScale = Vector3.one * Mathf.Lerp(Mathf.Max(1.2f, R * 0.4f), Mathf.Max(2.4f, R * 0.8f), euOut); // ot gabarita shattla; 1.2-2.4 m - nizhniy predel zadaniya
                kvad.transform.position = start + napravlenie * (Mathf.Max(0.8f, R * 0.3f) * euOut); // dreyf naruzhu ot gabarita

                if (cam != null)
                {
                    Vector3 otKvadaKKamere = cam.transform.position - kvad.transform.position;
                    if (otKvadaKKamere.sqrMagnitude > 0.0001f)
                        kvad.transform.rotation = Quaternion.LookRotation(-otKvadaKKamere.normalized, Vector3.up);
                }

                yield return null;
            }

            Destroy(mesh);
            Destroy(kvad);
        }

        /// <summary>Кэшированный материал квадов пыли. null, если pylList не
        /// назначен (см. <see cref="pylList"/> и InterfeysBuilder.ObespechitPylList) —
        /// это ошибка настройки редактора, не повод падать в рантайме.</summary>
        private Material ObespechitMaterialSprayta()
        {
            if (_matSprayty != null) return _matSprayty;
            if (pylList == null)
            {
                Debug.LogWarning("[шаттл] PylSpraytami включён, но pylList не назначен — "
                        + "спрайт-квады пропущены. Назначается InterfeysBuilder.KadrShattl "
                        + "через AssetDatabase.LoadAssetAtPath, запусти этот пункт меню.");
                return null;
            }

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            mat.SetTexture("_BaseMap", pylList);
            mat.SetColor("_BaseColor", new Color(0xE8 / 255f, 0xB4 / 255f, 0x8F / 255f, 1f) /* bylo D9895A a0.65 (zadanie): na 75 m ot kamery davalo effektivnuyu alfu ~0.2, pyl ne chitalas (seriya shattl6, diff_heat) */);
            // Та же схема прозрачности URP, что уже проверена на "steam" в
            // ColonyOursBuilder и на ParticleSystem-варианте пыли выше в этом файле.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = 3000;

            _matSprayty = mat;
            return _matSprayty;
        }

        private IEnumerator KlubyPyli()
        {
            Vector3 centr = new Vector3(_pozitsiyaPokoya.x, _y0, _pozitsiyaPokoya.z);
            float radius = 3.8f;
            var pad = GameObject.Find("ploshchadka-shattla");
            if (pad != null)
            {
                var rends = pad.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    centr = new Vector3(b.center.x, _y0, b.center.z);
                    radius = Mathf.Clamp((b.size.x + b.size.z) * 0.25f, 3.6f, 4.0f);
                }
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0xD9 / 255f, 0x89 / 255f, 0x5A / 255f));
            mat.SetFloat("_Smoothness", 0.1f);

            int kolichestvo = Random.Range(6, 9); // 6-8 клубов, план п.4 задания
            for (int i = 0; i < kolichestvo; i++)
            {
                float ugol = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 napravlenie = new Vector3(Mathf.Cos(ugol), 0f, Mathf.Sin(ugol));
                Vector3 start = centr + napravlenie * radius;
                StartCoroutine(OdinKlub(start, napravlenie, mat));
            }
            yield break;
        }

        /// <summary>Один клуб-сфера: 0.4с рост 0.8->2.0м с уходом от кромки,
        /// затем 1.2с схлопывание в 0 (растворение без альфы, план п.4).</summary>
        private IEnumerator OdinKlub(Vector3 start, Vector3 napravlenie, Material mat)
        {
            var sfera = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sfera.name = "klub-pyli-zapasnoy";
            var kollayder = sfera.GetComponent<Collider>();
            if (kollayder != null) Destroy(kollayder);
            sfera.GetComponent<Renderer>().sharedMaterial = mat;
            sfera.transform.position = start + Vector3.up * 0.3f;

            const float T_ROST = 0.4f;
            const float T_RASTVORENIE = 1.2f;
            float t = 0f;
            while (t < T_ROST)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / T_ROST);
                sfera.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 2.0f, u);
                sfera.transform.position += napravlenie * Time.deltaTime * 0.8f;
                yield return null;
            }

            t = 0f;
            while (t < T_RASTVORENIE)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / T_RASTVORENIE);
                sfera.transform.localScale = Vector3.one * Mathf.Lerp(2.0f, 0f, u);
                sfera.transform.position += napravlenie * Time.deltaTime * 0.3f;
                yield return null;
            }

            Destroy(sfera);
        }

        private ParticleSystem ObespechitPyl()
        {
            if (_pyl != null) return _pyl;

            // ВАЖНО: эмиттер НЕ вешается на transform шаттла. Шаттл улетает на
            // 14+9 м за кадр, и эмиттер, унаследовавший его движение, тащил бы
            // точку рождения частиц за собой — кольцо пыли уехало бы с пода в
            // небо вместе с кораблём. Simulation Space = World спасает только
            // уже рождённые частицы, а не точку СПАВНА следующего Burst.
            // Эмиттер живёт отдельным объектом на паде, находится по имени, а
            // не по иерархии — переживает перекомпиляцию (обнуление _pyl).
            var sushch = GameObject.Find("pyl-poda-shattla");
            if (sushch != null)
            {
                _pyl = sushch.GetComponent<ParticleSystem>();
                if (_pyl != null) return _pyl;
            }

            float radius = 3.8f;
            var pad = GameObject.Find("ploshchadka-shattla");
            if (pad != null)
            {
                var rends = pad.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    radius = Mathf.Clamp((b.size.x + b.size.z) * 0.25f, 3.6f, 4.0f);
                }
            }

            var go = new GameObject("pyl-poda-shattla");
            if (pad != null) go.transform.SetParent(pad.transform, true);
            // Пивот модели шаттла смещён от меша на ~24 м (память проекта «ставить по
            // центру геометрии»): transform.position = (8.75, 4.80, -11.25), а меш стоит
            // на площадке в (-10.89, 4.06, -24.21). Эмиттер — по центру меша, на уровне его низа.
            var rendShattla = GetComponentInChildren<Renderer>();
            if (rendShattla != null)
            {
                var b = rendShattla.bounds;
                var sdvig = transform.position - _pozitsiyaPokoya;   // если сейчас не в покое — вернуть к покою
                go.transform.position = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z) - sdvig;
            }
            else
                go.transform.position = new Vector3(_pozitsiyaPokoya.x, _y0 + 0.05f, _pozitsiyaPokoya.z);
            go.transform.rotation = Quaternion.identity;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.0f, 1.6f);   // 0.4-0.6 м не читались в кадре 1600x900 (попытка 1 UI-23)
            main.gravityModifier = 0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            // Реголит #C86A3C, приглушённый под пыль: альфа 140/255, без
            // изменения оттенка по времени жизни (план, §2).
            main.startColor = new Color(0xC8 / 255f, 0x6A / 255f, 0x3C / 255f, 140f / 255f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f; // только кромка, не заливка
            shape.rotation = new Vector3(-90f, 0f, 0f); // диск лёг на землю, нормаль вверх

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            float konetsPosadki = T_SNIZHENIE + T_ZAVISANIE + T_POSADKA;
            var bursts = new System.Collections.Generic.List<ParticleSystem.Burst>
            {
                // Касание (план §2: 40-60 частиц за импульс).
                new ParticleSystem.Burst(T_KASANIE, (short)90, (short)90),
                // Отрыв.
                new ParticleSystem.Burst(T_VZLET_START, (short)50, (short)50),
                // Добивочный слабее, переход к дуге (t≈0.3 фазы 7).
                new ParticleSystem.Burst(T_VZLET_START + 0.3f * T_VZLET, (short)20, (short)20),
            };
            // Редкие клубы во время стоянки (план: 4-8/сек редкими клубами, не
            // туман). Bursts требуют абсолютного времени системы, а реальная
            // стоянка случайна (6-10 с) — покрываем окно с запасом до +9.5с
            // после посадки; если стоянка окажется короче, лишние bursts
            // просто не наступят до Stop(), это не портит картину.
            // Unity держит не больше 8 бёрстов на систему (SetBurstCount clamps): 3 уже заняты,
            // на стоянку остаётся 5 клубов с шагом 1.8 с.
            for (float tk = konetsPosadki + 0.6f; tk < konetsPosadki + 9.6f && bursts.Count < 8; tk += 1.8f)
                bursts.Add(new ParticleSystem.Burst(tk, (short)3, (short)5));
            emission.SetBursts(bursts.ToArray());

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            var baseCol = new Color(0xD9 / 255f, 0x89 / 255f, 0x5A / 255f);   // светлее грунта, иначе клуб сливается с реголитом
            grad.SetKeys(
                new[] { new GradientColorKey(baseCol, 0f), new GradientColorKey(baseCol, 1f) },
                new[] { new GradientAlphaKey(210f / 255f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var soL = ps.sizeOverLifetime;
            soL.enabled = true;
            soL.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 3f)));

            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            // Лёгкий подъём первую треть жизни, дальше оседает — план §2.
            // все три оси в одном режиме (Curve), иначе Unity пишет «Velocity curves must all be
            // in the same mode» и модуль не работает — пыль не появлялась на кадре касания
            var nol = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0f));
            vol.x = new ParticleSystem.MinMaxCurve(0f, nol);
            vol.z = new ParticleSystem.MinMaxCurve(0f, nol);
            vol.y = new ParticleSystem.MinMaxCurve(0.3f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.33f, 1f), new Keyframe(1f, 0f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Lit"));
            // Та же схема прозрачности URP, что уже проверена на "steam" в
            // ColonyOursBuilder (_Mode там не существует, нужны _Surface/_Blend
            // плюс ключевое слово — иначе клуб выходит непрозрачным).
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = 3000;
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            _pyl = ps;
            return _pyl;
        }
    }
}
