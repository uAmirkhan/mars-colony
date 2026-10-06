using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Панель теплицы по фото заказчика 06.09 («Теплица 1»): деревянная рама, ряды тёмного грунта, в рядах
    /// горшки; пустые горшки — терракотовые кольца, растущие — иконка культуры с полоской времени, готовые —
    /// сочная иконка (тап = собрать). После последнего горшка — коробка «+» (докупить горшок / улучшить теплицу).
    /// Снизу тёмный лоток с культурами: тап по культуре выбирает её, тап по пустому горшку сеет выбранную.
    /// Число рядов задаёт уровень теплицы (<see cref="Teplitsa.Ryadov"/>).
    /// </summary>
    public sealed class PanelTeplitsy : MonoBehaviour
    {
        public enum Sost { Pusto, Rastet, Gotovo }
        public sealed class Gorshok { public int idx; public Sost sost; public string kultura; public Sprite ikonka; public float progress; public string podpis; public Action tap; }
        public sealed class Kultura { public string id; public Sprite ikonka; public int cena; public bool hvataet; }
        public sealed class Model
        {
            public string zagolovok; public int ryadov;
            public Sprite uluchshitIkonka; public string uluchshitPodpis; public bool uluchshitAktivno; public Action uluchshit;   // кнопка улучшения справа от заголовка; null — не показывать
            public List<Gorshok> gorshki = new List<Gorshok>();
            public string plyusPodpis; public bool plyusAktiven; public Action plyus;    // «+ 70 кр» или «Улучшить · 300»
            public List<Kultura> kultury = new List<Kultura>();
            public string vybrano;                                                        // id выбранной культуры
            public Action<string> vybrat;
            public string podskazka;                                                      // строка под лотком
        }

        private const float SHIRINA = 1000f, K_PANEL = 1042f / SHIRINA;
        private const float VERH = 64f, RAMA_W = 880f, RAMA_POLE = 14f, RYAD_H = 78f, RYAD_ZAZOR = 6f, GORSHOK = 64f;
        private const float LOTOK_H = 84f, IKONKA_LOTKA = 60f, OTSTUP_NIZ = 24f;
        private static readonly Color DEREVO = ElementyHolsta.Hex("C98A4E"), GRUNT = ElementyHolsta.Hex("3B2622"), TERRAKOTA = ElementyHolsta.Hex("D4784A"), LOTOK = ElementyHolsta.Hex("2E1C18");

        private RectTransform _koren, _telo; private Text _zagolovok; private ElementyHolsta _el; private bool _gotova; private Action _priZakrytii;
        public bool Otkryta => _koren != null && _koren.gameObject.activeSelf;

        public static PanelTeplitsy Obespechit()
        {
            var est = FindFirstObjectByType<PanelTeplitsy>(FindObjectsInactive.Include);
            if (est != null) { if (est._el == null) est.Postroit(); return est; }   // после перезагрузки домена в Play несериализуемое пусто — пересобрать до первого Ikonka()
            var holst = GameObject.Find("interfeys");
            if (holst == null) return null;
            var go = new GameObject("panel-teplitsy", typeof(RectTransform)); go.transform.SetParent(holst.transform, false);
            var p = go.AddComponent<PanelTeplitsy>(); p.Postroit(); return p;
        }

        public Sprite Ikonka(string goodId) { if (_el == null) Postroit(); return _el != null ? _el.Ikonka(goodId) : null; }   // ссылка на панель у игры переживает перезагрузку домена, _el — нет

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

            if (m.uluchshit != null)
            {
                var go = new GameObject("d-uluchshit", typeof(RectTransform)); go.transform.SetParent(_telo, false);
                var urt = (RectTransform)go.transform; urt.anchorMin = urt.anchorMax = new Vector2(0.5f, 1f); urt.pivot = new Vector2(0.5f, 1f); urt.anchoredPosition = new Vector2(230f, -8f); urt.sizeDelta = new Vector2(70f, 56f);
                var ufon = go.AddComponent<Image>(); ufon.color = new Color(1f, 1f, 1f, 0f);
                var uik = _el.Sprayt(urt, "ikonka", m.uluchshitIkonka, 34f); uik.anchorMin = uik.anchorMax = new Vector2(0.5f, 1f); uik.pivot = new Vector2(0.5f, 1f); uik.anchoredPosition = new Vector2(0f, -2f);
                if (!m.uluchshitAktivno) uik.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.5f);
                var ut = _el.Tekst(urt, "cena", 12, FontStyle.Bold, TextAnchor.MiddleCenter, m.uluchshitAktivno ? ElementyHolsta.KORICHNEVY : ElementyHolsta.Hex("E06A5A"));
                ut.rectTransform.anchorMin = ut.rectTransform.anchorMax = new Vector2(0.5f, 0f); ut.rectTransform.pivot = new Vector2(0.5f, 0f); ut.rectTransform.anchoredPosition = Vector2.zero; ut.rectTransform.sizeDelta = new Vector2(90f, 16f); ut.text = m.uluchshitPodpis;
                var ub = go.AddComponent<Button>(); ub.targetGraphic = ufon; ub.interactable = m.uluchshitAktivno; ub.onClick.AddListener(() => m.uluchshit?.Invoke()); go.AddComponent<NazhatieKnopki>();
            }
            int ryadov = Mathf.Clamp(m.ryadov, 1, 4);
            // Рама с рядами грунта — картинка заказчика `rama-N` (N рядов, 06.09); ряды и колонки замерены по картинке
            var spr = _el.Element("rama-" + ryadov);
            float kr = spr != null ? RAMA_W / spr.rect.width : 1f;
            float ramaH = spr != null ? spr.rect.height * kr : RAMA_POLE * 2f + ryadov * RYAD_H;
            var rama = _el.Sprayt(_telo, "d-rama", spr, RAMA_W); rama.sizeDelta = new Vector2(RAMA_W, ramaH);
            rama.anchorMin = rama.anchorMax = new Vector2(0.5f, 1f); rama.pivot = new Vector2(0.5f, 1f); rama.anchoredPosition = new Vector2(0f, -VERH);
            _k = kr; _ryadyY = RYADY_Y[ryadov - 1]; _ishW = spr != null ? spr.rect.width : 1320f;

            int n = 0;
            foreach (var g in m.gorshki) { PostroitGorshok(rama, g, n); n++; }
            if (n < ryadov * Teplitsa.V_RYADU && m.plyusPodpis != null) PostroitPlyus(rama, m, n);

            float y = VERH + ramaH + 10f;
            float shagLotka = IKONKA_LOTKA + 22f;
            // Лоток культур — элемент заказчика `lotok-teplitsy`
            var lspr = _el.Element("lotok-teplitsy");
            float lotW = Mathf.Max(360f, m.kultury.Count * shagLotka + 90f);
            float lotH = lspr != null ? lotW * lspr.rect.height / lspr.rect.width : LOTOK_H;
            var lotok = _el.Sprayt(_telo, "d-lotok", lspr, lotW); lotok.sizeDelta = new Vector2(lotW, lotH);
            lotok.anchorMin = lotok.anchorMax = new Vector2(0.5f, 1f); lotok.pivot = new Vector2(0.5f, 1f); lotok.anchoredPosition = new Vector2(0f, -y);
            float x0 = -(m.kultury.Count - 1) * shagLotka * 0.5f;
            for (int i = 0; i < m.kultury.Count; i++)
            {
                var k = m.kultury[i]; bool vybr = k.id == m.vybrano;
                var go = new GameObject("k-" + k.id, typeof(RectTransform)); go.transform.SetParent(lotok, false);
                var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(x0 + i * shagLotka, 0f); rt.sizeDelta = new Vector2(IKONKA_LOTKA + 16f, LOTOK_H - 8f);
                var fon = go.AddComponent<Image>(); fon.color = vybr ? new Color(1f, 1f, 1f, 0.22f) : new Color(1f, 1f, 1f, 0f);
                var ik = _el.Sprayt(rt, "ikonka", k.ikonka, vybr ? IKONKA_LOTKA + 8f : IKONKA_LOTKA); ik.anchorMin = ik.anchorMax = new Vector2(0.5f, 0.5f); ik.anchoredPosition = new Vector2(0f, 4f);
                var ii = ik.GetComponent<Image>(); ii.raycastTarget = false; if (!k.hvataet) ii.color = new Color(1f, 1f, 1f, 0.45f);
                var t = _el.Tekst(rt, "cena", 12, FontStyle.Bold, TextAnchor.MiddleCenter, k.hvataet ? ElementyHolsta.Hex("FBEBBA") : ElementyHolsta.Hex("E06A5A"));
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0f); t.rectTransform.pivot = new Vector2(0.5f, 0f); t.rectTransform.anchoredPosition = new Vector2(0f, 2f); t.rectTransform.sizeDelta = new Vector2(80f, 16f); t.text = k.cena + " кр";
                var b = go.AddComponent<Button>(); b.targetGraphic = fon; string id = k.id; b.onClick.AddListener(() => m.vybrat?.Invoke(id)); go.AddComponent<NazhatieKnopki>();
            }
            y += lotH + 4f;
            if (!string.IsNullOrEmpty(m.podskazka))
            {
                var t = _el.Tekst(_telo, "d-podskazka", 16, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.PODPIS);
                t.rectTransform.anchorMin = new Vector2(0f, 1f); t.rectTransform.anchorMax = new Vector2(1f, 1f); t.rectTransform.pivot = new Vector2(0.5f, 1f); t.rectTransform.anchoredPosition = new Vector2(0f, -y); t.rectTransform.sizeDelta = new Vector2(-60f, 24f); t.text = m.podskazka;
                y += 24f;
            }
            _koren.sizeDelta = new Vector2(SHIRINA, y + OTSTUP_NIZ);
            _koren.gameObject.SetActive(true); _koren.SetAsLastSibling();
        }

        public void Skryt()
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return;
            _koren.gameObject.SetActive(false);
            var cb = _priZakrytii; _priZakrytii = null; cb?.Invoke();
        }

        // Центры рядов грунта (px исходной картинки rama-N) и границы грунта по x — замер 06.09
        private static readonly float[][] RYADY_Y = { new[] { 95f }, new[] { 83f, 229f }, new[] { 95f, 252f, 405f }, new[] { 73f, 183f, 293f, 417f } };
        private const float GRUNT_X0 = 45f, GRUNT_X1 = 1275f;
        private float _k = 1f, _ishW = 1320f; private float[] _ryadyY = RYADY_Y[1];

        private Vector2 PozitsiyaGorshka(int n)
        {
            int r = Mathf.Min(n / Teplitsa.V_RYADU, _ryadyY.Length - 1), c = n % Teplitsa.V_RYADU;
            float shag = (GRUNT_X1 - GRUNT_X0) / Teplitsa.V_RYADU;
            return new Vector2((GRUNT_X0 + shag * (c + 0.5f) - _ishW * 0.5f) * _k, -_ryadyY[r] * _k);
        }

        private void PostroitGorshok(RectTransform rama, Gorshok g, int n)
        {
            var go = new GameObject("g-" + g.idx, typeof(RectTransform)); go.transform.SetParent(rama, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = PozitsiyaGorshka(n); rt.sizeDelta = new Vector2(GORSHOK + 16f, GORSHOK + 20f);
            var fon = go.AddComponent<Image>(); fon.color = new Color(1f, 1f, 1f, 0f);
            // Горшки заказчика 06.09: пустой `gorshok`, росток `gorshok-rostok` (любая культура), спелый `gorshok-spelyy-<good_id>`.
            // Нет спелого спрайта для культуры — пустой горшок + иконка товара. Своих заглушек нет: нет спрайта — нет горшка.
            Sprite sprGor = _el.Element("gorshok"); bool sPlant = false;
            // Грибы первой стадии не имеют — растут сразу грибами (заказчик 06.09)
            if (g.sost == Sost.Rastet && g.kultura == "mushrooms" && _el.Element("gorshok-spelyy-mushrooms") != null) { sprGor = _el.Element("gorshok-spelyy-mushrooms"); sPlant = true; }
            else if (g.sost == Sost.Rastet && _el.Element("gorshok-rostok") != null) { sprGor = _el.Element("gorshok-rostok"); sPlant = true; }
            else if (g.sost == Sost.Gotovo && g.kultura != null && _el.Element("gorshok-spelyy-" + g.kultura) != null) { sprGor = _el.Element("gorshok-spelyy-" + g.kultura); sPlant = true; }
            var gor = _el.Sprayt(rt, "gorshok", sprGor, GORSHOK);
            gor.anchorMin = gor.anchorMax = new Vector2(0.5f, 0.5f); gor.pivot = new Vector2(0.5f, 0f); gor.GetComponent<Image>().raycastTarget = false;
            // Ширина горшка одна на все состояния; растение над горшком добавляет высоту вверх, дно горшка на одной линии
            // В спелых картинках сам горшок занимает ~70% ширины кадра — ширину кадра поднимаем, чтобы горшки всех состояний были одного размера
            float gw = sPlant && g.sost == Sost.Gotovo ? GORSHOK * 1.35f : GORSHOK;
            float gh = sprGor != null ? gw * sprGor.rect.height / sprGor.rect.width : gw;
            gor.sizeDelta = new Vector2(gw, gh); gor.anchoredPosition = new Vector2(0f, -GORSHOK * 0.5f);
            if (g.sost != Sost.Pusto && g.ikonka != null && !sPlant)
            {
                var ik = _el.Sprayt(rt, "ikonka", g.ikonka, g.sost == Sost.Gotovo ? GORSHOK : GORSHOK * 0.8f); ik.anchorMin = ik.anchorMax = new Vector2(0.5f, 0.5f); ik.anchoredPosition = new Vector2(0f, g.sost == Sost.Gotovo ? 8f : 6f);
                var ii = ik.GetComponent<Image>(); ii.raycastTarget = false; if (g.sost == Sost.Rastet) ii.color = new Color(0.8f, 0.85f, 0.8f, 0.9f);
            }
            if (g.sost == Sost.Rastet)
            {
                var pol = _el.Polosa(rt, GORSHOK, 7f, g.progress); pol.anchorMin = pol.anchorMax = new Vector2(0.5f, 0f); pol.pivot = new Vector2(0.5f, 0f); pol.anchoredPosition = new Vector2(0f, 2f);
                if (g.podpis != null)
                {
                    var t = _el.Tekst(rt, "vremya", 11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
                    t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0f); t.rectTransform.pivot = new Vector2(0.5f, 0f); t.rectTransform.anchoredPosition = new Vector2(0f, 10f); t.rectTransform.sizeDelta = new Vector2(80f, 14f); t.text = g.podpis;
                    t.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
                }
            }
            if (g.sost == Sost.Gotovo && _el.Element("marker-gotovo") != null)
            {
                var mk = _el.Sprayt(rt, "gotovo", _el.Element("marker-gotovo"), 22f); mk.anchorMin = mk.anchorMax = new Vector2(1f, 1f); mk.anchoredPosition = new Vector2(-8f, -8f); mk.GetComponent<Image>().raycastTarget = false;
            }
            var b = go.AddComponent<Button>(); b.targetGraphic = fon; b.interactable = g.tap != null;
            var cb = b.colors; cb.pressedColor = new Color(1f, 1f, 1f, 0.35f); cb.highlightedColor = new Color(1f, 1f, 1f, 0.15f); cb.disabledColor = new Color(1f, 1f, 1f, 0f); b.colors = cb;
            b.onClick.AddListener(() => g.tap?.Invoke()); go.AddComponent<NazhatieKnopki>();
        }

        private void PostroitPlyus(RectTransform rama, Model m, int n)
        {
            var go = new GameObject("g-plyus", typeof(RectTransform)); go.transform.SetParent(rama, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = PozitsiyaGorshka(n); rt.sizeDelta = new Vector2(GORSHOK + 20f, GORSHOK + 20f);
            var fon = go.AddComponent<Image>(); fon.color = new Color(1f, 1f, 1f, 0f);
            // Коробка «+» — из фото заказчика (`gorshok-plus.png`)
            var kor = _el.Sprayt(rt, "korobka", _el.Element("gorshok-plus"), GORSHOK + 4f);
            kor.anchorMin = kor.anchorMax = new Vector2(0.5f, 0.5f); kor.pivot = new Vector2(0.5f, 0.5f); kor.anchoredPosition = new Vector2(0f, 2f); kor.GetComponent<Image>().raycastTarget = false;
            if (!m.plyusAktiven) kor.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.5f);
            var t = _el.Tekst(rt, "cena", 11, FontStyle.Bold, TextAnchor.MiddleCenter, m.plyusAktiven ? ElementyHolsta.Hex("FBEBBA") : ElementyHolsta.Hex("E06A5A"));
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0f); t.rectTransform.pivot = new Vector2(0.5f, 0f); t.rectTransform.anchoredPosition = new Vector2(0f, 1f); t.rectTransform.sizeDelta = new Vector2(110f, 14f); t.text = m.plyusPodpis;
            t.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            var b = go.AddComponent<Button>(); b.targetGraphic = fon; b.interactable = m.plyusAktiven; b.onClick.AddListener(() => m.plyus?.Invoke()); go.AddComponent<NazhatieKnopki>();
        }

        private RectTransform Pryamougolnik(RectTransform roditel, string imya, Color cvet, Vector2 razmer, Vector2 poz)
        {
            var rt = _el.Kartinka(roditel, imya, "pole-sklad", cvet, 1f, cvet);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f); rt.anchoredPosition = poz; rt.sizeDelta = razmer;
            rt.GetComponent<Image>().raycastTarget = false;
            return rt;
        }
    }
}
