using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Обучение (спека 08.09, фича 2): последовательный проход по группам объектов. На каждом шаге камера
    /// подъезжает и приближается к группе так, чтобы она была в центре, над ней качается стрелка,
    /// над карточкой цели стоит окно с двумя-тремя предложениями. Тап в любом месте ведёт дальше,
    /// «Пропустить» закрывает обучение на любом шаге.
    ///
    /// Повторяющиеся объекты идут одним шагом (все теплицы, всё жильё, обе буровые): камера и стрелка
    /// берут центр группы. Пройденное или пропущенное запоминается игрой в счётчике «obuchenie».
    /// Окно — элемент заказчика `okno-obucheniya` (Khan 08.09). Стрелка пока рисуется кодом как заглушка
    /// по прямому разрешению заказчика («пока сделай что-нибудь от себя, потом добавим иконку»).
    /// </summary>
    public sealed class Obuchenie : MonoBehaviour
    {
        public sealed class Shag
        {
            public string zagolovok, tekst;
            public Func<Bounds?> gruppa;   // габарит группы: камера идёт к центру; null — камера стоит
            public Func<List<Bounds>> chleny;   // габариты каждого объекта группы: по стрелке над каждым (Khan 08.09); null — одна стрелка над группой
        }

        // Спрайт окна 1068×482: ширина подобрана так, чтобы окно легло между карточкой цели (верх ~332) и счётчиками шапки (низ ~640)
        private const float SHIRINA = 620f, VYSOTA = SHIRINA * 482f / 1068f;
        private const float PAUZA_POSLE_PODVODA = 0.5f;
        private const float STRELKA_W = 72f, STRELKA_H = 92f, STRELKA_OTSTUP = 26f, STRELKA_KACH = 14f;

        private RectTransform _koren, _okno, _scrim, _strelka; private ElementyHolsta _el; private bool _gotova;
        private readonly List<RectTransform> _strelki = new List<RectTransform>(); private List<Bounds> _tekChleny;
        private Text _zagolovok, _tekst, _schet, _galochka;
        private List<Shag> _shagi; private int _tekushchiy = -1; private Bounds? _tekGruppa;
        private Action<Bounds> _podvesti; private Action<bool> _zavershit;   // true — пройдено, false — пропущено
        private float _gotovKDalshe; private Camera _cam; private Canvas _holst;

        public bool Idet => _koren != null && _koren.gameObject.activeSelf;

        public static Obuchenie Obespechit()
        {
            var est = FindFirstObjectByType<Obuchenie>(FindObjectsInactive.Include);
            if (est != null) { if (est._el == null) est.Postroit(); return est; }
            var holst = GameObject.Find("interfeys");
            if (holst == null) return null;
            var go = new GameObject("obuchenie", typeof(RectTransform));
            go.transform.SetParent(holst.transform, false);
            var o = go.AddComponent<Obuchenie>();
            o.Postroit();
            return o;
        }

        private void Postroit()
        {
            _koren = (RectTransform)transform;
            for (int i = _koren.childCount - 1; i >= 0; i--) Destroy(_koren.GetChild(i).gameObject);
            ElementyHolsta.Rastyanut(_koren, Vector2.zero);
            _el = ElementyHolsta.Sobrat(transform.parent);
            _holst = GetComponentInParent<Canvas>();

            // Прозрачный экран-ловушка: любой тап по нему — следующий шаг; мир под ним не кликается
            var scrimGo = new GameObject("scrim", typeof(RectTransform)); scrimGo.transform.SetParent(_koren, false);
            _scrim = (RectTransform)scrimGo.transform; ElementyHolsta.Rastyanut(_scrim, Vector2.zero);
            var si = scrimGo.AddComponent<Image>(); si.color = new Color(0f, 0f, 0f, 0f);
            var sb = scrimGo.AddComponent<Button>(); sb.transition = Selectable.Transition.None; sb.onClick.AddListener(Dalshe);

            // Стрелка над объектом шага: качается вверх-вниз, рисуется кодом (заглушка до иконки заказчика)
            var strGo = new GameObject("strelka", typeof(RectTransform)); strGo.transform.SetParent(_koren, false);
            _strelka = (RectTransform)strGo.transform; _strelka.anchorMin = _strelka.anchorMax = new Vector2(0f, 0f); _strelka.pivot = new Vector2(0.5f, 0f);
            _strelka.sizeDelta = new Vector2(STRELKA_W, STRELKA_H);
            var sprStrelka = _el.Element("strelka-obucheniya") ?? Resources.Load<Sprite>("UI/Elementy/strelka-obucheniya");
            var simg = strGo.AddComponent<Image>(); simg.sprite = sprStrelka != null ? sprStrelka : StrelkaZaglushka(); simg.raycastTarget = false; simg.preserveAspect = true;
            strGo.SetActive(false);

            string element = _el.Element("okno-obucheniya") != null ? "okno-obucheniya" : "panel-sklad";
            _okno = _el.Kartinka(_koren, "okno", element, Color.white, 1f);
            var oi = _okno.GetComponent<Image>(); oi.type = Image.Type.Simple; oi.preserveAspect = false;
            _okno.anchorMin = _okno.anchorMax = new Vector2(0f, 0f); _okno.pivot = new Vector2(0f, 0f);
            _okno.anchoredPosition = new Vector2(28f, 345f); _okno.sizeDelta = new Vector2(SHIRINA, VYSOTA);
            var ob = _okno.gameObject.AddComponent<Button>(); ob.transition = Selectable.Transition.None; ob.onClick.AddListener(Dalshe);

            // Раскладка по спрайту: рамка ~4 % по краям, крест справа сверху, квадрат чекбокса слева снизу
            _zagolovok = _el.Tekst(_okno, "zagolovok", 26, FontStyle.Bold, TextAnchor.MiddleLeft, ElementyHolsta.KORICHNEVY);
            var zr = _zagolovok.rectTransform; zr.anchorMin = new Vector2(0f, 1f); zr.anchorMax = new Vector2(1f, 1f); zr.pivot = new Vector2(0.5f, 1f);
            zr.anchoredPosition = new Vector2(0f, -26f); zr.sizeDelta = new Vector2(0f, 36f);
            zr.offsetMin = new Vector2(48f, zr.offsetMin.y); zr.offsetMax = new Vector2(-100f, zr.offsetMax.y);

            _schet = _el.Tekst(_okno, "schet", 18, FontStyle.Bold, TextAnchor.MiddleRight, ElementyHolsta.PODPIS);
            var sr = _schet.rectTransform; sr.anchorMin = sr.anchorMax = new Vector2(1f, 1f); sr.pivot = new Vector2(1f, 1f);
            sr.anchoredPosition = new Vector2(-100f, -30f); sr.sizeDelta = new Vector2(100f, 28f);

            _tekst = _el.Tekst(_okno, "tekst", 21, FontStyle.Normal, TextAnchor.UpperLeft, ElementyHolsta.KORICHNEVY);
            var tr = _tekst.rectTransform; tr.anchorMin = new Vector2(0f, 0f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 0.5f);
            tr.offsetMin = new Vector2(48f, 70f); tr.offsetMax = new Vector2(-48f, -64f);
            _tekst.horizontalOverflow = HorizontalWrapMode.Wrap; _tekst.verticalOverflow = VerticalWrapMode.Truncate;
            _tekst.resizeTextForBestFit = true; _tekst.resizeTextMinSize = 15; _tekst.resizeTextMaxSize = 21;

            // «Пропустить обучение»: квадрат нарисован в спрайте слева снизу, галочка и подпись — наши. Кнопка, а не Toggle:
            // тап ставит галочку и сразу закрывает обучение.
            var chGo = new GameObject("propustit", typeof(RectTransform)); chGo.transform.SetParent(_okno, false);
            var chr = (RectTransform)chGo.transform; chr.anchorMin = chr.anchorMax = new Vector2(0f, 0f); chr.pivot = new Vector2(0f, 0f);
            chr.anchoredPosition = new Vector2(30f, 26f); chr.sizeDelta = new Vector2(300f, 44f);
            var fon = chGo.AddComponent<Image>(); fon.color = new Color(1f, 1f, 1f, 0f);
            var kr = new GameObject("kvadrat", typeof(RectTransform)).transform as RectTransform; kr.SetParent(chr, false);
            kr.anchorMin = kr.anchorMax = new Vector2(0f, 0.5f); kr.pivot = new Vector2(0f, 0.5f); kr.anchoredPosition = new Vector2(4f, 0f); kr.sizeDelta = new Vector2(36f, 36f);
            var ki = kr.gameObject.AddComponent<Image>(); ki.color = new Color(0.93f, 0.86f, 0.72f, element == "okno-obucheniya" ? 0f : 1f); ki.raycastTarget = false;
            _galochka = _el.Tekst(kr, "galochka", 30, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.OBVODKA_CTA); ElementyHolsta.Rastyanut(_galochka.rectTransform, Vector2.zero); _galochka.text = "✓"; _galochka.enabled = false;
            var podp = _el.Tekst(chr, "podpis", 18, FontStyle.Bold, TextAnchor.MiddleLeft, ElementyHolsta.PODPIS);
            podp.rectTransform.anchorMin = new Vector2(0f, 0f); podp.rectTransform.anchorMax = new Vector2(1f, 1f); podp.rectTransform.offsetMin = new Vector2(50f, 0f); podp.rectTransform.offsetMax = Vector2.zero;
            podp.text = "Пропустить обучение";
            // Не Button: у UnityEvent кнопки в этой сборке слушатель, добавленный из Postroit, не вызывался
            // через Invoke (проверено 08.09: DynamicInvoke делегата работает, onClick.Invoke — нет). Тап ловим сами.
            chGo.AddComponent<TapObucheniya>().deystvie = () => { _galochka.enabled = true; Zakryt(false); };

            // Крест нарисован в спрайте справа сверху: невидимая кнопка над ним — то же «Пропустить»
            var krGo = new GameObject("krest", typeof(RectTransform)); krGo.transform.SetParent(_okno, false);
            var krr = (RectTransform)krGo.transform; krr.anchorMin = krr.anchorMax = new Vector2(1f, 1f); krr.pivot = new Vector2(1f, 1f);
            krr.anchoredPosition = new Vector2(-14f, -8f); krr.sizeDelta = new Vector2(64f, 64f);
            var kri = krGo.AddComponent<Image>(); kri.color = new Color(1f, 1f, 1f, 0f);
            krGo.AddComponent<TapObucheniya>().deystvie = () => Zakryt(false);

            var podskazka = _el.Tekst(_okno, "podskazka", 16, FontStyle.Italic, TextAnchor.MiddleRight, ElementyHolsta.PODPIS);
            var pr = podskazka.rectTransform; pr.anchorMin = pr.anchorMax = new Vector2(1f, 0f); pr.pivot = new Vector2(1f, 0f);
            pr.anchoredPosition = new Vector2(-40f, 30f); pr.sizeDelta = new Vector2(260f, 28f); podskazka.text = "тап по экрану — дальше";

            _gotova = true;
            _koren.gameObject.SetActive(false);
        }

        /// <summary>Жёлтая стрелка вниз с тёмной обводкой, 64×84, рисуется по маске. Заглушка до иконки заказчика.</summary>
        private static Sprite StrelkaZaglushka()
        {
            const int w = 64, h = 84;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float cx = x - w * 0.5f + 0.5f;
                // остриё внизу (y < 38): треугольник; древко сверху: полоса шириной 20
                bool ostriye = y < 38 && Mathf.Abs(cx) <= (y / 38f) * 30f;
                bool drevko = y >= 30 && Mathf.Abs(cx) <= 10f;
                bool vnutri = ostriye || drevko;
                bool kaymaO = y >= 3 && y < 41 && Mathf.Abs(cx) <= ((y - 3) / 38f) * 30f + 3f;
                bool kaymaD = y >= 30 && y < h - 3 && Mathf.Abs(cx) <= 13f;
                bool kayma = kaymaO || kaymaD;
                bool blik = vnutri && ((drevko && cx < -4f) || (ostriye && y > 8 && cx < -(y / 38f) * 30f + 8f));
                Color32 c = new Color32(0, 0, 0, 0);
                if (kayma) c = new Color32(92, 58, 12, 255);
                if (vnutri) c = blik ? new Color32(255, 236, 120, 255) : new Color32(255, 196, 0, 255);
                px[y * w + x] = c;
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 100f);
        }

        /// <summary>Запуск: список шагов, как подводить камеру к группе, что делать в конце (true — пройдено, false — пропущено).</summary>
        public void Nachat(List<Shag> shagi, Action<Bounds> podvesti, Action<bool> zavershit)
        {
            if (!_gotova || _el == null || _okno == null) Postroit();
            _shagi = shagi; _podvesti = podvesti; _zavershit = zavershit; _tekushchiy = -1; _tekGruppa = null;
            if (_galochka != null) _galochka.enabled = false;
            _cam = Camera.main;
            _koren.gameObject.SetActive(true); _koren.SetAsLastSibling();
            _gotovKDalshe = 0f;
            Dalshe();
        }

        private void Dalshe()
        {
            if (!Idet || Time.unscaledTime < _gotovKDalshe) return;   // защита от двойного тапа во время подъезда
            _tekushchiy++;
            if (_shagi == null || _tekushchiy >= _shagi.Count) { Zakryt(true); return; }
            var sh = _shagi[_tekushchiy];
            _zagolovok.text = sh.zagolovok; _tekst.text = sh.tekst; _schet.text = (_tekushchiy + 1) + " / " + _shagi.Count;
            _tekGruppa = sh.gruppa?.Invoke();
            _tekChleny = sh.chleny?.Invoke();
            if (_tekChleny != null && _tekChleny.Count == 0) _tekChleny = null;
            if (_tekGruppa.HasValue) _podvesti?.Invoke(_tekGruppa.Value);
            _strelka.gameObject.SetActive(_tekGruppa.HasValue && _tekChleny == null);
            ObespechitStrelki(_tekChleny != null ? _tekChleny.Count : 0);
            _gotovKDalshe = Time.unscaledTime + PAUZA_POSLE_PODVODA;
        }

        /// <summary>Стрелки для членов группы: копии первой, по числу объектов; лишние прячутся.</summary>
        private void ObespechitStrelki(int skolko)
        {
            while (_strelki.Count < skolko)
            {
                var go = Instantiate(_strelka.gameObject, _koren); go.name = "strelka-" + _strelki.Count;
                _strelki.Add((RectTransform)go.transform);
            }
            for (int i = 0; i < _strelki.Count; i++) _strelki[i].gameObject.SetActive(i < skolko);
            if (skolko > 0) foreach (var s in _strelki) s.SetSiblingIndex(_okno.GetSiblingIndex());   // под окном, над подложкой
        }

        private void Stavit(RectTransform strelka, Bounds b, float faza)
        {
            Vector3 verh = new Vector3(b.center.x, b.max.y, b.center.z);
            Vector3 ekran = _cam.WorldToScreenPoint(verh);
            if (ekran.z < 0f) { strelka.gameObject.SetActive(false); return; }
            if (!strelka.gameObject.activeSelf) strelka.gameObject.SetActive(true);
            var hr = (RectTransform)_holst.transform;
            Vector2 lok = ElementyHolsta.EkranVHolst(hr, ekran);
            float kach = (Mathf.Sin(faza) * 0.5f + 0.5f) * STRELKA_KACH;
            strelka.anchoredPosition = lok + hr.rect.size * 0.5f + new Vector2(0f, STRELKA_OTSTUP + kach);   // якорь стрелки — левый низ холста
            float masshtab = 1f + Mathf.Sin(faza) * 0.06f;
            strelka.localScale = new Vector3(masshtab, masshtab, 1f);
        }

        private void Update()
        {
            if (!Idet || !_tekGruppa.HasValue || _strelka == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null || _holst == null) return;
            float faza = Time.unscaledTime * 4.2f;
            if (_tekChleny != null) { for (int i = 0; i < _tekChleny.Count && i < _strelki.Count; i++) Stavit(_strelki[i], _tekChleny[i], faza); }
            else Stavit(_strelka, _tekGruppa.Value, faza);
        }

        /// <summary>Тап по элементу окна обучения: прямой вызов действия, без UnityEvent.</summary>
        private sealed class TapObucheniya : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
        {
            public Action deystvie;
            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e) => deystvie?.Invoke();
        }

        private void Zakryt(bool proydeno)
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return;
            _tekGruppa = null; _tekChleny = null;
            if (_strelka != null) _strelka.gameObject.SetActive(false);
            foreach (var s in _strelki) if (s != null) s.gameObject.SetActive(false);
            _koren.gameObject.SetActive(false);
            _zavershit?.Invoke(proydeno);
        }
    }
}
