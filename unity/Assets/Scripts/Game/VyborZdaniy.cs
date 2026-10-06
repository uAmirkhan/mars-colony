using System.Collections.Generic;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Выбор здания кликом: единственный обработчик ввода сцены-витрины MAIN.
    ///
    /// ПОЧЕМУ НЕ ЧЕРЕЗ ColonyGame. `ColonyGame` уже умеет собирать
    /// `ClickTarget` и бить лучом — тот же прием переиспользован здесь
    /// (`ClickTarget.Popadanie`/`ClickBounds`, ничего заново не считается).
    /// Но сам `ColonyGame` тянет за собой доменную экономику: на `Awake` он
    /// создает `ColonyState.CreateNew()` и каждый кадр кормит ею
    /// `ZhivoyInterfeys.Obnovit(...)` — а тот пишет живые числа поверх
    /// карточки цели и счетчиков, которые в MAIN сейчас статичны и приняты
    /// предыдущими витками именно такими. Поставить `ColonyGame` в эту сцену
    /// значит с первого кадра Play переписать чужой принятый экран данными
    /// из системы, которую этот виток не трогает (рубеж 2 еще не начат).
    /// Поэтому здесь отдельный, узкий обработчик: только клик по зданию,
    /// только подсветка и подпись, ничего из домена.
    ///
    /// Единственная в сцене копия — ставится сборщиком интерфейса
    /// (`InterfeysBuilder`) один раз, как и остальной рантайм холста.
    ///
    /// ПОЧЕМУ ОХРАНА СКЛАДА ЧИТАЕТ КЭШ, А НЕ ЖИВОЙ `activeSelf`. Найдено
    /// вживую на витке "ночь-2": один физический клик по затемнению склада
    /// ЗАКРЫВАЛ склад (`EventSystem` → `Button.onClick` → `EkranSklada.
    /// Zakryt()`) И одновременно выбирал здание под ним — тот же клик, тот
    /// же кадр (см. диагностику `[diag-klik]` в отчете витка: `Zakryt` и
    /// `Vybrat` в один `Time.frameCount`). Первая попытка чинить это
    /// `[DefaultExecutionOrder(-100)]` не помогла: порядок `Update()` между
    /// этим классом и `EventSystem` внутри ОДНОГО кадра оказался не тем
    /// рычагом — обе системы читают один и тот же `Input.GetMouseButtonDown`
    /// в одном кадре, и то, что закрытие склада успевает произойти РАНЬШЕ
    /// проверки охраны, не лечится перестановкой Update() (эмпирически
    /// проверено, осталось воспроизводимым). Настоящий фикс — не читать
    /// состояние склада "прямо сейчас", а держать снимок с КОНЦА ПРОШЛОГО
    /// кадра (`_skladOtkrytProshlyKadr`, обновляется в `LateUpdate()`): клик,
    /// закрывающий склад в текущем кадре, не может задним числом изменить
    /// то, каким склад был на НАЧАЛО этого кадра, — тот самый момент, когда
    /// игрок реально произвёл тап.
    /// </summary>
    public sealed class VyborZdaniy : MonoBehaviour
    {
        private readonly List<ClickTarget> _tseli = new List<ClickTarget>();
        private IgraKolonii _igra;
        /// <summary>Цель, созданная после Awake (дроны получают цель кодом игры).</summary>
        public void Dobavit(ClickTarget t) { if (t != null && !_tseli.Contains(t)) _tseli.Add(t); }
        private Renderer[] _badges;
        private Camera _kamera;
        private ZhivoyInterfeys _zhivoy;

        private BuildingClickTarget _vybrano;

        /// <summary>Ночь 3: выбор здания — вход в игру. IgraKolonii подписывается и открывает панель действий.</summary>
        public static event System.Action<BuildingClickTarget> Vybrano;
        public static event System.Action Snyato;
        private KonturZdaniya _kontur;

        // Виток "ночь-2": экран склада блокирует тапы по миру, пока открыт
        // (спека раздел 3, состояние 7). Ищем компонент один раз, не по
        // имени каждый кадр — тот же принцип, что и с остальными ссылками
        // в этом классе.
        private MarsColony.Game.EkranSklada _sklad;

        /// <summary>
        /// Снимок "склад был открыт" с КОНЦА прошлого кадра — не живой
        /// `_sklad.gameObject.activeSelf`. См. докстринг класса про гонку
        /// закрывающего клика внутри одного кадра.
        /// </summary>
        private bool _skladOtkrytProshlyKadr;

        // Виток UI-10: маяк цели. Источник — `QuestMarker`, тот же, что и у
        // карточки цели снизу экрана (см. `InterfeysBuilder.ObespechitQuestMarker`).
        // `VyborZdaniy` уже держит камеру и холст ради карточки имени — тот же
        // кадр обновления несёт и маяк, вторая копия Update() не заводится.
        private MayakTseli _mayak;
        private QuestMarker _quest;
        private bool _mayakZalogirovan;
        private bool _mayakPokazanNaStarte;

        /// <summary>
        /// Насколько запас по X при поиске мешающих бейджей шире габарита
        /// здания — карточка еще не знает свою итоговую ширину (она зависит
        /// от текста, см. `ZhivoyInterfeys`), поэтому берем половину ее
        /// потолка с запасом.
        /// </summary>
        private const float ZAPAS_PO_X_PX = ZhivoyInterfeys.MAX_SHIRINA_KARTOCHKI * 0.5f;

        /// <summary>
        /// Отступ низа карточки от макушки силуэта, px. Приёмка попытки 2 задала
        /// вилку 8..16: ниже подпись липнет к зданию, выше — отрывается от него
        /// и читается отдельным элементом интерфейса, а не ярлыком объекта.
        /// </summary>
        // public, а не private: `InterfeysBuilder.KontaktVydeleniya` считает
        // ту же точку-якорь ВНЕ Play (там Update() этого класса не тикает) и
        // не должен держать вторую копию числа — редакторская сборка живёт в
        // другой сборке (Assembly-CSharp-Editor), `internal` через границу
        // сборок не видно без InternalsVisibleTo, поэтому public, как и у
        // MAX_SHIRINA_KARTOCHKI в ZhivoyInterfeys чуть выше по файлу.
        public const float OTSTUP_PX = 12f;

        /// <summary>Высота карточки, px. Нужна, чтобы знать, какие бейджи она накроет.</summary>
        private const float VYSOTA_KARTOCHKI_PX = 52f;

        /// <summary>Бейджи, погашенные под текущей карточкой. Возвращаются при снятии выбора.</summary>
        private readonly List<Renderer> _pogasheno = new List<Renderer>();

        private void Awake() => Sobrat();

        /// <summary>Сбор ссылок. Вызывается из Awake и повторно из Update, когда перезагрузка домена в Play обнулила приватные поля
        /// (без этого после любой пересборки кода клики по миру молча умирали — Khan 06.09).</summary>
        private void Sobrat()
        {
            _tseli.Clear();
            _kamera = Camera.main;
            _zhivoy = FindFirstObjectByType<ZhivoyInterfeys>();
            _tseli.AddRange(FindObjectsByType<ClickTarget>(FindObjectsSortMode.None));

            _mayak = FindFirstObjectByType<MayakTseli>(FindObjectsInactive.Include);   // маяк в холсте выключен до первого показа: без Include ссылка всегда null и маяк никогда не появляется сам
            _quest = FindFirstObjectByType<QuestMarker>();
            // Include: "ekran-sklada" стартует выключенным (см.
            // InterfeysBuilder.PostroitEkranSklada) — обычный
            // FindFirstObjectByType его на старте не увидит вовсе.
            _sklad = FindFirstObjectByType<MarsColony.Game.EkranSklada>(FindObjectsInactive.Include);

            // Желтые бейджи дефицита питания ("badge" — см. `Metki/metka-*`
            // в сцене) не дети здания, а отдельные объекты рядом с ним:
            // здание своим габаритом их не видит. Приемка попытки 1 поймала
            // карточку, вставшую поверх двух таких бейджей. Кэшируем список
            // один раз — сцена статична, бейджи никуда не переезжают.
            var vseRenderery = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var spisokBeydzhey = new List<Renderer>();
            foreach (var r in vseRenderery)
                if (r.gameObject.name == "badge")
                    spisokBeydzhey.Add(r);
            _badges = spisokBeydzhey.ToArray();
        }

        private bool _nazhatoVMire;

        private void Update()
        {
            if (_kamera == null || _zhivoy == null || _tseli.Count == 0) Sobrat();
            if (Input.GetMouseButtonUp(0) && UpravlenieKameroy.Peretaskivanie) _nazhatoVMire = false;
            // Виток "ночь-2", спека раздел 3 состояние 7: пока открыт экран
            // склада, тапы по миру не обрабатываются вовсе — ни выбор, ни
            // снятие. Склад сам решает, что делать со своими тапами (крест,
            // затемнение); этому классу они не должны быть видны.
            //
            // Гейт по СНИМКУ с конца прошлого кадра (`_skladOtkrytProshlyKadr`),
            // не по живому `_sklad.gameObject.activeSelf` — см. докстринг
            // класса про гонку закрывающего клика внутри одного кадра.
            // Ночь 3: клик по панели действий (UI) — не клик по миру. Иначе тап по кнопке «ПОСЕЯТЬ»
            // снимал бы выбор здания и закрывал саму панель.
            bool nadUI = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            if (Input.GetMouseButtonDown(0) && (nadUI || _skladOtkrytProshlyKadr))
            {
                // Khan 06.09: «пропадает возможность кликать по миру». Пишем, кто именно съел клик, чтобы поймать триггер.
                string kto = _skladOtkrytProshlyKadr ? "экран склада" : "UI";
                if (nadUI)
                {
                    var ped = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = Input.mousePosition };
                    var rez = new List<UnityEngine.EventSystems.RaycastResult>(); UnityEngine.EventSystems.EventSystem.current.RaycastAll(ped, rez);
                    if (rez.Count > 0) { var t = rez[0].gameObject.transform; kto = t.name; while (t.parent != null) { t = t.parent; kto = t.name + "/" + kto; } }
                }
                Debug.Log("[выбор] клик по миру не дошёл: перехватил " + kto);
            }
            // Камера теперь тянется мышью (Khan 08.09): выбор здания срабатывает на ОТПУСКАНИИ и только
            // если указатель почти не сдвинулся — иначе каждое перетаскивание открывало бы панель.
            if (!_skladOtkrytProshlyKadr && !nadUI && Input.GetMouseButtonDown(0)) _nazhatoVMire = true;
            if (Input.GetMouseButtonUp(0) && _nazhatoVMire && !UpravlenieKameroy.Peretaskivanie && !_skladOtkrytProshlyKadr)
            {
                _nazhatoVMire = false;
                ClickTarget popadanie = Podobrat(Input.mousePosition);
                if (popadanie is BuildingClickTarget zdanie)
                    Vybrat(zdanie);
                else
                    Snyat();
            }

            // Карточка следует за зданием каждый кадр, а не только в момент
            // клика: камера сцены статична на рубеже 1, но привязка к
            // компоненту, а не к разовому снимку координат, переживет ее
            // движение, когда оно появится на рубеже 4.
            // Позиция карточки имени — в LateUpdate: камера двигается в своём Update, и при обычном
            // Update карточка считалась по прошлому кадру и дёргалась вслед за камерой (Khan 08.09).

            ObnovitMayak();
        }

        /// <summary>
        /// Обновляет снимок состояния склада ПОСЛЕ всех Update() этого кадра
        /// (включая `EventSystem`, который мог его закрыть кликом по
        /// затемнению) — снимок используется только со СЛЕДУЮЩЕГО кадра.
        /// </summary>
        private void LateUpdate()
        {
            // Маяк цели при старте: раньше ObnovitMayak звался только из выбора здания,
            // и до первого тапа маяка над целью не было (судья ночи 2: −10 за это).
            if (!_mayakPokazanNaStarte && _quest != null && _quest.HasTarget && _kamera != null && _zhivoy != null && _zhivoy.Holst != null)
            { ObnovitMayak(); if (_mayak != null && _mayak.Vidim) _mayakPokazanNaStarte = true; }   // флаг только по факту показа, иначе один неудачный кадр на старте гасит попытки навсегда
            _skladOtkrytProshlyKadr = _sklad != null && _sklad.gameObject.activeSelf;
            // Имя выбранного здания — здесь же: камера двигается в своём Update, и при обычном
            // Update карточка считалась по прошлому кадру и дёргалась вслед за камерой (Khan 08.09).
            ImyaVybrannogo();
        }

        /// <summary>Карточка имени над выбранным зданием; зовётся из LateUpdate, после движения камеры.</summary>
        private void ImyaVybrannogo()
        {
            if (_vybrano == null || _zhivoy == null || _kamera == null) return;
            if (_igra == null) _igra = FindFirstObjectByType<IgraKolonii>();
            if (_igra != null && _igra.PanelOtkryta) { _zhivoy.SkrytImyaZdaniya(); return; }   // панель открыта — имя над зданием не нужно (Khan 06.09)
            Vector2 tochkaNadZdaniem = SchitatTochkuKartochki(_vybrano, _kamera);
            _zhivoy.PokazatImyaZdaniya(_vybrano.Label, tochkaNadZdaniem, _kamera);
            GasitBadzhiPod(tochkaNadZdaniem);
        }

        /// <summary>
        /// Ставит маяк цели над куполом, на который указывает `QuestMarker`,
        /// и гасит его на время выделения этого же здания циановым контуром.
        ///
        /// Приёмка попытки 1: отступ, заданный в метрах мира
        /// (`QuestMarker.marker_height`), на экране такого масштаба даёт
        /// почти ноль — маяк лёг на макушку купола. Починено переносом
        /// отступа целиком в экранные px холста (`MayakTseli.Pokazat`).
        ///
        /// Приёмка попытки 2 и 3: ни объединённый мировой `Bounds` ("верх
        /// центра" — занижал макушку на 37 px), ни объединение экранных bbox
        /// всех рендереров (задирало на 51 px) не совпали с силуэтом — у
        /// купола каркас из нескольких мешей, и никакой прямоугольный охват
        /// не равен самой верхней точке настоящей геометрии. Единственный
        /// источник, совпавший с кадром — максимум по экранной проекции всех
        /// вершин меша (замер y=518 против кадровых 512-520). Считает и
        /// кэширует его <see cref="QuestMarker.MakushkaMir"/> — здесь только
        /// проекция уже готовой мировой точки, вторая копия перебора вершин
        /// не заводится.
        /// </summary>
        private void ObnovitMayak()
        {
            if (_mayak == null || _quest == null || !_quest.HasTarget
                || _kamera == null || _zhivoy == null || _zhivoy.Holst == null)
                return;

            bool eto_tsel_vybrana = _vybrano != null && _vybrano.gameObject == _quest.target;
            _mayak.PriVydeleniiZdaniya(eto_tsel_vybrana);
            if (eto_tsel_vybrana)
                return;

            // Формула: маяк.xy = ЭкранВХолст(cam.WorldToScreenPoint(quest.MakushkaMir(cam))).
            Vector3 makushkaMir = _quest.MakushkaMir(_kamera);
            Vector2 makushkaEkran = _kamera.WorldToScreenPoint(makushkaMir);
            makushkaEkran.x = EkrannyyCentrX(_quest.target, _kamera);   // P11: пин по центру целевого купола, не по вершине на шве
            _mayak.Pokazat(_zhivoy.Holst, makushkaEkran, _kamera);

            // Приёмка числом (виток UI-10): логируем один раз, а не каждый
            // кадр — факт "маяк стоит над правильным куполом" не меняется
            // по 60 раз в секунду, и спам в консоль скрыл бы настоящую
            // ошибку, если она появится.
            if (!_mayakZalogirovan)
            {
                _mayakZalogirovan = true;
                Debug.Log($"[mayak] маяк цели над '{_quest.target.name}', "
                        + $"экранная точка ({makushkaEkran.x:0}, {makushkaEkran.y:0}), "
                        + $"активных маяков в кадре: {MayakTseli.AktivnyhVKadre()}");
            }
        }

        /// <summary>
        /// Экранная точка-якорь карточки: макушка ВЫБРАННОГО меша по вершинам
        /// (см. `BuildingClickTarget.MakushkaMir`), поднятая на `OTSTUP_PX`.
        ///
        /// Виток "ночь-4" (правка по критику кадра, `OTCHET-nochi-2.md` виток
        /// 3, пункт 3: «плашка названия парит без привязки»; инспектор той же
        /// ночи отдельно поймал числом: плашка легла на 90 px ближе к
        /// СОСЕДНЕМУ пропу, чем к своему зданию). Прежняя версия брала
        /// объединённый экранный bbox видимых рендереров (`SchitatEkrannyy
        /// BboxRendererov` ниже, оставлен в файле не как код, а как
        /// зафиксированная причина отказа от него) — коробка по осям МИРА в
        /// изометрии шире и выше самого силуэта, и на резной/составной
        /// геометрии её якорь мог отклониться к массе объекта-соседа, если
        /// его габарит пересекался с габаритом здания на экране. Макушка по
        /// вершинам мешей САМОГО объекта такой двусмысленности не имеет —
        /// тот же приём, что уже верно ставит маяк цели (`QuestMarker.
        /// MakushkaMir`, см. Grep "Makushka").
        /// </summary>
        /// <summary>
        /// Экранный X центра объединённых bounds рендереров ОБЪЕКТА (не соседей): горизонтальный якорь
        /// плашки и маяка (P10/P11). По вертикали bounds не годится (см. докстринг ObnovitMayak), по горизонтали центр массы габарита и есть середина силуэта.
        /// </summary>
        private static float EkrannyyCentrX(GameObject go, Camera cam)
        {
            // Частицы (пыль под шаттлом) и выключенные рендереры не считаются: их габарит уносил карточку «Шаттл» на полэкрана вправо (Khan 06.09)
            Bounds b = default; bool est = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (!est) { b = r.bounds; est = true; } else b.Encapsulate(r.bounds);
            }
            if (!est) return cam.WorldToScreenPoint(go.transform.position).x;
            return cam.WorldToScreenPoint(b.center).x;
        }

        private Vector2 SchitatTochkuKartochki(BuildingClickTarget zdanie, Camera cam)
        {
            Vector3 makushkaMir = zdanie.MakushkaMir(cam);
            Vector3 makushkaEkran = cam.WorldToScreenPoint(makushkaMir);
            // P10 (АД F1/F2, критик): X — экранный центр рендереров здания, не самой верхней вершины:
            // у ангара в изометрии высшая вершина — дальний угол, плашка уходила на ~57 px влево от силуэта (play_tap_angar.png). Y — по-прежнему макушка.
            return new Vector2(EkrannyyCentrX(zdanie.gameObject, cam), makushkaEkran.y + OTSTUP_PX);
        }

        /// <summary>
        /// Экранный прямоугольник по объединению видимых рендереров объекта.
        /// Отличается от <see cref="SchitatEkrannyyBbox"/> тем, что берёт не один
        /// общий габарит, а каждый меш отдельно: сумма их проекций гораздо ближе
        /// к настоящему силуэту, чем проекция описанной коробки.
        /// </summary>
        private static Rect SchitatEkrannyyBboxRendererov(GameObject go, Camera cam)
        {
            var rs = go.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0)
                return new Rect();

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var r in rs)
            {
                // системы частиц и прочее без меша в силуэт не входят
                if (r is ParticleSystemRenderer || r is TrailRenderer)
                    continue;
                Rect k = SchitatEkrannyyBbox(r.bounds, cam);
                if (k.xMin < minX) minX = k.xMin;
                if (k.xMax > maxX) maxX = k.xMax;
                if (k.yMin < minY) minY = k.yMin;
                if (k.yMax > maxY) maxY = k.yMax;
            }
            if (minX > maxX)
                return new Rect();
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Гасит бейджи, попадающие под карточку, и возвращает их обратно при
        /// снятии выделения. Подпись здания важнее метки «нет питания», а
        /// полупрозрачный визор поверх жёлтого кружка портит обоих: приёмка
        /// попытки 1 поймала съеденную первую букву имени именно так.
        /// </summary>
        private void GasitBadzhiPod(Vector2 tochka)
        {
            VernutBadzhi();
            if (_badges == null)
                return;

            // Ширину берём ФАКТИЧЕСКУЮ у карточки, а не потолок. Потолок 420 при
            // реальных 221 давал полосу вдвое шире подписи, и под «накрытое»
            // попадали значки, которые карточка не задевает. В изометрии в такую
            // колонку влезает половина карты по глубине, а пропавший значок
            // «нет питания» в кадре незаметен — тихая потеря игрового сигнала.
            float polShiriny = (_zhivoy != null && _zhivoy.ShirinaKartochkiImeni > 1f
                                ? _zhivoy.ShirinaKartochkiImeni : ZAPAS_PO_X_PX * 2f) * 0.5f + 6f;
            var pod = Rect.MinMaxRect(tochka.x - polShiriny, tochka.y - 4f,
                                      tochka.x + polShiriny, tochka.y + VYSOTA_KARTOCHKI_PX + 4f);
            foreach (var badge in _badges)
            {
                if (badge == null || !badge.enabled)
                    continue;
                if (!pod.Overlaps(SchitatEkrannyyBbox(badge.bounds, _kamera)))
                    continue;
                badge.enabled = false;
                _pogasheno.Add(badge);
            }
        }

        /// <summary>Возвращает погашенные бейджи. Вызывается при снятии выбора.</summary>
        private void VernutBadzhi()
        {
            foreach (var b in _pogasheno)
                if (b != null)
                    b.enabled = true;
            _pogasheno.Clear();
        }

        /// <summary>
        /// Экранный прямоугольник габарита: проекция ВСЕХ восьми углов, а не
        /// одной мировой точки. Приемка попытки 1 поймала карточку левее
        /// здания на 68 px — при развороте камеры (yaw 45°, pitch 38°)
        /// мировой центр X/Z, спроецированный на экран, не совпадает с
        /// экранным центром видимого силуэта: силуэт — это проекция ВСЕГО
        /// объема, а не одной его точки. Восемь углов и min/max по экрану —
        /// единственный способ получить настоящий видимый центр.
        ///
        /// Публичный и статический, чтобы `CeliDiag` бил той же арифметикой,
        /// а не отдельной копией той же формулы.
        /// </summary>
        public static Rect SchitatEkrannyyBbox(Bounds b, Camera cam)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 ugol = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 ekran = cam.WorldToScreenPoint(ugol);
                if (ekran.x < minX) minX = ekran.x;
                if (ekran.x > maxX) maxX = ekran.x;
                if (ekran.y < minY) minY = ekran.y;
                if (ekran.y > maxY) maxY = ekran.y;
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private void Vybrat(BuildingClickTarget zdanie)
        {
            // Виток "ночь-2": решение заказчика по открытому вопросу спеки
            // (раздел 3, состояние 6) — повторный тап по уже выбранному
            // зданию снимает выбор, симметрично тапу в пустоту. Карточки
            // объекта в этом срезе еще нет, снятие — единственный разумный
            // отклик на второй тап по тому же силуэту.
            if (_vybrano == zdanie)
            {
                Snyat();
                return;
            }
            SnyatKontur();
            _vybrano = zdanie;
            _kontur = KonturZdaniya.Vklyuchit(zdanie.gameObject);
            Vybrano?.Invoke(zdanie);
        }

        private void Snyat()
        {
            SnyatKontur();
            VernutBadzhi();
            _vybrano = null;
            if (_zhivoy != null)
                _zhivoy.SkrytImyaZdaniya();
            Snyato?.Invoke();
        }

        private void SnyatKontur()
        {
            if (_kontur != null)
            {
                _kontur.Ubrat();
                _kontur = null;
            }
        }

        /// <summary>
        /// Та же арифметика, что у `ColonyGame.Pick`: луч из камеры через
        /// курсор, из всех задетых габаритов — ближний.
        /// </summary>
        /// <summary>Публичная обёртка для приёмки: что выбрал бы клик по этой экранной точке.</summary>
        public ClickTarget PodobratNaEkrane(Vector3 screenPoint) => Podobrat(screenPoint);
        public int ChisloTseley => _tseli.Count;

        private ClickTarget Podobrat(Vector3 screenPoint)
        {
            if (_kamera == null)
                return null;

            Ray luch = _kamera.ScreenPointToRay(screenPoint);
            ClickTarget blizhayshiy = null;
            float minOtklonenie = float.MaxValue;

            // Из задетых габаритов — тот, чей центр ближе к лучу, а не тот, в чью коробку луч вошёл раньше:
            // огромный AABB ангара склада накрывал площадку и дрон рядом, и клик по дрону открывал склад (Khan 06.09).
            foreach (ClickTarget tsel in _tseli)
            {
                if (!tsel.Popadanie(luch, out float rasstoyanie))
                    continue;
                Vector3 c = tsel.ClickBounds.center;
                float otklonenie = Vector3.Cross(luch.direction, c - luch.origin).magnitude;   // расстояние от центра до луча
                if (otklonenie < minOtklonenie)
                {
                    minOtklonenie = otklonenie;
                    blizhayshiy = tsel;
                }
            }
            return blizhayshiy;
        }
    }
}
