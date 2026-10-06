using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Панель грузового шаттла по эталону заказчика (`design/etalons/V1/…x7az8n….png`): вид на шаттл сверху,
    /// открытый отсек с тремя контейнерами; в контейнере иконка товара и «×N» — белым, если на складе не хватает,
    /// зелёным, если хватает; тап по контейнеру грузит; загруженный закрыт галочкой. Ниже — награда рейса
    /// (модуль + XP). В рейсе — табло назначения и таймер с «Ускорить». Аналог поезда Township (ТЗ-поправки п.1),
    /// слово «заказ» здесь не используется. Шаттл — `shattl-verh.png` (2125×496, контейнеры по центрам x 1048/1235/1425, y 245).
    /// </summary>
    public sealed class PanelShattla : MonoBehaviour
    {
        public sealed class Konteyner
        {
            public Sprite ikonka;
            public string imya;
            public int est, nuzhno, zagruzheno;
            public int xp;
            public Action zagruzit;    // тап по отсеку: открыть карточку отсека (или забрать груз при выгрузке)
        }

        public sealed class Model
        {
            public string zagolovok;
            public List<Konteyner> konteynery = new List<Konteyner>();
            public string nagrada;            // подсказка внизу окна
            public int nagradaXp;             // награда рейса под заголовком: число и звёздочка опыта
            public string nagradaPodpis;      // та же строка без числа — на разгрузке ею подписан привезённый груз
            public bool vReyse;
            public string naznachenie;        // табло
            public string ostalos;
            public float progress;
            public string knopkaUskorit; public bool uskoritAktivna; public Action uskorit;
            public string podskazka;          // «Шаттл заходит на посадку»
            /// <summary>Составляющая топлива взлёта: иконка ресурса и «есть/нужно» (спека 08.09).</summary>
            public sealed class Toplivo { public Sprite ikonka; public string tekst; public bool hvatit; }
            public List<Toplivo> toplivo = new List<Toplivo>();   // пусто — строку топлива не рисуем
            public bool toplivoEst;
            /// <summary>Панель работает на выгрузку: в контейнерах привезённое, тап забирает (Khan 08.09).</summary>
            public bool vygruzka;
        }

        private const float SHIRINA = 900f, K_PANEL = 1042f / SHIRINA;
        // Шаттл заказчика 06.09 вечер: открытый `nokdo8` и закрытый `4wxmrx` — одна модель в одном размере (IoU контуров 0.94),
        // база 821×643; колонки отсеков по стенкам x 223/349/466/590, створки y 165..245 и 395..475, проём y 250..390
        private const float KART_ISH_W = 821f, KART_ISH_H = 643f;
        // 840 → 772: строка награды под заголовком добавила окну высоты, и заголовок уезжал за верх
        // экрана. Картинка шаттла и всё, что от неё считается (отсеки, створки), ужимаются вместе (08.09).
        private const float KART_W = 772f, KART_K = KART_W / KART_ISH_W, KART_H = KART_ISH_H * KART_K;
        private static readonly float[] CENTRY_X = { 286f, 407f, 528f };
        private const float CENTR_Y = 320f, YACH_W = 118f, YACH_H = 136f;   // ровно проём между стенками
        private static readonly float[] SLAYS_X0 = { 223f, 349f, 466f }, SLAYS_X1 = { 349f, 466f, 590f };
        private const float SLAYS_Y0 = 158f, SLAYS_Y1 = 482f;

        /// <summary>Слайс отсека i в нужном состоянии поверх базы без отсеков. Без спрайта — ничего (заглушек нет).</summary>
        private void Slays(int i, bool zakryt)
        {
            var spr = _el.Element("shattl-otsek-" + (i + 1) + (zakryt ? "-zakryt" : "-otkryt"));
            if (spr == null) return;
            var rt = _el.Sprayt(_telo, "kont-slays-" + i, spr, 10f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2((SLAYS_X0[i] - KART_ISH_W * 0.5f) * KART_K, -VERH - SLAYS_Y0 * KART_K);
            rt.sizeDelta = new Vector2((SLAYS_X1[i] - SLAYS_X0[i]) * KART_K, (SLAYS_Y1 - SLAYS_Y0) * KART_K);
            var img = rt.GetComponent<Image>(); img.preserveAspect = false; img.raycastTarget = false;
        }
        // VERH вырос с 60 до 104: под заголовком встала строка награды со звёздочкой опыта (Khan 08.09),
        // и всё, что считается от VERH — картинка шаттла, отсеки, слайсы створок — опустилось вместе с ней.
        private const float VERH = 104f, NIZ_STROKI = 64f, OTSTUP_NIZ = 26f;
        /// <summary>Иконка груза в отсеке: почти весь проём, как на эталоне выгрузки заказчика.</summary>
        private const float IKONKA_GRUZA = 96f;

        private RectTransform _koren, _telo, _kartinka;
        private Text _zagolovok;
        private ElementyHolsta _el;
        private bool _gotova;
        private Action _priZakrytii;

        public bool Otkryta => _koren != null && _koren.gameObject.activeSelf;

        public static PanelShattla Obespechit()
        {
            var est = FindFirstObjectByType<PanelShattla>(FindObjectsInactive.Include);
            if (est != null) { if (est._el == null) est.Postroit(); return est; }   // после перезагрузки домена в Play несериализуемое пусто — пересобрать до первого Ikonka()
            var holst = GameObject.Find("interfeys");
            if (holst == null) return null;
            var go = new GameObject("panel-shattla", typeof(RectTransform));
            go.transform.SetParent(holst.transform, false);
            var p = go.AddComponent<PanelShattla>();
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
            _el = ElementyHolsta.Sobrat(transform.parent);

            _telo = _el.Kartinka(_koren, "telo", "panel-sklad", Color.white, K_PANEL);
            ElementyHolsta.Rastyanut(_telo, Vector2.zero);
            _zagolovok = _el.Tekst(_telo, "zagolovok", 24, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
            _zagolovok.resizeTextForBestFit = true; _zagolovok.resizeTextMinSize = 14; _zagolovok.resizeTextMaxSize = 24;
            _zagolovok.horizontalOverflow = HorizontalWrapMode.Wrap; _zagolovok.verticalOverflow = VerticalWrapMode.Truncate;
            var zr = _zagolovok.rectTransform; zr.anchorMin = new Vector2(0f, 1f); zr.anchorMax = new Vector2(1f, 1f); zr.pivot = new Vector2(0.5f, 1f);
            zr.anchoredPosition = new Vector2(0f, -18f); zr.sizeDelta = new Vector2(-150f, 36f);
            _el.Krest(_telo, new Vector2(-30f, -30f), Skryt);

            // База без грузовых отсеков (`shattl-verh-bez-otsekov`): отсеки рисуются слайсами по состоянию (Khan 06.09: «вырежи весь отсек и подменяй»)
            _kartinka = _el.Sprayt(_telo, "shattl", _el.Element("shattl-verh-bez-otsekov") ?? _el.Element("shattl-verh"), KART_W);
            _kartinka.anchorMin = _kartinka.anchorMax = new Vector2(0.5f, 1f); _kartinka.pivot = new Vector2(0.5f, 1f);
            _kartinka.anchoredPosition = new Vector2(0f, -VERH); _kartinka.sizeDelta = new Vector2(KART_W, KART_H);
            var ki = _kartinka.GetComponent<Image>(); ki.preserveAspect = false; ki.raycastTarget = false;

            _gotova = true;
            _koren.gameObject.SetActive(false);
        }

        private string _klyuchModeli;

        /// <summary>Всё, от чего зависит раскладка панели. Таймер рейса и полоса прогресса сюда не входят: они
        /// обновляются на месте, без перестройки.</summary>
        private static string Klyuch(Model m)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(m.zagolovok).Append('|').Append(m.vReyse).Append('|').Append(m.nagrada).Append('|').Append(m.podskazka).Append('|')
              .Append(m.naznachenie).Append('|').Append(m.knopkaUskorit).Append('|').Append(m.uskoritAktivna).Append('|')
              .Append(m.toplivoEst).Append('|');
            sb.Append(m.vygruzka).Append('|').Append(m.nagradaXp).Append('|').Append(m.nagradaPodpis).Append('|');
            if (m.toplivo != null) foreach (var tp in m.toplivo) sb.Append(tp.tekst).Append(tp.hvatit ? '+' : '-');
            foreach (var k in m.konteynery)
                sb.Append(k.imya).Append(',').Append(k.est).Append(',').Append(k.nuzhno).Append(',').Append(k.zagruzheno).Append(',')
                  .Append(k.zagruzit != null).Append(';');
            return sb.ToString();
        }

        public void Pokazat(Model m, Action priZakrytii = null)
        {
            if (!_gotova || _el == null || _telo == null) Postroit();
            _priZakrytii = priZakrytii;
            string klyuch = Klyuch(m);
            if (Otkryta && klyuch == _klyuchModeli)
            {
                // Раскладка та же — перестраивать нечего. Раньше панель сносилась и собиралась заново каждые
                // 45 кадров, и это читалось как мерцание (Khan 07.09). Обновляем только живые надписи.
                var tablo = _telo.Find("stroka-tablo");
                if (tablo != null)
                {
                    var tt = tablo.Find("tekst")?.GetComponent<Text>();
                    if (tt != null) tt.text = m.naznachenie + "   ·   " + m.ostalos;
                    foreach (var img in tablo.GetComponentsInChildren<Image>(true)) if (img.type == Image.Type.Filled) img.fillAmount = m.progress;
                }
                return;
            }
            _klyuchModeli = klyuch;
            _zagolovok.text = m.zagolovok;
            for (int i = _telo.childCount - 1; i >= 0; i--) { var c = _telo.GetChild(i); if (c.name.StartsWith("kont-") || c.name.StartsWith("stroka-")) { c.gameObject.SetActive(false); Destroy(c.gameObject); } }

            if (m.nagradaXp <= 0 && !string.IsNullOrEmpty(m.nagradaPodpis))
            {
                // Разгрузка: та же строка под заголовком, но без числа — она подписывает не начисление,
                // а сам груз в контейнерах (Khan 08.09, рисунок поверх кадра).
                var np = _el.Tekst(_telo, "stroka-nagrada-verh", 22, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
                np.rectTransform.anchorMin = new Vector2(0f, 1f); np.rectTransform.anchorMax = new Vector2(1f, 1f); np.rectTransform.pivot = new Vector2(0.5f, 1f);
                np.rectTransform.anchoredPosition = new Vector2(0f, -58f); np.rectTransform.sizeDelta = new Vector2(-160f, 34f);
                np.text = m.nagradaPodpis;
                np.resizeTextForBestFit = true; np.resizeTextMinSize = 12; np.resizeTextMaxSize = 22;
            }
            if (m.nagradaXp > 0)
            {
                // «НАГРАДА ЗА РЕЙС 696 ★» под самым заголовком: раньше это висело строкой внизу вместе
                // с подсказкой и буквами XP, которых в игре больше нигде нет (Khan 08.09).
                // Текст прижат вправо к фиксированному краю, значок — сразу за ним. По `preferredWidth`
                // значок вставал криво: при автоподгонке кегля реальная ширина строки другая (Khan 08.09).
                const float MESTO_POD_ZNACHOK = 44f;
                var nr = _el.Tekst(_telo, "stroka-nagrada-verh", 22, FontStyle.Bold, TextAnchor.MiddleRight, ElementyHolsta.KORICHNEVY);
                nr.rectTransform.anchorMin = new Vector2(0f, 1f); nr.rectTransform.anchorMax = new Vector2(1f, 1f); nr.rectTransform.pivot = new Vector2(0.5f, 1f);
                nr.rectTransform.anchoredPosition = new Vector2(-MESTO_POD_ZNACHOK * 0.5f, -58f); nr.rectTransform.sizeDelta = new Vector2(-120f - MESTO_POD_ZNACHOK, 34f);
                nr.text = "НАГРАДА ЗА РЕЙС  " + m.nagradaXp;
                nr.resizeTextForBestFit = true; nr.resizeTextMinSize = 12; nr.resizeTextMaxSize = 22;
                var zv = _el.Element("znachok-opyt-xp");
                if (zv != null)
                {
                    float pravyyKray = (SHIRINA - 120f - MESTO_POD_ZNACHOK) * 0.5f - MESTO_POD_ZNACHOK * 0.5f;
                    var zi = _el.Sprayt(_telo, "stroka-nagrada-zvezda", zv, 32f);
                    zi.anchorMin = zi.anchorMax = new Vector2(0.5f, 1f); zi.pivot = new Vector2(0f, 0.5f);
                    zi.anchoredPosition = new Vector2(pravyyKray + 6f, -58f - 17f);
                }
            }

            // Сначала слайсы отсеков (под иконками и хитбоксами): заполнен или в рейсе — закрыт, иначе открыт; лишние колонки закрыты
            for (int i = 0; i < CENTRY_X.Length; i++)
                Slays(i, i >= m.konteynery.Count || m.vReyse || m.konteynery[i].zagruzheno >= m.konteynery[i].nuzhno);
            for (int i = 0; i < m.konteynery.Count && i < CENTRY_X.Length; i++) PostroitKonteyner(m.konteynery[i], i, m.vReyse, m.vygruzka);

            float y = VERH + KART_H + 8f;
            if (m.vReyse)
            {
                // Табло назначения: тёмная пилюля с буквами, справа таймер
                var tablo = _el.Kartinka(_telo, "stroka-tablo", "bar-trek", Color.white, 254f / 48f, new Color(0.12f, 0.12f, 0.12f));
                tablo.anchorMin = tablo.anchorMax = new Vector2(0.5f, 1f); tablo.pivot = new Vector2(0.5f, 1f); tablo.anchoredPosition = new Vector2(-120f, -y); tablo.sizeDelta = new Vector2(520f, 48f);
                var tt = _el.Tekst(tablo, "tekst", 20, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.Hex("FBEBBA"));
                ElementyHolsta.Rastyanut(tt.rectTransform, Vector2.zero); tt.text = m.naznachenie + "   ·   " + m.ostalos;
                tt.resizeTextForBestFit = true; tt.resizeTextMinSize = 12; tt.resizeTextMaxSize = 20;
                var pol = _el.Polosa(tablo, 480f, 8f, m.progress); pol.anchorMin = pol.anchorMax = new Vector2(0.5f, 0f); pol.pivot = new Vector2(0.5f, 1f); pol.anchoredPosition = new Vector2(0f, -4f);
                if (m.knopkaUskorit != null)
                {
                    var kn = m.knopkaUskorit.StartsWith("⚡ ")
                        ? _el.KnopkaValyuty(_telo, ElementyHolsta.MonetaIzotopov(), m.knopkaUskorit.Substring(2), new Vector2(170f, 50f), m.uskoritAktivna, m.uskorit)
                        : _el.Knopka(_telo, m.knopkaUskorit, new Vector2(170f, 50f), m.uskoritAktivna, m.uskorit);
                    kn.name = "stroka-uskorit"; kn.anchorMin = kn.anchorMax = new Vector2(0.5f, 1f); kn.pivot = new Vector2(0.5f, 1f); kn.anchoredPosition = new Vector2(250f, -y + 1f);
                }
                y += 48f + 16f;
            }
            else if (!string.IsNullOrEmpty(m.nagrada) || !string.IsNullOrEmpty(m.podskazka))
            {
                var t = _el.Tekst(_telo, "stroka-nagrada", 18, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
                t.rectTransform.anchorMin = new Vector2(0f, 1f); t.rectTransform.anchorMax = new Vector2(1f, 1f); t.rectTransform.pivot = new Vector2(0.5f, 1f);
                t.rectTransform.anchoredPosition = new Vector2(0f, -y); t.rectTransform.sizeDelta = new Vector2(-60f, 30f);
                t.text = string.IsNullOrEmpty(m.podskazka) ? m.nagrada : m.podskazka;
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = 18; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
                y += 30f + 8f;
            }
            if (m.toplivo != null && m.toplivo.Count > 0)
            {
                // Топливо взлёта — иконками ресурсов, а не словами (Khan 08.09): игрок узнаёт метан, баллон
                // и молнию по складу и HUD. Число под иконкой зелёное, когда набрано, красное — когда нет.
                // Иконки топлива крупные — вровень с грузом в отсеках: мелкие 30 px читались хуже
                // самих ресурсов, ради которых рейс и грузится (Khan 08.09).
                const float IKONKA = 62f, SHAG_TEKSTA = 6f, SHIRINA_TEKSTA = 74f, ZAZOR = 26f;
                float shagGruppy = IKONKA + SHAG_TEKSTA + SHIRINA_TEKSTA + ZAZOR;
                float vsego = shagGruppy * m.toplivo.Count - ZAZOR;
                float x = -vsego * 0.5f;
                for (int i = 0; i < m.toplivo.Count; i++)
                {
                    var tp = m.toplivo[i];
                    if (tp.ikonka != null)
                    {
                        var ik = _el.Sprayt(_telo, "stroka-toplivo-ikonka-" + i, tp.ikonka, IKONKA);
                        ik.anchorMin = new Vector2(0f, 1f); ik.anchorMax = new Vector2(0f, 1f); ik.pivot = new Vector2(0f, 1f);
                        ik.anchoredPosition = new Vector2(SHIRINA * 0.5f + x, -y);
                        var img = ik.GetComponent<Image>(); if (img != null) img.color = tp.hvatit ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                    }
                    var ch = _el.Tekst(_telo, "stroka-toplivo-chislo-" + i, 24, FontStyle.Bold, TextAnchor.MiddleLeft,
                                       tp.hvatit ? ElementyHolsta.Hex("2E7D32") : ElementyHolsta.Hex("C0392B"));
                    ch.rectTransform.anchorMin = new Vector2(0f, 1f); ch.rectTransform.anchorMax = new Vector2(0f, 1f); ch.rectTransform.pivot = new Vector2(0f, 1f);
                    ch.rectTransform.anchoredPosition = new Vector2(SHIRINA * 0.5f + x + IKONKA + SHAG_TEKSTA, -y - (IKONKA - 30f) * 0.5f);
                    ch.rectTransform.sizeDelta = new Vector2(SHIRINA_TEKSTA, 30f);
                    ch.text = tp.tekst;
                    ch.resizeTextForBestFit = true; ch.resizeTextMinSize = 12; ch.resizeTextMaxSize = 24;
                    x += shagGruppy;
                }
                y += IKONKA + 4f;

                var podpis = _el.Tekst(_telo, "stroka-toplivo-podpis", 15, FontStyle.Bold, TextAnchor.MiddleCenter,
                                       m.toplivoEst ? ElementyHolsta.Hex("2E7D32") : ElementyHolsta.Hex("C0392B"));
                podpis.rectTransform.anchorMin = new Vector2(0f, 1f); podpis.rectTransform.anchorMax = new Vector2(1f, 1f); podpis.rectTransform.pivot = new Vector2(0.5f, 1f);
                podpis.rectTransform.anchoredPosition = new Vector2(0f, -y); podpis.rectTransform.sizeDelta = new Vector2(-40f, 22f);
                podpis.text = m.toplivoEst ? "Топливо для взлёта загружено" : "Без топлива шаттл не взлетит";
                podpis.resizeTextForBestFit = true; podpis.resizeTextMinSize = 10; podpis.resizeTextMaxSize = 15;
                y += 22f + 6f;
            }
            _koren.sizeDelta = new Vector2(SHIRINA, y + OTSTUP_NIZ);
            _koren.gameObject.SetActive(true);
            _koren.SetAsLastSibling();
        }

        public void Skryt()
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return;
            _klyuchModeli = null;
            _koren.gameObject.SetActive(false);
            var cb = _priZakrytii; _priZakrytii = null;
            cb?.Invoke();
        }

        private void ZakrytKonteyner(int i) { }   // незадействованная колонка закрыта слайсом (см. Slays)

        private void PostroitKonteyner(Konteyner k, int i, bool vReyse, bool vygruzka)
        {
            // Хитбокс поверх нарисованного контейнера
            var go = new GameObject("kont-" + i, typeof(RectTransform)); go.transform.SetParent(_telo, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((CENTRY_X[i] - KART_ISH_W * 0.5f) * KART_K, -VERH - CENTR_Y * KART_K);
            rt.sizeDelta = new Vector2(YACH_W * KART_K, YACH_H * KART_K);
            var fon = go.AddComponent<Image>(); fon.color = new Color(1f, 1f, 1f, 0f);

            bool polon = !vygruzka && k.zagruzheno >= k.nuzhno;
            bool zakryt = polon || vReyse;   // закрытый отсек нарисован слайсом; иконку груза внутри не показываем
            if (!zakryt)
            {
                // Иконка во весь проём: мелкая в 44 px не читалась с телефона, а эталон заказчика
                // (`shattl-vygruzka`) показывает груз крупно, с числом прямо у иконки (Khan 08.09).
                var ik = _el.Sprayt(rt, "ikonka", k.ikonka, IKONKA_GRUZA);
                ik.anchorMin = ik.anchorMax = new Vector2(0.5f, 1f); ik.pivot = new Vector2(0.5f, 1f);
                ik.anchoredPosition = new Vector2(0f, -2f);
            }

            if (vygruzka && k.nuzhno <= 0) { PustoyOtsek(go, fon); return; }   // отсек уже разгружен: открыт и пуст
            int ostatok = Math.Max(0, k.nuzhno - k.zagruzheno);
            bool hvataet = k.est >= ostatok;
            // Число у самой иконки, справа снизу — как на эталоне. Крупное и с плотной обводкой:
            // мелкое ×N на светлом отсеке не читалось (Khan 07.09).
            var t = _el.Tekst(rt, "chislo", 30, FontStyle.Bold, TextAnchor.MiddleRight, polon || hvataet ? ElementyHolsta.Hex("7BE36B") : Color.white);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(1f, 0f); t.rectTransform.pivot = new Vector2(1f, 0f);
            t.rectTransform.anchoredPosition = new Vector2(-4f, 8f);
            t.rectTransform.sizeDelta = new Vector2(96f, 34f);
            t.text = polon ? "✓" : "×" + (vygruzka ? k.nuzhno : ostatok);
            var ol = t.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0.05f, 0.2f, 0.05f, 1f); ol.effectDistance = new Vector2(2f, -2f);
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 16; t.resizeTextMaxSize = 30;

            // Ни кнопки «ЗАГРУЗИТЬ», ни цены в изотопах внутри отсека: тап открывает карточку отсека,
            // а нехватка разбирается в общем окне (Khan 08.09). В отсеке остаются только груз и счёт.
            var b = go.AddComponent<Button>(); b.targetGraphic = fon; b.interactable = !polon && !vReyse && k.zagruzit != null;
            var cb = b.colors; cb.pressedColor = new Color(1f, 1f, 1f, 0.35f); cb.highlightedColor = new Color(1f, 1f, 1f, 0.2f); cb.disabledColor = new Color(1f, 1f, 1f, 0f); b.colors = cb;
            b.onClick.AddListener(() => k.zagruzit?.Invoke());
            go.AddComponent<NazhatieKnopki>();
        }

        /// <summary>Разгруженный отсек: створка открыта, внутри пусто, тап ничего не делает.</summary>
        private static void PustoyOtsek(GameObject go, Image fon)
        {
            var b = go.AddComponent<Button>(); b.targetGraphic = fon; b.interactable = false;
            var cb = b.colors; cb.disabledColor = new Color(1f, 1f, 1f, 0f); b.colors = cb;
        }
    }
}
