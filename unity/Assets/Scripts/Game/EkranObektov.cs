using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Список объектов (спека 08.09, фича 1): кнопка меню в правом верхнем углу открывает экран
    /// в раскладке склада — сетка плиток, на каждой иконка объекта и подпись. Тап по плитке
    /// закрывает список, подводит камеру к объекту и открывает его панель. Это не карта:
    /// заказчик просил «как товары на складе, только здания».
    ///
    /// Иконки плиток — файлы `Resources/UI/Ikonki/spisok/<klyuch>`, их генерирует заказчик.
    /// Пока файла нет, плитка показывает только подпись: своих заглушек не рисуем.
    /// Строится кодом на холсте `interfeys`, как остальные панели; переживает перезагрузку домена
    /// через Obespechit.
    /// </summary>
    public sealed class EkranObektov : MonoBehaviour
    {
        public sealed class Plitka
        {
            public string klyuch;        // имя файла иконки в Resources/UI/Ikonki/spisok
            public string podpis;        // читаемое название, сокращается при переполнении
            public Action deystvie;      // что делать по тапу
            public bool gotovo;          // зелёная галочка: есть, что собрать
        }

        private const float SHIRINA = 1400f, VYSOTA = 880f, K_PANEL = 1042f / SHIRINA;
        private const int KOLONOK = 5;
        /// <summary>Подмена ширины холста для проверки узких экранов из редактора; 0 — настоящая ширина.</summary>
        public static float ShirinaHolstaTest = 0f;
        private const float PLITKA_W = 236f, PLITKA_H = 196f, IKONKA = 118f, SHAG_X = 256f, SHAG_Y = 212f, VERH = 150f;

        private RectTransform _koren, _telo, _scrim; private ElementyHolsta _el; private bool _gotova;
        private readonly List<GameObject> _plitki = new List<GameObject>();

        public bool Otkryta => _koren != null && _koren.gameObject.activeSelf;

        public static EkranObektov Obespechit()
        {
            var est = FindFirstObjectByType<EkranObektov>(FindObjectsInactive.Include);
            if (est != null) { if (est._el == null) est.Postroit(); return est; }
            var holst = GameObject.Find("interfeys");
            if (holst == null) return null;
            var go = new GameObject("ekran-obektov", typeof(RectTransform));
            go.transform.SetParent(holst.transform, false);
            var p = go.AddComponent<EkranObektov>();
            p.Postroit();
            return p;
        }

        private void Postroit()
        {
            _koren = (RectTransform)transform;
            for (int i = _koren.childCount - 1; i >= 0; i--) Destroy(_koren.GetChild(i).gameObject);
            ElementyHolsta.Rastyanut(_koren, Vector2.zero);
            _el = ElementyHolsta.Sobrat(transform.parent);

            // Затемнение на весь экран: тап по нему закрывает список
            var scrimGo = new GameObject("scrim", typeof(RectTransform)); scrimGo.transform.SetParent(_koren, false);
            _scrim = (RectTransform)scrimGo.transform; ElementyHolsta.Rastyanut(_scrim, Vector2.zero);
            var si = scrimGo.AddComponent<Image>(); si.color = new Color(0f, 0f, 0f, 0.55f);
            var sb = scrimGo.AddComponent<Button>(); sb.transition = Selectable.Transition.None; sb.onClick.AddListener(Skryt);

            _telo = _el.Kartinka(_koren, "telo", "panel-sklad", Color.white, K_PANEL);
            _telo.anchorMin = _telo.anchorMax = new Vector2(0.5f, 0.5f); _telo.pivot = new Vector2(0.5f, 0.5f);
            _telo.anchoredPosition = Vector2.zero; _telo.sizeDelta = new Vector2(SHIRINA, VYSOTA);

            var zag = _el.Tekst(_telo, "zagolovok", 30, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
            var zr = zag.rectTransform; zr.anchorMin = new Vector2(0f, 1f); zr.anchorMax = new Vector2(1f, 1f); zr.pivot = new Vector2(0.5f, 1f);
            zr.anchoredPosition = new Vector2(0f, -22f); zr.sizeDelta = new Vector2(-160f, 40f); zag.text = "Объекты колонии";
            _el.Krest(_telo, new Vector2(-34f, -34f), Skryt);

            _gotova = true;
            _koren.gameObject.SetActive(false);
        }

        public void Pokazat(List<Plitka> plitki)
        {
            if (!_gotova || _el == null || _telo == null) Postroit();
            foreach (var p in _plitki) if (p != null) Destroy(p);
            _plitki.Clear();

            // Узкие экраны (4:3 — холст 1200 при эталонной высоте 900): панель не шире холста, колонок меньше,
            // плитки ужимаются масштабом, чтобы ряды влезли по высоте.
            var hr = (RectTransform)transform.parent;
            float shirinaHolsta = ShirinaHolstaTest > 0f ? ShirinaHolstaTest : hr.rect.width;
            float shirinaPaneli = Mathf.Min(SHIRINA, shirinaHolsta - 40f);
            _telo.sizeDelta = new Vector2(shirinaPaneli, VYSOTA);
            int kolonok = Mathf.Clamp(Mathf.FloorToInt((shirinaPaneli - 80f) / SHAG_X), 3, KOLONOK);
            int ryadov = (plitki.Count + kolonok - 1) / kolonok;
            float dostupno = VYSOTA - VERH - 80f;   // снизу рамка панели толще, чем сверху
            float k = Mathf.Min(1f, dostupno / (ryadov * SHAG_Y));
            float shagX = SHAG_X * k, shagY = SHAG_Y * k;
            float vysotaSetki = ryadov * shagY, y0 = -VERH - (dostupno - vysotaSetki) * 0.5f - shagY * 0.5f;
            for (int i = 0; i < plitki.Count; i++)
            {
                var p = plitki[i];
                int kol = i % kolonok, ryad = i / kolonok;
                // Последний неполный ряд центрируется
                int vRyadu = Mathf.Min(kolonok, plitki.Count - ryad * kolonok);
                float xr = -vRyadu * shagX * 0.5f + shagX * 0.5f + kol * shagX;
                var go = new GameObject("plitka-" + p.klyuch, typeof(RectTransform)); go.transform.SetParent(_telo, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(xr, y0 - ryad * shagY); rt.sizeDelta = new Vector2(PLITKA_W, PLITKA_H);
                rt.localScale = new Vector3(k, k, 1f);
                var fon = go.AddComponent<Image>(); fon.color = new Color(0.36f, 0.22f, 0.10f, 0.10f);
                var spr = Resources.Load<Sprite>("UI/Ikonki/spisok/" + p.klyuch);
                if (spr != null)
                {
                    var ik = _el.Sprayt(rt, "ikonka", spr, IKONKA);
                    ik.anchorMin = ik.anchorMax = new Vector2(0.5f, 1f); ik.pivot = new Vector2(0.5f, 1f); ik.anchoredPosition = new Vector2(0f, -12f);
                }
                if (p.gotovo && _el.Element("marker-gotovo") != null)
                {
                    var mk = _el.Sprayt(rt, "gotovo", _el.Element("marker-gotovo"), 40f);
                    mk.anchorMin = mk.anchorMax = new Vector2(1f, 1f); mk.pivot = new Vector2(1f, 1f); mk.anchoredPosition = new Vector2(-8f, -8f);
                }
                // Подпись в две строки максимум, ужимается до 14 px — текст не должен наезжать на соседей
                var t = _el.Tekst(rt, "podpis", 22, FontStyle.Bold, TextAnchor.MiddleCenter, ElementyHolsta.KORICHNEVY);
                t.rectTransform.anchorMin = new Vector2(0f, 0f); t.rectTransform.anchorMax = new Vector2(1f, 0f); t.rectTransform.pivot = new Vector2(0.5f, 0f);
                t.rectTransform.anchoredPosition = new Vector2(0f, 8f); t.rectTransform.sizeDelta = new Vector2(-16f, 54f);
                t.text = p.podpis; t.resizeTextForBestFit = true; t.resizeTextMinSize = 14; t.resizeTextMaxSize = 22;
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
                var b = go.AddComponent<Button>(); b.targetGraphic = fon;
                var cb = b.colors; cb.highlightedColor = new Color(1f, 1f, 1f, 0.9f); cb.pressedColor = new Color(0.6f, 0.9f, 0.6f, 0.9f); b.colors = cb;
                var d = p.deystvie; b.onClick.AddListener(() => { Skryt(); d?.Invoke(); });
                go.AddComponent<NazhatieKnopki>();
                _plitki.Add(go);
            }
            ObespechitZvuk();
            _koren.gameObject.SetActive(true);
            _koren.SetAsLastSibling();
        }

        private RectTransform _knopkaZvuka;

        /// <summary>
        /// Кнопка звука в углу меню (просьба Khan 08.09: «добавь функцию отключить звук»).
        /// Громкость живёт в PlayerPrefs, поэтому выбор переживает перезапуск игры. Своей иконки
        /// динамика в наборе заказчика нет, поэтому кнопка текстовая — рисовать её самому нельзя.
        /// </summary>
        private void ObespechitZvuk()
        {
            bool vklyuchen = MuzykaFona.Gromkost > 0f;
            if (_knopkaZvuka != null) Destroy(_knopkaZvuka.gameObject);
            _knopkaZvuka = _el.Knopka(_telo, vklyuchen ? "ЗВУК: ВКЛ" : "ЗВУК: ВЫКЛ", new Vector2(240f, 56f), true, PereklyuchitZvuk);
            _knopkaZvuka.name = "knopka-zvuka";
            // Внутри кремового поля панели, слева внизу: у рамки широкие прозрачные поля, и по верхнему
            // левому углу кнопка вылезала за окно на счётчики HUD (кадр 08.09).
            _knopkaZvuka.anchorMin = _knopkaZvuka.anchorMax = new Vector2(0f, 0f);
            _knopkaZvuka.pivot = new Vector2(0f, 0f);
            _knopkaZvuka.anchoredPosition = new Vector2(150f, 46f);
        }

        private void PereklyuchitZvuk()
        {
            bool bylVklyuchen = MuzykaFona.Gromkost > 0f;
            MuzykaFona.Gromkost = bylVklyuchen ? 0f : MuzykaFona.GROMKOST_PO_UMOLCHANIYU;
            Debug.Log("[игра] звук " + (bylVklyuchen ? "выключен" : "включён"));
            ObespechitZvuk();
        }

        public void Skryt()
        {
            if (_koren == null || !_koren.gameObject.activeSelf) return;
            _koren.gameObject.SetActive(false);
        }
    }
}
