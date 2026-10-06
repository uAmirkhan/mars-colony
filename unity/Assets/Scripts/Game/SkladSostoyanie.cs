using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Ворота UI-26: продажа товара со склада. Раньше склад умел только
    /// написать "Нажмите на товар, чтобы продать" — самого нажатия не было ни
    /// разу (см. отчет ночи). Это единственное живое место экрана: запас,
    /// открытый попап-степпер и тултип "пусто" держатся здесь, в рантайме, а
    /// не в редакторском коде — `InterfeysBuilder.PostroitEkranSklada`
    /// один раз строит узлы и один раз кладет сюда стартовые числа
    /// (TOVARY_SKLADA — заглушка до моста с TS-доменом, см. TODO там же),
    /// дальше все правки состояния идут только через эту компоненту.
    ///
    /// Кредиты начисляются НЕ прямой правкой текста HUD-счетчика: счетчик
    /// каждый кадр перезаписывает `ZhivoyInterfeys.Obnovit` из
    /// `ColonyGame.State.credits` — прямая запись текста слезла бы с экрана
    /// на следующем кадре молча. Правильная точка — само поле `State.credits`
    /// (тот же прием, что `ColonyState.Apply` уже делает для доменных
    /// действий), тогда HUD подхватывает число сам, заодно бесплатно получая
    /// уже собранную всплывашку "+X".
    /// </summary>
    public sealed class SkladSostoyanie : MonoBehaviour
    {
        private int _kredityBezIgry = -1;
        private static UnityEngine.UI.Text NaytiTekstKreditov()
        {
            var holst = GameObject.Find("interfeys"); if (holst == null) return null;
            var sch = holst.transform.Find("schetchik-kredity"); if (sch == null) return null;
            foreach (var t in sch.GetComponentsInChildren<UnityEngine.UI.Text>(true)) if (t.name == "znachenie") return t;
            return null;
        }

        // Этап 3, блок 1, правка A1, пункт 4: та же капсула кредитов, что
        // ищет NaytiTekstKreditov, но нужен медальон-иконка как мишень
        // полёта монеты, а не число.
        private static RectTransform NaytiMedalonKreditov()
        {
            var holst = GameObject.Find("interfeys"); if (holst == null) return null;
            var sch = holst.transform.Find("schetchik-kredity"); if (sch == null) return null;
            var korpus = sch.Find("korpus"); if (korpus == null) return null;
            return korpus.Find("medalyon") as RectTransform;
        }
        [System.Serializable]
        public struct Tovar
        {
            public string id;
            public string nazvanie;
            public int price;
            public int qty;
            public int row; // ряд сетки (0 = верхний) — решает, встанет попап над карточкой или под ней
            public Image ikonka;
            public Text chislo;
        }

        public Tovar[] tovary;

        public int emkost;
        public int emkostCap;
        public Text emkostTekst;

        // Попап-степпер — один переиспользуемый узел на все товары, якорится к
        // нажатой карточке (см. `PozicionirovatPopup`), а не к центру панели
        // (инспектор ворот UI-26: спека `spec-sklad.md` раздел 2.6 — "пузырь",
        // не модалка на всю сетку).
        public GameObject popup;
        public RectTransform popupTelo;
        public Text popupNazvanie;
        public Text popupChislo;
        public Text popupProdatTekst; // текст кнопки "Продать за {sum} кр." — отдельной строки "Итого" нет (спека 2.6)

        // Тултип "Пока нет на складе" — тоже один переиспользуемый узел.
        public GameObject tultip;
        public Text tultipTekst;

        // P7 (АД зона 3): половина ширины утопленного поля pole-sklad минус отступ 12px —
        // граница, за которую пузырь-степпер не может уехать по горизонтали. Считается
        // один раз в InterfeysBuilder.PostroitEkranSklada (поле симметрично относительно центра панели, тот
        // же центр несёт и сам popup); 999 здесь — запасное значение "клэмпа почти нет",
        // если билдер по какой-то причине не присвоил число.
        public float poleKlampPolovina = 999f;

        int _otkryt = -1;
        int _stepperN = 1;
        float _tultipSkrytVremya = -1f;

        void Update()
        {
            if (tultip != null && tultip.activeSelf && Time.unscaledTime >= _tultipSkrytVremya)
                tultip.SetActive(false);
        }

        /// <summary>
        /// Картошка появилась в конфиге после того, как сборщик запёк экран склада на 17 товаров
        /// (Khan 08.09: «картошки на складе нет как ресурса»). Восемнадцатая ячейка сетки 6×3 свободна:
        /// клонируем последнюю карточку в неё и дописываем товар в массив. Пересборка всего холста
        /// сборщиком ради одной ячейки рискованнее.
        /// </summary>
        private bool _kartoshkaEst;
        private void ObespechitKartoshku()
        {
            if (_kartoshkaEst || tovary == null) return;
            _kartoshkaEst = true;
            foreach (var t in tovary) if (t.id == "potato") return;
            Transform obrazets = null;
            foreach (var tr in GetComponentsInChildren<Transform>(true)) if (tr.name == "tovar-coffee_ration") { obrazets = tr; break; }
            if (obrazets == null) return;
            var go = Instantiate(obrazets.gameObject, obrazets.parent);
            go.name = "tovar-potato";
            var rt = (RectTransform)go.transform; var ort = (RectTransform)obrazets;
            rt.anchoredPosition = ort.anchoredPosition + new Vector2(140f, 0f);   // шаг колонки сборщика
            var spr = Resources.Load<Sprite>("UI/Elementy/ikonka-potato");
            Image ik = null; Text ch = null;
            foreach (var img in go.GetComponentsInChildren<Image>(true)) if (img.gameObject != go && img.sprite != null) { ik = img; if (spr != null) img.sprite = spr; }
            foreach (var t in go.GetComponentsInChildren<Text>(true)) { ch = t; t.text = "0"; }
            var kn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true);
            int idx = tovary.Length;
            if (kn != null)
            {
                for (int i = 0; i < kn.onClick.GetPersistentEventCount(); i++) kn.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                kn.onClick.AddListener(() => Tap(idx));
            }
            var good = MarsColony.Domain.Config.Goods.Of("potato");
            var spisok = new System.Collections.Generic.List<Tovar>(tovary);
            spisok.Add(new Tovar { id = "potato", nazvanie = good.name, price = good.price, qty = 0, row = 2, ikonka = ik, chislo = ch });
            tovary = spisok.ToArray();
            _sinhronizirovano = false;
        }

        // ------------------------------------------------------------ вкладка «МОДУЛИ»
        //
        // Вкладки «ТОВАРЫ» и «МОДУЛИ» сборщик нарисовал картинками, но переключения не было и модули
        // нигде не показывались: игрок не знал, что шаттл их вообще возит (Khan 08.09). Здесь вкладки
        // становятся кнопками, а под модули строится своя сетка — клонами карточек товаров, чтобы
        // не расходиться со стилем поля.
        private bool _vkladkiGotovy;
        private Transform _poleTovarov, _poleModuley;
        private UnityEngine.UI.Image _tabTovary, _tabModuli;
        private Text _tabTovaryTekst, _tabModuliTekst;
        private readonly List<(string klyuch, Text chislo, Image ikonka)> _kartochkiModuley = new List<(string, Text, Image)>();
        private static readonly string[] MODULI_PORYADOK = { "panel", "frame", "sealant", "filter", "cable", "drill_head", "reactor_cell" };

        private void ObespechitVkladki()
        {
            if (_vkladkiGotovy || tovary == null) return;
            _vkladkiGotovy = true;
            Transform obrazets = null, panel = null;
            foreach (var tr in GetComponentsInChildren<Transform>(true))
            {
                if (tr.name == "tovar-algae") obrazets = tr;   // первая карточка сетки: от неё считается раскладка модулей
                if (tr.name == "vkladka-tovary") { _tabTovary = tr.GetComponent<Image>(); _tabTovaryTekst = tr.GetComponentInChildren<Text>(true); panel = tr.parent; }
                if (tr.name == "vkladka-moduli") { _tabModuli = tr.GetComponent<Image>(); _tabModuliTekst = tr.GetComponentInChildren<Text>(true); }
            }
            if (obrazets == null || _tabTovary == null || _tabModuli == null) return;
            _poleTovarov = obrazets.parent;

            // Поле модулей — копия поля товаров целиком (с рамкой и отступами), из которой убраны карточки:
            // так сетка стоит ровно там же, где товары, и не надо повторять раскладку сборщика руками.
            var kopiya = Instantiate(_poleTovarov.gameObject, _poleTovarov.parent);
            kopiya.name = "pole-moduli";
            _poleModuley = kopiya.transform;
            var lishnie = new List<GameObject>();
            foreach (Transform c in _poleModuley) if (c.name.StartsWith("tovar-")) lishnie.Add(c.gameObject);
            var obrazetsKopii = lishnie.Count > 0 ? lishnie[0] : null;
            for (int i = 1; i < lishnie.Count; i++) Destroy(lishnie[i]);

            if (obrazetsKopii == null) return;
            var oKart = (RectTransform)obrazetsKopii.transform;
            // Первая карточка сетки сборщика: колонка 0, строка 0. Образец — «tovar-algae», он и есть первый.
            Vector2 nachaloSetki = oKart.anchoredPosition;
            for (int i = 0; i < MODULI_PORYADOK.Length; i++)
            {
                var go = i == 0 ? obrazetsKopii : Instantiate(obrazetsKopii, _poleModuley);
                go.name = "modul-" + MODULI_PORYADOK[i];
                var rt = (RectTransform)go.transform;
                rt.anchoredPosition = nachaloSetki + new Vector2((i % 6) * 140f, -(i / 6) * 150f);
                var spr = Resources.Load<Sprite>("UI/Ikonki/moduli/" + MODULI_PORYADOK[i]);
                Image ik = null; Text ch = null;
                foreach (var img in go.GetComponentsInChildren<Image>(true)) if (img.gameObject != go && img.sprite != null) { ik = img; if (spr != null) { img.sprite = spr; img.color = Color.white; } }
                foreach (var t in go.GetComponentsInChildren<Text>(true)) { ch = t; t.text = "0"; }
                var kn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true);
                if (kn != null) { for (int j = 0; j < kn.onClick.GetPersistentEventCount(); j++) kn.onClick.SetPersistentListenerState(j, UnityEngine.Events.UnityEventCallState.Off); kn.onClick.RemoveAllListeners(); }
                _kartochkiModuley.Add((MODULI_PORYADOK[i], ch, ik));
            }

            var knTovary = _tabTovary.gameObject.GetComponent<Button>() ?? _tabTovary.gameObject.AddComponent<Button>();
            knTovary.targetGraphic = _tabTovary; knTovary.transition = Selectable.Transition.None;
            knTovary.onClick.RemoveAllListeners(); knTovary.onClick.AddListener(() => PokazatVkladku(false));
            var knModuli = _tabModuli.gameObject.GetComponent<Button>() ?? _tabModuli.gameObject.AddComponent<Button>();
            knModuli.targetGraphic = _tabModuli; knModuli.transition = Selectable.Transition.None;
            knModuli.onClick.RemoveAllListeners(); knModuli.onClick.AddListener(() => PokazatVkladku(true));
            PokazatVkladku(false);
        }

        private void PokazatVkladku(bool moduli)
        {
            if (_poleTovarov == null || _poleModuley == null) return;
            _poleTovarov.gameObject.SetActive(!moduli);
            _poleModuley.gameObject.SetActive(moduli);
            var aktiv = Resources.Load<Sprite>("UI/Elementy/vkladka-aktivnaya");
            var neaktiv = Resources.Load<Sprite>("UI/Elementy/vkladka-neaktivnaya");
            if (aktiv != null && neaktiv != null)
            {
                _tabTovary.sprite = moduli ? neaktiv : aktiv;
                _tabModuli.sprite = moduli ? aktiv : neaktiv;
            }
            var korichnevy = new Color(0.357f, 0.231f, 0.118f);
            var blednyy = new Color(0.541f, 0.447f, 0.337f);
            if (_tabTovaryTekst != null) _tabTovaryTekst.color = moduli ? blednyy : korichnevy;
            if (_tabModuliTekst != null) _tabModuliTekst.color = moduli ? korichnevy : blednyy;
        }

        /// <summary>Ночь 3: числа склада берутся из домена каждый кадр, пока экран открыт.</summary>
        public void ObnovitIzDomena(ColonyState state)
        {
            if (tovary == null || state == null) return;
            ObespechitKartoshku();
            ObespechitVkladki();
            for (int i = 0; i < tovary.Length; i++)
            {
                int q = MarsColony.Domain.Warehouse.QtyOf(state.warehouse, tovary[i].id);
                if (q == tovary[i].qty && _sinhronizirovano) continue;
                tovary[i].qty = q;
                if (tovary[i].chislo != null) tovary[i].chislo.text = q.ToString();
                if (tovary[i].ikonka != null)
                {
                    tovary[i].ikonka.color = q <= 0 ? new Color(0.42f, 0.42f, 0.42f, 0.62f) : Color.white;
                    tovary[i].ikonka.transform.localScale = Vector3.one * (q <= 0 ? 0.9f : 1f);
                }
            }
            _sinhronizirovano = true;
            var igra = IgraKolonii.Ekz;
            if (igra != null && igra.Igra != null)
                foreach (var (klyuch, chislo, ikonka) in _kartochkiModuley)
                {
                    int n = igra.Igra.moduli.TryGetValue(klyuch, out int v) ? v : 0;
                    if (chislo != null) chislo.text = n.ToString();
                    if (ikonka != null) { ikonka.color = n <= 0 ? new Color(0.42f, 0.42f, 0.42f, 0.62f) : Color.white; ikonka.transform.localScale = Vector3.one * (n <= 0 ? 0.9f : 1f); }
                }
            emkost = MarsColony.Domain.Warehouse.TotalQty(state.warehouse); emkostCap = state.warehouse.capacity;
            if (emkostTekst != null) emkostTekst.text = "Ёмкость: " + emkost + " / " + emkostCap;
        }
        bool _sinhronizirovano;

        /// <summary>Тап по карточке товара на поле. Вызывается персистентным слушателем
        /// с запечённым индексом (`UnityEventTools.AddIntPersistentListener`).</summary>
        public void Tap(int index)
        {
            if (tovary == null || index < 0 || index >= tovary.Length)
                return;

            if (tovary[index].qty <= 0)
            {
                PokazatTultip(index);
                return;
            }
            OtkrytPopup(index);
        }

        void PokazatTultip(int index)
        {
            if (tultip == null)
                return;

            Image ikon = tovary[index].ikonka;
            if (ikon != null)
            {
                Transform kartochka = ikon.transform.parent; // узел "tovar-*" на поле
                var rt = tultip.GetComponent<RectTransform>();
                rt.SetParent(kartochka, false);
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 8f);
            }
            if (tultipTekst != null)
                tultipTekst.text = "Пока нет на складе";
            tultip.SetActive(true);
            _tultipSkrytVremya = Time.unscaledTime + 1.5f;
        }

        void OtkrytPopup(int index)
        {
            _otkryt = index;
            // Степпер стартует с полного запаса, не с 1 (инспектор, спека 2.6:
            // "диапазон от 1 до available, старт — весь available").
            _stepperN = Mathf.Clamp(tovary[index].qty, 1, Mathf.Max(1, tovary[index].qty));

            if (popupNazvanie != null)
                popupNazvanie.text = tovary[index].nazvanie;

            PozicionirovatPopup(index);
            ObnovitStepper();
            if (popup != null)
                popup.SetActive(true);

            // Этап 3, блок 1, правка A1, пункт 3: пузырь появляется масштабом
            // 0.9->1 за 100 мс из точки якоря (пивот тела уже стоит по центру
            // самого пузыря — `PozicionirovatPopup` его туда и ставит).
            if (popupTelo != null)
            {
                if (_animPopup != null) StopCoroutine(_animPopup);
                _animPopup = StartCoroutine(AnimatsiiInterfeysa.AnimatPanel(
                    popupTelo, null, AnimatsiiInterfeysa.POPUP_MASSHTAB_START, 1f, 1f, 1f,
                    AnimatsiiInterfeysa.POPUP_POYAVLENIE_S, perelet: true));
            }
        }

        Coroutine _animPopup;

        /// <summary>
        /// Ставит тело попапа рядом с нажатой карточкой — над ней, если сверху
        /// достаточно места до ленты/вкладок панели, иначе под ней (инспектор
        /// ворот UI-26). Верхний ряд сетки (`row == 0`) всегда лежит вплотную к
        /// вкладкам — там места сверху нет физически ни при каком товаре,
        /// поэтому ряд решает вопрос однозначно, без замера пикселей на кадре.
        ///
        /// Тело остаётся ребёнком `popup` (растянут на всю панель, тот же
        /// центр, что и у панели) — переносить его в дерево карточки нельзя:
        /// оно тогда всплыло бы РАНЬШЕ своего затемнения по порядку отрисовки
        /// и пряталось бы под ним. Поэтому позиция считается через
        /// мир-экран-локаль (`RectTransformUtility`), тот же прием, что уже
        /// ставит карточку имени здания (`ZhivoyInterfeys.PokazatImyaZdaniya`).
        /// </summary>
        void PozicionirovatPopup(int index)
        {
            if (popup == null || popupTelo == null)
                return;
            Image ikon = tovary[index].ikonka;
            if (ikon == null)
                return;
            var itemRt = ikon.transform.parent as RectTransform; // узел "tovar-*" на поле
            var parentRt = popup.GetComponent<RectTransform>();
            if (itemRt == null || parentRt == null)
                return;

            var canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null ? canvas.worldCamera : null;

            var corners = new Vector3[4]; // 0 BL, 1 TL, 2 TR, 3 BR
            itemRt.GetWorldCorners(corners);
            bool nadKartochkoy = tovary[index].row != 0;
            Vector3 tochkaMira = nadKartochkoy ? (corners[1] + corners[2]) * 0.5f : (corners[0] + corners[3]) * 0.5f;

            Vector2 ekrannaya = RectTransformUtility.WorldToScreenPoint(cam, tochkaMira);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, ekrannaya, cam, out Vector2 lokalnaya))
                return;

            // P7: низ пузыря 8px выше верха иконки, если карточка не в первом ряду; в первом ряду
            // пузырь всегда уходит под карточку, зазор там числом не оговорен, оставлен как был.
            const float zazorNad = 8f, zazorPod = 10f;
            float teloH = popupTelo.rect.height;
            lokalnaya.y += nadKartochkoy ? (teloH * 0.5f + zazorNad) : -(teloH * 0.5f + zazorPod);

            float polShiriny = popupTelo.rect.width * 0.5f;
            // P7: клэмп внутри поля pole-sklad, не всей панели — раньше лимит считался от половины
            // ширины ПАНЕЛИ, и пузырь мог уехать в декоративные поля за краем утопленной зоны.
            float limX = Mathf.Max(0f, poleKlampPolovina - polShiriny);
            lokalnaya.x = Mathf.Clamp(lokalnaya.x, -limX, limX);

            popupTelo.anchoredPosition = lokalnaya;
        }

        /// <summary>Закрытие без продажи — крест, тап по своему затемнению попапа. Вызывается
        /// персистентным слушателем, поэтому сигнатура без параметров.</summary>
        public void ZakrytPopup()
        {
            _otkryt = -1;
            if (popup != null)
                popup.SetActive(false);
        }

        public void StepperMinus()
        {
            if (_otkryt < 0) return;
            _stepperN = Mathf.Clamp(_stepperN - 1, 1, Mathf.Max(1, tovary[_otkryt].qty));
            ObnovitStepper();
        }

        public void StepperPlus()
        {
            if (_otkryt < 0) return;
            _stepperN = Mathf.Clamp(_stepperN + 1, 1, Mathf.Max(1, tovary[_otkryt].qty));
            ObnovitStepper();
        }

        void ObnovitStepper()
        {
            if (_otkryt < 0) return;
            int itogo = tovary[_otkryt].price * _stepperN;
            if (popupChislo != null) popupChislo.text = _stepperN.ToString();
            // Сумма — прямо в тексте кнопки подтверждения (инспектор, спека 2.6:
            // "Продать за {sum} кр." с жетоном кредитов слева), отдельной
            // строки "Итого" больше нет.
            if (popupProdatTekst != null) popupProdatTekst.text = "Продать за " + itogo + " кр.";
        }

        /// <summary>Подтверждение продажи — кнопка "ПРОДАТЬ". Персистентный слушатель,
        /// поэтому тоже без параметров: индекс открытого товара уже хранится в `_otkryt`.</summary>
        public void Prodat()
        {
            if (_otkryt < 0 || tovary == null || _otkryt >= tovary.Length)
                return;

            int index = _otkryt;
            int n = Mathf.Clamp(_stepperN, 1, Mathf.Max(0, tovary[index].qty));
            if (n <= 0) { ZakrytPopup(); return; }

            int itogo = tovary[index].price * n;
            int bylo = tovary[index].qty;
            tovary[index].qty -= n;

            if (tovary[index].chislo != null)
                tovary[index].chislo.text = tovary[index].qty.ToString();
            if (tovary[index].ikonka != null)
                tovary[index].ikonka.color = tovary[index].qty <= 0
                    ? new Color(0.42f, 0.42f, 0.42f, 0.62f) // P9, = InterfeysBuilder.TsvetPustogoTovara (Editor-сборка недоступна рантайму); тот же приглушенный тон, что у пустой карточки в TovarNaPole
                    : Color.white;
            if (tovary[index].ikonka != null)
                tovary[index].ikonka.transform.localScale = Vector3.one * (tovary[index].qty <= 0 ? 0.9f : 1f); // P9, = InterfeysBuilder.MASSHTAB_PUSTOGO_TOVARA

            emkost -= n;
            if (emkostTekst != null)
                emkostTekst.text = "Ёмкость: " + emkost + " / " + emkostCap;

            var game = FindFirstObjectByType<ColonyGame>();
            int bylKreditov = -1, stalKreditov = -1;
            var igra = IgraKolonii.Ekz;
            if (igra != null)
            {
                // Ночь 3: правда о складе и кредитах — в домене IgraKolonii; экран лишь отражает.
                bylKreditov = igra.State.credits;
                igra.Prodat(tovary[index].id, n);
                stalKreditov = igra.State.credits;
                tovary[index].qty = MarsColony.Domain.Warehouse.QtyOf(igra.State.warehouse, tovary[index].id);
                if (tovary[index].chislo != null) tovary[index].chislo.text = tovary[index].qty.ToString();
                emkost = MarsColony.Domain.Warehouse.TotalQty(igra.State.warehouse); emkostCap = igra.State.warehouse.capacity;
                if (emkostTekst != null) emkostTekst.text = "Ёмкость: " + emkost + " / " + emkostCap;
            }
            else if (game != null && game.State != null)
            {
                bylKreditov = game.State.credits;
                game.State.credits += itogo;
                stalKreditov = game.State.credits;
            }
            else
            {
                // В сцене-витрине MAIN объекта ColonyGame нет (ночь 2: лог «кредиты -1->-1», HUD стоял на 50).
                // Тогда склад сам ведёт кредиты и пишет число в HUD; стартовое значение читается из текста один раз.
                var hud = NaytiTekstKreditov();
                if (hud != null)
                {
                    if (_kredityBezIgry < 0 && !int.TryParse(hud.text, out _kredityBezIgry)) _kredityBezIgry = 0;
                    bylKreditov = _kredityBezIgry; _kredityBezIgry += itogo; stalKreditov = _kredityBezIgry;
                    hud.text = _kredityBezIgry.ToString();

                    // Без ColonyGame текст HUD правится напрямую (выше) — та
                    // ветка, которую `ZhivoyInterfeys.Obnovit` не видит (там
                    // нет State для сверки было/стало), поэтому всплывашку
                    // "+N" зовём отсюда явно (пункт 4 правки A1).
                    var zhivoy = hud.GetComponentInParent<ZhivoyInterfeys>();
                    if (zhivoy != null) zhivoy.PokazatPribavkuKreditov(hud.rectTransform, itogo);
                }
                else Debug.LogWarning("[sklad] ColonyGame и число кредитов HUD не найдены — кредиты не начислены");
            }

            Debug.Log($"[sklad] продано {tovary[index].id} x {n} за {itogo}: запас {bylo}->{tovary[index].qty}, кредиты {bylKreditov}->{stalKreditov}");

            // Полёт монеты-иконки от кнопки "Продать" к капсуле кредитов —
            // безусловно, независимо от того, кто начислил кредиты выше
            // (пункт 4 правки A1). Клонируем готовую 28px-иконку "moneta" из
            // самой кнопки, а не грузим спрайт заново.
            if (popupProdatTekst != null)
            {
                var knopkaProdat = popupProdatTekst.transform.parent; // узел "popup-prodat"
                var moneta = knopkaProdat != null ? knopkaProdat.Find("moneta") as RectTransform : null;
                var medalyon = NaytiMedalonKreditov();
                var holst = GameObject.Find("interfeys");
                if (moneta != null && medalyon != null && holst != null)
                    StartCoroutine(AnimatsiiInterfeysa.LetetMonetaKKreditam(moneta, medalyon, holst.transform));
            }

            ZakrytPopup();
        }
    }
}
