using System;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Общее окно «нехватка ресурсов» (эталон заказчика: Township, «Нехватка продуктов»).
    ///
    /// Появляется в момент, когда игрок нажал действие, а ресурса под него не хватает: погрузка
    /// в шаттл, запуск рецепта, отправка заказа дрону, улучшение техники. До 08.09 такие отказы
    /// уходили в консоль, а игроку доставалась мёртвая кнопка — он не понимал ни причины, ни того,
    /// сколько именно не хватает. Здесь показано ровно это: чего и сколько не достаёт и цена докупки.
    ///
    /// Окно ничего не решает само: цену и само действие покупки передаёт вызывающий (домен считает,
    /// игра списывает). Кнопка гаснет, если изотопов не хватает, но цена остаётся видимой.
    /// </summary>
    public sealed class OknoNehvatki : MonoBehaviour
    {
        public sealed class Model
        {
            public string zagolovok = "НЕХВАТКА РЕСУРСОВ";
            public string tekst = "Получите недостающие ресурсы, оплатив их изотопами.";
            public Sprite ikonka;
            public string imya;
            public int nehvataet;
            public int cena;                 // в изотопах; 0 — покупка невозможна, окно только объясняет
            public bool hvataetValyuty = true;
            public Action kupit;
        }

        private const float SHIRINA = 520f, K_PANEL = 1042f / SHIRINA;

        private RectTransform _koren, _telo;
        private ElementyHolsta _el;
        private Action _posleZakrytiya;

        public bool Otkryto => _koren != null && _koren.gameObject.activeSelf;

        public static OknoNehvatki Obespechit()
        {
            var est = FindFirstObjectByType<OknoNehvatki>(FindObjectsInactive.Include);
            if (est != null) { if (est._el == null) est.Postroit(); return est; }
            var holst = GameObject.Find("interfeys");
            if (holst == null) return null;
            var go = new GameObject("okno-nehvatki", typeof(RectTransform));
            go.transform.SetParent(holst.transform, false);
            var o = go.AddComponent<OknoNehvatki>();
            o.Postroit();
            return o;
        }

        private void Postroit()
        {
            _koren = (RectTransform)transform;
            for (int i = _koren.childCount - 1; i >= 0; i--) Destroy(_koren.GetChild(i).gameObject);
            ElementyHolsta.Rastyanut(_koren, Vector2.zero);   // на весь экран: под окном лежит затемнение
            _el = ElementyHolsta.Sobrat(transform.parent);
            _koren.gameObject.SetActive(false);
        }

        public void Pokazat(Model m, Action posleZakrytiya = null)
        {
            if (_el == null || _koren == null) Postroit();
            _posleZakrytiya = posleZakrytiya;
            for (int i = _koren.childCount - 1; i >= 0; i--) { var c = _koren.GetChild(i); c.gameObject.SetActive(false); Destroy(c.gameObject); }

            // Затемнение: тап мимо окна закрывает его — иначе на телефоне из модалки не выйти мимо креста
            ElementyHolsta.Rastyanut(_koren, Vector2.zero);
            var zat = new GameObject("zatemnenie", typeof(RectTransform));
            zat.transform.SetParent(_koren, false);
            var zrt = (RectTransform)zat.transform; ElementyHolsta.Rastyanut(zrt, Vector2.zero);
            var zi = zat.AddComponent<Image>(); zi.color = new Color(0f, 0f, 0f, 0.45f);
            var zb = zat.AddComponent<Button>(); zb.targetGraphic = zi; zb.transition = Selectable.Transition.None;
            zb.onClick.AddListener(Skryt);

            _telo = _el.Kartinka(_koren, "telo", "panel-sklad", Color.white, K_PANEL);
            _telo.anchorMin = _telo.anchorMax = new Vector2(0.5f, 0.5f); _telo.pivot = new Vector2(0.5f, 0.5f);
            _telo.anchoredPosition = Vector2.zero;
            _telo.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;   // тап по самому окну его не закрывает

            float y = 22f;
            var zag = _el.Tekst(_telo, "zagolovok", 26, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
            Stroka(zag.rectTransform, y, 36f, -110f); zag.text = m.zagolovok;
            zag.resizeTextForBestFit = true; zag.resizeTextMinSize = 14; zag.resizeTextMaxSize = 26;
            y += 36f + 10f;

            var opis = _el.Tekst(_telo, "opisanie", 17, FontStyle.Normal, TextAnchor.UpperCenter, ElementyHolsta.PODPIS);
            opis.horizontalOverflow = HorizontalWrapMode.Wrap; opis.verticalOverflow = VerticalWrapMode.Overflow;
            Stroka(opis.rectTransform, y, 46f, -80f); opis.text = m.tekst;
            y += 46f + 6f;

            if (m.ikonka != null)
            {
                var ik = _el.Sprayt(_telo, "ikonka", m.ikonka, 108f);
                ik.anchorMin = ik.anchorMax = new Vector2(0.5f, 1f); ik.pivot = new Vector2(0.5f, 1f);
                ik.anchoredPosition = new Vector2(0f, -y);
                y += 108f + 2f;
            }

            var chislo = _el.Tekst(_telo, "chislo", 34, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
            Stroka(chislo.rectTransform, y, 40f, -80f);
            chislo.text = m.nehvataet.ToString() + (string.IsNullOrEmpty(m.imya) ? "" : "  " + m.imya);
            chislo.resizeTextForBestFit = true; chislo.resizeTextMinSize = 16; chislo.resizeTextMaxSize = 34;
            y += 40f + 12f;

            if (m.cena > 0 && m.kupit != null)
            {
                var kn = _el.KnopkaValyuty(_telo, ElementyHolsta.MonetaIzotopov(), m.cena.ToString(), new Vector2(280f, 60f), m.hvataetValyuty,
                                           m.hvataetValyuty ? (Action)(() => { var d = m.kupit; Skryt(); d?.Invoke(); }) : null);
                kn.name = "knopka-kupit";
                kn.anchorMin = kn.anchorMax = new Vector2(0.5f, 1f); kn.pivot = new Vector2(0.5f, 1f);
                kn.anchoredPosition = new Vector2(0f, -y);
                y += 60f + 6f;
                if (!m.hvataetValyuty)
                {
                    var net = _el.Tekst(_telo, "net-valyuty", 15, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.Hex("C0392B"));
                    Stroka(net.rectTransform, y, 22f, -60f); net.text = "Изотопов не хватает";
                    y += 22f + 4f;
                }
            }

            _telo.sizeDelta = new Vector2(SHIRINA, y + 24f);
            _el.Krest(_telo, new Vector2(-26f, -26f), Skryt);
            _koren.gameObject.SetActive(true);
            _koren.SetAsLastSibling();
        }

        /// <summary>Строка во всю ширину окна, отступ сверху y; otstupBokov — сужение по краям.</summary>
        private static void Stroka(RectTransform rt, float y, float vysota, float otstupBokov)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -y); rt.sizeDelta = new Vector2(otstupBokov, vysota);
        }


        /// <summary>
        /// Окно держится последним в холсте, пока открыто. Панель шаттла перестраивается сама, когда
        /// меняется её модель (например, набежала энергия в строке топлива — это примерно раз в пять
        /// секунд), и вызывает `SetAsLastSibling` — карточка уезжала ПОД неё и визуально исчезала,
        /// оставаясь при этом живой и ловящей тапы (Khan 08.09).
        /// </summary>
        private void LateUpdate()
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return;
            var rod = _koren.parent;
            if (rod != null && _koren.GetSiblingIndex() != rod.childCount - 1) _koren.SetAsLastSibling();
        }

        public void Skryt()
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return;
            _koren.gameObject.SetActive(false);
            var d = _posleZakrytiya; _posleZakrytiya = null; d?.Invoke();
        }
    }
}
