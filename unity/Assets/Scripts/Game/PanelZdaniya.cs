using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Списочная панель действий (шаттл, дроны): лента с заголовком, строки-карточки
    /// «иконка — имя — подпись — полоса — кнопка», крест. Собирается в рантайме из элементов комплекта
    /// заказчика, уже лежащих на холсте (panel.png карточки цели, pole-sklad и knopka-a склада, bar-trek
    /// и bar-zalivka HUD, knopka-krest-2), теми же девятидольными настройками, что у экрана склада:
    /// pixelsPerUnitMultiplier = размер источника / размер на экране, иначе борта 9-slice схлопываются
    /// в «наконечники» (так выглядела панель 06.09 до переделки). Для теплиц, фабрик и добычи —
    /// <see cref="PanelKonveyera"/> по референсу Township.
    ///
    /// Одна панель на сцену: показ новой заменяет содержимое, а не плодит объекты.
    /// </summary>
    public sealed class PanelZdaniya : MonoBehaviour
    {
        public sealed class Stroka
        {
            public Sprite ikonka;
            public string imya;
            public string podpis;
            public string knopka;          // null — без кнопки
            public bool knopkaAktivna = true;
            public Action deystvie;
            public float progress = -1f;   // 0..1 — полоса под подписью
        }

        // Геометрия panel.png (1220×624): лента y 0…140, тело от y 73; при ширине 680 масштаб 1.794.
        // Гладкая панель склада (panel-sklad.png 1042×797) без ленты: лента только у карточки цели (заказчик 06.09, Д11)
        private const float SHIRINA = 680f, K_PANEL = 1042f / SHIRINA;
        private const float VYSOTA_SHAPKI = 66f;
        private const float VYSOTA_STROKI = 96f, ZAZOR = 10f, OTSTUP_NIZ = 30f, POLYA = 26f;
        private const float IKONKA = 68f, KNOPKA_W = 176f, KNOPKA_H = 60f, POLOSA_H = 10f;

        private RectTransform _koren, _telo;
        private Text _zagolovok;
        private readonly List<GameObject> _stroki = new List<GameObject>();
        private string[] _forma;   // форма каждой строки: по ней решаем, можно ли обновить без пересборки
        private ElementyHolsta _el;
        private Action _priZakrytii;
        private bool _gotova;

        public bool Otkryta => _koren != null && _koren.gameObject.activeSelf;

        public static PanelZdaniya Obespechit()
        {
            var est = FindFirstObjectByType<PanelZdaniya>(FindObjectsInactive.Include);
            if (est != null) return est;
            var holst = GameObject.Find("interfeys");
            if (holst == null) { Debug.LogWarning("[панель] холст interfeys не найден"); return null; }
            var go = new GameObject("panel-zdaniya", typeof(RectTransform));
            go.transform.SetParent(holst.transform, false);
            var p = go.AddComponent<PanelZdaniya>();
            p.Postroit();
            return p;
        }

        public Sprite Ikonka(string goodId) => _el != null ? _el.Ikonka(goodId) : null;

        private void Postroit()
        {
            _koren = (RectTransform)transform;
            for (int i = _koren.childCount - 1; i >= 0; i--) Destroy(_koren.GetChild(i).gameObject);   // сироты после перезагрузки домена
            _koren.anchorMin = new Vector2(0.5f, 0f); _koren.anchorMax = new Vector2(0.5f, 0f); _koren.pivot = new Vector2(0.5f, 0f);
            _koren.sizeDelta = new Vector2(SHIRINA, VYSOTA_SHAPKI + OTSTUP_NIZ);
            _koren.anchoredPosition = new Vector2(0f, 40f);
            _el = ElementyHolsta.Sobrat(transform.parent);

            _telo = _el.Kartinka(_koren, "telo", "panel-sklad", Color.white, K_PANEL);
            ElementyHolsta.Rastyanut(_telo, Vector2.zero);

            _zagolovok = _el.Tekst(_telo, "zagolovok", 24, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
            _zagolovok.resizeTextForBestFit = true; _zagolovok.resizeTextMinSize = 14; _zagolovok.resizeTextMaxSize = 24;
            _zagolovok.horizontalOverflow = HorizontalWrapMode.Wrap; _zagolovok.verticalOverflow = VerticalWrapMode.Truncate;
            var zr = _zagolovok.rectTransform; zr.anchorMin = new Vector2(0f, 1f); zr.anchorMax = new Vector2(1f, 1f); zr.pivot = new Vector2(0.5f, 1f);
            zr.anchoredPosition = new Vector2(0f, -18f); zr.sizeDelta = new Vector2(-150f, 36f);   // не заезжает под крест

            _el.Krest(_telo, new Vector2(-30f, -30f), Skryt);

            _gotova = true;
            _koren.gameObject.SetActive(false);
        }

        public void Pokazat(string zagolovok, IList<Stroka> stroki, Action priZakrytii = null)
        {
            if (!_gotova || _el == null || _telo == null) Postroit();   // _el не сериализуется: после перезагрузки домена в Play строим заново
            _priZakrytii = priZakrytii;
            // Открытая панель обновляется четырежды в секунду. Сносить и строить строки заново — это
            // видимое мигание иконки и кнопки (Khan 07.09). Если состав строк не изменился, правим
            // тексты, полосу и действие на месте, а объекты не трогаем.
            if (ObnovitNaMeste(zagolovok, stroki)) return;
            foreach (var g in _stroki) if (g != null) Destroy(g);
            _stroki.Clear();
            for (int i = _telo.childCount - 1; i >= 0; i--) { var c = _telo.GetChild(i); if (c.name.StartsWith("stroka-")) { c.gameObject.SetActive(false); Destroy(c.gameObject); } }
            _zagolovok.text = zagolovok;
            float shag = VYSOTA_STROKI + ZAZOR;
            float polnaya = VYSOTA_SHAPKI + stroki.Count * shag - ZAZOR + OTSTUP_NIZ;
            // Панель не выше ~78% экрана: длинный список (заказы дронов) прокручивается, а не уезжает за верх экрана (Khan 06.09)
            float maxVysota = _koren.parent is RectTransform rodit ? rodit.rect.height * 0.78f : polnaya;
            bool skroll = polnaya > maxVysota;
            float vysota = skroll ? maxVysota : polnaya;
            _koren.sizeDelta = new Vector2(SHIRINA, vysota);
            RectTransform rod = _telo; float y0 = -VYSOTA_SHAPKI;
            if (skroll)
            {
                var vp = new GameObject("stroka-viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image), typeof(ScrollRect)); vp.transform.SetParent(_telo, false);
                var vprt = (RectTransform)vp.transform; vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one; vprt.offsetMin = new Vector2(0f, OTSTUP_NIZ); vprt.offsetMax = new Vector2(0f, -VYSOTA_SHAPKI);
                var vpi = vp.GetComponent<Image>(); vpi.color = new Color(1f, 1f, 1f, 0f);   // прозрачная, но ловит перетаскивание
                var content = new GameObject("content", typeof(RectTransform)); content.transform.SetParent(vprt, false);
                var crt = (RectTransform)content.transform; crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(0.5f, 1f); crt.anchoredPosition = Vector2.zero; crt.sizeDelta = new Vector2(0f, stroki.Count * shag - ZAZOR);
                var sr = vp.GetComponent<ScrollRect>(); sr.content = crt; sr.viewport = vprt; sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30f; sr.inertia = true;
                rod = crt; y0 = 0f;
            }
            _forma = new string[stroki.Count];
            for (int i = 0; i < stroki.Count; i++) { _forma[i] = FormaStroki(stroki[i]); _stroki.Add(PostroitStroku(stroki[i], i, rod, y0 - i * shag)); }
            foreach (var g in _stroki) g.SetActive(true);
            _koren.gameObject.SetActive(true);
            _koren.SetAsLastSibling();
        }

        /// <summary>Обновление без пересборки: состав строк тот же — меняются только значения.
        /// Возвращает false, если форма списка изменилась и нужна честная пересборка.</summary>
        private bool ObnovitNaMeste(string zagolovok, IList<Stroka> stroki)
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return false;
            if (_forma == null || _stroki.Count != stroki.Count) return false;
            for (int i = 0; i < stroki.Count; i++)
                if (_stroki[i] == null || _forma[i] != FormaStroki(stroki[i])) return false;

            _zagolovok.text = zagolovok;
            for (int i = 0; i < stroki.Count; i++)
            {
                var s = stroki[i];
                var rt = (RectTransform)_stroki[i].transform;

                if (s.ikonka != null)
                {
                    var ik = rt.Find("ikonka");
                    if (ik != null) { var img = ik.GetComponent<Image>(); if (img != null && img.sprite != s.ikonka) img.sprite = s.ikonka; }
                }
                var imya = rt.Find("imya");
                if (imya != null) { var t = imya.GetComponent<Text>(); if (t != null && t.text != s.imya) t.text = s.imya; }
                if (s.podpis != null)
                {
                    var pod = rt.Find("podpis");
                    if (pod != null) { var t = pod.GetComponent<Text>(); if (t != null && t.text != s.podpis) t.text = s.podpis; }
                }
                if (s.progress >= 0f)
                {
                    var trek = (RectTransform)rt.Find("trek");
                    var maska = trek != null ? (RectTransform)trek.Find("maska") : null;
                    if (trek != null && maska != null)
                    {
                        float w = trek.sizeDelta.x * Mathf.Clamp01(s.progress);
                        if (!Mathf.Approximately(maska.sizeDelta.x, w)) maska.sizeDelta = new Vector2(w, maska.sizeDelta.y);
                    }
                }
                if (s.knopka != null)
                {
                    var kn = rt.Find("knopka");
                    var b = kn != null ? kn.GetComponent<Button>() : null;
                    if (b != null) { b.onClick.RemoveAllListeners(); var d = s.deystvie; b.onClick.AddListener(() => d?.Invoke()); }
                }
            }
            return true;
        }

        /// <summary>Форма строки: что нельзя изменить, не пересобрав её. Тексты и проценты сюда не входят.</summary>
        private static string FormaStroki(Stroka s)
            => (s.ikonka != null ? "i" : "-") + (s.podpis != null ? "p" : "-") + (s.progress >= 0f ? "b" : "-")
             + (s.knopka ?? "-") + (s.knopkaAktivna ? "+" : "x");

        public void Skryt()
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return;
            _koren.gameObject.SetActive(false);
            _forma = null;   // следующее открытие строит строки заново
            var cb = _priZakrytii; _priZakrytii = null;
            cb?.Invoke();
        }

        private GameObject PostroitStroku(Stroka s, int i, RectTransform rod, float y)
        {
            var go = new GameObject("stroka-" + i, typeof(RectTransform)); go.transform.SetParent(rod, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y); rt.sizeDelta = new Vector2(-2f * POLYA, VYSOTA_STROKI);

            var fon = _el.Kartinka(rt, "fon", "pole-sklad", Color.white, 736f / (VYSOTA_STROKI * 2f));
            ElementyHolsta.Rastyanut(fon, Vector2.zero);

            float x = 18f;
            if (s.ikonka != null)
            {
                var ik = _el.Sprayt(rt, "ikonka", s.ikonka, IKONKA); ik.anchorMin = ik.anchorMax = new Vector2(0f, 0.5f); ik.pivot = new Vector2(0f, 0.5f);
                ik.anchoredPosition = new Vector2(x, 0f);
                x += IKONKA + 16f;
            }
            float shirinaTeksta = SHIRINA - 2f * POLYA - x - (s.knopka != null ? KNOPKA_W + 34f : 20f);
            bool estPolosa = s.progress >= 0f;
            float yImeni = s.podpis == null ? 0f : (estPolosa ? 24f : 15f);

            var imya = _el.Tekst(rt, "imya", 20, FontStyle.Bold, TextAnchor.MiddleLeft, ElementyHolsta.KORICHNEVY);
            imya.rectTransform.anchorMin = imya.rectTransform.anchorMax = new Vector2(0f, 0.5f); imya.rectTransform.pivot = new Vector2(0f, 0.5f);
            imya.rectTransform.anchoredPosition = new Vector2(x, yImeni); imya.rectTransform.sizeDelta = new Vector2(shirinaTeksta, 30f); imya.text = s.imya;
            imya.horizontalOverflow = HorizontalWrapMode.Wrap; imya.verticalOverflow = VerticalWrapMode.Truncate; imya.resizeTextForBestFit = true; imya.resizeTextMinSize = 13; imya.resizeTextMaxSize = 20;

            if (s.podpis != null)
            {
                var pod = _el.Tekst(rt, "podpis", 16, FontStyle.Normal, TextAnchor.MiddleLeft, ElementyHolsta.PODPIS);
                pod.rectTransform.anchorMin = pod.rectTransform.anchorMax = new Vector2(0f, 0.5f); pod.rectTransform.pivot = new Vector2(0f, 0.5f);
                pod.rectTransform.anchoredPosition = new Vector2(x, estPolosa ? 0f : -13f); pod.rectTransform.sizeDelta = new Vector2(shirinaTeksta, 26f); pod.text = s.podpis;
                pod.horizontalOverflow = HorizontalWrapMode.Wrap; pod.verticalOverflow = VerticalWrapMode.Truncate; pod.resizeTextForBestFit = true; pod.resizeTextMinSize = 11; pod.resizeTextMaxSize = 16;
            }
            if (estPolosa)
            {
                var trek = _el.Polosa(rt, shirinaTeksta, POLOSA_H, s.progress);
                trek.anchorMin = trek.anchorMax = new Vector2(0f, 0.5f); trek.pivot = new Vector2(0f, 0.5f);
                trek.anchoredPosition = new Vector2(x, -24f);
            }
            if (s.knopka != null)
            {
                var kn = s.knopka != null && s.knopka.StartsWith("⚡ ")
                    ? _el.KnopkaValyuty(rt, ElementyHolsta.MonetaIzotopov(), s.knopka.Substring(2), new Vector2(KNOPKA_W, KNOPKA_H), s.knopkaAktivna, s.deystvie)
                    : _el.Knopka(rt, s.knopka, new Vector2(KNOPKA_W, KNOPKA_H), s.knopkaAktivna, s.deystvie);
                kn.anchorMin = kn.anchorMax = new Vector2(1f, 0.5f); kn.pivot = new Vector2(1f, 0.5f); kn.anchoredPosition = new Vector2(-16f, 0f);
            }
            return go;
        }
    }

    /// <summary>
    /// Общий набор элементов комплекта заказчика с холста и фабрика типовых кусков интерфейса
    /// (девятидольная картинка с правильным множителем, кнопка knopka-a с обводкой текста, полоса
    /// bar-trek/bar-zalivka под маской, крест knopka-krest-2). Один источник для панелей рантайма.
    /// </summary>
    public sealed class ElementyHolsta
    {
        public static readonly Color KORICHNEVY = Hex("5B3B1E"), PODPIS = Hex("7A5A3A"), OBVODKA_CTA = Hex("04640C");

        private readonly Dictionary<string, Sprite> _elementy = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Sprite> _ikonki = new Dictionary<string, Sprite>();
        private Font _shrift;

        public static ElementyHolsta Sobrat(Transform holst)
        {
            var e = new ElementyHolsta();
            foreach (var img in holst.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null) continue;
                string sn = img.sprite.name;
                if (!e._elementy.ContainsKey(sn)) e._elementy[sn] = img.sprite;
                var rod = img.transform.parent;
                if (rod != null && rod.name.StartsWith("tovar-") && img.gameObject.name != rod.name) e._ikonki[rod.name.Substring(6)] = img.sprite;
            }
            // Новые элементы заказчика (партия 06.09) на холсте не лежат — берём из Resources/UI (спрайт по имени файла)
            foreach (var spr in Resources.LoadAll<Sprite>("UI")) if (spr != null && !e._elementy.ContainsKey(spr.name)) e._elementy[spr.name] = spr;
            foreach (var t in holst.GetComponentsInChildren<Text>(true)) if (t.font != null) { e._shrift = t.font; break; }
            if (e._shrift == null) e._shrift = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var net = new List<string>();
            foreach (var n in new[] { "panel", "pole-sklad", "knopka-a", "knopka-krest-2", "bar-trek", "bar-zalivka" }) if (!e._elementy.ContainsKey(n)) net.Add(n);
            if (net.Count > 0) Debug.LogWarning("[панель] на холсте нет элементов: " + string.Join(", ", net) + " — вместо них плоские подложки");
            return e;
        }

        public Sprite Ikonka(string goodId) => goodId == null ? null : _ikonki.TryGetValue(goodId, out var s) ? s : Element("ikonka-" + goodId);   // новые товары без узла на холсте — из Resources/UI/Elementy/ikonka-<id>
        public Sprite Element(string imya) => _elementy.TryGetValue(imya, out var s) ? s : null;

        /// <summary>
        /// Экранная точка → координаты холста (относительно его пивота), арифметикой.
        ///
        /// Почему не `RectTransformUtility.ScreenPointToLocalPointInRectangle`: у холста
        /// Screen Space Camera поза в мире обновляется в конце кадра, а не в LateUpdate. Пока
        /// камера стояла, разницы не было; с 08.09 камера ездит, и пересчёт в LateUpdate брал
        /// плоскость холста от ПРОШЛОГО положения камеры — значки уезжали в сторону движения на
        /// сотни пикселей и вставали на место только когда камера останавливалась (замер: при
        /// сдвиге 0,28 м за кадр расхождение 509 px, при остановке 0). Арифметика позы холста не
        /// знает вовсе и потому не зависит от того, в какой фазе кадра её позвали.
        /// </summary>
        public static Vector2 EkranVHolst(RectTransform holst, Vector2 ekran)
        {
            if (holst == null) return ekran - new Vector2(Screen.width, Screen.height) * 0.5f;
            var canvas = holst.GetComponent<Canvas>();
            float k = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            return ekran / k - Vector2.Scale(holst.rect.size, holst.pivot);
        }

        /// <summary>Девятидольная картинка элемента; без спрайта — плоская подложка запасного цвета.</summary>
        public RectTransform Kartinka(RectTransform roditel, string imya, string element, Color cvet, float mnozhitel, Color? zapasnoy = null)
        {
            var spr = Element(element);
            var go = new GameObject(imya, typeof(RectTransform)); go.transform.SetParent(roditel, false);
            var img = go.AddComponent<Image>(); img.sprite = spr; img.raycastTarget = true;
            img.color = spr != null ? cvet : (zapasnoy ?? new Color(1f, 0.97f, 0.9f, 0.85f));
            if (spr != null && spr.border.sqrMagnitude > 0f) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = mnozhitel; }
            return (RectTransform)go.transform;
        }

        public RectTransform Sprayt(RectTransform roditel, string imya, Sprite spr, float razmer)
        {
            var go = new GameObject(imya, typeof(RectTransform)); go.transform.SetParent(roditel, false);
            var img = go.AddComponent<Image>(); img.sprite = spr; img.color = Color.white; img.preserveAspect = true; img.raycastTarget = false;
            img.enabled = spr != null;
            var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(razmer, razmer);
            return rt;
        }

        public Text Tekst(RectTransform roditel, string imya, int razmer, FontStyle stil, TextAnchor yakor, Color cvet)
        {
            var go = new GameObject(imya, typeof(RectTransform)); go.transform.SetParent(roditel, false);
            var t = go.AddComponent<Text>(); t.font = _shrift; t.fontSize = razmer; t.fontStyle = stil; t.alignment = yakor; t.color = cvet;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            return t;
        }

        /// <summary>Зелёная кнопка knopka-a (450 px высоты источника) с белым текстом, обводкой и тенью, как «Расширить» на складе.</summary>
        public RectTransform Knopka(RectTransform roditel, string tekst, Vector2 razmer, bool aktivna, Action deystvie)
        {
            var kn = Kartinka(roditel, "knopka", "knopka-a", aktivna ? Color.white : new Color(0.72f, 0.72f, 0.72f, 0.9f), 450f / razmer.y, new Color(0.2f, 0.6f, 0.2f));
            kn.sizeDelta = razmer;
            var kt = Tekst(kn, "tekst", 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Rastyanut(kt.rectTransform, new Vector2(0f, 2f)); kt.rectTransform.sizeDelta = new Vector2(-22f, -12f); kt.text = tekst;
            kt.resizeTextForBestFit = true; kt.resizeTextMinSize = 11; kt.resizeTextMaxSize = 18; kt.horizontalOverflow = HorizontalWrapMode.Wrap; kt.verticalOverflow = VerticalWrapMode.Truncate;
            var ol = kt.gameObject.AddComponent<Outline>(); ol.effectColor = OBVODKA_CTA; ol.effectDistance = new Vector2(2f, -2f);
            var sh = kt.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.5f); sh.effectDistance = new Vector2(0f, -2f);
            var b = kn.gameObject.AddComponent<Button>(); b.targetGraphic = kn.GetComponent<Image>(); b.interactable = aktivna;
            b.onClick.AddListener(() => deystvie?.Invoke());
            kn.gameObject.AddComponent<NazhatieKnopki>();
            return kn;
        }

        /// <summary>
        /// Кнопка с ценой в валюте: слева монета той же валюты, что в шапке (изотопы — znachok-izotopy),
        /// справа число крупным шрифтом. Заменяет символы «⚛»/«⚡» в тексте: они мелкие и не читались
        /// (Khan 07.09: «маленькая фигура монеты и читабельный текст»).
        /// </summary>
        public RectTransform KnopkaValyuty(RectTransform roditel, Sprite moneta, string chislo, Vector2 razmer, bool aktivna, Action deystvie)
        {
            var kn = Knopka(roditel, "", razmer, aktivna, deystvie);
            var kt = kn.Find("tekst").GetComponent<Text>();
            float ik = Mathf.Round(razmer.y * 0.72f);
            kt.text = chislo; kt.alignment = TextAnchor.MiddleLeft;
            kt.resizeTextMinSize = 14; kt.resizeTextMaxSize = Mathf.RoundToInt(razmer.y * 0.62f); kt.horizontalOverflow = HorizontalWrapMode.Overflow;
            kt.rectTransform.anchorMin = new Vector2(0f, 0f); kt.rectTransform.anchorMax = new Vector2(1f, 1f); kt.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            kt.rectTransform.offsetMin = new Vector2(ik + 12f, 2f); kt.rectTransform.offsetMax = new Vector2(-6f, -2f);
            if (moneta != null)
            {
                var m = Sprayt(kn, "moneta", moneta, ik);
                m.anchorMin = m.anchorMax = new Vector2(0f, 0.5f); m.pivot = new Vector2(0f, 0.5f); m.anchoredPosition = new Vector2(7f, 1f);
            }
            return kn;
        }

        /// <summary>Монета изотопов из шапки (Resources/UI/Ikonki/znachok-izotopy).</summary>
        public static Sprite MonetaIzotopov() => Resources.Load<Sprite>("UI/Ikonki/znachok-izotopy");

        /// <summary>Полоса bar-trek с заливкой bar-zalivka под маской по доле, как бар опыта в HUD.</summary>
        public RectTransform Polosa(RectTransform roditel, float shirina, float vysota, float dolya)
        {
            var trek = Kartinka(roditel, "trek", "bar-trek", new Color(1f, 1f, 1f, 0.8f), 254f / vysota, new Color(0.35f, 0.2f, 0.1f, 0.35f));
            trek.sizeDelta = new Vector2(shirina, vysota);
            var maska = new GameObject("maska", typeof(RectTransform)); maska.transform.SetParent(trek, false);
            var mr = (RectTransform)maska.transform; mr.anchorMin = mr.anchorMax = new Vector2(0f, 0.5f); mr.pivot = new Vector2(0f, 0.5f);
            mr.anchoredPosition = Vector2.zero; mr.sizeDelta = new Vector2(shirina * Mathf.Clamp01(dolya), vysota);
            maska.AddComponent<RectMask2D>();
            var zal = Kartinka(mr, "zalivka", "bar-zalivka", Color.white, 284f / vysota, new Color(0.18f, 0.75f, 0.25f));
            zal.anchorMin = zal.anchorMax = new Vector2(0f, 0.5f); zal.pivot = new Vector2(0f, 0.5f); zal.anchoredPosition = Vector2.zero; zal.sizeDelta = new Vector2(shirina, vysota);
            return trek;
        }

        /// <summary>Крест закрытия knopka-krest-2 на кромке тела: хитбокс 84, картинка 60.</summary>
        public RectTransform Krest(RectTransform telo, Vector2 pozitsiya, Action zakryt)
        {
            var krest = new GameObject("krest", typeof(RectTransform)); krest.transform.SetParent(telo, false);
            var kr = (RectTransform)krest.transform; kr.anchorMin = kr.anchorMax = new Vector2(1f, 1f); kr.pivot = new Vector2(0.5f, 0.5f);
            kr.anchoredPosition = pozitsiya; kr.sizeDelta = new Vector2(84f, 84f);
            var kimg = krest.AddComponent<Image>(); kimg.color = new Color(1f, 1f, 1f, 0f);
            var ki = Sprayt(kr, "ikonka", Element("knopka-krest-2"), 60f);
            ki.anchorMin = ki.anchorMax = new Vector2(0.5f, 0.5f); ki.pivot = new Vector2(0.5f, 0.5f);
            if (Element("knopka-krest-2") == null) { var t = Tekst(kr, "x", 34, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.75f, 0.2f, 0.15f)); Rastyanut(t.rectTransform, Vector2.zero); t.text = "×"; }
            var kb = krest.AddComponent<Button>(); kb.targetGraphic = kimg; kb.onClick.AddListener(() => zakryt?.Invoke());
            krest.AddComponent<NazhatieKnopki>();
            return kr;
        }

        public static void Rastyanut(RectTransform rt, Vector2 sdvig)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = sdvig;
        }

        public static Color Hex(string h) => ColorUtility.TryParseHtmlString("#" + h, out var c) ? c : Color.magenta;
    }
}
