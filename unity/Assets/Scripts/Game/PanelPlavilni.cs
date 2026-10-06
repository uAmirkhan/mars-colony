using System;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Панель плавильни по эталону заказчика 06.09 (`Gemini…u1pe9n…jpg`): тёмно-синее поле, сверху воронка со льдом
    /// и бейдж «вход/20», под ней капли (только пока плавит) и полоска до следующей единицы, снизу канистра с
    /// бейджем «выход/5» (тап — забрать), в лотке — лёд со склада «×N» (тап — засыпать порцию).
    /// Воронка с огнём, капли и канистра вырезаны из панели заказчика 06.09 (`plavilnya-voronka/kapli/kanistra`), пустая канистра — `kanistra-pustaya`.
    /// </summary>
    public sealed class PanelPlavilni : MonoBehaviour
    {
        public sealed class Model
        {
            public string zagolovok;
            public int vhod, vhodMax, vyhod, vyhodMax;
            public bool rabotaet; public float progress; public string ostalos;
            public Sprite ikonkaLda; public int ldaNaSklade; public Action zagruzit;   // null — грузить нечего/некуда
            public Action zabrat;                                                      // null — канистра пуста / склад полон
            public string podskazka;
        }

        /// <summary>Ось капель, px от середины поля. Горлышко канистры в спрайте смещено вправо на 8 px из 182
        /// (в панели 5,7), носик воронки — на 3 px из 272 (2,2); капли в своём спрайте по центру. Раньше все три
        /// стояли по x=0, и струя не попадала в горлышко (Khan 07.09: «соединить в единое целое»).</summary>
        private const float OS_X = 5.7f;
        private const float SHIRINA = 620f, K_PANEL = 1042f / SHIRINA, VERH = 64f, POLE_W = 560f, POLE_H = 470f, LOTOK_H = 84f, OTSTUP_NIZ = 24f;
        private static readonly Color POLE = ElementyHolsta.Hex("2C3B4E"), LOTOK = ElementyHolsta.Hex("223142");

        private RectTransform _koren, _telo; private Text _zagolovok; private ElementyHolsta _el; private bool _gotova; private Action _priZakrytii;
        public bool Otkryta => _koren != null && _koren.gameObject.activeSelf;

        public static PanelPlavilni Obespechit()
        {
            var est = FindFirstObjectByType<PanelPlavilni>(FindObjectsInactive.Include);
            if (est != null) { if (est._el == null) est.Postroit(); return est; }
            var holst = GameObject.Find("interfeys");
            if (holst == null) return null;
            var go = new GameObject("panel-plavilni", typeof(RectTransform)); go.transform.SetParent(holst.transform, false);
            var p = go.AddComponent<PanelPlavilni>(); p.Postroit(); return p;
        }

        public Sprite Ikonka(string goodId) { if (_el == null) Postroit(); return _el != null ? _el.Ikonka(goodId) : null; }

        private void Postroit()
        {
            _koren = (RectTransform)transform;
            for (int i = _koren.childCount - 1; i >= 0; i--) Destroy(_koren.GetChild(i).gameObject);
            _koren.anchorMin = new Vector2(0.5f, 0f); _koren.anchorMax = new Vector2(0.5f, 0f); _koren.pivot = new Vector2(0.5f, 0f); _koren.anchoredPosition = new Vector2(0f, 36f);
            _el = ElementyHolsta.Sobrat(transform.parent);
            _telo = _el.Kartinka(_koren, "telo", "panel-sklad", Color.white, K_PANEL); ElementyHolsta.Rastyanut(_telo, Vector2.zero);
            _zagolovok = _el.Tekst(_telo, "zagolovok", 26, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
            var zr = _zagolovok.rectTransform; zr.anchorMin = new Vector2(0f, 1f); zr.anchorMax = new Vector2(1f, 1f); zr.pivot = new Vector2(0.5f, 1f); zr.anchoredPosition = new Vector2(0f, -18f); zr.sizeDelta = new Vector2(-150f, 36f);
            _el.Krest(_telo, new Vector2(-30f, -30f), Skryt);
            _gotova = true; _koren.gameObject.SetActive(false);
        }

        public void Pokazat(Model m, Action priZakrytii = null)
        {
            if (!_gotova || _el == null || _telo == null) Postroit();
            _priZakrytii = priZakrytii; _zagolovok.text = m.zagolovok;
            for (int i = _telo.childCount - 1; i >= 0; i--) { var c = _telo.GetChild(i); if (c.name.StartsWith("d-")) { c.gameObject.SetActive(false); Destroy(c.gameObject); } }

            var pole = Pryam(_telo, "d-pole", POLE, new Vector2(POLE_W, POLE_H), new Vector2(0f, -VERH));

            // Воронка со льдом + бейдж входа
            var vor = Kart(pole, "voronka", "plavilnya-voronka", 200f, new Vector2(OS_X - 2.2f, -8f));   // 272×250 → 200×184
            if (m.vhod == 0) vor.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.55f);

            // Капли и полоска — только пока плавит
            if (m.rabotaet)
            {
                Kart(pole, "kapli", "plavilnya-kapli", 30f, new Vector2(OS_X, -194f));   // 44×186 → 30×127, между носиком и канистрой
                var pol = _el.Polosa(pole, 180f, 8f, m.progress); pol.anchorMin = pol.anchorMax = new Vector2(0.5f, 1f); pol.pivot = new Vector2(0.5f, 1f); pol.anchoredPosition = new Vector2(150f, -240f); pol.sizeDelta = new Vector2(120f, 8f);
                var t = _el.Tekst(pole, "vremya", 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white); t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 1f); t.rectTransform.pivot = new Vector2(0.5f, 1f); t.rectTransform.anchoredPosition = new Vector2(150f, -252f); t.rectTransform.sizeDelta = new Vector2(100f, 18f); t.text = m.ostalos;
            }

            // Канистра с выходом (тап — забрать)
            // Пустая канистра — отдельный элемент заказчика; с водой — из эталона панели
            var kan = Kart(pole, "kanistra", m.vyhod == 0 && _el.Element("kanistra-pustaya") != null ? "kanistra-pustaya" : "plavilnya-kanistra", 130f, new Vector2(0f, -304f));   // 182×225 → 130×161, низ на 465 из 470
            if (m.zabrat != null)
            {
                var img = kan.GetComponent<Image>(); img.raycastTarget = true;
                var b = kan.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(() => m.zabrat?.Invoke()); kan.gameObject.AddComponent<NazhatieKnopki>();
                if (_el.Element("marker-gotovo") != null) { var mk = _el.Sprayt(kan, "gotovo", _el.Element("marker-gotovo"), 32f); mk.anchorMin = mk.anchorMax = new Vector2(1f, 1f); mk.anchoredPosition = new Vector2(-10f, -10f); }
            }

            // Лоток: лёд со склада
            float y = VERH + POLE_H + 12f;
            var lotok = Pryam(_telo, "d-lotok", LOTOK, new Vector2(POLE_W, LOTOK_H), new Vector2(0f, -y));
            var go = new GameObject("led", typeof(RectTransform)); go.transform.SetParent(lotok, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0f, 0.5f); rt.anchoredPosition = new Vector2(12f, 0f); rt.sizeDelta = new Vector2(150f, LOTOK_H - 10f);
            var fon = go.AddComponent<Image>(); fon.color = m.zagruzit != null ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0f);
            var ik = _el.Sprayt(rt, "ikonka", m.ikonkaLda, 56f); ik.anchorMin = ik.anchorMax = new Vector2(0f, 0.5f); ik.pivot = new Vector2(0f, 0.5f); ik.anchoredPosition = new Vector2(6f, 0f);
            if (m.zagruzit == null) ik.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.45f);
            var tl = _el.Tekst(rt, "chislo", 20, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white); tl.rectTransform.anchorMin = tl.rectTransform.anchorMax = new Vector2(0f, 0.5f); tl.rectTransform.pivot = new Vector2(0f, 0.5f); tl.rectTransform.anchoredPosition = new Vector2(68f, 0f); tl.rectTransform.sizeDelta = new Vector2(80f, 30f); tl.text = "×" + m.ldaNaSklade;
            var bl = go.AddComponent<Button>(); bl.targetGraphic = fon; bl.interactable = m.zagruzit != null; bl.onClick.AddListener(() => m.zagruzit?.Invoke()); go.AddComponent<NazhatieKnopki>();
            var tp = _el.Tekst(lotok, "podskazka", 14, FontStyle.Bold, TextAnchor.MiddleRight, ElementyHolsta.Hex("BFD3E6")); tp.rectTransform.anchorMin = new Vector2(0f, 0f); tp.rectTransform.anchorMax = new Vector2(1f, 1f); tp.rectTransform.offsetMin = new Vector2(170f, 0f); tp.rectTransform.offsetMax = new Vector2(-16f, 0f); tp.text = "Воронка " + m.vhod + "/" + m.vhodMax + " · канистра " + m.vyhod + "/" + m.vyhodMax + "\n" + m.podskazka; tp.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += LOTOK_H;

            _koren.sizeDelta = new Vector2(SHIRINA, y + OTSTUP_NIZ);
            _koren.gameObject.SetActive(true); _koren.SetAsLastSibling();
        }

        public void Skryt()
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return;
            _koren.gameObject.SetActive(false);
            var cb = _priZakrytii; _priZakrytii = null; cb?.Invoke();
        }

        private RectTransform Kart(RectTransform rod, string imya, string element, float shirina, Vector2 poz)
        {
            var spr = _el.Element(element);
            var rt = _el.Sprayt(rod, imya, spr, shirina);
            if (spr != null) rt.sizeDelta = new Vector2(shirina, shirina * spr.rect.height / spr.rect.width);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f); rt.anchoredPosition = poz;
            return rt;
        }

        private void Beydzh(RectTransform rod, string imya, string tekst, Vector2 poz)
        {
            var b = _el.Kartinka(rod, imya, "pilyulya-znacheniya", Color.white, 1f, new Color(0.1f, 0.12f, 0.16f, 0.9f));
            b.anchorMin = b.anchorMax = new Vector2(0.5f, 1f); b.pivot = new Vector2(0.5f, 0.5f); b.anchoredPosition = poz; b.sizeDelta = new Vector2(118f, 46f); b.GetComponent<Image>().raycastTarget = false;
            var t = _el.Tekst(b, "tekst", 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white); ElementyHolsta.Rastyanut(t.rectTransform, Vector2.zero); t.text = tekst;
        }

        private RectTransform Pryam(RectTransform rod, string imya, Color cvet, Vector2 razmer, Vector2 poz)
        {
            var rt = _el.Kartinka(rod, imya, "pole-sklad", cvet, 1f, cvet);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f); rt.anchoredPosition = poz; rt.sizeDelta = razmer; rt.GetComponent<Image>().raycastTarget = false;
            return rt;
        }
    }
}
