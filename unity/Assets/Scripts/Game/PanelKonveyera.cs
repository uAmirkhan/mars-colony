using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Панель производства по референсу Township («Молокозавод», «Ткацкая фабрика», raw/Playrix/tawnship_train
    /// photo_30 и photo_35): лента с названием и крестом, под ней «конвейер» — ряд слотов очереди
    /// (пусто / ждёт / готовится с таймером / готово), ниже лоток с иконками рецептов; тап по иконке
    /// или слоту показывает карточку (имя, входы «есть/надо», XP, время, кнопка действия).
    /// Используется теплицами, фабриками и площадками добычи; списочная <see cref="PanelZdaniya"/> —
    /// для шаттла и дронов. Элементы — комплект заказчика с холста через <see cref="ElementyHolsta"/>.
    /// </summary>
    public sealed class PanelKonveyera : MonoBehaviour
    {
        public enum Sost { Pusto, Zhdet, Rabotaet, Gotovo }

        public sealed class Vhod { public Sprite ikonka; public string imya; public int est; public int nado; }

        /// <summary>Карточка над лотком: что это, что нужно, сколько ждать, что нажать.</summary>
        public sealed class Karta
        {
            public Sprite ikonka;
            public string imya;
            public List<Vhod> vhody = new List<Vhod>();
            public string vremya;          // «45 с», null — без пилюли времени
            public int xp;                 // 0 — без пилюли XP
            public string zametka;         // строка под входами («Склад полон», «Партия 2–4»)
            public string knopka;          // null — без кнопки
            public bool knopkaAktivna = true;
            public Action deystvie;
        }

        public sealed class Slot
        {
            public Sost sost;
            public Sprite ikonka;
            public string podpis;          // под иконкой: таймер, «ждёт», «готово»
            public float progress = -1f;
            public Karta karta;            // что показать по тапу (ускорить, забрать, отменить)
            public Action tap;             // прямое действие по тапу (забрать); если null — только карточка
        }

        public sealed class Recept
        {
            public string id;
            public Sprite ikonka;
            public bool dostupen = true;   // иначе иконка притушена
            public Karta karta;
        }

        public sealed class Model
        {
            public string zagolovok;
            public List<Slot> sloty = new List<Slot>();
            public List<Recept> recepty = new List<Recept>();
            public string podskazka;       // текст в лотке, когда рецептов нет
        }

        // Гладкая панель склада без ленты (Д11); заголовок внутри тела
        private const float SHIRINA = 860f, K_PANEL = 1042f / SHIRINA;
        private const float POLYA = 30f, KONV_Y = 70f, KONV_H = 164f, YACHEYKA = 118f, ZAZOR_YACH = 14f;
        private const float LOTOK_Y = KONV_Y + KONV_H + 12f, LOTOK_H = 108f, IKONKA_RECEPTA = 74f, SHAG_RECEPTA = 100f, OTSTUP_NIZ = 26f;
        private const float KARTA_W = 372f, KARTA_H = 158f;   // 372: две пилюли + кнопка в один ряд без наездов

        private static readonly Color CVET_KONV = ElementyHolsta.Hex("BCC8D0"), CVET_YACH = ElementyHolsta.Hex("2E4550"), CVET_KREM = ElementyHolsta.Hex("FBEBBA");

        private RectTransform _koren, _telo, _konveyer, _lotok, _karta;
        private Text _zagolovok;
        private ElementyHolsta _el;
        private Model _model;
        private string _vybrano;           // ключ открытой карточки: "r:<id>" или "s:<idx>"
        private bool _gotova;
        private string _podpisModeli;   // состав панели без таймеров: пока он тот же, панель не пересобирается
        private Action _priZakrytii;

        public bool Otkryta => _koren != null && _koren.gameObject.activeSelf;

        public static PanelKonveyera Obespechit()
        {
            var est = FindFirstObjectByType<PanelKonveyera>(FindObjectsInactive.Include);
            if (est != null) { if (est._el == null) est.Postroit(); return est; }   // после перезагрузки домена в Play: иконки нужны до Pokazat
            var holst = GameObject.Find("interfeys");
            if (holst == null) { Debug.LogWarning("[конвейер] холст interfeys не найден"); return null; }
            var go = new GameObject("panel-konveyera", typeof(RectTransform));
            go.transform.SetParent(holst.transform, false);
            var p = go.AddComponent<PanelKonveyera>();
            p.Postroit();
            return p;
        }

        public Sprite Ikonka(string goodId) { if (_el == null) Postroit(); return _el != null ? _el.Ikonka(goodId) : null; }   // ссылка на панель у игры переживает перезагрузку домена, _el — нет

        private void Postroit()
        {
            _koren = (RectTransform)transform;
            for (int i = _koren.childCount - 1; i >= 0; i--) Destroy(_koren.GetChild(i).gameObject);
            _koren.anchorMin = new Vector2(0.5f, 0f); _koren.anchorMax = new Vector2(0.5f, 0f); _koren.pivot = new Vector2(0.5f, 0f);
            _koren.anchoredPosition = new Vector2(0f, 36f);
            _koren.sizeDelta = new Vector2(SHIRINA, LOTOK_Y + LOTOK_H + OTSTUP_NIZ);
            _el = ElementyHolsta.Sobrat(transform.parent);

            _telo = _el.Kartinka(_koren, "telo", "panel-sklad", Color.white, K_PANEL);
            ElementyHolsta.Rastyanut(_telo, Vector2.zero);

            _zagolovok = _el.Tekst(_telo, "zagolovok", 24, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
            _zagolovok.resizeTextForBestFit = true; _zagolovok.resizeTextMinSize = 14; _zagolovok.resizeTextMaxSize = 24;
            _zagolovok.horizontalOverflow = HorizontalWrapMode.Wrap; _zagolovok.verticalOverflow = VerticalWrapMode.Truncate;
            var zr = _zagolovok.rectTransform; zr.anchorMin = new Vector2(0f, 1f); zr.anchorMax = new Vector2(1f, 1f); zr.pivot = new Vector2(0.5f, 1f);
            zr.anchoredPosition = new Vector2(0f, -18f); zr.sizeDelta = new Vector2(-150f, 36f);

            _el.Krest(_telo, new Vector2(-30f, -30f), Skryt);

            // Конвейер: светлый металл + тёмная дорожка внутри
            // Стойка-конвейер заказчика (konveyer.png 1249×553, борта 150/140): множитель по высоте, дорожка внутри уже нарисована
            _konveyer = _el.Kartinka(_telo, "konveyer", "konveyer", Color.white, 553f / KONV_H, new Color(0.72f, 0.78f, 0.82f));
            _konveyer.anchorMin = new Vector2(0f, 1f); _konveyer.anchorMax = new Vector2(1f, 1f); _konveyer.pivot = new Vector2(0.5f, 1f);
            _konveyer.anchoredPosition = new Vector2(0f, -KONV_Y); _konveyer.sizeDelta = new Vector2(-2f * POLYA, KONV_H);
            if (_el.Element("konveyer") == null)
            {
                var dorozhka = _el.Kartinka(_konveyer, "dorozhka", "bar-trek", new Color(0.55f, 0.62f, 0.68f, 0.9f), 254f / (KONV_H - 24f), new Color(0.25f, 0.32f, 0.38f));
                ElementyHolsta.Rastyanut(dorozhka, Vector2.zero); dorozhka.sizeDelta = new Vector2(-24f, -24f);
            }

            // Лоток рецептов: тёмное стекло
            _lotok = _el.Element("lotok") != null
                ? _el.Kartinka(_telo, "lotok", "lotok", Color.white, 510f / LOTOK_H)
                : _el.Kartinka(_telo, "lotok", "bar-trek", new Color(1f, 1f, 1f, 0.75f), 254f / LOTOK_H, new Color(0.15f, 0.12f, 0.1f, 0.6f));
            _lotok.anchorMin = new Vector2(0.5f, 1f); _lotok.anchorMax = new Vector2(0.5f, 1f); _lotok.pivot = new Vector2(0.5f, 1f);
            _lotok.anchoredPosition = new Vector2(0f, -LOTOK_Y); _lotok.sizeDelta = new Vector2(SHIRINA - 2f * POLYA, LOTOK_H);

            _gotova = true;
            _koren.gameObject.SetActive(false);
        }

        public void Pokazat(Model m, Action priZakrytii = null)
        {
            if (!_gotova || _el == null || _konveyer == null) Postroit();   // _el не сериализуется: после перезагрузки домена в Play строим заново
            _priZakrytii = priZakrytii;
            // Панель открыта и её состав не изменился — меняем только таймеры и полосы. Пересборка
            // сносила иконки и кнопку каждые 0,75 с, и это видно как мигание (Khan 07.09).
            string podpis = PodpisModeli(m);
            if (_koren != null && _koren.gameObject.activeSelf && podpis == _podpisModeli)
            {
                _model = m;
                ObnovitTaymery(m);
                return;
            }
            _podpisModeli = podpis;
            _model = m;
            _zagolovok.text = m.zagolovok;
            Ochistit(_konveyer, "slot-"); Ochistit(_lotok, "recept-"); Ochistit(_lotok, "podskazka");
            if (_karta != null) { Destroy(_karta.gameObject); _karta = null; }
            for (int i = _telo.childCount - 1; i >= 0; i--) if (_telo.GetChild(i).name == "karta") Destroy(_telo.GetChild(i).gameObject);

            for (int i = 0; i < m.sloty.Count; i++) PostroitSlot(m.sloty[i], i);

            float lotokW = Mathf.Max(SHAG_RECEPTA * m.recepty.Count + 26f, 240f);
            _lotok.sizeDelta = new Vector2(Mathf.Min(lotokW, SHIRINA - 2f * POLYA), LOTOK_H);
            _lotok.gameObject.SetActive(m.recepty.Count > 0 || !string.IsNullOrEmpty(m.podskazka));   // пустой лоток не показываем
            for (int i = 0; i < m.recepty.Count; i++) PostroitRecept(m.recepty[i], i, m.recepty.Count);
            if (m.recepty.Count == 0 && !string.IsNullOrEmpty(m.podskazka))
            {
                var t = _el.Tekst(_lotok, "podskazka", 20, FontStyle.Normal, TextAnchor.MiddleCenter, CVET_KREM);
                ElementyHolsta.Rastyanut(t.rectTransform, Vector2.zero); t.text = m.podskazka; t.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            // Карточка остаётся открытой между обновлениями панели (раз в 30 кадров), если её предмет ещё есть
            if (string.IsNullOrEmpty(_vybrano) || !PokazatKartu(_vybrano)) _vybrano = null;   // reload превращает null-строку в ""

            _koren.sizeDelta = new Vector2(SHIRINA, _lotok.gameObject.activeSelf ? LOTOK_Y + LOTOK_H + OTSTUP_NIZ : KONV_Y + KONV_H + OTSTUP_NIZ);
            _koren.gameObject.SetActive(true);
            _koren.SetAsLastSibling();
        }

        public void Skryt()
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return;
            _koren.gameObject.SetActive(false);
            _vybrano = null;
            _podpisModeli = null;   // следующее открытие строит панель заново
            var cb = _priZakrytii; _priZakrytii = null;
            cb?.Invoke();
        }

        /// <summary>Состав панели без того, что тикает: таймеры слотов и заполнение полос сюда не входят.
        /// Смена подписи означает, что панель надо честно пересобрать.</summary>
        private string PodpisModeli(Model m)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(m.zagolovok).Append('|').Append(m.podskazka).Append('|').Append(_vybrano).Append('|');
            foreach (var s in m.sloty)
                sb.Append((int)s.sost).Append(',').Append(s.ikonka != null ? s.ikonka.name : "-").Append(',')
                  .Append(s.progress >= 0f ? 'b' : '-').Append(',').Append(s.tap != null ? 't' : '-').Append(',')
                  .Append(PodpisKarty(s.karta)).Append(';');
            sb.Append('|');
            foreach (var r in m.recepty)
                sb.Append(r.id).Append(',').Append(r.dostupen ? '+' : 'x').Append(',').Append(PodpisKarty(r.karta)).Append(';');
            return sb.ToString();
        }

        private static string PodpisKarty(Karta k)
        {
            if (k == null) return "-";
            var sb = new System.Text.StringBuilder();
            sb.Append(k.imya).Append('/').Append(k.vremya).Append('/').Append(k.xp).Append('/').Append(k.zametka)
              .Append('/').Append(k.knopka).Append('/').Append(k.knopkaAktivna ? '+' : 'x');
            foreach (var v in k.vhody) sb.Append('/').Append(v.imya).Append(':').Append(v.est).Append(':').Append(v.nado);
            return sb.ToString();
        }

        /// <summary>Единственное, что меняется между пересборками: текст таймера на слоте и его полоса.</summary>
        private void ObnovitTaymery(Model m)
        {
            for (int i = 0; i < m.sloty.Count; i++)
            {
                var yach = _konveyer.Find("slot-" + i);
                if (yach == null) continue;
                var s = m.sloty[i];
                var pil = yach.Find("pilyulya");
                if (pil != null && !string.IsNullOrEmpty(s.podpis))
                {
                    var t = pil.Find("tekst");
                    var tt = t != null ? t.GetComponent<Text>() : null;
                    if (tt != null && tt.text != s.podpis) tt.text = s.podpis;
                }
                if (s.progress >= 0f)
                {
                    var trek = (RectTransform)yach.Find("trek");
                    var maska = trek != null ? (RectTransform)trek.Find("maska") : null;
                    if (trek != null && maska != null)
                    {
                        float w = trek.sizeDelta.x * Mathf.Clamp01(s.progress);
                        if (!Mathf.Approximately(maska.sizeDelta.x, w)) maska.sizeDelta = new Vector2(w, maska.sizeDelta.y);
                    }
                }
            }
        }

        private static void Ochistit(RectTransform rod, string prefiks)
        {
            for (int i = rod.childCount - 1; i >= 0; i--) { var c = rod.GetChild(i); if (c.name.StartsWith(prefiks)) { c.gameObject.SetActive(false); Destroy(c.gameObject); } }
        }

        private void PostroitSlot(Slot s, int i)
        {
            // Слот = открытая коробка заказчика (korobka-pustaya.png); без спрайта — тёмная ячейка
            bool estKorobka = _el.Element("korobka-pustaya") != null;
            var yach = estKorobka
                ? _el.Kartinka(_konveyer, "slot-" + i, "korobka-pustaya", Color.white, 1f)
                : _el.Kartinka(_konveyer, "slot-" + i, "pole-sklad", CVET_YACH, 736f / (YACHEYKA * 2f), new Color(0.18f, 0.27f, 0.31f));
            yach.anchorMin = yach.anchorMax = new Vector2(0f, 0.5f); yach.pivot = new Vector2(0f, 0.5f);
            yach.anchoredPosition = new Vector2(22f + i * (YACHEYKA + ZAZOR_YACH), estKorobka ? -6f : 0f); yach.sizeDelta = new Vector2(YACHEYKA, YACHEYKA);
            var img = yach.GetComponent<Image>();
            if (estKorobka) { img.type = Image.Type.Simple; img.preserveAspect = true; }

            if (s.sost == Sost.Pusto)
            {
                if (!estKorobka)
                {
                    var plus = _el.Tekst(yach, "plus", 44, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(CVET_KREM.r, CVET_KREM.g, CVET_KREM.b, 0.3f));
                    ElementyHolsta.Rastyanut(plus.rectTransform, new Vector2(0f, 2f)); plus.text = "+";
                }
            }
            else
            {
                bool gotovo = s.sost == Sost.Gotovo;
                var ik = _el.Sprayt(yach, "ikonka", s.ikonka, 66f);
                ik.anchorMin = ik.anchorMax = new Vector2(0.5f, 1f); ik.pivot = new Vector2(0.5f, 1f); ik.anchoredPosition = new Vector2(0f, estKorobka ? 6f : -10f);   // товар «в коробке», чуть выше её кромки
                if (s.sost == Sost.Zhdet) ik.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.55f);
                if (!string.IsNullOrEmpty(s.podpis))
                {
                    var pil = _el.Kartinka(yach, "pilyulya", "bar-trek", new Color(1f, 1f, 1f, 0.9f), 254f / 26f, new Color(0.1f, 0.1f, 0.1f, 0.7f));
                    pil.anchorMin = pil.anchorMax = new Vector2(0.5f, 0f); pil.pivot = new Vector2(0.5f, 0f);
                    pil.anchoredPosition = new Vector2(0f, s.progress >= 0f ? 16f : 10f); pil.sizeDelta = new Vector2(YACHEYKA - 16f, 26f);
                    if (gotovo) pil.GetComponent<Image>().color = new Color(0.35f, 0.8f, 0.35f, 0.95f);
                    var t = _el.Tekst(pil, "tekst", 14, FontStyle.Bold, TextAnchor.MiddleCenter, gotovo ? Color.white : CVET_KREM);
                    ElementyHolsta.Rastyanut(t.rectTransform, new Vector2(0f, 1f)); t.text = s.podpis;
                    t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = 14;
                }
                if (s.progress >= 0f)
                {
                    var pol = _el.Polosa(yach, YACHEYKA - 20f, 8f, s.progress);
                    pol.anchorMin = pol.anchorMax = new Vector2(0.5f, 0f); pol.pivot = new Vector2(0.5f, 0f); pol.anchoredPosition = new Vector2(0f, 5f);
                }
                if (gotovo)
                {
                    var znak = _el.Kartinka(yach, "gotovo", "knopka-a", Color.white, 450f / 34f, new Color(0.2f, 0.6f, 0.2f));
                    znak.anchorMin = znak.anchorMax = new Vector2(1f, 1f); znak.pivot = new Vector2(1f, 1f); znak.anchoredPosition = new Vector2(6f, 6f); znak.sizeDelta = new Vector2(34f, 34f);
                    var g = _el.Tekst(znak, "tekst", 22, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white); ElementyHolsta.Rastyanut(g.rectTransform, new Vector2(0f, 1f)); g.text = "✓";
                    znak.GetComponent<Image>().raycastTarget = false;
                }
            }

            int idx = i;
            var b = yach.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            var cb = b.colors; cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f); cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); b.colors = cb;
            b.onClick.AddListener(() =>
            {
                // Строки не пересобираются на каждом обновлении, поэтому действие берём из свежей
                // модели, а не из замыкания: иначе кнопка звала бы функцию прошлого состояния.
                var cur = (_model != null && idx < _model.sloty.Count) ? _model.sloty[idx] : s;
                if (cur.tap != null) { cur.tap(); return; }
                if (cur.karta != null) PereklyuchitKartu("s:" + idx);
            });
            yach.gameObject.AddComponent<NazhatieKnopki>();
        }

        private void PostroitRecept(Recept r, int i, int n)
        {
            float x0 = -(n - 1) * SHAG_RECEPTA * 0.5f;
            var go = new GameObject("recept-" + i, typeof(RectTransform)); go.transform.SetParent(_lotok, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x0 + i * SHAG_RECEPTA, 0f); rt.sizeDelta = new Vector2(SHAG_RECEPTA - 8f, LOTOK_H - 12f);
            var fon = go.AddComponent<Image>(); fon.color = new Color(1f, 1f, 1f, 0f);
            bool vybran = _vybrano == "r:" + r.id;
            if (vybran) { fon.color = new Color(1f, 1f, 1f, 0.18f); }
            var ik = _el.Sprayt(rt, "ikonka", r.ikonka, IKONKA_RECEPTA);
            ik.anchorMin = ik.anchorMax = new Vector2(0.5f, 0.5f); ik.pivot = new Vector2(0.5f, 0.5f); ik.anchoredPosition = Vector2.zero;
            if (!r.dostupen) ik.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.45f);
            if (r.ikonka == null)
            {
                var t = _el.Tekst(rt, "imya", 15, FontStyle.Bold, TextAnchor.MiddleCenter, CVET_KREM); ElementyHolsta.Rastyanut(t.rectTransform, Vector2.zero);
                t.text = r.karta?.imya ?? r.id; t.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            string klyuch = "r:" + r.id;
            var b = go.AddComponent<Button>(); b.targetGraphic = fon;
            var cb = b.colors; cb.normalColor = fon.color; cb.highlightedColor = new Color(1f, 1f, 1f, 0.25f); cb.pressedColor = new Color(1f, 1f, 1f, 0.35f); cb.selectedColor = fon.color; b.colors = cb;
            b.onClick.AddListener(() => PereklyuchitKartu(klyuch));
            go.AddComponent<NazhatieKnopki>();
        }

        private void PereklyuchitKartu(string klyuch)
        {
            _podpisModeli = null;   // карточка открывается или закрывается — панель пересобирается честно
            if (_vybrano == klyuch) { _vybrano = null; if (_karta != null) { Destroy(_karta.gameObject); _karta = null; } Pokazat(_model, _priZakrytii); return; }
            _vybrano = klyuch;
            Pokazat(_model, _priZakrytii);
        }

        /// <summary>Строит карточку по ключу; false — предмета уже нет (панель обновилась).</summary>
        private bool PokazatKartu(string klyuch)
        {
            Karta k = null; float x = 0f; float y;
            if (klyuch.Length < 3) return false;
            if (klyuch.StartsWith("r:"))
            {
                string id = klyuch.Substring(2); int i = _model.recepty.FindIndex(r => r.id == id);
                if (i < 0 || _model.recepty[i].karta == null) return false;
                k = _model.recepty[i].karta;
                x = _lotok.anchoredPosition.x - (_model.recepty.Count - 1) * SHAG_RECEPTA * 0.5f + i * SHAG_RECEPTA;
                y = -LOTOK_Y - 12f;   // низ карточки чуть заходит на лоток, верх не трогает заголовок
            }
            else
            {
                int i = int.Parse(klyuch.Substring(2));
                if (i >= _model.sloty.Count || _model.sloty[i].karta == null) return false;
                k = _model.sloty[i].karta;
                x = -SHIRINA * 0.5f + POLYA + 22f + i * (YACHEYKA + ZAZOR_YACH) + YACHEYKA * 0.5f;
                y = -KONV_Y - KONV_H * 0.5f;   // карточка над слотом: её низ у середины конвейера
            }
            x = Mathf.Clamp(x, -SHIRINA * 0.5f + KARTA_W * 0.5f + 8f, SHIRINA * 0.5f - KARTA_W * 0.5f - 8f);

            if (_karta != null) Destroy(_karta.gameObject);
            _karta = _el.Element("kartochka-podskazka") != null
                ? _el.Kartinka(_telo, "karta", "kartochka-podskazka", Color.white, 565f / (KARTA_H + 36f))   // +36: хвостик снизу вне тела карточки
                : _el.Kartinka(_telo, "karta", "pole-sklad", Color.white, 736f / (KARTA_H * 2f));
            _karta.anchorMin = _karta.anchorMax = new Vector2(0.5f, 1f); _karta.pivot = new Vector2(0.5f, 0f);
            bool sHvostikom = _el.Element("kartochka-podskazka") != null;
            _karta.anchoredPosition = new Vector2(x, y - (sHvostikom ? 6f : 0f)); _karta.sizeDelta = new Vector2(KARTA_W, KARTA_H + (sHvostikom ? 36f : 0f));
            _karta.SetAsLastSibling();

            var im = _el.Tekst(_karta, "imya", 18, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
            im.rectTransform.anchorMin = new Vector2(0f, 1f); im.rectTransform.anchorMax = new Vector2(1f, 1f); im.rectTransform.pivot = new Vector2(0.5f, 1f);
            im.rectTransform.anchoredPosition = new Vector2(0f, -10f); im.rectTransform.sizeDelta = new Vector2(-24f, 30f); im.text = k.imya;
            im.resizeTextForBestFit = true; im.resizeTextMinSize = 12; im.resizeTextMaxSize = 18; im.horizontalOverflow = HorizontalWrapMode.Wrap; im.verticalOverflow = VerticalWrapMode.Truncate;
            var cherta = new GameObject("cherta", typeof(RectTransform)); cherta.transform.SetParent(_karta, false);
            var ci = cherta.AddComponent<Image>(); ci.color = new Color(0.36f, 0.23f, 0.12f, 0.18f); ci.raycastTarget = false;
            var cr = (RectTransform)cherta.transform; cr.anchorMin = new Vector2(0f, 1f); cr.anchorMax = new Vector2(1f, 1f); cr.pivot = new Vector2(0.5f, 1f);
            cr.anchoredPosition = new Vector2(0f, -44f); cr.sizeDelta = new Vector2(-40f, 2f);

            // Входы: иконка + «есть/надо», красным если не хватает
            float shag = 92f; float x0 = -(k.vhody.Count - 1) * shag * 0.5f;
            for (int i = 0; i < k.vhody.Count; i++)
            {
                var v = k.vhody[i];
                var vi = _el.Sprayt(_karta, "vhod-" + i, v.ikonka, 36f);
                vi.anchorMin = vi.anchorMax = new Vector2(0.5f, 1f); vi.pivot = new Vector2(1f, 0.5f); vi.anchoredPosition = new Vector2(x0 + i * shag - 2f, -64f);
                bool hvataet = v.est >= v.nado;
                var vt = _el.Tekst(_karta, "vhod-t-" + i, 16, FontStyle.Bold, TextAnchor.MiddleLeft, hvataet ? ElementyHolsta.KORICHNEVY : ElementyHolsta.Hex("C0392B"));
                vt.rectTransform.anchorMin = vt.rectTransform.anchorMax = new Vector2(0.5f, 1f); vt.rectTransform.pivot = new Vector2(0f, 0.5f);
                vt.rectTransform.anchoredPosition = new Vector2(x0 + i * shag + 2f, -64f); vt.rectTransform.sizeDelta = new Vector2(52f, 26f);
                vt.text = v.est + "/" + v.nado;
                if (v.ikonka == null) vt.text = v.imya + " " + vt.text;
            }
            if (k.vhody.Count == 0 && !string.IsNullOrEmpty(k.zametka))
            {
                var z = _el.Tekst(_karta, "zametka", 15, FontStyle.Normal, TextAnchor.MiddleCenter, ElementyHolsta.PODPIS);
                z.rectTransform.anchorMin = new Vector2(0f, 1f); z.rectTransform.anchorMax = new Vector2(1f, 1f); z.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                z.rectTransform.anchoredPosition = new Vector2(0f, -64f); z.rectTransform.sizeDelta = new Vector2(-24f, 36f); z.text = k.zametka; z.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            else if (!string.IsNullOrEmpty(k.zametka))
            {
                var z = _el.Tekst(_karta, "zametka", 15, FontStyle.Normal, TextAnchor.MiddleCenter, ElementyHolsta.PODPIS);
                z.rectTransform.anchorMin = new Vector2(0f, 1f); z.rectTransform.anchorMax = new Vector2(1f, 1f); z.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                z.rectTransform.anchoredPosition = new Vector2(0f, -90f); z.rectTransform.sizeDelta = new Vector2(-24f, 22f); z.text = k.zametka;
            }

            // Низ карточки: пилюли XP и времени слева, кнопка справа
            float px = 16f; float niz = sHvostikom ? 36f : 0f;   // пилюли от левого края карточки
            if (k.xp > 0) { Pilyulya(_karta, "xp", k.xp + " XP", px, niz + 18f, ElementyHolsta.Hex("2F7BD6"), _el.Element("znachok-opyt-xp")); px += 102f; }
            if (!string.IsNullOrEmpty(k.vremya)) Pilyulya(_karta, "vremya", k.vremya, px, niz + 18f, ElementyHolsta.Hex("2E9E44"), null);
            if (k.knopka != null)
            {
                var kn = k.knopka != null && k.knopka.StartsWith("⚡ ")
                    ? _el.KnopkaValyuty(_karta, ElementyHolsta.MonetaIzotopov(), k.knopka.Substring(2), new Vector2(132f, 46f), k.knopkaAktivna, k.deystvie)
                    : _el.Knopka(_karta, k.knopka, new Vector2(132f, 46f), k.knopkaAktivna, k.deystvie);
                kn.anchorMin = kn.anchorMax = new Vector2(1f, 0f); kn.pivot = new Vector2(1f, 0f); kn.anchoredPosition = new Vector2(-14f, niz + 10f);
            }
            return true;
        }

        /// <summary>Пилюля значения заказчика (pilyulya-znacheniya.png 1448×518, гнездо под значок слева): значок в гнезде, число справа.</summary>
        private void Pilyulya(RectTransform rod, string imya, string tekst, float x, float y, Color cvet, Sprite znachok)
        {
            const float H = 34f, W = 96f;
            bool svoya = _el.Element("pilyulya-znacheniya") != null;
            var pil = svoya
                ? _el.Kartinka(rod, "pilyulya-" + imya, "pilyulya-znacheniya", Color.white, 518f / H)
                : _el.Kartinka(rod, "pilyulya-" + imya, "pole-sklad", new Color(0.95f, 0.9f, 0.78f), 736f / 60f, new Color(0.95f, 0.9f, 0.78f));
            pil.anchorMin = pil.anchorMax = new Vector2(0f, 0f); pil.pivot = new Vector2(0f, 0f); pil.anchoredPosition = new Vector2(x, y); pil.sizeDelta = new Vector2(W, H);
            pil.GetComponent<Image>().raycastTarget = false;
            float gnezdo = svoya ? 30f : 0f;
            if (znachok != null)
            {
                var z = _el.Sprayt(pil, "znachok", znachok, 24f); z.anchorMin = z.anchorMax = new Vector2(0f, 0.5f); z.pivot = new Vector2(0.5f, 0.5f); z.anchoredPosition = new Vector2(16f, 0f);
            }
            else if (svoya)
            {
                var z = _el.Tekst(pil, "znachok", 15, FontStyle.Bold, TextAnchor.MiddleCenter, cvet); z.rectTransform.anchorMin = z.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                z.rectTransform.pivot = new Vector2(0.5f, 0.5f); z.rectTransform.anchoredPosition = new Vector2(16f, 0f); z.rectTransform.sizeDelta = new Vector2(24f, 24f); z.text = "⏱";
            }
            var t = _el.Tekst(pil, "tekst", 14, FontStyle.Bold, TextAnchor.MiddleCenter, cvet);
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.rectTransform.offsetMin = new Vector2(gnezdo, 0f); t.rectTransform.offsetMax = new Vector2(-6f, 1f); t.text = tekst;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = 14;
        }
    }
}
