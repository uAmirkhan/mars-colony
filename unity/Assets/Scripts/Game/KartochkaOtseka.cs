using System;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Карточка отсека шаттла (эталон заказчика: Township, окно вагона).
    ///
    /// Тап по контейнеру больше не грузит молча: сначала открывается эта карточка — награда за отсек,
    /// сам ресурс со счётом «есть / нужно» и одна кнопка «ЗАГРУЗИТЬ». Кнопки «Запрос» у нас нет:
    /// заказ ресурса у соседей — механика Township, которой в колонии не существует (решение Khan 08.09).
    ///
    /// Нехватка ресурса здесь не показывается ценой в изотопах: по нажатию «ЗАГРУЗИТЬ» вызывающий
    /// открывает общее окно <see cref="OknoNehvatki"/>. Так донат перестал быть единственной кнопкой,
    /// торчащей в отсеке над лежащим на складе товаром.
    /// </summary>
    public sealed class KartochkaOtseka : MonoBehaviour
    {
        public sealed class Model
        {
            public Sprite ikonka;
            public string imya;
            public int est, nuzhno;
            public int xp;
            public Action zagruzit;
        }

        private const float SHIRINA = 460f, K_PANEL = 1042f / SHIRINA;

        private RectTransform _koren, _telo;
        private ElementyHolsta _el;

        public bool Otkryta => _koren != null && _koren.gameObject.activeSelf;

        public static KartochkaOtseka Obespechit()
        {
            var est = FindFirstObjectByType<KartochkaOtseka>(FindObjectsInactive.Include);
            if (est != null) { if (est._el == null) est.Postroit(); return est; }
            var holst = GameObject.Find("interfeys");
            if (holst == null) return null;
            var go = new GameObject("kartochka-otseka", typeof(RectTransform));
            go.transform.SetParent(holst.transform, false);
            var k = go.AddComponent<KartochkaOtseka>();
            k.Postroit();
            return k;
        }

        private void Postroit()
        {
            _koren = (RectTransform)transform;
            for (int i = _koren.childCount - 1; i >= 0; i--) Destroy(_koren.GetChild(i).gameObject);
            ElementyHolsta.Rastyanut(_koren, Vector2.zero);
            _el = ElementyHolsta.Sobrat(transform.parent);
            _koren.gameObject.SetActive(false);
        }

        public void Pokazat(Model m)
        {
            if (_el == null || _koren == null) Postroit();
            for (int i = _koren.childCount - 1; i >= 0; i--) { var c = _koren.GetChild(i); c.gameObject.SetActive(false); Destroy(c.gameObject); }

            ElementyHolsta.Rastyanut(_koren, Vector2.zero);   // корень мог потерять растяжку после перезагрузки домена
            var zat = new GameObject("zatemnenie", typeof(RectTransform));
            zat.transform.SetParent(_koren, false);
            ElementyHolsta.Rastyanut((RectTransform)zat.transform, Vector2.zero);
            var zi = zat.AddComponent<Image>(); zi.color = new Color(0f, 0f, 0f, 0.35f);
            var zb = zat.AddComponent<Button>(); zb.targetGraphic = zi; zb.transition = Selectable.Transition.None;
            zb.onClick.AddListener(Skryt);

            _telo = _el.Kartinka(_koren, "telo", "panel-sklad", Color.white, K_PANEL);
            _telo.anchorMin = _telo.anchorMax = new Vector2(0.5f, 0.5f); _telo.pivot = new Vector2(0.5f, 0.5f);
            _telo.anchoredPosition = Vector2.zero;
            _telo.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            float y = 20f;
            // Награда — числом со звёздочкой опыта: букв «XP» в игре больше нигде нет (Khan 08.09).
            // Текст прижат вправо к фиксированному краю, значок стоит сразу за этим краем: позиция по
            // `preferredWidth` врала, потому что при `resizeTextForBestFit` реальный кегль меньше
            // заявленного, и звёздочка наезжала на число (Khan 08.09).
            const float MESTO_POD_ZNACHOK = 38f;
            var nagr = _el.Tekst(_telo, "nagrada", 22, FontStyle.Bold, TextAnchor.MiddleRight, ElementyHolsta.KORICHNEVY);
            nagr.rectTransform.anchorMin = new Vector2(0f, 1f); nagr.rectTransform.anchorMax = new Vector2(1f, 1f); nagr.rectTransform.pivot = new Vector2(0.5f, 1f);
            // Справа отступ под крест закрытия: строка со значком заезжала прямо под него (Khan 08.09)
            nagr.rectTransform.anchoredPosition = new Vector2(-MESTO_POD_ZNACHOK * 0.5f, -y); nagr.rectTransform.sizeDelta = new Vector2(-160f - MESTO_POD_ZNACHOK, 32f);
            nagr.text = "НАГРАДА:  " + m.xp;
            nagr.resizeTextForBestFit = true; nagr.resizeTextMinSize = 12; nagr.resizeTextMaxSize = 22;
            var zvezda = _el.Element("znachok-opyt-xp");
            if (zvezda != null)
            {
                float pravyyKray = (SHIRINA - 160f - MESTO_POD_ZNACHOK) * 0.5f - MESTO_POD_ZNACHOK * 0.5f;
                var zvrt = _el.Sprayt(_telo, "nagrada-zvezda", zvezda, 28f);
                zvrt.anchorMin = zvrt.anchorMax = new Vector2(0.5f, 1f); zvrt.pivot = new Vector2(0f, 0.5f);
                zvrt.anchoredPosition = new Vector2(pravyyKray + 6f, -y - 16f);
            }
            y += 32f + 10f;

            // Ресурс в светлой рамке, как в эталоне: слева иконка, справа «есть / нужно»
            var ramka = _el.Kartinka(_telo, "ramka-resursa", "pole-svetloe", Color.white, K_PANEL, new Color(1f, 0.97f, 0.9f, 0.9f));
            ramka.anchorMin = new Vector2(0f, 1f); ramka.anchorMax = new Vector2(1f, 1f); ramka.pivot = new Vector2(0.5f, 1f);
            ramka.anchoredPosition = new Vector2(0f, -y); ramka.sizeDelta = new Vector2(-56f, 110f);
            if (m.ikonka != null)
            {
                var ik = _el.Sprayt(ramka, "ikonka", m.ikonka, 86f);
                ik.anchorMin = ik.anchorMax = new Vector2(0f, 0.5f); ik.pivot = new Vector2(0f, 0.5f);
                ik.anchoredPosition = new Vector2(22f, 0f);
            }
            bool hvataet = m.est >= m.nuzhno;
            var schet = _el.Tekst(ramka, "schet", 30, FontStyle.Bold, TextAnchor.MiddleCenter, hvataet ? ElementyHolsta.Hex("2E7D32") : ElementyHolsta.Hex("C0392B"));
            schet.rectTransform.anchorMin = schet.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); schet.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            schet.rectTransform.anchoredPosition = new Vector2(40f, 0f); schet.rectTransform.sizeDelta = new Vector2(200f, 44f);
            schet.text = m.est + " / " + m.nuzhno;
            schet.resizeTextForBestFit = true; schet.resizeTextMinSize = 14; schet.resizeTextMaxSize = 30;
            var imya = _el.Tekst(ramka, "imya", 15, FontStyle.Normal, TextAnchor.MiddleCenter, ElementyHolsta.PODPIS);
            imya.rectTransform.anchorMin = new Vector2(0f, 0f); imya.rectTransform.anchorMax = new Vector2(1f, 0f); imya.rectTransform.pivot = new Vector2(0.5f, 0f);
            imya.rectTransform.anchoredPosition = new Vector2(0f, 6f); imya.rectTransform.sizeDelta = new Vector2(-20f, 20f);
            imya.text = m.imya;
            y += 110f + 12f;

            var kn = _el.Knopka(_telo, "ЗАГРУЗИТЬ", new Vector2(300f, 62f), true, () => { var d = m.zagruzit; Skryt(); d?.Invoke(); });
            kn.anchorMin = kn.anchorMax = new Vector2(0.5f, 1f); kn.pivot = new Vector2(0.5f, 1f);
            kn.anchoredPosition = new Vector2(0f, -y);
            y += 62f + 6f;

            _telo.sizeDelta = new Vector2(SHIRINA, y + 22f);
            _el.Krest(_telo, new Vector2(-24f, -24f), Skryt);
            _koren.gameObject.SetActive(true);
            _koren.SetAsLastSibling();
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
        }
    }
}
