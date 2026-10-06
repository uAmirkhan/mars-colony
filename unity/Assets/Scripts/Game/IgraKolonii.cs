using System;
using System.Collections.Generic;
using MarsColony.Domain;
using MarsColony.Domain.Config;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Состояние всей игры (ночь 3): экономика колонии (домен) плюс то, чего в веб-домене не было
    /// как единого стора — фабрики по зданиям, площадки добычи, рейс шаттла, доска дронов, модули.
    /// Сохраняется целиком в PlayerPrefs (Newtonsoft), колония — плоским снимком (в ней словарь и
    /// readonly-список, которые JsonUtility и автосоздание Newtonsoft портят).
    /// </summary>
    public sealed class SostoyanieIgry
    {
        public ColonyState kolonia;
        /// <summary>Ключ — роль здания (rol у BuildingClickTarget).</summary>
        public Dictionary<string, List<FactorySlot>> fabriki = new Dictionary<string, List<FactorySlot>>();
        /// <summary>Ключ — good_id добываемого: water_ice, regolith.</summary>
        public Dictionary<string, MiningSite> dobycha = new Dictionary<string, MiningSite>();
        public ShuttleTrip shattl;
        public Plavilnya plavilnya = new Plavilnya();
        public int prilyotov;
        public List<OrderSlot> drony = new List<OrderSlot>();
        public Dictionary<string, int> moduli = new Dictionary<string, int>();
        /// <summary>
        /// Привезённое рейсом и ещё не вынутое игроком, ПО НОМЕРУ ОТСЕКА: длина списка равна числу
        /// отсеков, пустая строка — отсек уже разгружен. Список-очередь тут не годится: при удалении
        /// элемента груз сдвигался влево, и закрывался не тот контейнер, по которому нажали (Khan 08.09).
        /// </summary>
        public List<string> privezeno = new List<string>();
        public int izotopy;
        /// <summary>Уровни техники, шаттла и дронов (спека 08.09, ангар спецтехники). Ключи — `Uluchsheniya.VSE`.</summary>
        public Dictionary<string, int> urovni = new Dictionary<string, int>();
        /// <summary>Энергия (спека 08.09): копится по времени от энергостанции и солнечных плит, ни на что не тратится.</summary>
        public double energiya;
        /// <summary>Unix-секунды последнего начисления энергии; 0 — ещё не начислялась.</summary>
        public double energiyaObnovlena;
        /// <summary>Счётчики первых действий для цепочки целей: posevov, sborov, zagruzok, dronov, dobychi, gorshkov.</summary>
        public Dictionary<string, int> schetchiki = new Dictionary<string, int>();
    }

    /// <summary>
    /// Рантайм игры в сцене MAIN (спека `loop/SPEC-NOCH-3-igra.md`). Триггер всего — игрок: панель
    /// действий открывается по тапу на здание (`VyborZdaniy.Vybrano`), каждое действие — вызов домена
    /// и `Apply`. Само ничего не капает: техника едет по тапу «запустить», шаттл улетает, когда
    /// заполнен последний отсек, дроны летят по «Отправить».
    ///
    /// Часы — реальное время (unix-секунды), ускорение ×N для проверки идёт через `Tuning.time_scale`,
    /// поэтому числа каркаса не трогаются, а сохранение переживает смену ускорения.
    /// База максимальная (решение заказчика): всё открыто, уровень для проверок открытия — 99.
    /// </summary>
    public sealed class IgraKolonii : MonoBehaviour
    {
        public static IgraKolonii Ekz { get; private set; }

        [Tooltip("Ускорение времени для проверки: 1 — времена каркаса, 10 — в десять раз быстрее.")]
        [SerializeField] private float _uskorenie = 1f;
        [Tooltip("Сбросить сохранение при входе в Play (проверка с чистого старта).")]
        [SerializeField] private bool _sbrositSohranenie = false;

        public SostoyanieIgry Igra { get; private set; }
        public ColonyState State => Igra.kolonia;

        public const int UROVEN_DOSTUPA = 99;

        /// <summary>Изотопы на старте: в каркасе стартового запаса нет (FTUE описывает только кредиты), взято 30 — на два-три ускорения по полу цены 10.</summary>

        public const int STARTOVYE_IZOTOPY = 30;
        private const string KLYUCH = "mars-colony-igra-v2";
        private const float PERIOD_SOHRANENIYA = 5f;

        // ---- роли зданий сцены ----
        private const string ROL_TEPLITSY = "kupol-geodezicheskiy", ROL_SKLADA = "sklad-bunkery", ROL_SHATTLA = "ploshchadka-shattla", ROL_ZHILOGO = "zhiloy-kupol";
        private const string ROL_ENERGOSTANTSII = "energostantsiya", ROL_PLITY = "solnechnaya-plita", ROL_OBUCHENIYA = "obuchenie";
        private const string ROL_ANGARA = "sklad-angar";
        /// <summary>Поселенцы (спека 08.09): по ролям жилых зданий. Купола всех видов равны. Число только показывается.</summary>
        private static readonly Dictionary<string, int> POSELENTSY = new Dictionary<string, int>
        {
            { ROL_ZHILOGO, 20 }, { "modul-tonnelnyy", 10 }, { "zhiloy-bashnya", 40 },
        };
        /// <summary>Энергия (спека 08.09): станция 20 в час, каждая плита 5 в час, хранилище 500.</summary>
        private const double ENERGIYA_STANTSIYA_V_CHAS = 20, ENERGIYA_PLITA_V_CHAS = 5, ENERGIYA_POTOLOK = 500;
        private static readonly Dictionary<string, BuildingType> FABRIKI = new Dictionary<string, BuildingType>
        {
            { "zavod-pishchevoy", BuildingType.food_module },
            { "stantsiya-plavilnya-lda", BuildingType.water_plant },
            { "o2-skvazhina", BuildingType.atmospheric_module },
        };
        private static readonly Dictionary<string, string> DOBYCHA = new Dictionary<string, string>
        {
            { "mashina-ledosbor", "water_ice" },
            { "mashina-regolitosbor", "regolith" },
            { "burovaya-05", "methane" },     // заказчик 06.09: в одной буровой метан, в другой металл
            { "burovaya-02", "iron_ore" },
        };
        private static readonly Dictionary<string, string> DRONY = new Dictionary<string, string>
        {
            { "mesto-vzleta-dron-1", "dron-1" },
            { "mesto-vzleta-dron-2", "dron-2" },
        };
        private const int MASHIN_LDA = 3, MASHIN_REGOLITA = 1, MASHIN_BUROVOY = 1, SLOTOV_FABRIKI = 2;

        private readonly List<BuildingClickTarget> _teplitsy = new List<BuildingClickTarget>();
        private readonly Dictionary<BuildingClickTarget, int> _indeksGryadki = new Dictionary<BuildingClickTarget, int>();
        private readonly List<BuildingClickTarget> _zhilye = new List<BuildingClickTarget>();
        private int _plit;

        /// <summary>Сумма поселенцев по жилым зданиям сцены.</summary>
        public int Poselentsy { get { int n = 0; foreach (var z in _zhilye) if (POSELENTSY.TryGetValue(z.TargetId, out int v)) n += v; return n; } }
        public double EnergiyaVChas => ENERGIYA_STANTSIYA_V_CHAS + ENERGIYA_PLITA_V_CHAS * _plit;

        /// <summary>
        /// Начисление энергии, до потолка. Хранение переживает сейв через energiyaObnovlena.
        ///
        /// Деление на time_scale обязательно: все таймеры домена на него УМНОЖАЮТСЯ (`Mining`,
        /// `Production`, прилёт шаттла), то есть при ускорении x10 час каркаса длится шесть реальных
        /// минут. Приход энергии считался по реальным часам и был единственным таймером мимо правила
        /// `Tuning.cs`: рейс шёл 7,5 реальных минут, а 30 энергии на взлёт копились 28 — топливный
        /// гейт выходил вчетверо дороже задуманного (проверка кода 08.09).
        /// </summary>
        private void NachislitEnergiyu(double now)
        {
            if (Igra.energiyaObnovlena <= 0) { Igra.energiyaObnovlena = now; return; }
            double dt = now - Igra.energiyaObnovlena;
            if (dt <= 0) return;
            double masshtab = TuningIgry != null && TuningIgry.time_scale > 0 ? TuningIgry.time_scale : 1.0;
            Igra.energiya = Math.Min(ENERGIYA_POTOLOK, Igra.energiya + EnergiyaVChas * dt / 3600.0 / masshtab);
            Igra.energiyaObnovlena = now;
        }
        /// <summary>Контейнеров в отсеке шаттла — три, по эталону заказчика (вид сверху, 06.09).</summary>
        private const int OTSEKOV_SHATTLA = 3;
        private PanelZdaniya _panel;
        private PanelShattla _panelSh;
        private KartochkaOtseka _kartochka;      // окно отсека: награда, «есть/нужно», «ЗАГРУЗИТЬ»
        private OknoNehvatki _oknoNehvatki;      // общее окно нехватки ресурса с покупкой за изотопы
        private PanelTeplitsy _panelT;
        private PanelPlavilni _panelP;
        private const string ROL_PLAVILNI = "stantsiya-plavilnya-lda";
        private string _vybranaKultura;   // культура, выбранная в лотке панели теплицы
        private PanelKonveyera _konv;
        private ZhivoyInterfeys _zhivoy;
        private SkladSostoyanie _sklad;
        private EkranSklada _ekranSklada;
        private EkranObektov _ekranObektov;
        private Obuchenie _obuchenie;
        private PolyotShattla _polyot;
        private Camera _cam;
        private BuildingClickTarget _vybrano;
        private float _sohranitV;
        private bool _gryazno;
        private int _uroven;
        /// <summary>Открыта ли любая панель здания — карточка имени над зданием в это время не показывается (иначе она мерцала: VyborZdaniy показывал каждый кадр, игра прятала каждый кадр).</summary>
        public bool PanelOtkryta { get; private set; }
        private readonly List<MarkerZdaniya> _markery = new List<MarkerZdaniya>();
        private ElementyHolsta _el;
        public Camera Kamera => _cam;
        private readonly System.Random _rnd = new System.Random();
        private DeficitLockState _defitsit = DeficitLock.Create();
        private Tuning _tuning;

        public double Now => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        private Tuning TuningIgry => _tuning ??= Tuning_();
        private Tuning Tuning_() { var t = TuningDefaults.Default(); t.time_scale = 1.0 / Math.Max(_uskorenie, 0.01f); return t; }
        private double Rnd() => _rnd.NextDouble();

        private void Awake() => Obespechit();

        /// <summary>Инициализация переживает перезагрузку домена в Play (Awake второй раз не зовётся).</summary>
        private void Obespechit()
        {
            Ekz = this;
            Application.runInBackground = true;   // редактор без фокуса иначе замораживает Update и корутины (06.09)
            PolyotShattla.IgrovoyRezhim = true;
            MarshrutyDronov.IgrovoyRezhim = true;
            if (_cam == null) _cam = Camera.main;
            UpravlenieKameroy.Obespechit(_cam);   // свободная камера (Khan 08.09)
            if (_zhivoy == null) _zhivoy = FindFirstObjectByType<ZhivoyInterfeys>();
            if (_sklad == null) _sklad = FindFirstObjectByType<SkladSostoyanie>(FindObjectsInactive.Include);
            if (_ekranSklada == null) _ekranSklada = FindFirstObjectByType<EkranSklada>(FindObjectsInactive.Include);
            if (_polyot == null) _polyot = FindFirstObjectByType<PolyotShattla>(FindObjectsInactive.Include);
            if (Igra == null)
            {
                Igra = _sbrositSohranenie ? null : SohranenieIgry.Zagruzit(KLYUCH);
                if (Igra == null && !_sbrositSohranenie) Igra = SohranenieIgry.ZagruzitPoUmolchaniyu();   // чужая машина: стартовый сейв из сборки
                if (Igra == null) Igra = new SostoyanieIgry { kolonia = ColonyState.CreateNew(), izotopy = STARTOVYE_IZOTOPY };
                if (_sbrositSohranenie) SohranenieIgry.Sbrosit(KLYUCH);
                SobratZdaniya();
                ObespechitSostav();
                _uroven = Progress.UrovenPoOpytu(State.xp);
            }
            if (_teplitsy.Count == 0) SobratZdaniya();   // после перезагрузки домена в Play списки зданий пусты
            PrimenitUrovni();
            PodklyuchitRasshirenieSklada();   // после загрузки Igra: кнопке нужна цена из состояния
            TsentrirovatSklad();
        }

        private void OnEnable()
        {
            Obespechit();
            VyborZdaniy.Vybrano += PriVybore;
            VyborZdaniy.Snyato += PriSnyatii;
        }

        private void OnDisable()
        {
            VyborZdaniy.Vybrano -= PriVybore;
            VyborZdaniy.Snyato -= PriSnyatii;
        }

        private void Start()
        {
            ObespechitInterfeys();
            Debug.Log($"[игра] старт: кредиты {State.credits}, опыт {State.xp} (ур. {_uroven}), склад {Warehouse.TotalQty(State.warehouse)}/{State.warehouse.capacity}, "
                    + $"теплиц {_teplitsy.Count}, фабрик {Igra.fabriki.Count}, площадок добычи {Igra.dobycha.Count}, отсеков шаттла {(Igra.shattl != null ? Igra.shattl.slots.Count : 0)}, "
                    + $"заказов дронов {Igra.drony.Count}, ускорение ×{_uskorenie}");
        }

        private void SobratZdaniya()
        {
            _teplitsy.Clear(); _indeksGryadki.Clear(); _zhilye.Clear(); _plit = 0;
            var plity = GameObject.Find("batarei-plity");
            if (plity != null) foreach (Transform p in plity.transform) if (p.name.StartsWith("batareya-")) _plit++;
            foreach (var t in FindObjectsByType<BuildingClickTarget>(FindObjectsSortMode.None))
            {
                if (t.TargetId == ROL_TEPLITSY) _teplitsy.Add(t);
                if (POSELENTSY.ContainsKey(t.TargetId)) _zhilye.Add(t);
            }
            _teplitsy.Sort((a, b) => string.CompareOrdinal(a.gameObject.name, b.gameObject.name));
            for (int i = 0; i < _teplitsy.Count; i++) _indeksGryadki[_teplitsy[i]] = i;
        }

        /// <summary>
        /// Горшки, не приписанные ни к одной теплице (остались от старых раскладок рядов), раздаются
        /// теплицам, где есть место, иначе пустые убираются из счёта. Проверка 07.09 нашла в сейве два
        /// созревших горшка вне теплиц: их нельзя было собрать, а цель на них указывала.
        /// </summary>
        private void PriyutitGorshkiBezTeplitsy()
        {
            var svoi = new HashSet<int>();
            foreach (var t in State.teplitsy) foreach (int idx in t.gorshki) svoi.Add(idx);
            for (int idx = 0; idx < State.fields.Count; idx++)
            {
                if (svoi.Contains(idx)) continue;
                Teplitsa priyut = null;
                foreach (var t in State.teplitsy) if (t.gorshki.Count < Teplitsa.MaxGorshkov(t.uroven)) { priyut = t; break; }
                if (priyut != null) { priyut.gorshki.Add(idx); Debug.Log($"[игра] горшок {idx} вне теплиц ({State.fields[idx].state}) приписан к теплице {State.teplitsy.IndexOf(priyut) + 1}"); }
                else
                {
                    // Мест нет: горшок обнуляется, чтобы «созревший» урожай, который негде собрать, не висел в состоянии.
                    Debug.Log($"[игра] горшок {idx} вне теплиц ({State.fields[idx].state}) — мест нет, обнулён");
                    State.fields[idx] = Production.CreateField(idx);
                }
            }
        }

        // ------------------------------------------------------------ список объектов (спека 08.09, фича 1)

        /// <summary>Кнопка меню в правом верхнем углу открывала склад (запечённый слушатель из сборщика).
        /// Теперь она открывает список объектов, склад — плиткой внутри списка и тапом по ангару.</summary>
        private void PodklyuchitKnopkuMenyu()
        {
            var holst = GameObject.Find("interfeys");
            var kn = holst != null ? holst.transform.Find("knopka-menyu")?.GetComponent<Button>() : null;
            if (kn == null) { Debug.LogWarning("[игра] knopka-menyu не найдена — список объектов не открыть"); return; }
            for (int i = 0; i < kn.onClick.GetPersistentEventCount(); i++) kn.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            kn.onClick.RemoveAllListeners();
            kn.onClick.AddListener(PokazatSpisokObektov);
        }

        private BuildingClickTarget TselPoImeni(string imya)
        {
            foreach (var z in FindObjectsByType<BuildingClickTarget>(FindObjectsSortMode.None)) if (z.gameObject.name == imya) return z;
            foreach (var z in FindObjectsByType<BuildingClickTarget>(FindObjectsSortMode.None)) if (z.gameObject.name.StartsWith(imya)) return z;
            return null;
        }

        /// <summary>Плитка: подвести камеру и открыть панель объекта, как по тапу в мире.</summary>
        private Action Perehod(BuildingClickTarget z)
        {
            if (z == null) return null;
            return () =>
            {
                PodvestiKameru(z);
                var vybor = FindFirstObjectByType<VyborZdaniy>();
                if (vybor != null) vybor.SendMessage("Vybrat", z, SendMessageOptions.DontRequireReceiver);
            };
        }

        public void PokazatSpisokObektov()
        {
            if (_ekranObektov == null) _ekranObektov = EkranObektov.Obespechit();
            if (_ekranObektov == null) return;
            if (_ekranObektov.Otkryta) { _ekranObektov.Skryt(); return; }
            double now = Now;
            var plitki = new List<EkranObektov.Plitka>();
            for (int i = 0; i < _teplitsy.Count; i++)
            {
                var pole = State.PoleTeplitsy(i);
                plitki.Add(new EkranObektov.Plitka { klyuch = "teplitsa", podpis = "Теплица " + (i + 1), deystvie = Perehod(_teplitsy[i]), gotovo = pole != null && pole.state == FieldState.READY });
            }
            plitki.Add(new EkranObektov.Plitka { klyuch = "shattl", podpis = "Шаттл", deystvie = Perehod(TselPoImeni(ROL_SHATTLA)), gotovo = Igra.shattl != null && Igra.shattl.state == ShuttleState.ARRIVED });
            int nomerDrona = 1;
            foreach (var kv in DRONY) { plitki.Add(new EkranObektov.Plitka { klyuch = "dron", podpis = "Дрон " + nomerDrona, deystvie = Perehod(TselPoImeni(kv.Key)) }); nomerDrona++; }
            bool zavodGotov = false; foreach (var sl in Igra.fabriki["zavod-pishchevoy"]) if (sl.state == FactorySlotState.READY) zavodGotov = true;
            plitki.Add(new EkranObektov.Plitka { klyuch = "zavod", podpis = "Пищевой завод", deystvie = Perehod(TselPoImeni("zavod-pishchevoy")), gotovo = zavodGotov });
            plitki.Add(new EkranObektov.Plitka { klyuch = "plavilnya", podpis = "Плавильня", deystvie = Perehod(TselPoImeni("stantsiya-plavilnya-lda")), gotovo = Igra.plavilnya != null && Igra.plavilnya.vyhod > 0 });
            plitki.Add(new EkranObektov.Plitka { klyuch = "ledosbor", podpis = "Ледосбор", deystvie = Perehod(TselPoImeni("mashina-ledosbor-01")), gotovo = Igra.dobycha["water_ice"].state == MiningState.READY });
            plitki.Add(new EkranObektov.Plitka { klyuch = "regolit", podpis = "Реголит", deystvie = Perehod(TselPoImeni("mashina-regolitosbor")), gotovo = Igra.dobycha["regolith"].state == MiningState.READY });
            bool kislorodGotov = false; foreach (var sl in Igra.fabriki["o2-skvazhina"]) if (sl.state == FactorySlotState.READY) kislorodGotov = true;
            plitki.Add(new EkranObektov.Plitka { klyuch = "kislorod", podpis = "Кислородная скважина", deystvie = Perehod(TselPoImeni("o2-skvazhina")), gotovo = kislorodGotov });
            plitki.Add(new EkranObektov.Plitka { klyuch = "burovaya-ruda", podpis = "Буровая: руда", deystvie = Perehod(TselPoImeni("burovaya-02")), gotovo = Igra.dobycha.ContainsKey("iron_ore") && Igra.dobycha["iron_ore"].state == MiningState.READY });
            plitki.Add(new EkranObektov.Plitka { klyuch = "burovaya-metan", podpis = "Буровая: метан", deystvie = Perehod(TselPoImeni("burovaya-05")), gotovo = Igra.dobycha.ContainsKey("methane") && Igra.dobycha["methane"].state == MiningState.READY });
            plitki.Add(new EkranObektov.Plitka { klyuch = "sklad", podpis = "Склад", deystvie = () => { if (_ekranSklada != null && !_ekranSklada.gameObject.activeSelf) _ekranSklada.Perekluchit(); } });
            plitki.Add(new EkranObektov.Plitka { klyuch = "angar", podpis = "Ангар спецтехники", deystvie = Perehod(TselPoImeni(ROL_ANGARA)) });
            plitki.Add(new EkranObektov.Plitka { klyuch = "energostantsiya", podpis = "Энергостанция", deystvie = Perehod(TselPoImeni("kupol-grib")) });
            plitki.Add(new EkranObektov.Plitka { klyuch = "obuchenie", podpis = "Обучение", deystvie = NachatObuchenie });
            _panel?.Skryt(); _konv?.Skryt(); _panelSh?.Skryt(); _panelT?.Skryt(); _panelP?.Skryt();
            _ekranObektov.Pokazat(plitki);
        }

        // ------------------------------------------------------------ ангар спецтехники (спека 08.09)

        private static readonly (string klyuch, string imya, string chto)[] TEHNIKA_ANGARA =
        {
            (Uluchsheniya.LED, "Ледосборщики", "добыча льда"),
            (Uluchsheniya.REGOLIT, "Сборщик реголита", "добыча реголита"),
            (Uluchsheniya.RUDA, "Буровая: руда", "добыча руды"),
            (Uluchsheniya.METAN, "Буровая: метан", "добыча метана"),
            (Uluchsheniya.SHATTL, "Грузовой шаттл", "время рейса"),
            (Uluchsheniya.DRONY, "Дроны-курьеры", "время рейса"),
        };

        private void PanelAngara()
        {
            var stroki = new List<PanelZdaniya.Stroka>();
            int rudyNaSklade = Warehouse.AvailableOf(State.warehouse, "iron_ore");
            foreach (var (klyuch, imya, chto) in TEHNIKA_ANGARA)
            {
                int uroven = UrovenTehniki(klyuch);
                Uluchsheniya.Tsena(uroven, out int kredity, out int ruda, out int moduley);
                var modulId = Uluchsheniya.MODUL_TEHNIKI[klyuch];
                string modulKlyuch = modulId.ToString();
                int modulEst = Igra.moduli.TryGetValue(modulKlyuch, out int mv) ? mv : 0;
                string modulImya = Modules.MODULES[modulId].name;
                bool potolok = !Uluchsheniya.EstKuda(uroven);
                bool hvataet = !potolok && State.credits >= kredity && rudyNaSklade >= ruda && modulEst >= moduley;
                // Коротко и в одну строку: подпись строки панели обрезается, длинный текст не влезал (Khan 08.09)
                string effekt = klyuch == Uluchsheniya.SHATTL || klyuch == Uluchsheniya.DRONY
                    ? "рейс " + (Uluchsheniya.MnozhitelVremeni(uroven) * 100).ToString("0") + " %"
                    : "+" + Uluchsheniya.PribavkaDobychi(uroven) + " за цикл";
                string podpis = potolok ? effekt + " · максимум"
                    : effekt + " · " + kredity + " кр · " + ruda + " руды · " + modulImya + " x" + moduley;
                string k = klyuch;   // копия для замыкания кнопки
                stroki.Add(new PanelZdaniya.Stroka
                {
                    ikonka = Resources.Load<Sprite>("UI/Ikonki/spisok/" + IkonkaTehniki(klyuch)),
                    imya = imya + " · ур. " + uroven + " из " + Uluchsheniya.UROVEN_MAX,
                    podpis = podpis,
                    progress = (uroven - Uluchsheniya.UROVEN_START) / (float)(Uluchsheniya.UROVEN_MAX - Uluchsheniya.UROVEN_START),
                    knopka = potolok ? null : "УЛУЧШИТЬ",
                    knopkaAktivna = hvataet,
                    deystvie = () => UluchshitTehniku(k),
                });
            }
            _panel.Pokazat("Ангар спецтехники", stroki);
        }

        private static string IkonkaTehniki(string klyuch) =>
            klyuch == Uluchsheniya.LED ? "ledosbor"
            : klyuch == Uluchsheniya.REGOLIT ? "regolit"
            : klyuch == Uluchsheniya.RUDA ? "burovaya-ruda"
            : klyuch == Uluchsheniya.METAN ? "burovaya-metan"
            : klyuch == Uluchsheniya.SHATTL ? "shattl" : "dron";

        public void UluchshitTehniku(string klyuch)
        {
            int uroven = UrovenTehniki(klyuch);
            if (!Uluchsheniya.EstKuda(uroven)) { Debug.Log($"[игра] ангар: {klyuch} уже на максимуме"); return; }
            Uluchsheniya.Tsena(uroven, out int kredity, out int ruda, out int moduley);
            string modulKlyuch = Uluchsheniya.MODUL_TEHNIKI[klyuch].ToString();
            int modulEst = Igra.moduli.TryGetValue(modulKlyuch, out int mv) ? mv : 0;
            if (State.credits < kredity)
            {
                Debug.Log($"[игра] ангар: не хватает кредитов ({State.credits} < {kredity})");
                NehvatkaBezPokupki(_el?.Element("moneta"), "кредитов", kredity - State.credits, "Кредиты за изотопы не покупаются — продайте товары или отправьте заказ.");
                return;
            }
            if (Warehouse.AvailableOf(State.warehouse, "iron_ore") < ruda)
            {
                Debug.Log($"[игра] ангар: не хватает руды (нужно {ruda})");
                NehvatkaTovara("iron_ore", ruda - Warehouse.AvailableOf(State.warehouse, "iron_ore"), () => UluchshitTehniku(klyuch));
                return;
            }
            if (modulEst < moduley)
            {
                Debug.Log($"[игра] ангар: не хватает модулей {modulKlyuch} (нужно {moduley}, есть {modulEst}) — привезёт шаттл");
                NehvatkaBezPokupki(Resources.Load<Sprite>("UI/Ikonki/moduli/" + modulKlyuch), ImyaModulya(modulKlyuch), moduley - modulEst, "Модули привозит шаттл — отправьте рейс.");
                return;
            }
            if (!Warehouse.Consume(State.warehouse, "iron_ore", ruda)) { Debug.Log("[игра] ангар: руда не списалась"); return; }
            Igra.moduli[modulKlyuch] = modulEst - moduley;
            State.credits -= kredity;
            Igra.urovni[klyuch] = uroven + 1;
            PrimenitUrovni();
            Debug.Log($"[игра] ангар: {klyuch} улучшен до {uroven + 1} за {kredity} кр, {ruda} руды и {moduley}×{modulKlyuch}");
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        /// <summary>Уровни, которые видны не в домене, а в сцене: скорость дронов. Зовётся на старте и после покупки.</summary>
        private void PrimenitUrovni()
        {
            MarshrutyDronov.MnozhitelSkorosti = (float)(1.0 / Uluchsheniya.MnozhitelVremeni(UrovenTehniki(Uluchsheniya.DRONY)));
        }

        // ------------------------------------------------------------ обучение (спека 08.09, фича 2)

        /// <summary>Габарит группы объектов по именам (или префиксам): камера идёт к центру, стрелка встаёт над верхом; null — ничего не нашли.</summary>
        private Bounds? TsentrGruppy(params string[] imena)
        {
            Bounds? b = null;
            foreach (var z in FindObjectsByType<BuildingClickTarget>(FindObjectsSortMode.None))
            {
                bool nado = false; foreach (var im in imena) if (z.gameObject.name == im || z.gameObject.name.StartsWith(im)) nado = true;
                if (!nado) continue;
                if (b == null) b = z.ClickBounds; else { var bb = b.Value; bb.Encapsulate(z.ClickBounds); b = bb; }
            }
            return b;
        }

        /// <summary>Габариты каждого объекта группы — по стрелке на каждый.</summary>
        private List<Bounds> ChlenyGruppy(params string[] imena)
        {
            var spisok = new List<Bounds>();
            foreach (var z in FindObjectsByType<BuildingClickTarget>(FindObjectsSortMode.None))
            {
                bool nado = false; foreach (var im in imena) if (z.gameObject.name == im || z.gameObject.name.StartsWith(im)) nado = true;
                if (nado) spisok.Add(z.ClickBounds);
            }
            return spisok;
        }

        public void NachatObuchenie()
        {
            if (_obuchenie == null) _obuchenie = Obuchenie.Obespechit();
            if (_obuchenie == null || _obuchenie.Idet) return;
            _panel?.Skryt(); _konv?.Skryt(); _panelSh?.Skryt(); _panelT?.Skryt(); _panelP?.Skryt(); _ekranObektov?.Skryt();
            if (_ekranSklada != null && _ekranSklada.gameObject.activeSelf) _ekranSklada.Zakryt();   // иначе склад остаётся под окнами обучения
            var shagi = new List<Obuchenie.Shag>
            {
                new Obuchenie.Shag { zagolovok = "Теплицы", tekst = "Здесь растут культуры колонии. Тапните пустой горшок, чтобы посеять, и созревший, чтобы собрать. Улучшение теплицы добавляет ряды.", gruppa = () => TsentrGruppy(ROL_TEPLITSY), chleny = () => ChlenyGruppy(ROL_TEPLITSY) },
                new Obuchenie.Shag { zagolovok = "Склад", tekst = "Всё собранное лежит на складе. Лишнее можно продать за кредиты, а вместимость расширить. Полный склад останавливает сбор.", gruppa = () => TsentrGruppy(ROL_SKLADA) },   // «sklad-angar» — это Ангар спецтехники, склад игрока называется sklad-bunkery (Khan 08.09)
                new Obuchenie.Shag { zagolovok = "Грузовой шаттл", tekst = "Шаттл увозит заказ на орбиту. Загрузите три отсека со склада, и он улетит, а вернётся со строй-модулями и опытом.", gruppa = () => TsentrGruppy(ROL_SHATTLA) },
                new Obuchenie.Shag { zagolovok = "Дрон-курьер 1", tekst = "Дроны выполняют заказы колонистов. Отправьте заказ с товарами и получите кредиты с надбавкой сразу, дрон повезёт его по очереди.", gruppa = () => TsentrGruppy("mesto-vzleta-dron-1", "dron-1") },
                new Obuchenie.Shag { zagolovok = "Дрон-курьер 2", tekst = "Второй дрон на другом краю колонии. Оба берут заказы из одной очереди, так что отправлять можно не дожидаясь возвращения.", gruppa = () => TsentrGruppy("mesto-vzleta-dron-2", "dron-2") },
                new Obuchenie.Shag { zagolovok = "Плавильня льда", tekst = "Плавильня превращает лёд в воду. Засыпьте лёд в воронку и заберите канистру, когда наполнится.", gruppa = () => TsentrGruppy("stantsiya-plavilnya-lda") },
                new Obuchenie.Shag { zagolovok = "Пищевой завод", tekst = "Завод готовит батончики и суп из урожая. Поставьте рецепт в очередь и заберите готовое.", gruppa = () => TsentrGruppy("zavod-pishchevoy") },
                new Obuchenie.Shag { zagolovok = "Ледосбор", tekst = "Ледосборщики добывают водяной лёд у глыб. Запустите добычу по тапу и заберите, когда машины закончат.", gruppa = () => TsentrGruppy("mashina-ledosbor"), chleny = () => ChlenyGruppy("mashina-ledosbor") },
                new Obuchenie.Shag { zagolovok = "Реголит", tekst = "Сборщик реголита копает грунт у карьера. Реголит нужен заказам шаттла и колонистов.", gruppa = () => TsentrGruppy("mashina-regolitosbor") },
                // Буровые и жильё — отдельными шагами: объекты далеко друг от друга, центр группы попадал в пустое место (Khan 08.09)
                new Obuchenie.Shag { zagolovok = "Буровая: руда", tekst = "Буровая в карьере добывает железную руду. Запускается по тапу, готовое забирается так же.", gruppa = () => TsentrGruppy("burovaya-02") },
                new Obuchenie.Shag { zagolovok = "Буровая: метан", tekst = "Вторая буровая, на опорах, добывает метан. Он нужен заказам и пойдёт на топливо шаттла.", gruppa = () => TsentrGruppy("burovaya-05") },
                new Obuchenie.Shag { zagolovok = "Кислородная станция", tekst = "Станция делает кислородные баллоны из железной руды. Это фабрика с очередью, как пищевой завод.", gruppa = () => TsentrGruppy("o2-skvazhina") },
                new Obuchenie.Shag { zagolovok = "Энергостанция", tekst = "Солнечные плиты и станция дают энергию. Она копится сама, счётчик с молнией слева в шапке.", gruppa = () => TsentrGruppy("kupol-grib") },
                new Obuchenie.Shag { zagolovok = "Жилые купола", tekst = "Купола вмещают поселенцев, по двадцать каждый. Их число в шапке слева, под опытом.", gruppa = () => TsentrGruppy(ROL_ZHILOGO), chleny = () => ChlenyGruppy(ROL_ZHILOGO) },
                new Obuchenie.Shag { zagolovok = "Туннельные дома и башня", tekst = "Туннельный дом даёт десять поселенцев, башня сорок. Пока это только счётчик, ограничения по людям придут позже.", gruppa = () => TsentrGruppy("modul-tonnelnyy", "zhiloy-bashnya"), chleny = () => ChlenyGruppy("modul-tonnelnyy", "zhiloy-bashnya") },
                new Obuchenie.Shag { zagolovok = "Ангар спецтехники", tekst = "В ангаре улучшается техника: каждый уровень добавляет единицу к добыче, а шаттлу и дронам срезает время рейса. Платите кредитами и железной рудой.", gruppa = () => TsentrGruppy(ROL_ANGARA) },
                new Obuchenie.Shag { zagolovok = "Список объектов", tekst = "Кнопка в правом верхнем углу открывает список всех объектов колонии. Оттуда можно перейти к любому из них и повторить обучение.", gruppa = () => null },
            };
            _bazovayaVysota = _cam != null ? _cam.transform.position.y : _bazovayaVysota;
            _obuchenie.Nachat(shagi, b => PodvestiKameru(b.center, PRIBLIZHENIE_OBUCHENIYA), proydeno =>
            {
                VosstanovitVysotu();
                Igra.schetchiki["obuchenie"] = 1;
                Debug.Log("[игра] обучение " + (proydeno ? "пройдено" : "пропущено"));
                Izmeneno();
            });
        }

        // ------------------------------------------------------------ подвод камеры

        /// <summary>Доля дистанции до объекта, которая остаётся при приближении в обучении (Khan 08.09: «приближать, чтобы был по центру»).</summary>
        private const float PRIBLIZHENIE_OBUCHENIYA = 0.55f;
        private float _bazovayaVysota = 57.5f;

        /// <summary>Подвод к точке мира с приближением: объект в центре кадра (по высоте 0.55 экрана, чтобы окно
        /// обучения слева снизу его не закрывало), камера подтягивается вдоль своего взгляда на долю priblizhenie.</summary>
        public void PodvestiKameru(Vector3 centr, float priblizhenie = 1f)
        {
            if (_cam == null) return;
            // Сначала считаем позицию на базовой высоте: подвод без приближения возвращает камеру на неё
            Vector3 vpered = _cam.transform.forward;
            Vector3 poz = _cam.transform.position; poz += vpered * ((poz.y - _bazovayaVysota) / -vpered.y);   // на базовую высоту вдоль взгляда
            var luch = new Ray(poz, vpered);
            // Точка, которую хотим видеть в (0.5, 0.55): берём луч через эту точку экрана с текущей ориентацией
            var luchEkr = _cam.ViewportPointToRay(new Vector3(0.5f, 0.55f, 0f));
            var ploskost = new Plane(Vector3.up, new Vector3(0f, centr.y, 0f));
            if (!ploskost.Raycast(new Ray(poz + (luchEkr.origin - _cam.transform.position), luchEkr.direction), out float d)) return;
            Vector3 podTochkoy = poz + (luchEkr.origin - _cam.transform.position) + luchEkr.direction * d;
            Vector3 sdvig = centr - podTochkoy; sdvig.y = 0f;
            Vector3 tsel = UpravlenieKameroy.Zazhat(_cam, poz + sdvig);
            if (priblizhenie < 1f)
            {
                // Подтянуть вдоль взгляда к точке под (0.5, 0.55) — центр кадра не смещается
                if (ploskost.Raycast(new Ray(tsel + (luchEkr.origin - _cam.transform.position), luchEkr.direction), out float d2))
                {
                    Vector3 tochka = tsel + (luchEkr.origin - _cam.transform.position) + luchEkr.direction * d2;
                    tsel = tochka + (tsel - tochka) * priblizhenie;
                }
            }
            if (_podvod != null) StopCoroutine(_podvod);
            _podvod = StartCoroutine(Podvod(tsel));
        }

        /// <summary>Вернуть камеру на базовую высоту после обучения, не меняя точку под центром.</summary>
        private void VosstanovitVysotu()
        {
            if (_cam == null) return;
            Vector3 vpered = _cam.transform.forward;
            Vector3 poz = _cam.transform.position; poz += vpered * ((poz.y - _bazovayaVysota) / -vpered.y);
            if (_podvod != null) StopCoroutine(_podvod);
            _podvod = StartCoroutine(Podvod(UpravlenieKameroy.Zazhat(_cam, poz)));
        }

        private Coroutine _podvod;
        private const float T_PODVODA = 0.45f;

        /// <summary>Сдвиг камеры по XZ так, чтобы центр объекта оказался в центре экрана; наклон и высота не меняются.
        /// Используется списком объектов и обучением.</summary>
        public void PodvestiKameru(BuildingClickTarget z)
        {
            if (z == null || _cam == null) return;
            PodvestiKameru(z.ClickBounds.center, 1f);
        }

        private System.Collections.IEnumerator Podvod(Vector3 kuda)
        {
            Vector3 otkuda = _cam.transform.position; float t = 0f;
            while (t < T_PODVODA)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / T_PODVODA);
                _cam.transform.position = Vector3.Lerp(otkuda, kuda, k);
                yield return null;
            }
            _cam.transform.position = kuda; _podvod = null;
        }

        /// <summary>Панель склада запечена сборщиком с якорем к левому краю под узкий холст, и на широком экране
        /// стоит левее центра (Khan 08.09). Перевешиваем панель и её тень на центр, сохраняя их взаимный сдвиг.</summary>
        private void TsentrirovatSklad()
        {
            if (_ekranSklada == null) return;
            var panel = _ekranSklada.transform.Find("sklad-panel") as RectTransform;
            var ten = _ekranSklada.transform.Find("sklad-panel-ten") as RectTransform;
            if (panel == null) return;
            Vector2 sdvigTeni = ten != null ? ten.anchoredPosition - panel.anchoredPosition : Vector2.zero;
            panel.anchorMin = new Vector2(0.5f, panel.anchorMin.y); panel.anchorMax = new Vector2(0.5f, panel.anchorMax.y);
            panel.anchoredPosition = new Vector2((panel.pivot.x - 0.5f) * panel.rect.width, panel.anchoredPosition.y);   // центр панели на якоре 0.5 с учётом пивота
            if (ten != null)
            {
                ten.anchorMin = new Vector2(0.5f, ten.anchorMin.y); ten.anchorMax = new Vector2(0.5f, ten.anchorMax.y);
                ten.anchoredPosition = new Vector2(panel.anchoredPosition.x + sdvigTeni.x, ten.anchoredPosition.y);
            }
        }

        // ------------------------------------------------------------ расширение склада

        private Button _knopkaRasshirit; private Text _tekstRasshirit;

        /// <summary>Кнопка «РАСШИРИТЬ» на экране склада построена сборщиком пустой (каркас §13 держал склад
        /// нерасширяемым). Заказчик 07.09 попросил рабочую кнопку: подключаем к домену на старте.</summary>
        private void PodklyuchitRasshirenieSklada()
        {
            if (_ekranSklada == null) return;
            var uzel = _ekranSklada.transform.Find("knopka-rasshirit");
            if (uzel == null) foreach (var b in _ekranSklada.GetComponentsInChildren<Button>(true)) if (b.gameObject.name == "knopka-rasshirit") { uzel = b.transform; break; }
            if (uzel == null) { Debug.LogWarning("[игра] кнопка knopka-rasshirit на экране склада не найдена"); return; }
            _knopkaRasshirit = uzel.GetComponent<Button>() ?? uzel.gameObject.AddComponent<Button>();
            _tekstRasshirit = uzel.GetComponentInChildren<Text>(true);
            _knopkaRasshirit.onClick.RemoveAllListeners();
            _knopkaRasshirit.onClick.AddListener(RasshiritSklad);
            ObnovitKnopkuRasshireniya();
        }

        private void ObnovitKnopkuRasshireniya()
        {
            if (_knopkaRasshirit == null || Igra == null) return;
            int cena = Warehouse.UpgradePrice(State.warehouse);
            bool mozhno = cena > 0 && State.credits >= cena;
            _knopkaRasshirit.interactable = cena > 0;
            if (_tekstRasshirit != null) _tekstRasshirit.text = cena == 0 ? "МАКСИМУМ" : "РАСШИРИТЬ · " + cena;
            var img = _knopkaRasshirit.targetGraphic as Image ?? _knopkaRasshirit.GetComponent<Image>();
            if (img != null) img.color = mozhno ? Color.white : new Color(0.72f, 0.72f, 0.72f, 0.9f);
        }

        public void RasshiritSklad()
        {
            int cena = Warehouse.UpgradePrice(State.warehouse);
            if (cena <= 0) { Debug.Log("[игра] склад: потолок вместимости"); return; }
            if (State.credits < cena)
            {
                Debug.Log($"[игра] склад: не хватает кредитов ({State.credits} < {cena})");
                NehvatkaBezPokupki(_el?.Element("moneta"), "кредитов", cena - State.credits, "Кредиты за изотопы не покупаются — продайте товары или отправьте заказ.");
                return;
            }
            if (!Warehouse.UpgradeCapacity(State.warehouse)) return;
            State.credits -= cena;
            Debug.Log($"[игра] склад расширен до {State.warehouse.capacity} за {cena} кр");
            Izmeneno();
            ObnovitKnopkuRasshireniya();
        }

        /// <summary>Домен получает столько слотов, сколько зданий стоит в сцене; недостающее создаётся.</summary>
        private void ObespechitSostav()
        {
            while (State.teplitsy.Count < _teplitsy.Count) State.DobavitTeplitsu();
            PriyutitGorshkiBezTeplitsy();
            foreach (var kv in FABRIKI)
            {
                if (!Igra.fabriki.TryGetValue(kv.Key, out var slots) || slots == null) { slots = new List<FactorySlot>(); Igra.fabriki[kv.Key] = slots; }
                while (slots.Count < SLOTOV_FABRIKI) slots.Add(Production.CreateFactorySlot(slots.Count, kv.Value));
            }
            if (!Igra.dobycha.ContainsKey("water_ice")) Igra.dobycha["water_ice"] = Mining.CreateSite("water_ice", MASHIN_LDA);
            if (!Igra.dobycha.ContainsKey("regolith")) Igra.dobycha["regolith"] = Mining.CreateSite("regolith", MASHIN_REGOLITA);
            if (!Igra.dobycha.ContainsKey("methane")) Igra.dobycha["methane"] = Mining.CreateSite("methane", MASHIN_BUROVOY);
            if (!Igra.dobycha.ContainsKey("iron_ore")) Igra.dobycha["iron_ore"] = Mining.CreateSite("iron_ore", MASHIN_BUROVOY);
            if (Igra.shattl == null) { Igra.prilyotov = 1; Igra.shattl = NovyyReys(null); }
            else if (Igra.shattl.state != ShuttleState.IN_TRANSIT && Igra.shattl.slots.Count < Economy.SLOT_COUNT_MIN)
            {
                // Сейв с деградированным рейсом (меньше SLOT_COUNT_MIN отсеков) — уже загруженное вернуть на склад и перегенерировать (Khan 06.09: «минимум три»)
                foreach (var sl in Igra.shattl.slots) if (sl.qty_filled > sl.qty_purchased)
                {
                    int v = sl.qty_filled - sl.qty_purchased;
                    if (Warehouse.Deposit(State.warehouse, sl.good_id, v)) Debug.Log($"[игра] шаттл: {sl.good_id}×{v} возвращено на склад");
                    else { int kr = v * Goods.Of(sl.good_id).price; State.credits += kr; Debug.Log($"[игра] шаттл: склад полон, {sl.good_id}×{v} компенсировано {kr} кр"); }   // склад кладёт всё или ничего
                }
                Debug.LogWarning($"[игра] шаттл: в сейве рейс на {Igra.shattl.slots.Count} отсеков — генерирую заново");
                Igra.shattl = NovyyReys(null); Izmeneno();
            }
            DomainLog.Sink = msg => Debug.LogWarning("[домен] " + msg);
            if (Igra.shattl.state == ShuttleState.IN_TRANSIT && _polyot != null) _polyot.SchitatUletevshim();   // сейв: рейс в пути — шаттла на площадке нет (проверка п.1, 07.09)
            // Дроны сами по себе не были целями клика — только ящики-площадки 1,6 м (Khan 06.09: «куда нажимать?»).
            // Вешаем цель на дрон с ролью его площадки: клик по дрону открывает ту же панель заказов.
            var vybor = FindFirstObjectByType<VyborZdaniy>();
            foreach (var kv in DRONY)
            {
                var dronGo = GameObject.Find(kv.Value);
                if (dronGo == null || dronGo.GetComponent<ClickTarget>() != null) continue;
                var ct = dronGo.AddComponent<BuildingClickTarget>(); ct.Nastroit(kv.Key, "Дрон-курьер " + kv.Value.Substring(kv.Value.Length - 1));
                vybor?.Dobavit(ct);
            }
            int nado = Math.Max(3, Drone.SlotsAtLevel(UrovenZakazov));
            while (Igra.drony.Count < nado) Igra.drony.Add(NovyyZakaz(Igra.drony.Count));
        }

        private void ObespechitInterfeys()
        {
            if (_panel == null) _panel = PanelZdaniya.Obespechit();
            if (_konv == null) _konv = PanelKonveyera.Obespechit();
            if (_panelSh == null) _panelSh = PanelShattla.Obespechit();
            if (_kartochka == null) _kartochka = KartochkaOtseka.Obespechit();
            if (_oknoNehvatki == null) _oknoNehvatki = OknoNehvatki.Obespechit();
            if (_panelT == null) _panelT = PanelTeplitsy.Obespechit();
            if (_panelP == null) _panelP = PanelPlavilni.Obespechit();
            if (_ekranObektov == null) { _ekranObektov = EkranObektov.Obespechit(); PodklyuchitKnopkuMenyu(); }
            MuzykaFona.Obespechit(gameObject);   // музыка играет, если в Resources/Audio/muzyka что-то лежит
            if (_obuchenie == null) _obuchenie = Obuchenie.Obespechit();
            if (_markery.Count == 0)
            {
                var holst = GameObject.Find("interfeys").transform;
                _el = ElementyHolsta.Sobrat(holst);
                for (int i = holst.childCount - 1; i >= 0; i--) if (holst.GetChild(i).name.StartsWith("marker-gryadki-")) Destroy(holst.GetChild(i).gameObject);   // сироты старых маркеров
                // Пин цели над теплицей убран (заказчик 06.09, геймдизайнер п.3.8): карточка ЦЕЛЬ уже называет объект и ведёт к нему кнопкой
                var mayak = FindFirstObjectByType<MayakTseli>(FindObjectsInactive.Include);
                if (mayak != null) Destroy(mayak.gameObject);
                var byli = new HashSet<string>();
                // Кнопка карточки цели ведёт к зданию цели (раньше висела без обработчика)
                var kd = holst.Find("kartochka-tseli/knopka-deystviya");
                if (kd != null)
                {
                    var kb = kd.GetComponent<UnityEngine.UI.Button>() ?? kd.gameObject.AddComponent<UnityEngine.UI.Button>();
                    kb.onClick.RemoveAllListeners(); kb.onClick.AddListener(KTseli);
                    if (kd.GetComponent<NazhatieKnopki>() == null) kd.gameObject.AddComponent<NazhatieKnopki>();
                }
                foreach (var t in FindObjectsByType<BuildingClickTarget>(FindObjectsSortMode.None))
                {
                    string rol = t.TargetId;
                    bool interaktiv = _indeksGryadki.ContainsKey(t) || FABRIKI.ContainsKey(rol) || DOBYCHA.ContainsKey(rol) || DRONY.ContainsKey(rol) || rol == ROL_SHATTLA;
                    if (!interaktiv) continue;
                    if (rol == "mashina-ledosbor" && !byli.Add(rol)) continue;   // три ледосбора — один пузырь
                    _markery.Add(MarkerZdaniya.Sozdat(t, this, _el));
                }
            }
        }

        /// <summary>Что колония умеет производить: культуры, рецепты стоящих фабрик и добываемое, для которого
        /// есть площадка (лёд, реголит). Руды и метана в заказах быть не должно — их негде добыть.</summary>
        private List<string> DostupnyeTovary()
        {
            var tipy = new HashSet<BuildingType>(FABRIKI.Values);
            var sp = new List<string>();
            foreach (var g in Goods.GOODS.Values)
            {
                if (g.kind == GoodKind.crop) { sp.Add(g.id); continue; }
                if (g.required_building.HasValue && tipy.Contains(g.required_building.Value)) sp.Add(g.id);
            }
            foreach (var k in Igra.dobycha.Keys) if (!sp.Contains(k)) sp.Add(k);
            return sp;
        }
        private int UrovenZakazov => Math.Max(5, _uroven);

        /// <summary>ТЗ 5.3: награда за каждый достигнутый уровень (100·N^1.2 кредитов к десяткам, 20 изотопов, +25 на «пустых»
        /// уровнях) и всплеск в HUD. Замков нет (база максимальная), поэтому награда — единственное, что уровень даёт.</summary>
        private void Levelap(int novyy)
        {
            int kredity = 0, izotopy = 0;
            for (int n = _uroven + 1; n <= novyy; n++)
            {
                var nagrada = Levels.LevelUpReward(n);
                kredity += nagrada.credits; izotopy += nagrada.isotopes;
            }
            State.credits += kredity; Igra.izotopy += izotopy;
            Debug.Log($"[игра] уровень {_uroven} → {novyy}: +{kredity} кр, +{izotopy} изотопов");
            _uroven = novyy;
            if (_zhivoy != null) _zhivoy.PokazatLevelap(novyy, kredity, izotopy);
            Izmeneno();
        }

        // ------------------------------------------------------------ кадр

        private void Update()
        {
            if (Igra == null) Obespechit();
            if (_panel == null || _markery.Count == 0) ObespechitInterfeys();
            double now = Now;

            foreach (var f in State.fields) Production.RefreshField(f, now);
            foreach (var slots in Igra.fabriki.Values) foreach (var s in slots) Production.RefreshFactorySlot(s, now);
            foreach (var site in Igra.dobycha.Values) Mining.Refresh(site, now);
            ObnovitShattl(now);
            ProverkaVzlyota(Igra.shattl);   // ждал топлива — стартует, как только оно появилось
            ObnovitDrony(now);
            if (Igra.plavilnya == null) Igra.plavilnya = new Plavilnya();
            Igra.plavilnya.Obnovit(now, DlitPlavki);

            MashinyDobychi.DobychaLdaIdet = Igra.dobycha["water_ice"].state == MiningState.WORKING;
            MashinyDobychi.DobychaRegolitaIdet = Igra.dobycha["regolith"].state == MiningState.WORKING;

            int ur = Progress.UrovenPoOpytu(State.xp);
            if (ur > _uroven) Levelap(ur);

            // Интерфейс обновляется 4 раза в секунду, а не каждый кадр: тексты цели, маркеры и карточка
            // порождали ~13 КБ мусора на кадр (замер 06.09), а в WebGL это сборка мусора каждые
            // пару секунд — те самые рывки. Сразу после действия игрока (Izmeneno) — без ожидания.
            bool tik = _obnovitSrazu || Time.frameCount % 15 == 0;
            bool panelOtkryta = (_panel != null && _panel.Otkryta) || (_konv != null && _konv.Otkryta) || (_panelSh != null && _panelSh.Otkryta) || (_panelT != null && _panelT.Otkryta) || (_panelP != null && _panelP.Otkryta) || (_ekranSklada != null && _ekranSklada.gameObject.activeSelf) || (_ekranObektov != null && _ekranObektov.Otkryta) || (_obuchenie != null && _obuchenie.Idet);   // в обучении маркеры и имя здания не нужны
            PanelOtkryta = panelOtkryta;
            if (tik)
            {
                _obnovitSrazu = false;
                var tsel = VychislitTsel(now);
                NachislitEnergiyu(now);
                if (_zhivoy != null) { _zhivoy.Tsel = tsel; _zhivoy.Izotopy = Igra.izotopy; _zhivoy.Poselentsy = Poselentsy; _zhivoy.Energiya = Igra.energiya; _zhivoy.Obnovit(State, now); }
                // Карточка цели прячется только под полноэкранным складом. Раньше её убирала любая панель,
                // и цель исчезала ровно в тот момент, когда игрок по ней нажимал (Khan 07.09: «после первого
                // нажатия оно просто испаряется»). Нижние панели её не перекрывают: карточка стоит слева внизу.
                bool skladNaVesEkran = (_ekranSklada != null && _ekranSklada.gameObject.activeSelf) || (_ekranObektov != null && _ekranObektov.Otkryta);
                if (_zhivoy != null) _zhivoy.PokazatKartochkuTseli(!skladNaVesEkran && tsel.vidna);
                ObnovitMarkery(now, panelOtkryta);
                if (_sklad != null && _sklad.gameObject.activeInHierarchy) { _sklad.ObnovitIzDomena(State); ObnovitKnopkuRasshireniya(); }
            }
            if (panelOtkryta && _zhivoy != null) _zhivoy.SkrytImyaZdaniya();
            // Перестройка открытой панели уничтожает кнопку между нажатием и отпусканием — клик теряется (Khan 06.09: «надо нажимать несколько раз»).
            // Поэтому раз в 45 кадров и только когда ничего не нажато.
            bool nazhato = Input.GetMouseButton(0) || Input.touchCount > 0;
            if (_vybrano != null && !nazhato && Time.frameCount % 45 == 0 && ((_panel != null && _panel.Otkryta) || (_konv != null && _konv.Otkryta) || (_panelSh != null && _panelSh.Otkryta) || (_panelT != null && _panelT.Otkryta) || (_panelP != null && _panelP.Otkryta))) PokazatPanel(_vybrano);
            if (_gryazno && Time.unscaledTime >= _sohranitV) Sohranit();
        }

        /// <summary>Маркеры над зданиями двигаются вместе с камерой каждый кадр: логика их состояния идёт
        /// в тике 4 раза в секунду, и на движении камеры они отставали и дёргались (Khan 08.09).</summary>
        private void LateUpdate()
        {
            for (int i = 0; i < _markery.Count; i++) _markery[i].KadrovoeObnovlenie();
        }

        private void OnApplicationQuit() { if (_gryazno) Sohranit(); }

        public ProductionContext Kontekst(double now)
        {
            var ctx = State.ContextAt(now);
            ctx.level = UROVEN_DOSTUPA;
            ctx.tuning = TuningIgry;
            return ctx;
        }

        private bool _obnovitSrazu;

        public void Izmeneno()
        {
            _obnovitSrazu = true;
            if (!_gryazno) _sohranitV = Time.unscaledTime + PERIOD_SOHRANENIYA;
            _gryazno = true;
        }

        private void Sohranit() { SohranenieIgry.Sohranit(KLYUCH, Igra); _gryazno = false; }

        /// <summary>Склад пополнился — очереди фабрик, ждавшие входов, стартуют (ТЗ производства 2.2).</summary>
        private void SkladPopolnen(double now)
        {
            var ctx = Kontekst(now);
            foreach (var slots in Igra.fabriki.Values) Production.OnWarehouseStockIncreased(slots, ctx);
        }

        /// <summary>Карточка цели: открыть теплицу, к которой относится цель (готовая → пустая → растущая).</summary>
        private void KTseli()
        {
            if (Schet("obuchenie") == 0 || (_zhivoy != null && _zhivoy.Tsel.rol == ROL_OBUCHENIYA)) { NachatObuchenie(); return; }
            if (_teplitsy.Count == 0) return;
            BuildingClickTarget cel = ZdanieTseli() ?? _teplitsy[0];
            var vybor = FindFirstObjectByType<VyborZdaniy>();
            if (vybor != null) vybor.SendMessage("Vybrat", cel, SendMessageOptions.DontRequireReceiver);
        }

        // ------------------------------------------------------------ маркеры-пузыри над объектами

        /// <summary>Правила геймдизайнера (ТЗ-поправки п.3): при открытой панели маркеров нет; в кадре не больше трёх, приоритет ближним к центру экрана.</summary>
        private void ObnovitMarkery(double now, bool panelOtkryta)
        {
            if (panelOtkryta) { foreach (var m in _markery) m.Skryt(); return; }
            var kandidaty = new List<MarkerZdaniya>();
            foreach (var m in _markery) if (m.Podgotovit(now)) kandidaty.Add(m);
            if (kandidaty.Count > 3)
            {
                Vector2 centr = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                kandidaty.Sort((a, b) => (a.EkrannayaTochka - centr).sqrMagnitude.CompareTo((b.EkrannayaTochka - centr).sqrMagnitude));
                for (int i = 3; i < kandidaty.Count; i++) kandidaty[i].Skryt();
                kandidaty.RemoveRange(3, kandidaty.Count - 3);
            }
            foreach (var m in kandidaty) m.Narisovat();
        }

        /// <summary>Здание текущей цели карточки ЦЕЛЬ — единственное, над которым показывается «можно начать».</summary>
        private BuildingClickTarget _zdanieTseliKesh; private int _kadrZdaniyaTseli = -1;

        private BuildingClickTarget ZdanieTseli()
        {
            if (_kadrZdaniyaTseli == Time.frameCount) return _zdanieTseliKesh;   // маркеры зовут это на каждый пузырь каждый кадр
            _kadrZdaniyaTseli = Time.frameCount; _zdanieTseliKesh = NaytiZdanieTseli(); return _zdanieTseliKesh;
        }

        private BuildingClickTarget NaytiZdanieTseli()
        {
            var tsel = VychislitTsel(Now);
            if (!tsel.vidna || tsel.rol == ROL_OBUCHENIYA) return null;
            if (tsel.rol == ROL_TEPLITSY)
            {
                int idx = _tselTeplitsa >= 0 ? _tselTeplitsa : FieldView.TekushchayaCelPolya(State);   // ровно та теплица, о которой говорит текст цели
                foreach (var t in _teplitsy) if (_indeksGryadki[t] == idx) return t;
                return _teplitsy.Count > 0 ? _teplitsy[0] : null;
            }
            foreach (var m in _markery) { var z = m.Zdanie; if (z != null && z.TargetId == tsel.rol) return z; }
            foreach (var z in FindObjectsByType<BuildingClickTarget>(FindObjectsSortMode.None)) if (z.TargetId == tsel.rol) return z;
            return null;
        }

        public struct Tsel { public string glagol, tekst, rol, ikonka; public bool vidna; }

        /// <summary>Теплица, о которой говорит текущая цель; -1 — цель не про конкретную теплицу.
        /// Текст цели брал первый готовый горшок из плоского списка, а карточка открывала теплицу
        /// по своему правилу — и открывалась не та (Khan 07.09).</summary>
        private int _tselTeplitsa = -1;

        /// <summary>Культура, которую цель просит собрать сейчас. Держится, пока её созревшие
        /// горшки есть хоть в одной теплице: цель не должна меняться под руками игрока.</summary>
        private string _tselKultura;

        private int Schet(string k) => Igra.schetchiki.TryGetValue(k, out int v) ? v : 0;
        private void Schitat(string k) { Igra.schetchiki[k] = Schet(k) + 1; }

        /// <summary>
        /// Карточка ЦЕЛЬ — цепочка первых действий (заказчик 06.09: «задание висит и не исчезает»): каждый шаг
        /// закрывается фактом действия, а не состоянием мира, поэтому «посеять» пропадает после первого посева,
        /// даже если пустые горшки остались. После цепочки карточка ситуативная: есть готовый урожай — «собрать»,
        /// шаттл ждёт груз, который есть на складе — «загрузить»; иначе карточка скрыта.
        /// </summary>
        private Tsel VychislitTsel(double now)
        {
            string rolDrona = null; foreach (var k in DRONY.Keys) { rolDrona = k; break; }
            string rolDobychi = null; foreach (var k in DOBYCHA.Keys) { rolDobychi = k; break; }
            _tselTeplitsa = -1;
            string kultura = CropChoice.CheapestUnlocked(State.level);
            string imyaK = kultura != null ? Goods.Of(kultura).name : "культуру";
            if (Schet("obuchenie") == 0) return new Tsel { glagol = "НАЧАТЬ", tekst = "Пройти обучение" + "\n" + "по колонии", rol = ROL_OBUCHENIYA, vidna = true };
            if (Schet("posevov") == 0) return new Tsel { glagol = "ПОСЕЯТЬ", tekst = "Посеять " + imyaK + "\nв теплице", rol = ROL_TEPLITSY, ikonka = kultura, vidna = true };
            if (Schet("sborov") == 0)
            {
                FieldSlot luchshiy = null;
                foreach (var f in State.fields) if (f.state == FieldState.READY) { luchshiy = f; break; } else if (f.state == FieldState.GROWING && (luchshiy == null || f.ends_at < luchshiy.ends_at)) luchshiy = f;
                if (luchshiy != null) _tselTeplitsa = State.TeplitsaGorshka(luchshiy.idx);
                if (luchshiy != null && luchshiy.state == FieldState.READY) return new Tsel { glagol = "СОБРАТЬ", tekst = "Собрать урожай\n" + Goods.Of(luchshiy.good_id).name, rol = ROL_TEPLITSY, ikonka = luchshiy.good_id, vidna = true };
                if (luchshiy != null) { int left = FieldView.RemainingSec(luchshiy, now); return new Tsel { glagol = "ЖДАТЬ", tekst = Goods.Of(luchshiy.good_id).name + " растут\n" + string.Format("{0:00}:{1:00}", left / 60, left % 60), rol = ROL_TEPLITSY, ikonka = luchshiy.good_id, vidna = true }; }
                return new Tsel { glagol = "ПОСЕЯТЬ", tekst = "Посеять " + imyaK + "\nв теплице", rol = ROL_TEPLITSY, ikonka = kultura, vidna = true };
            }
            if (Schet("zagruzok") == 0 && Igra.shattl != null) return new Tsel { glagol = "ЗАГРУЗИТЬ", tekst = "Загрузить контейнер\nшаттла", rol = ROL_SHATTLA, ikonka = null, vidna = true };
            if (Schet("dronov") == 0 && rolDrona != null) return new Tsel { glagol = "ОТПРАВИТЬ", tekst = "Отправить дрона\nс заказом", rol = rolDrona, vidna = true };
            if (Schet("dobychi") == 0 && rolDobychi != null) return new Tsel { glagol = "ЗАПУСТИТЬ", tekst = "Запустить добычу\nводяного льда", rol = rolDobychi, vidna = true };
            if (Schet("gorshkov") == 0) return new Tsel { glagol = "КУПИТЬ", tekst = "Купить горшок\nв теплице (+)", rol = ROL_TEPLITSY, vidna = true };
            // Ситуативные цели после обучения.
            // Цель «собрать» держится за ОДНУ культуру, пока её созревшие горшки есть хоть в одной
            // теплице, и ведёт по теплицам по порядку, пропуская те, где этой культуры нет
            // (Khan 07.09). Раньше цель брала первый готовый горшок из плоского списка и после
            // первого же сбора перескакивала на другую культуру и другую теплицу.
            string sobrat = _tselKultura != null && State.PervayaTeplitsaSKulturoy(_tselKultura) >= 0 ? _tselKultura : null;
            // Новая культура выбирается тоже по теплицам, а не по плоскому списку горшков: в списке есть
            // горшки вне теплиц, и цель указывала на культуру, которую негде собрать (проверка 07.09).
            if (sobrat == null) sobrat = State.PervayaGotovayaKultura();
            _tselKultura = sobrat;
            if (sobrat != null && Warehouse.IsFull(State.warehouse))
            {
                // Урожай есть, а класть некуда: «СОБРАТЬ» при полном складе — тап впустую и без объяснения.
                _tselTeplitsa = -1;
                return new Tsel { glagol = "ОСВОБОДИТЬ", tekst = "Склад полон" + "\n" + "Отправьте или продайте товар", rol = ROL_SKLADA, ikonka = sobrat, vidna = true };
            }
            if (sobrat != null)
            {
                _tselTeplitsa = State.PervayaTeplitsaSKulturoy(sobrat);
                return new Tsel { glagol = "СОБРАТЬ", tekst = "Собрать урожай\n" + Goods.Of(sobrat).name, rol = ROL_TEPLITSY, ikonka = sobrat, vidna = true };
            }
            if (Igra.shattl != null && Igra.shattl.state != ShuttleState.IN_TRANSIT)
                foreach (var sl in Igra.shattl.slots) if (sl.qty_filled < sl.qty_required && Warehouse.AvailableOf(State.warehouse, sl.good_id) > 0) return new Tsel { glagol = "ЗАГРУЗИТЬ", tekst = "Загрузить шаттл:\n" + Goods.Of(sl.good_id).name, rol = ROL_SHATTLA, ikonka = sl.good_id, vidna = true };
            return new Tsel { vidna = false };
        }

        /// <summary>Что показать над объектом: простаивает (иконка того, что можно сделать), работает (остаток), готово. Скрыт — когда делать нечего.</summary>
        public MarkerZdaniya.Rezhim SostoyanieMarkera(BuildingClickTarget z, double now, out Sprite ikonka, out string tekst)
        {
            ikonka = null; tekst = null;
            string rol = z.TargetId;
            bool etoTsel = ZdanieTseli() == z;   // «можно начать» — только над зданием цели (п.3.2); таймеров на карте нет (п.3.3)
            if (_indeksGryadki.TryGetValue(z, out int idx))
            {
                var f = State.PoleTeplitsy(idx);
                if (f == null) return MarkerZdaniya.Rezhim.Skryt;
                if (f.state == FieldState.READY) return MarkerZdaniya.Rezhim.Gotovo;
                if (f.state == FieldState.GROWING) return MarkerZdaniya.Rezhim.Skryt;
                if (!etoTsel) return MarkerZdaniya.Rezhim.Skryt;
                ikonka = _el?.Ikonka("algae"); return MarkerZdaniya.Rezhim.Prostoy;
            }
            if (rol == ROL_PLAVILNI)
            {
                var pl = Igra.plavilnya;
                if (pl.vyhod > 0) return MarkerZdaniya.Rezhim.Gotovo;
                if (pl.Rabotaet || !etoTsel) return MarkerZdaniya.Rezhim.Skryt;
                if (Warehouse.AvailableOf(State.warehouse, Plavilnya.Vhodnoy) > 0) { ikonka = _el?.Ikonka(Plavilnya.Vhodnoy); return MarkerZdaniya.Rezhim.Prostoy; }
                return MarkerZdaniya.Rezhim.Skryt;
            }
            if (FABRIKI.TryGetValue(rol, out var tip))
            {
                double min = double.MaxValue; bool est = false, gotovo = false, svobodno = false;
                foreach (var sl in Igra.fabriki[rol])
                {
                    if (sl.state == FactorySlotState.READY) gotovo = true;
                    else if (sl.state == FactorySlotState.PRODUCING) { est = true; min = Math.Min(min, sl.ends_at - now); }
                    else if (sl.state == FactorySlotState.EMPTY) svobodno = true;
                }
                if (gotovo) return MarkerZdaniya.Rezhim.Gotovo;
                if (est || !svobodno || !etoTsel) return MarkerZdaniya.Rezhim.Skryt;
                foreach (var good in Goods.GOODS.Values)
                {
                    if (good.kind != GoodKind.factory || good.required_building != tip) continue;
                    bool hvataet = true;
                    foreach (var inp in good.inputs) if (Warehouse.AvailableOf(State.warehouse, inp.good_id) < inp.qty) { hvataet = false; break; }
                    if (hvataet) { ikonka = _el?.Ikonka(good.id); return MarkerZdaniya.Rezhim.Prostoy; }
                }
                return MarkerZdaniya.Rezhim.Skryt;   // готовить не из чего — не зовём
            }
            if (DOBYCHA.TryGetValue(rol, out string gid) && Igra.dobycha.TryGetValue(gid, out var site))
            {
                if (site.state == MiningState.READY) return MarkerZdaniya.Rezhim.Gotovo;
                if (site.state == MiningState.WORKING || !etoTsel) return MarkerZdaniya.Rezhim.Skryt;
                ikonka = _el?.Ikonka(gid); return MarkerZdaniya.Rezhim.Prostoy;
            }
            if (DRONY.TryGetValue(rol, out string dron))
            {
                var kurery = MarshrutyDronov.Ekz;
                if ((kurery != null && !kurery.Svoboden(dron)) || !etoTsel) return MarkerZdaniya.Rezhim.Skryt;
                foreach (var zk in Igra.drony)
                {
                    if (zk.state != OrderSlotState.active) continue;
                    bool vse = true;
                    foreach (var poz in zk.positions) if (Warehouse.AvailableOf(State.warehouse, poz.good_id) < poz.qty) { vse = false; break; }
                    if (vse) { ikonka = _el?.Element("znachok-dron"); return MarkerZdaniya.Rezhim.Prostoy; }
                }
                return MarkerZdaniya.Rezhim.Skryt;
            }
            if (rol == ROL_SHATTLA)
            {
                var trip = Igra.shattl;
                // Привезённый груз ждёт в контейнерах — знак «готово» над площадкой, независимо от цели:
                // иначе игрок не узнает, что рейс вернулся, пока сам не откроет панель (Khan 08.09).
                if (EstNezabrannyyGruz()) return MarkerZdaniya.Rezhim.Gotovo;
                if (trip == null) return MarkerZdaniya.Rezhim.Skryt;
                if (trip.state == ShuttleState.IN_TRANSIT || !etoTsel) return MarkerZdaniya.Rezhim.Skryt;
                if (_polyot != null && !_polyot.NaPloshchadke) return MarkerZdaniya.Rezhim.Skryt;
                foreach (var sl in trip.slots) if (sl.qty_filled < sl.qty_required && Warehouse.AvailableOf(State.warehouse, sl.good_id) > 0) { ikonka = _el?.Element("znachok-shattl"); return MarkerZdaniya.Rezhim.Prostoy; }
                return MarkerZdaniya.Rezhim.Skryt;
            }
            return MarkerZdaniya.Rezhim.Skryt;
        }

        // ------------------------------------------------------------ выбор здания → панель

        private void PriVybore(BuildingClickTarget zdanie)
        {
            _vybrano = zdanie;
            if (zdanie.TargetId == ROL_SKLADA)
            {
                if (_ekranSklada != null && !_ekranSklada.gameObject.activeSelf) _ekranSklada.Perekluchit();
                return;
            }
            PokazatPanel(zdanie);
        }

        private void PriSnyatii()
        {
            _vybrano = null;
            if (_panel != null) _panel.Skryt();
            if (_konv != null) _konv.Skryt();
            if (_panelSh != null) _panelSh.Skryt();
            if (_panelT != null) _panelT.Skryt();
            if (_panelP != null) _panelP.Skryt();
            _vybranaKultura = null;
        }

        private void PokazatPanel(BuildingClickTarget zdanie)
        {
            if (_panel == null || _konv == null) return;
            string rol = zdanie.TargetId;
            // Конвейерная панель (теплицы, фабрики, добыча) и списочная (шаттл, дроны) — одна за раз
            if (_indeksGryadki.TryGetValue(zdanie, out int idx)) { _panel.Skryt(); _konv.Skryt(); _panelSh?.Skryt(); _panelP?.Skryt(); PanelGryadki(idx); return; }   // _panelP тоже: тап по теплице при открытой плавильне оставлял две панели (проверка мышью 08.09)
            _panelT?.Skryt();
            if (rol == ROL_PLAVILNI) { _panel.Skryt(); _konv.Skryt(); _panelSh?.Skryt(); PanelPlavilniPokazat(zdanie.Label); return; }
            _panelP?.Skryt();
            if (FABRIKI.ContainsKey(rol)) { _panel.Skryt(); _panelSh?.Skryt(); PanelFabriki(rol, zdanie.Label); return; }
            _konv.Skryt();
            if (rol == ROL_SHATTLA || rol.StartsWith("shattl")) { _panel.Skryt(); OtkrytPanelShattla(); return; }
            _panelSh?.Skryt();
            if (DOBYCHA.TryGetValue(rol, out string good)) { PanelDobychi(good, zdanie.Label); return; }
            if (DRONY.TryGetValue(rol, out string dron)) { PanelDronov(dron); return; }
            if (rol == ROL_ANGARA) { PanelAngara(); return; }
            if (rol == ROL_ENERGOSTANTSII || rol == ROL_PLITY) { PanelEnergii(); return; }
            if (POSELENTSY.TryGetValue(rol, out int mest)) { PanelZhilya(zdanie.Label, mest); return; }
            _panel.Skryt();
        }

        /// <summary>Энергостанция и плиты открывают одну панель: запас, приход станции и плит.</summary>
        private void PanelEnergii()
        {
            NachislitEnergiyu(Now);
            var stroki = new List<PanelZdaniya.Stroka>
            {
                new PanelZdaniya.Stroka { ikonka = Resources.Load<Sprite>("UI/Ikonki/znachok-uskorit"), imya = "Энергия: " + Igra.energiya.ToString("0.0") + " / " + ENERGIYA_POTOLOK.ToString("0"),
                    podpis = "+" + EnergiyaVChas.ToString("0") + " в час", progress = (float)(Igra.energiya / ENERGIYA_POTOLOK) },
                new PanelZdaniya.Stroka { imya = "Энергостанция", podpis = "+" + ENERGIYA_STANTSIYA_V_CHAS.ToString("0") + " в час · хранит до " + ENERGIYA_POTOLOK.ToString("0") },
                new PanelZdaniya.Stroka { imya = "Солнечные плиты × " + _plit, podpis = "+" + ENERGIYA_PLITA_V_CHAS.ToString("0") + " в час каждая" },
            };
            _panel.Pokazat("Энергостанция", stroki);
        }

        private void PanelZhilya(string imya, int mest)
        {
            var stroki = new List<PanelZdaniya.Stroka>
            {
                new PanelZdaniya.Stroka { imya = "+" + mest + " поселенцев", podpis = "Всего в колонии: " + Poselentsy },
            };
            _panel.Pokazat(imya, stroki);
        }

        // ------------------------------------------------------------ грядки

        private void PanelGryadki(int t)
        {
            double now = Now;
            var tep = State.teplitsy[t];
            var m = new PanelTeplitsy.Model { zagolovok = "Теплица " + (t + 1) + " · ур. " + tep.uroven, ryadov = Teplitsa.Ryadov(tep.uroven) };
            if (Warehouse.IsFull(State.warehouse) && State.PoleTeplitsy(t) != null && State.PoleTeplitsy(t).state == FieldState.READY)
                m.podskazka = "Склад полон: урожай ждёт места";   // иначе тап по готовому горшку молча ничего не делает
            int gotovyh = 0, pustyh = 0;
            foreach (int pot in tep.gorshki)
            {
                var slot = Production.RefreshField(State.fields[pot], now);
                var g = new PanelTeplitsy.Gorshok { idx = pot };
                int p = pot;
                if (slot.state == FieldState.EMPTY)
                {
                    g.sost = PanelTeplitsy.Sost.Pusto; pustyh++;
                    g.tap = () => { if (_vybranaKultura != null) Poseyat(p, _vybranaKultura); };
                }
                else
                {
                    var good = Goods.Of(slot.good_id);
                    g.ikonka = _panelT.Ikonka(slot.good_id); g.kultura = slot.good_id;
                    if (slot.state == FieldState.READY)
                    {
                        gotovyh++;
                        bool estMesto = Warehouse.CanAccept(State.warehouse, Goods.HarvestQty(slot.good_id));
                        g.sost = PanelTeplitsy.Sost.Gotovo; g.tap = estMesto ? (Action)(() => Sobrat(p)) : null;
                    }
                    else
                    {
                        int ostalos = Math.Max(0, (int)Math.Ceiling(slot.ends_at - now));
                        g.sost = PanelTeplitsy.Sost.Rastet; g.podpis = Vremya(ostalos); g.progress = Mathf.Clamp01(1f - (float)(ostalos / Dlit(good.prod_time_sec)));
                        g.tap = () => UskoritGryadku(p);
                    }
                }
                m.gorshki.Add(g);
            }
            // «+»: докупить горшок, пока есть место в рядах уровня; иначе — улучшить теплицу
            if (tep.gorshki.Count < Teplitsa.MaxGorshkov(tep.uroven))
            {
                int cena = Teplitsa.CenaGorshka(tep.gorshki.Count);
                m.plyusPodpis = "+ " + cena + " кр"; m.plyusAktiven = State.credits >= cena; m.plyus = () => KupitGorshok(t);
            }
            else if (tep.uroven < Teplitsa.MAX_UROVEN)
            {
                int cena = Teplitsa.CenaUluchsheniya(tep.uroven);
                m.podskazka = "Ряды заполнены · улучшить теплицу за " + cena + " кр (ещё ряд горшков)";
                // Улучшение — справа от заголовка, только когда все горшки уровня куплены (Khan 06.09). Молния — временная иконка, потом голубая стрелка
                m.uluchshitIkonka = Resources.Load<Sprite>("UI/Ikonki/znachok-uroven"); m.uluchshitPodpis = cena + " кр";   // значок уровня, а не молния ускорения (Khan 08.09) m.uluchshitAktivno = State.credits >= cena; m.uluchshit = () => UluchshitTeplitsu(t);
            }
            var kultury = new List<Good>();
            foreach (var good in Goods.GOODS.Values) if (good.kind == GoodKind.crop) kultury.Add(good);
            kultury.Sort((x, y) => Array.IndexOf(Teplitsa.PORYADOK_KULTUR, x.id).CompareTo(Array.IndexOf(Teplitsa.PORYADOK_KULTUR, y.id)));   // не из списка — в начало, заметно
            foreach (var good in kultury)
            {
                int cena = Economy.PlantingCost(good.price);
                m.kultury.Add(new PanelTeplitsy.Kultura { id = good.id, ikonka = _panelT.Ikonka(good.id), cena = cena, hvataet = State.credits >= cena });
            }
            if (_vybranaKultura == null && pustyh > 0) _vybranaKultura = CropChoice.CheapestUnlocked(State.level);
            m.vybrano = _vybranaKultura;
            m.vybrat = id => { _vybranaKultura = id; PanelGryadki(t); };   // улучшение — только кнопкой у заголовка (мёртвый путь "__uluchshit" убран 07.09)
            if (m.podskazka == null)
                m.podskazka = gotovyh > 0 ? "Тап по готовому горшку — собрать" : pustyh > 0 ? "Выберите культуру и тапните по пустому горшку" : "Всё растёт · тап по горшку — ускорить";
            _panelT.Pokazat(m);
        }

        private void PokazatTeplitsuGorshka(int pot)
        {
            int t = State.TeplitsaGorshka(pot);
            if (t >= 0 && t < _teplitsy.Count) PokazatPanel(_teplitsy[t]);
        }

        public void KupitGorshok(int t)
        {
            var tep = State.teplitsy[t];
            if (tep.gorshki.Count >= Teplitsa.MaxGorshkov(tep.uroven)) return;
            int cena = Teplitsa.CenaGorshka(tep.gorshki.Count);
            if (State.credits < cena)
            {
                Debug.Log("[игра] горшок: не хватает кредитов");
                NehvatkaBezPokupki(_el?.Element("moneta"), "кредитов", cena - State.credits, "Кредиты за изотопы не покупаются — продайте товары или отправьте заказ.");
                return;
            }
            State.credits -= cena; State.DobavitGorshok(tep); Schitat("gorshkov");
            Debug.Log($"[игра] теплица {t + 1}: куплен горшок №{tep.gorshki.Count} за {cena} кр");
            Izmeneno(); if (t < _teplitsy.Count) PokazatPanel(_teplitsy[t]);
        }

        public void UluchshitTeplitsu(int t)
        {
            var tep = State.teplitsy[t];
            if (tep.uroven >= Teplitsa.MAX_UROVEN) return;
            if (tep.gorshki.Count < Teplitsa.MaxGorshkov(tep.uroven)) { Debug.Log("[игра] улучшение теплицы: ряды не заполнены"); return; }   // правило было только в UI (проверка 07.09)
            int cena = Teplitsa.CenaUluchsheniya(tep.uroven);
            if (State.credits < cena)
            {
                Debug.Log("[игра] улучшение теплицы: не хватает кредитов");
                NehvatkaBezPokupki(_el?.Element("moneta"), "кредитов", cena - State.credits, "Кредиты за изотопы не покупаются — продайте товары или отправьте заказ.");
                return;
            }
            State.credits -= cena; tep.uroven++;
            Debug.Log($"[игра] теплица {t + 1} → уровень {tep.uroven} за {cena} кр");
            Izmeneno(); if (t < _teplitsy.Count) PokazatPanel(_teplitsy[t]);
        }

        public void Poseyat(int idx, string goodId)
        {
            var res = Production.Plant(State.fields[idx], goodId, Kontekst(Now), State.fields);
            State.Apply(res);
            Debug.Log($"[игра] посев {goodId} в горшок {idx}: {(res.ok ? "ок" : res.reason.ToString())}, кредиты {State.credits}");
            if (res.ok) { Schitat("posevov"); Izmeneno(); }
            PokazatTeplitsuGorshka(idx);
        }

        public void UskoritGryadku(int idx)
        {
            var slot = State.fields[idx];
            if (slot.state != FieldState.GROWING) return;
            int cena = CenaUskoreniya(slot.ends_at - Now, GoodKind.crop);
            if (!SpisatIzotopy(cena, "горшок " + idx)) return;
            slot.ends_at = Now;
            Production.RefreshField(slot, Now);
            Izmeneno();
            PokazatTeplitsuGorshka(idx);
        }

        public void Sobrat(int idx)
        {
            string good = State.fields[idx].good_id;
            var res = Production.CollectField(State.fields[idx], Kontekst(Now));
            State.Apply(res);
            Debug.Log($"[игра] сбор {good} с горшка {idx}: {(res.ok ? "ок, +" + res.xp_gained + " XP" : res.reason.ToString())}, склад {Warehouse.TotalQty(State.warehouse)}/{State.warehouse.capacity}");
            if (res.ok) { Schitat("sborov"); SkladPopolnen(Now); Izmeneno(); }
            PokazatTeplitsuGorshka(idx);
        }

        // ------------------------------------------------------------ плавильня

        /// <summary>Длительность плавки одной единицы — время рецепта воды из домена с учётом ускорения.</summary>
        private double DlitPlavki => Dlit(Goods.Of(Plavilnya.Vyhodnoy).prod_time_sec);

        private void PanelPlavilniPokazat(string imya)
        {
            double now = Now;
            var pl = Igra.plavilnya;
            pl.Obnovit(now, DlitPlavki);
            int lda = Warehouse.AvailableOf(State.warehouse, Plavilnya.Vhodnoy);
            bool estMesto = Warehouse.FreeSpace(State.warehouse) > 0;
            double ost = pl.Ostalos(now);
            var m = new PanelPlavilni.Model
            {
                zagolovok = imya, vhod = pl.vhod, vhodMax = Plavilnya.VHOD_MAX, vyhod = pl.vyhod, vyhodMax = Plavilnya.VYHOD_MAX,
                rabotaet = pl.Rabotaet, progress = pl.Rabotaet ? Mathf.Clamp01(1f - (float)(ost / DlitPlavki)) : 0f, ostalos = Vremya(ost),
                ikonkaLda = _panelP.Ikonka(Plavilnya.Vhodnoy), ldaNaSklade = lda,
                zagruzit = (lda > 0 && pl.vhod < Plavilnya.VHOD_MAX) ? (Action)ZagruzitPlavilnyu : null,
                zabrat = (pl.vyhod > 0 && estMesto) ? (Action)ZabratPlavilnyu : null,
            };
            if (pl.vyhod > 0 && !estMesto) m.podskazka = "Склад полон — вода ждёт места";
            else if (pl.vyhod >= Plavilnya.VYHOD_MAX) m.podskazka = "Канистра полна — тапните, чтобы забрать";
            else if (pl.vhod == 0 && lda == 0) m.podskazka = "Нет льда: запустите ледосбор";
            else if (pl.vhod == 0) m.podskazka = "Тап по льду — засыпать " + Plavilnya.PORTSIYA_ZAGRUZKI + " в воронку";
            else m.podskazka = "Плавит по 1 льду за " + Vremya(DlitPlavki);
            _panelP.Pokazat(m);
        }

        public void ZagruzitPlavilnyu()
        {
            int n = Igra.plavilnya.Zagruzit(State.warehouse, Plavilnya.PORTSIYA_ZAGRUZKI);
            Debug.Log($"[игра] плавильня: засыпано {n} льда, в воронке {Igra.plavilnya.vhod}");
            if (n > 0) { Igra.plavilnya.Obnovit(Now, DlitPlavki); Schitat("plavki"); Izmeneno(); }
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        public void ZabratPlavilnyu()
        {
            int n = Igra.plavilnya.Zabrat(State.warehouse);
            if (n > 0) { State.xp += Goods.Of(Plavilnya.Vyhodnoy).base_xp * n; SkladPopolnen(Now); Izmeneno(); }
            Debug.Log($"[игра] плавильня: забрано {n} воды, склад {Warehouse.TotalQty(State.warehouse)}/{State.warehouse.capacity}");
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        // ------------------------------------------------------------ фабрики

        private void PanelFabriki(string rol, string imya)
        {
            double now = Now;
            var slots = Igra.fabriki[rol];
            var tip = FABRIKI[rol];
            var m = new PanelKonveyera.Model { zagolovok = imya };
            int svobodnyh = 0;
            foreach (var s in slots)
            {
                Production.RefreshFactorySlot(s, now);
                var sl = new PanelKonveyera.Slot();
                int idx = s.idx;
                if (s.state == FactorySlotState.EMPTY) { sl.sost = PanelKonveyera.Sost.Pusto; svobodnyh++; }
                else
                {
                    var good = Goods.Of(s.good_id);
                    int ostalos = Math.Max(0, (int)Math.Ceiling(s.ends_at - now));
                    sl.ikonka = _konv.Ikonka(s.good_id);
                    if (s.state == FactorySlotState.QUEUED)
                    {
                        sl.sost = PanelKonveyera.Sost.Zhdet; sl.podpis = "Ждёт";
                        sl.karta = new PanelKonveyera.Karta { ikonka = sl.ikonka, imya = good.name, zametka = "Не хватает: " + ChegoNeHvataet(good), knopka = "ОТМЕНИТЬ", deystvie = () => OtmenitOcheredi(rol, idx) };
                        foreach (var inp in good.inputs) sl.karta.vhody.Add(new PanelKonveyera.Vhod { ikonka = _konv.Ikonka(inp.good_id), imya = Goods.Of(inp.good_id).name, est = Warehouse.AvailableOf(State.warehouse, inp.good_id), nado = inp.qty });
                    }
                    else if (s.state == FactorySlotState.PRODUCING)
                    {
                        int cena = CenaUskoreniya(ostalos, GoodKind.factory);
                        sl.sost = PanelKonveyera.Sost.Rabotaet; sl.podpis = Vremya(ostalos); sl.progress = Mathf.Clamp01(1f - (float)(ostalos / Dlit(good.prod_time_sec)));
                        sl.karta = new PanelKonveyera.Karta { ikonka = sl.ikonka, imya = good.name, zametka = "Готовится" + (cena == 0 ? " · ускорение бесплатно" : " · ускорить за " + cena + " изотопов (у вас " + Igra.izotopy + ")"), vremya = Vremya(ostalos), xp = good.base_xp, knopka = cena == 0 ? "УСКОРИТЬ" : "⚡ " + cena, knopkaAktivna = Igra.izotopy >= cena, deystvie = () => UskoritFabriku(rol, idx) };
                    }
                    else
                    {
                        bool estMesto = Warehouse.CanAccept(State.warehouse, Goods.FACTORY_OUTPUT_QTY);
                        sl.sost = PanelKonveyera.Sost.Gotovo; sl.podpis = estMesto ? "Забрать" : "Склад полон"; sl.progress = 1f;
                        sl.tap = estMesto ? (Action)(() => ZabratFabriku(rol, idx)) : null;
                        sl.karta = new PanelKonveyera.Karta { ikonka = sl.ikonka, imya = good.name, zametka = estMesto ? "Готово: " + Goods.FACTORY_OUTPUT_QTY + " шт" : "Склад полон — продайте что-нибудь", xp = good.base_xp, knopka = "ЗАБРАТЬ", knopkaAktivna = estMesto, deystvie = () => ZabratFabriku(rol, idx) };
                    }
                }
                m.sloty.Add(sl);
            }
            foreach (var good in Goods.GOODS.Values)
            {
                if (good.kind != GoodKind.factory || good.required_building != tip) continue;
                bool hvataet = true;
                var k = new PanelKonveyera.Karta { ikonka = _konv.Ikonka(good.id), imya = good.name, vremya = Vremya(Dlit(good.prod_time_sec)), xp = good.base_xp };
                foreach (var inp in good.inputs)
                {
                    int est = Warehouse.AvailableOf(State.warehouse, inp.good_id);
                    if (est < inp.qty) hvataet = false;
                    k.vhody.Add(new PanelKonveyera.Vhod { ikonka = _konv.Ikonka(inp.good_id), imya = Goods.Of(inp.good_id).name, est = est, nado = inp.qty });
                }
                string gid = good.id;
                k.knopka = svobodnyh == 0 ? "ОЧЕРЕДЬ ПОЛНА" : (hvataet ? "ГОТОВИТЬ" : "В ОЧЕРЕДЬ");
                k.knopkaAktivna = svobodnyh > 0;
                k.deystvie = () => Zapustit(rol, gid);
                m.recepty.Add(new PanelKonveyera.Recept { id = good.id, ikonka = k.ikonka, dostupen = hvataet, karta = k });
            }
            if (m.recepty.Count == 0) m.podskazka = "Рецептов для этого здания нет";
            _konv.Pokazat(m);
        }

        public void UskoritFabriku(string rol, int idx)
        {
            var slot = Igra.fabriki[rol].Find(x => x.idx == idx);
            if (slot == null || slot.state != FactorySlotState.PRODUCING) return;
            int cena = CenaUskoreniya(slot.ends_at - Now, GoodKind.factory);
            if (!SpisatIzotopy(cena, rol + " слот " + idx)) return;
            slot.ends_at = Now;
            Production.RefreshFactorySlot(slot, Now);
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        private string ChegoNeHvataet(Good good)
        {
            var sp = new List<string>();
            foreach (var inp in good.inputs) { int est = Warehouse.AvailableOf(State.warehouse, inp.good_id); if (est < inp.qty) sp.Add(Goods.Of(inp.good_id).name + " " + est + "/" + inp.qty); }
            return sp.Count == 0 ? "—" : string.Join(", ", sp);
        }

        public void Zapustit(string rol, string goodId)
        {
            var slots = Igra.fabriki[rol];
            FactorySlot slot = slots.Find(s => s.state == FactorySlotState.EMPTY);
            if (slot == null) return;
            var res = Production.Enqueue(slot, goodId, Kontekst(Now));
            State.Apply(res);
            Debug.Log($"[игра] {rol}: {goodId} → {(res.ok ? (res.reason == ActionReason.no_inputs ? "в очередь, нет входов" : "готовится") : res.reason.ToString())}");
            if (res.ok) Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
            // Рецепт лёг в очередь только потому, что входов нет: показываем, чего и сколько (Khan 08.09)
            if (res.ok && res.reason == ActionReason.no_inputs && PervyyDefitsitRetsepta(goodId, out string vhod, out int netu))
                NehvatkaTovara(vhod, netu);
        }

        public void OtmenitOcheredi(string rol, int idx)
        {
            var s = Igra.fabriki[rol].Find(x => x.idx == idx);
            if (s == null || s.state != FactorySlotState.QUEUED) return;
            s.state = FactorySlotState.EMPTY; s.good_id = null; s.queued_at = 0;
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        public void ZabratFabriku(string rol, int idx)
        {
            var s = Igra.fabriki[rol].Find(x => x.idx == idx);
            if (s == null) return;
            string good = s.good_id;
            var res = Production.CollectFactory(s, Kontekst(Now));
            State.Apply(res);
            Debug.Log($"[игра] {rol}: забрано {good}: {(res.ok ? "+" + res.xp_gained + " XP" : res.reason.ToString())}");
            if (res.ok) { SkladPopolnen(Now); Izmeneno(); }
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        // ------------------------------------------------------------ добыча

        private void PanelDobychi(string goodId, string imya)
        {
            // Добыча — не фабрика: без конвейера и лотка, одна строка действия (заказчик 06.09)
            double now = Now;
            var site = Mining.Refresh(Igra.dobycha[goodId], now);
            var good = Goods.Of(goodId);
            var diap = Goods.GOOD_BASE_QTY[goodId];
            double dlit = Mining.BatchDurationSec(goodId, site.machines, TuningIgry);
            string tehnika = goodId == "water_ice" ? site.machines + " ледосбор" + (site.machines == 1 ? "" : "а")
                : goodId == "regolith" ? "реголитосборщик" : goodId == "methane" ? "буровая 05" : goodId == "iron_ore" ? "буровая 02" : "техника";
            var stroki = new List<PanelZdaniya.Stroka>();
            if (site.state == MiningState.EMPTY)
                stroki.Add(new PanelZdaniya.Stroka { ikonka = _panel.Ikonka(goodId), imya = good.name + ", партия " + diap.min + "–" + diap.max, podpis = tehnika + " · " + Vremya(dlit) + " · +" + good.base_xp + " XP за штуку", knopka = "ДОБЫВАТЬ", deystvie = () => ZapustitDobychu(goodId) });
            else if (site.state == MiningState.WORKING)
            {
                int ostalos = Math.Max(0, (int)Math.Ceiling(site.ends_at - now));
                stroki.Add(new PanelZdaniya.Stroka { ikonka = _panel.Ikonka(goodId), imya = good.name, podpis = tehnika + " работает, осталось " + Vremya(ostalos), progress = Mathf.Clamp01(1f - (float)(ostalos / dlit)) });
                stroki.Add(StrokaUskoreniya(CenaUskoreniya(ostalos, GoodKind.factory), () => UskoritDobychu(goodId)));
            }
            else
            {
                bool estMesto = Warehouse.CanAccept(State.warehouse, diap.max);
                stroki.Add(new PanelZdaniya.Stroka { ikonka = _panel.Ikonka(goodId), imya = good.name, podpis = estMesto ? "Партия готова" : "Склад полон", progress = 1f, knopka = "ЗАБРАТЬ", knopkaAktivna = estMesto, deystvie = () => ZabratDobychu(goodId) });
            }
            _panel.Pokazat(imya, stroki);
        }

        public void UskoritDobychu(string goodId)
        {
            var site = Igra.dobycha[goodId];
            if (site.state != MiningState.WORKING) return;
            int cena = CenaUskoreniya(site.ends_at - Now, GoodKind.factory);
            if (!SpisatIzotopy(cena, "добыча " + goodId)) return;
            site.ends_at = Now;
            Mining.Refresh(site, Now);
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        public void ZapustitDobychu(string goodId)
        {
            var res = Mining.Start(Igra.dobycha[goodId], Now, TuningIgry);
            Debug.Log($"[игра] добыча {goodId}: {(res.ok ? "запущена на " + Vremya(Mining.BatchDurationSec(goodId, Igra.dobycha[goodId].machines, TuningIgry)) : res.reason.ToString())}");
            if (res.ok) { Schitat("dobychi"); Izmeneno(); }
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        /// <summary>Хватает ли топлива на рейс: метан и кислород со склада, энергия из счётчика.</summary>
        /// <summary>Сколько топлива требует рейс на текущем уровне игрока (нуль — товар ещё не открыт).</summary>
        public void ToplivoNuzhno(out int metan, out int kislorod, out double energiya) =>
            Uluchsheniya.ToplivoReysa(_uroven, out metan, out kislorod, out energiya);

        public bool ToplivoEst(out int metan, out int kislorod, out double energiya)
        {
            metan = Warehouse.AvailableOf(State.warehouse, Uluchsheniya.TOPLIVO_METAN);
            kislorod = Warehouse.AvailableOf(State.warehouse, Uluchsheniya.TOPLIVO_KISLOROD);
            energiya = Igra.energiya;
            ToplivoNuzhno(out int nMetan, out int nKislorod, out double nEnergiya);
            return metan >= nMetan && kislorod >= nKislorod && energiya >= nEnergiya;
        }

        /// <summary>Списать топливо рейса. Возвращает ложь, если чего-то не хватило (тогда ничего не списано).</summary>
        private bool SpisatToplivo()
        {
            if (!ToplivoEst(out _, out _, out _)) return false;
            ToplivoNuzhno(out int nMetan, out int nKislorod, out double nEnergiya);
            if (nMetan > 0 && !Warehouse.Consume(State.warehouse, Uluchsheniya.TOPLIVO_METAN, nMetan)) return false;
            if (nKislorod > 0 && !Warehouse.Consume(State.warehouse, Uluchsheniya.TOPLIVO_KISLOROD, nKislorod))
            { if (nMetan > 0) Warehouse.Deposit(State.warehouse, Uluchsheniya.TOPLIVO_METAN, nMetan); return false; }
            Igra.energiya = Math.Max(0, Igra.energiya - nEnergiya);
            Debug.Log($"[игра] шаттл заправлен: −{nMetan} метана, −{nKislorod} кислорода, −{nEnergiya:0} энергии");
            return true;
        }

        /// <summary>Уровень техники или транспорта из состояния.</summary>
        public int UrovenTehniki(string klyuch) => Uluchsheniya.Uroven(Igra.urovni, klyuch);

        public void ZabratDobychu(string goodId)
        {
            int bylo = Warehouse.QtyOf(State.warehouse, goodId);
            var res = Mining.Collect(Igra.dobycha[goodId], State.warehouse, Rnd, Now, Uluchsheniya.PribavkaDobychi(UrovenTehniki(goodId)));   // уровень техники из ангара
            State.Apply(res);
            Debug.Log($"[игра] добыча {goodId}: {(res.ok ? "забрано " + (Warehouse.QtyOf(State.warehouse, goodId) - bylo) + " шт, +" + res.xp_gained + " XP" : res.reason.ToString())}");
            if (res.ok) { SkladPopolnen(Now); Izmeneno(); }
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        // ------------------------------------------------------------ шаттл

        private ShuttleTrip NovyyReys(ShuttleTrip pred)
        {
            var ctx = new ShuttleGenContext
            {
                level = UrovenZakazov, warehouse = State.warehouse,   // генератору — реальный уровень (как у дронов); 99 остаётся только для открытий (сверка 07.09: брекет 15+ давал тяжёлые заказы)
                available_goods = DostupnyeTovary(),
                previous = pred, is_first_trip = pred == null, arrival_no = Igra.prilyotov, rng = Rnd, deficit_locks = _defitsit, now = Now,
            };
            var trip = Shuttle.GenerateTrip(ctx);
            if (trip.slots.Count > OTSEKOV_SHATTLA) trip.slots.RemoveRange(OTSEKOV_SHATTLA, trip.slots.Count - OTSEKOV_SHATTLA);   // эталон заказчика 06.09: три контейнера в отсеке
            Debug.Log($"[игра] шаттл: заказ №{Igra.prilyotov} на {trip.slots.Count} отсеков, рейс {trip.trip_min:0} мин: " + string.Join(", ", trip.slots.ConvertAll(s => s.good_id + "×" + s.qty_required)));
            return trip;
        }

        /// <summary>Имя модуля для панели; неизвестный ключ показываем как есть, чтобы не терять груз молча.</summary>
        private static string ImyaModulya(string klyuch) =>
            Enum.TryParse(klyuch, out ModuleId id) && Modules.MODULES.ContainsKey(id) ? Modules.MODULES[id].name : klyuch;

        /// <summary>Есть ли в контейнерах хоть что-то незабранное.</summary>
        public bool EstNezabrannyyGruz()
        {
            if (Igra?.privezeno == null) return false;
            foreach (var k in Igra.privezeno) if (!string.IsNullOrEmpty(k)) return true;
            return false;
        }

        /// <summary>
        /// Выгрузка тапом (спека Khan 08.09): один контейнер — один модуль на склад модулей.
        /// Отсек гасится ровно тот, по которому нажали: место в списке не освобождается, а обнуляется.
        /// </summary>
        public void ZabratGruz(int i)
        {
            if (Igra.privezeno == null || i < 0 || i >= Igra.privezeno.Count) return;
            string k = Igra.privezeno[i];
            if (string.IsNullOrEmpty(k)) return;
            Igra.privezeno[i] = "";
            Igra.moduli[k] = (Igra.moduli.TryGetValue(k, out var v) ? v : 0) + 1;
            if (!EstNezabrannyyGruz()) Igra.privezeno.Clear();   // всё забрано — панель возвращается к загрузке
            Debug.Log($"[игра] из шаттла забрано: {ImyaModulya(k)} из отсека {i}");
            Izmeneno();
            OtkrytPanelShattla();
        }

        private DropContext KontekstDropa()
        {
            var stock = new ModuleCounts();
            foreach (var kv in Igra.moduli) if (Enum.TryParse(kv.Key, out ModuleId id)) stock.Set(id, kv.Value);
            // gated_open = true с 08.09: ангар спецтехники сделал буровую головку (гейтовый тир)
            // обязательной валютой прокачки реголита и руды с первого уровня. Пока флаг стоял ложью,
            // DropRoller давал гейтовому тиру вес 0, головка не выпадала ни разу, и две строки ангара
            // показывали цену, которую невозможно собрать — кнопка не активировалась никогда.
            return new DropContext { pity = new ModuleCounts(), stock = stock, need = new ModuleCounts(), warehouse_avg_24h = stock, gated_open = true, arrival_no = Igra.prilyotov, rng = Rnd };
        }

        private void ObnovitShattl(double now)
        {
            var trip = Igra.shattl;
            if (trip == null) return;
            if (trip.state == ShuttleState.IN_TRANSIT && now >= PriletShattla(trip))
            {
                // Прилёт: модули из контейнеров — на склад модулей, новый заказ, шаттл заходит на посадку.
                // Домен вскрывает контейнеры только в состоянии ARRIVED (проверка 07.09: без этого модули не выдавались ни разу)
                Shuttle.SkipFlight(trip, now);
                var poluchen = new List<string>();
                Igra.privezeno.Clear();
                for (int i = 0; i < OTSEKOV_SHATTLA; i++) Igra.privezeno.Add("");
                for (int i = 0; i < trip.slots.Count; i++)
                {
                    var m = Shuttle.CollectContainer(trip, i);
                    // Модуль не идёт на склад сам: он ждёт в СВОЁМ контейнере, пока игрок не заберёт его тапом.
                    // Раньше рейс заканчивался строкой в консоли, и привезённое просто появлялось в цифрах.
                    if (!m.HasValue) continue;
                    string k = m.Value.ToString();
                    if (i < Igra.privezeno.Count) Igra.privezeno[i] = k;
                    poluchen.Add(Modules.MODULES[m.Value].name);
                }
                Igra.prilyotov++;
                Igra.shattl = NovyyReys(trip);
                Debug.Log($"[игра] шаттл прилетел (№{Igra.prilyotov}), контейнеры: {string.Join(", ", poluchen)}");
                if (_polyot != null) _polyot.PriletetSnova();
                Izmeneno();
            }
        }

        /// <summary>Домен считает прилёт без ускорения (arrives_at = departed_at + trip_min·60); ускорение
        /// проверки применяется здесь, чтобы числа каркаса в домене остались нетронутыми.</summary>
        /// <summary>И-6: цена скипа по остатку рейса в минутах каркаса (остаток реального времени делится на time_scale).</summary>
        private int CenaSkipaShattla(ShuttleTrip trip, double ostalosSek) =>
            ostalosSek <= 0 ? 0 : Economy.ShuttleSkipPrice(ostalosSek / TuningIgry.time_scale / 60.0, trip.trip_min, trip.slots.Count);

        public void UskoritShattl()
        {
            var trip = Igra.shattl;
            if (trip == null || trip.state != ShuttleState.IN_TRANSIT) return;
            int cena = CenaSkipaShattla(trip, PriletShattla(trip) - Now);
            if (!SpisatIzotopy(cena, "скип рейса шаттла")) { Debug.Log($"[игра] ускорение шаттла: нужно {cena} изотопов, есть {Igra.izotopy}"); return; }
            Debug.Log($"[игра] ускорение шаттла за {cena} изотопов — прилёт на следующем кадре");
            trip.departed_at = Now - trip.trip_min * 60.0 * TuningIgry.time_scale - 0.01;   // прилёт наступает на следующем Update
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        private double PriletShattla(ShuttleTrip trip) =>
            trip.departed_at + trip.trip_min * 60.0 * TuningIgry.time_scale * Uluchsheniya.MnozhitelVremeni(UrovenTehniki(Uluchsheniya.SHATTL));

        private void OtkrytPanelShattla()
        {
            double now = Now;
            var trip = Igra.shattl;
            var m = new PanelShattla.Model();
            bool naMeste = _polyot == null || _polyot.NaPloshchadke;
            if (trip.state == ShuttleState.IN_TRANSIT)
            {
                // Шаттла на площадке нет — большое окно не открываем (Khan 06.09: «только когда есть шаттл»).
                // Маленькая карточка: сколько осталось и ускорение за изотопы.
                int ostalos = Math.Max(0, (int)Math.Ceiling(PriletShattla(trip) - now));
                int cena = CenaSkipaShattla(trip, ostalos);
                _panelSh.Skryt();
                _panel.Pokazat("Шаттл в рейсе", new List<PanelZdaniya.Stroka> { new PanelZdaniya.Stroka {
                    ikonka = _el?.Element("znachok-shattl"), imya = "Орбитальная станция", podpis = "Вернётся через " + Vremya(ostalos) + " · ускорить за " + cena + " изотопов (есть " + Igra.izotopy + ")",
                    knopka = cena == 0 ? "ПРИЛЁТ" : "⚡ " + cena, knopkaAktivna = Igra.izotopy >= cena, deystvie = UskoritShattl } });
                return;
            }
            if (EstNezabrannyyGruz())
            {
                // Рейс вернулся: в контейнерах лежит привезённое, и панель работает на выгрузку.
                // Загрузка следующего рейса откроется, когда игрок вынет последний модуль.
                m.vygruzka = true;
                m.zagolovok = "Шаттл вернулся · рейс " + Igra.prilyotov;
                // Отсеков всегда три: забранный остаётся открытым и пустым. Если убирать ячейку совсем,
                // створка закрывалась и шаттл на глазах менял вид посреди разгрузки (кадр 08.09).
                for (int i = 0; i < OTSEKOV_SHATTLA; i++)
                {
                    string k = i < Igra.privezeno.Count ? Igra.privezeno[i] : null;
                    bool estGruz = !string.IsNullOrEmpty(k); int idx = i;
                    m.konteynery.Add(new PanelShattla.Konteyner
                    {
                        ikonka = estGruz ? Resources.Load<Sprite>("UI/Ikonki/moduli/" + k) : null,
                        imya = estGruz ? ImyaModulya(k) : "",
                        est = estGruz ? 1 : 0, nuzhno = estGruz ? 1 : 0, zagruzheno = 0,
                        zagruzit = estGruz ? (Action)(() => ZabratGruz(idx)) : null,
                    });
                }
                m.nagradaPodpis = "НАГРАДА ЗА РЕЙС";           // подпись над контейнерами с привезённым
                m.nagrada = "Тап по контейнеру — забрать груз";
                if (!naMeste) m.podskazka = "Шаттл ещё не сел — разгрузка после посадки";
                _panelSh.Pokazat(m);
                return;
            }
            int xpVsego = 0;
            foreach (var s in trip.slots)
            {
                if (m.konteynery.Count >= OTSEKOV_SHATTLA) break;
                var good = Goods.Of(s.good_id);
                int est = Warehouse.AvailableOf(State.warehouse, s.good_id);
                bool polon = s.qty_filled >= s.qty_required;
                int idx = s.idx; xpVsego += Shuttle.SlotXp(s);
                m.konteynery.Add(new PanelShattla.Konteyner
                {
                    ikonka = _panelSh.Ikonka(s.good_id), imya = good.name, est = est, nuzhno = s.qty_required, zagruzheno = s.qty_filled, xp = Shuttle.SlotXp(s),
                    // Тап по отсеку открывает карточку (эталон Township), а не грузит сразу: игрок видит
                    // награду и счёт «есть/нужно». Цена в изотопах из отсека убрана совсем — нехватка
                    // разбирается в общем окне после нажатия «ЗАГРУЗИТЬ» (Khan 08.09).
                    zagruzit = (!polon && naMeste) ? (Action)(() => OtkrytKartochkuOtseka(idx)) : null,
                });
            }
            // Топливо взлёта — иконками ресурсов: метан и баллон кислорода со склада, молния энергии из HUD
            // (Khan 08.09: «должны стоять иконки ресурсов, а не просто надписи»).
            bool toplivoEst = ToplivoEst(out int metanEst, out int kislorodEst, out double energiyaEst);
            ToplivoNuzhno(out int nMetan, out int nKislorod, out double nEnergiya);
            if (nMetan > 0) m.toplivo.Add(new PanelShattla.Model.Toplivo { ikonka = _panelSh.Ikonka(Uluchsheniya.TOPLIVO_METAN), tekst = metanEst + "/" + nMetan, hvatit = metanEst >= nMetan });
            if (nKislorod > 0) m.toplivo.Add(new PanelShattla.Model.Toplivo { ikonka = _panelSh.Ikonka(Uluchsheniya.TOPLIVO_KISLOROD), tekst = kislorodEst + "/" + nKislorod, hvatit = kislorodEst >= nKislorod });
            if (nEnergiya > 0) m.toplivo.Add(new PanelShattla.Model.Toplivo { ikonka = Resources.Load<Sprite>("UI/Ikonki/znachok-uskorit"), tekst = energiyaEst.ToString("0") + "/" + nEnergiya.ToString("0"), hvatit = energiyaEst >= nEnergiya });
            m.toplivoEst = toplivoEst;
            m.zagolovok = naMeste ? "Грузовой шаттл · рейс " + (Igra.prilyotov + 1) : "Шаттл заходит на посадку";   // слово «заказ» — только у дронов (ТЗ-поправки п.1)
            m.nagradaXp = xpVsego;                                  // число со звёздочкой встаёт под заголовком
            m.nagrada = "Тап по контейнеру — карточка груза";        // кнопки «Загрузить» в отсеке больше нет
            if (!naMeste) m.podskazka = "Шаттл ещё не сел — загрузка после посадки";
            _panelSh.Pokazat(m);
        }

        /// <summary>Карточка отсека: награда, счёт «есть/нужно» и одна кнопка «ЗАГРУЗИТЬ» (эталон Township).</summary>
        private void OtkrytKartochkuOtseka(int idx)
        {
            var trip = Igra.shattl;
            var slot = trip?.slots.Find(x => x.idx == idx);
            if (slot == null || _kartochka == null) return;
            int est = Warehouse.AvailableOf(State.warehouse, slot.good_id);
            int nuzhno = Math.Max(0, slot.qty_required - slot.qty_filled);
            _kartochka.Pokazat(new KartochkaOtseka.Model
            {
                ikonka = _panelSh.Ikonka(slot.good_id), imya = Goods.Of(slot.good_id).name,
                est = est, nuzhno = nuzhno, xp = Shuttle.SlotXp(slot),
                zagruzit = () => { if (Warehouse.AvailableOf(State.warehouse, slot.good_id) >= nuzhno) Zagruzit(idx); else NehvatkaOtseka(idx); },
            });
        }

        /// <summary>Нехватка под погрузку отсека: показать, сколько не достаёт, и предложить докупку за изотопы.</summary>
        private void NehvatkaOtseka(int idx)
        {
            var trip = Igra.shattl;
            var slot = trip?.slots.Find(x => x.idx == idx);
            if (slot == null) return;
            int est = Warehouse.AvailableOf(State.warehouse, slot.good_id);
            int nehvataet = Math.Max(0, slot.qty_required - slot.qty_filled - est);
            int cena = Shuttle.SlotBuyoutPrice(slot);
            PokazatNehvatku(_panelSh.Ikonka(slot.good_id), Goods.Of(slot.good_id).name, nehvataet, cena, () => DokupitOtsek(idx));
        }

        /// <summary>
        /// Общее окно нехватки (спека Khan 08.09). Один вход для всех действий, которым не хватило
        /// ресурса: погрузка в шаттл, запуск рецепта, заказ дрона, улучшение техники. Цену считает
        /// домен (`RushCost.BuyoutPrice`), покупку выполняет переданное действие.
        /// </summary>
        public void PokazatNehvatku(Sprite ikonka, string imya, int nehvataet, int cena, Action kupit)
        {
            if (_oknoNehvatki == null) _oknoNehvatki = OknoNehvatki.Obespechit();
            if (_oknoNehvatki == null) { Debug.Log($"[игра] не хватает: {imya} x{nehvataet}"); return; }
            _oknoNehvatki.Pokazat(new OknoNehvatki.Model
            {
                ikonka = ikonka, imya = imya, nehvataet = nehvataet,
                cena = cena, hvataetValyuty = Igra.izotopy >= cena, kupit = kupit,
            });
        }

        /// <summary>Нехватка товара: цена докупки из домена, покупка кладёт товар на склад.</summary>
        private void NehvatkaTovara(string goodId, int nehvataet, Action posle = null)
        {
            if (nehvataet <= 0) return;
            int cena = RushCost.BuyoutPrice(goodId, nehvataet, State.warehouse);
            PokazatNehvatku(_panel.Ikonka(goodId), Goods.Of(goodId).name, nehvataet, cena, () => { KupitTovar(goodId, nehvataet, cena); posle?.Invoke(); });
        }

        /// <summary>Нехватка того, что за изотопы не купить (кредиты, модули): окно объясняет, кнопки нет.</summary>
        private void NehvatkaBezPokupki(Sprite ikonka, string imya, int skolko, string tekst)
        {
            if (_oknoNehvatki == null) _oknoNehvatki = OknoNehvatki.Obespechit();
            if (_oknoNehvatki == null) { Debug.Log($"[игра] не хватает: {imya} x{skolko}"); return; }
            _oknoNehvatki.Pokazat(new OknoNehvatki.Model { ikonka = ikonka, imya = imya, nehvataet = skolko, tekst = tekst, cena = 0 });
        }

        /// <summary>Докупка товара за изотопы. Полный склад откатывает покупку: иначе изотопы сгорают впустую.</summary>
        public void KupitTovar(string goodId, int qty, int cena)
        {
            if (qty <= 0) return;
            if (!SpisatIzotopy(cena, "докупка " + goodId)) return;
            if (!Warehouse.Deposit(State.warehouse, goodId, qty))
            {
                Igra.izotopy += cena;   // склад полон — возвращаем изотопы, покупка не состоялась
                Debug.Log($"[игра] докупка {goodId} x{qty}: склад полон, изотопы возвращены");
                NehvatkaBezPokupki(_panel.Ikonka(goodId), Goods.Of(goodId).name, qty, "Склад полон — освободите место, чтобы докупить.");
                return;
            }
            Debug.Log($"[игра] докуплено за изотопы: {goodId} x{qty} за {cena}");
            SkladPopolnen(Now); Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        /// <summary>Первый недостающий вход рецепта: что и сколько не хватает для запуска.</summary>
        private bool PervyyDefitsitRetsepta(string goodId, out string vhod, out int nehvataet)
        {
            vhod = null; nehvataet = 0;
            var good = Goods.Of(goodId);
            if (good == null || good.inputs == null) return false;
            foreach (var i in good.inputs)
            {
                int est = Warehouse.AvailableOf(State.warehouse, i.good_id);
                if (est < i.qty) { vhod = i.good_id; nehvataet = i.qty - est; return true; }
            }
            return false;
        }

        /// <summary>И-12: докупить остаток отсека за изотопы (ТЗ шаттла 6.2). Цену считает домен, списание здесь.</summary>
        public void DokupitOtsek(int idx)
        {
            var trip = Igra.shattl;
            if (trip == null || trip.state != ShuttleState.ORDER || idx < 0 || idx >= trip.slots.Count) return;
            int cena = Shuttle.SlotBuyoutPrice(trip.slots[idx]);
            if (cena <= 0 || !SpisatIzotopy(cena, "докупка отсека " + idx)) return;
            var res = Shuttle.BuyoutSlot(trip, idx, State.warehouse, Now, KontekstDropa(), _defitsit);
            Debug.Log($"[игра] шаттл: отсек {idx} докуплен за {cena} изотопов (+{res.loaded}){(res.departed ? " — все отсеки полны, отправление" : "")}");
            if (res.departed) OtpravitEsliEstToplivo(trip);
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        /// <summary>
        /// Домен отправляет рейс сам, как только полон последний отсек. Топливо — правило игры поверх этого:
        /// без метана, кислорода и энергии шаттл остаётся на площадке с полным грузом и ждёт заправки
        /// (решение Khan 08.09). Откат состояния в ORDER — единственный способ удержать его, не трогая домен.
        /// </summary>
        private void OtpravitEsliEstToplivo(ShuttleTrip trip)
        {
            if (trip == null) return;
            if (!SpisatToplivo())
            {
                // arrives_at домен успел посчитать от несостоявшегося вылета: оставить его — значит, что
                // после заправки рейс окажется «уже прибывшим» для доменных RefreshTrip/SkipPrice.
                trip.state = ShuttleState.ORDER; trip.departed_at = 0; trip.arrives_at = 0;
                ToplivoEst(out int m, out int k, out double e);
                ToplivoNuzhno(out int nm, out int nk, out double ne);
                Debug.Log($"[игра] шаттл заправлен не полностью и ждёт: метан {m}/{nm}, кислород {k}/{nk}, энергия {e:0}/{ne:0}");
                return;
            }
            trip.state = ShuttleState.IN_TRANSIT; trip.departed_at = Now; trip.arrives_at = Now + trip.trip_min * 60.0;
            int xp = Shuttle.TripXp(trip); State.xp += xp;
            if (_polyot != null) _polyot.ZagruzkaZavershena = true;
            Debug.Log($"[игра] шаттл улетел: +{xp} XP, прилёт через {Vremya(PriletShattla(trip) - Now)}");
        }

        /// <summary>Полностью загруженный рейс, ждущий топлива, стартует сам, как только топливо появилось.</summary>
        private void ProverkaVzlyota(ShuttleTrip trip)
        {
            if (trip == null || trip.state != ShuttleState.ORDER) return;
            foreach (var sl in trip.slots) if (sl.qty_filled < sl.qty_required) return;
            if (!ToplivoEst(out _, out _, out _)) return;
            OtpravitEsliEstToplivo(trip);
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        public void Zagruzit(int idx)
        {
            var trip = Igra.shattl;
            var res = Shuttle.LoadSlot(trip, idx, State.warehouse, Now, KontekstDropa(), _defitsit);
            if (!res.ok) { Debug.Log("[игра] шаттл: отсек " + idx + " не загружен"); return; }
            Schitat("zagruzok");
            Debug.Log($"[игра] шаттл: отсек {idx} +{res.loaded} {trip.slots[idx].good_id}{(res.departed ? " — все отсеки полны, отправление" : "")}");
            if (res.departed) OtpravitEsliEstToplivo(trip);
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        // ------------------------------------------------------------ дроны

        private OrderSlot NovyyZakaz(int idx)
        {
            var ctx = new GeneratorContext { level = UrovenZakazov, warehouse = State.warehouse, available_goods = DostupnyeTovary(), board = Igra.drony, rng = Rnd, deficit_locks = _defitsit, now = Now };
            return Drone.GenerateOrder(idx, ctx);
        }

        private void ObnovitDrony(double now)
        {
            for (int i = 0; i < Igra.drony.Count; i++)
            {
                var z = Igra.drony[i];
                if (z.state == OrderSlotState.empty_cooldown && now >= z.refresh_at) { Igra.drony[i] = NovyyZakaz(i); Izmeneno(); }
                // Самолечение старых сейвов: заказ, залипший в in_progress с пустыми позициями,
                // нельзя было ни отправить, ни обновить (найдено 08.09, домен чинится CancelLoading).
                else if (z.state == OrderSlotState.in_progress)
                {
                    bool pusto = true; foreach (var p in z.positions) if (p.qty_filled > 0) { pusto = false; break; }
                    if (pusto) { z.state = OrderSlotState.active; Debug.Log($"[игра] заказ {i} расклинен: in_progress без загрузки → active"); Izmeneno(); }
                }
            }
        }

        private void PanelDronov(string dron)
        {
            double now = Now;
            var stroki = new List<PanelZdaniya.Stroka>();
            // Khan 08.09: как вертолёт в Township — отправка не ждёт полёта. Награда сразу, полёт встаёт в очередь,
            // её разбирают оба дрона по мере освобождения. Кнопка «В ПУТИ» и блокировка ушли.
            bool svoboden = true;
            for (int i = 0; i < Igra.drony.Count; i++)
            {
                var z = Igra.drony[i]; int idx = i;
                if (z.state == OrderSlotState.empty_cooldown)
                {
                    int ostalos = Math.Max(0, (int)Math.Ceiling(z.refresh_at - now));
                    int cenaRefresha = Economy.DroneRefreshPrice(ostalos / TuningIgry.time_scale);
                    stroki.Add(new PanelZdaniya.Stroka { imya = "Заказ снят", podpis = "Новый через " + Vremya(ostalos) + " · сразу за " + cenaRefresha + " изотопов (есть " + Igra.izotopy + ")", knopka = "ОБНОВИТЬ", knopkaAktivna = Igra.izotopy >= cenaRefresha, deystvie = () => ObnovitZakaz(idx) });
                    continue;
                }
                var chasti = new List<string>(); bool vse = true;
                foreach (var p in z.positions) { int est = Warehouse.AvailableOf(State.warehouse, p.good_id); chasti.Add(p.qty + "×" + Goods.Of(p.good_id).name + (est >= p.qty ? "" : " (есть " + est + ")")); if (est < p.qty) vse = false; }
                bool mozhno = vse && svoboden && z.state != OrderSlotState.in_progress;
                stroki.Add(new PanelZdaniya.Stroka
                {
                    ikonka = z.positions.Count > 0 ? _panel.Ikonka(z.positions[0].good_id) : null,
                    imya = z.npc_name, podpis = string.Join(", ", chasti) + " → " + z.credits_reward + " кр, +" + z.xp_reward + " XP",
                    knopka = mozhno ? "ОТПРАВИТЬ" : (svoboden ? "ВЫБРОСИТЬ" : "В ПУТИ"), knopkaAktivna = svoboden,
                    deystvie = mozhno ? (Action)(() => OtpravitDrona(dron, idx)) : () => VybrositZakaz(idx),
                });
            }
            if (_ocheredPolyotov.Count > 0) stroki.Add(new PanelZdaniya.Stroka { imya = "Рейсов в очереди: " + _ocheredPolyotov.Count, podpis = "Дроны развозят заказы по очереди, награда уже начислена" });
            _panel.Pokazat("Дроны-курьеры · заказы колонистов", stroki);
        }

        public void OtpravitDrona(string dron, int idx)
        {
            var z = Igra.drony[idx];
            if (z.state != OrderSlotState.active) { Debug.Log("[игра] дрон: заказ " + idx + " не активен (" + z.state + ")"); return; }
            for (int i = 0; i < z.positions.Count; i++) Drone.LoadPosition(z, i, State.warehouse);
            var res = Drone.SendOrder(z, State.warehouse);
            if (!res.ok)
            {
                Debug.Log("[игра] дрон: заказ " + idx + " не собран");
                Drone.CancelLoading(z, State.warehouse);
                if (_vybrano != null) PokazatPanel(_vybrano);
                // Чего именно не хватило — окном, а не молчанием: докупка закрывает позицию и шлёт заказ снова
                for (int i = 0; i < z.positions.Count; i++)
                {
                    var poz = z.positions[i]; int nedost = poz.qty - poz.qty_filled;
                    if (nedost <= 0) continue;
                    int cenaPoz = Drone.PositionBuyoutPrice(poz); int nomer = i;
                    PokazatNehvatku(_panel.Ikonka(poz.good_id), Goods.Of(poz.good_id).name, nedost, cenaPoz, () =>
                    {
                        if (!SpisatIzotopy(cenaPoz, "докупка позиции заказа")) return;
                        Drone.BuyoutPosition(z, nomer);
                        Debug.Log($"[игра] дрон: позиция {poz.good_id} докуплена за {cenaPoz} изотопов");
                        Izmeneno(); OtpravitDrona(dron, idx);
                    });
                    break;
                }
                return;
            }
            Drone.ReleaseOrderDeficitLocks(z, _defitsit);   // И-13: заказ отправлен — его замки дефицита больше не держат товар
            var cel = _zhilye.Count > 0 ? _zhilye[_rnd.Next(_zhilye.Count)] : null;
            Vector3 celMir = cel != null ? cel.ClickBounds.center + Vector3.up * cel.ClickBounds.extents.y : transform.position;
            int kredity = res.credits, xp = res.xp;
            string npc = z.npc_name;
            Igra.drony[idx] = new OrderSlot { idx = idx, state = OrderSlotState.empty_cooldown, refresh_at = Now + 1, npc_name = npc };   // слот освободится сразуата дрона
            State.credits += kredity; State.xp += xp;   // награда сразу, как у вертолёта в Township; полёт — только картинка
            _ocheredPolyotov.Enqueue(new Polyot { cel = celMir, celImya = cel != null ? cel.gameObject.name : "?", npc = npc, dron = dron });
            ZapustitOcheredPolyotov();
            bool poletel = true;
            Schitat("dronov");
            Debug.Log($"[игра] дрон {dron}: заказ {npc} отправлен ({(poletel ? "летит" : "без визуала")}), награда {kredity} кр / {xp} XP");
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        // ------------------------------------------------------------ очередь полётов дронов

        private struct Polyot { public Vector3 cel; public string celImya, npc, dron; }
        private readonly Queue<Polyot> _ocheredPolyotov = new Queue<Polyot>();

        /// <summary>Раздаёт очередь полётов свободным дронам: сначала тому, с чьей площадки отправили, иначе любому.</summary>
        private void ZapustitOcheredPolyotov()
        {
            var kurery = MarshrutyDronov.Ekz;
            if (kurery == null) { _ocheredPolyotov.Clear(); return; }
            while (_ocheredPolyotov.Count > 0)
            {
                var p = _ocheredPolyotov.Peek();
                string svobodnyy = null;
                if (kurery.Svoboden(p.dron)) svobodnyy = p.dron;
                else foreach (var d in DRONY.Values) if (kurery.Svoboden(d)) { svobodnyy = d; break; }
                if (svobodnyy == null) return;
                _ocheredPolyotov.Dequeue();
                string celImya = p.celImya, npc = p.npc, dron = svobodnyy;
                bool poletel = kurery.Poletet(dron, p.cel,
                    () => Debug.Log($"[игра] {dron} сбросил груз у {celImya} для {npc}"),
                    () => { Debug.Log($"[игра] {dron} вернулся"); ZapustitOcheredPolyotov(); if (_vybrano != null && _panel.Otkryta) PokazatPanel(_vybrano); });
                if (!poletel) Debug.Log($"[игра] {dron}: полёт не запустился, заказ {npc} без визуала");
            }
        }

        /// <summary>Мгновенный рефреш пустого слота за изотопы по лестнице ТЗ 3.3.</summary>
        public void ObnovitZakaz(int idx)
        {
            var z = Igra.drony[idx];
            if (z.state != OrderSlotState.empty_cooldown) return;
            int cena = Economy.DroneRefreshPrice((z.refresh_at - Now) / TuningIgry.time_scale);
            if (!SpisatIzotopy(cena, "рефреш заказа " + idx)) return;
            Igra.drony[idx] = NovyyZakaz(idx);
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        public void VybrositZakaz(int idx)
        {
            var z = Igra.drony[idx];
            Drone.ReleaseReserved(z, State.warehouse);
            Drone.ReleaseOrderDeficitLocks(z, _defitsit);   // И-13: замки дефицита снимаются на терминальном событии (тестер 07.09: не снимались никогда)
            Drone.DiscardOrder(z, Now);
            Debug.Log($"[игра] дрон: заказ {z.npc_name} выброшен, слот пуст {Economy.DRONE_REFRESH_FREE_SEC / 60} мин");
            Izmeneno();
            if (_vybrano != null) PokazatPanel(_vybrano);
        }

        // ------------------------------------------------------------ склад

        public int Prodat(string goodId, int n)
        {
            var res = Production.Sell(goodId, Mathf.Clamp(n, 0, Warehouse.AvailableOf(State.warehouse, goodId)), Kontekst(Now));
            State.Apply(res);
            if (res.ok) Izmeneno();
            return res.ok ? res.credits_delta : 0;
        }

        // ------------------------------------------------------------ служебное

        private double Dlit(int prodTimeSec) => prodTimeSec * TuningIgry.time_scale;

        // ------------------------------------------------------------ ускорения (спека п.8, И-5/И-6, ТЗ 3.3)

        /// <summary>И-5: цена по остатку в секундах каркаса (реальный остаток делится на time_scale); ≤ 30 с — бесплатно.</summary>
        private int CenaUskoreniya(double ostalosRealSek, GoodKind kind) =>
            Economy.ProductionSpeedupCost(Math.Max(0, ostalosRealSek) / TuningIgry.time_scale, kind);

        private PanelZdaniya.Stroka StrokaUskoreniya(int cena, Action deystvie) => new PanelZdaniya.Stroka
        {
            imya = "Ускорить",
            podpis = cena == 0 ? "Бесплатно: осталось меньше 30 с" : cena + " изотопов · у вас " + Igra.izotopy,
            knopka = cena == 0 ? "УСКОРИТЬ" : "ЗА " + cena,
            knopkaAktivna = Igra.izotopy >= cena,
            deystvie = deystvie,
        };

        private bool SpisatIzotopy(int cena, string zaChto)
        {
            if (Igra.izotopy < cena) { Debug.Log($"[игра] ускорение ({zaChto}): не хватает изотопов {Igra.izotopy}/{cena}"); return false; }
            Igra.izotopy -= cena;
            Debug.Log($"[игра] ускорение ({zaChto}): -{cena} изотопов, осталось {Igra.izotopy}");
            return true;
        }

        public static string Vremya(double sek)
        {
            int s = (int)Math.Ceiling(sek);
            if (s >= 3600) return (s / 3600) + " ч " + (s % 3600 / 60) + " мин";
            if (s >= 60) return (s / 60) + ":" + (s % 60).ToString("00");
            return s + " с";
        }

        /// <summary>Иконка культуры над теплицей: тускло — растёт (с остатком времени), ярко и дышит — готово.</summary>
    }

    /// <summary>Сохранение: колония — плоским снимком, остальное — Newtonsoft (доменные классы с публичными полями).</summary>
    public static class SohranenieIgry
    {
        private sealed class Kletka { public string id; public int qty; public int reserved; }
        private sealed class Gryadka { public int idx; public int state; public string good; public double ends_at; }
        private sealed class TeplitsaSn { public int uroven; public List<int> gorshki; }
        private sealed class Snimok
        {
            public int credits, xp, level, capacity, izotopy, prilyotov;
            public double energiya, energiyaObnovlena;   // с 08.09; в старых сейвах нули — начисление начнётся с загрузки
            public Dictionary<string, int> urovni;        // с 08.09; в старых сейвах null — все на первом уровне
            public bool startVydan;   // стартовые изотопы уже выданы (сохранения до 06.09 их не знали)
            public List<Kletka> sklad = new List<Kletka>();
            public List<Gryadka> gryadki = new List<Gryadka>();
            public List<TeplitsaSn> teplitsy;   // до 06.09 — нет: каждая грядка была теплицей
            public Dictionary<string, List<FactorySlot>> fabriki;
            public Dictionary<string, MiningSite> dobycha;
            public ShuttleTrip shattl;
            public Plavilnya plavilnya;
            public List<OrderSlot> drony;
            public Dictionary<string, int> moduli;
            public List<string> privezeno;   // с 08.09; в старых сейвах null — контейнеры пусты
            public Dictionary<string, int> schetchiki;
        }

        private static readonly JsonSerializerSettings Nastroyki = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore, NullValueHandling = NullValueHandling.Include };

        public static void Sohranit(string klyuch, SostoyanieIgry igra)
        {
            var s = igra.kolonia;
            var sn = new Snimok { credits = s.credits, xp = s.xp, level = s.level, capacity = s.warehouse.capacity, izotopy = igra.izotopy, prilyotov = igra.prilyotov, startVydan = true,
                energiya = igra.energiya, energiyaObnovlena = igra.energiyaObnovlena, urovni = igra.urovni,
                fabriki = igra.fabriki, dobycha = igra.dobycha, shattl = igra.shattl, drony = igra.drony, moduli = igra.moduli, privezeno = igra.privezeno, schetchiki = igra.schetchiki, plavilnya = igra.plavilnya };
            foreach (var kv in s.warehouse.cells) sn.sklad.Add(new Kletka { id = kv.Key, qty = kv.Value.qty, reserved = kv.Value.reserved });
            foreach (var f in s.fields) sn.gryadki.Add(new Gryadka { idx = f.idx, state = (int)f.state, good = f.good_id, ends_at = f.ends_at });
            sn.teplitsy = new List<TeplitsaSn>(); foreach (var t in s.teplitsy) sn.teplitsy.Add(new TeplitsaSn { uroven = t.uroven, gorshki = new List<int>(t.gorshki) });
            try { PlayerPrefs.SetString(klyuch, JsonConvert.SerializeObject(sn, Nastroyki)); PlayerPrefs.Save(); }
            catch (Exception e) { Debug.LogWarning("[игра] сохранение не записалось: " + e.Message); }
        }

        /// <summary>Стартовый сейв, зашитый в сборку (`Resources/sohranenie-po-umolchaniyu.json`).
        /// Нужен, чтобы на чужой машине игра открывалась подготовленной колонией, а не пустой.
        /// Второй и последующие запуски идут уже с прогресса игрока из PlayerPrefs.</summary>
        public static SostoyanieIgry ZagruzitPoUmolchaniyu()
        {
            var ta = Resources.Load<TextAsset>("sohranenie-po-umolchaniyu");
            if (ta == null || string.IsNullOrWhiteSpace(ta.text)) return null;
            var res = IzJsona(ta.text);
            Debug.Log(res != null ? "[игра] поднят стартовый сейв из сборки" : "[игра] стартовый сейв не прочитался");
            return res;
        }

        public static SostoyanieIgry Zagruzit(string klyuch)
        {
            if (!PlayerPrefs.HasKey(klyuch)) return null;
            return IzJsona(PlayerPrefs.GetString(klyuch));
        }

        private static SostoyanieIgry IzJsona(string json)
        {
            try
            {
                var sn = JsonConvert.DeserializeObject<Snimok>(json, Nastroyki);
                var s = ColonyState.CreateNew();
                s.credits = sn.credits; s.xp = sn.xp; s.level = sn.level;
                s.warehouse = Warehouse.Create(sn.capacity > 0 ? sn.capacity : s.warehouse.capacity);
                foreach (var k in sn.sklad) { if (k.qty > 0) Warehouse.Deposit(s.warehouse, k.id, k.qty); if (k.reserved > 0) Warehouse.Reserve(s.warehouse, k.id, k.reserved); }
                s.fields.Clear();
                foreach (var g in sn.gryadki) s.fields.Add(new FieldSlot { idx = g.idx, state = (FieldState)g.state, good_id = string.IsNullOrEmpty(g.good) ? null : g.good, ends_at = g.ends_at });
                s.teplitsy.Clear();
                if (sn.teplitsy != null) foreach (var t in sn.teplitsy) s.teplitsy.Add(new Teplitsa { uroven = Math.Max(1, t.uroven), gorshki = t.gorshki ?? new List<int>() });
                else foreach (var f in new List<FieldSlot>(s.fields)) { var t = new Teplitsa(); t.gorshki.Add(f.idx); s.teplitsy.Add(t); while (t.gorshki.Count < Teplitsa.GORSHKOV_START) s.DobavitGorshok(t); }   // миграция: грядка → теплица с тремя горшками
                return new SostoyanieIgry
                {
                    kolonia = s, izotopy = sn.startVydan ? sn.izotopy : sn.izotopy + IgraKolonii.STARTOVYE_IZOTOPY, prilyotov = sn.prilyotov,
                    energiya = sn.energiya, energiyaObnovlena = sn.energiyaObnovlena, urovni = sn.urovni ?? new Dictionary<string, int>(),
                    fabriki = sn.fabriki ?? new Dictionary<string, List<FactorySlot>>(), dobycha = sn.dobycha ?? new Dictionary<string, MiningSite>(),
                    shattl = sn.shattl, drony = sn.drony ?? new List<OrderSlot>(), moduli = sn.moduli ?? new Dictionary<string, int>(), privezeno = sn.privezeno ?? new List<string>(), schetchiki = sn.schetchiki ?? new Dictionary<string, int>(), plavilnya = sn.plavilnya ?? new Plavilnya(),
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[игра] сохранение не прочиталось, начинаем заново: " + e.Message);
                return null;
            }
        }

        public static void Sbrosit(string klyuch) => PlayerPrefs.DeleteKey(klyuch);
    }
}
