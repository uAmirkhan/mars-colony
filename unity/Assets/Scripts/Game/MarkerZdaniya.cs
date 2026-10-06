using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Пузырь над интерактивным объектом (заказчик 06.09: «я бы не догадался, что дроны и машины можно
    /// запустить»). Язык Township: простаивает — пузырь с иконкой того, что здесь можно сделать
    /// (карточка-подсказка заказчика как подложка), работает — песочные часы с остатком времени
    /// (marker-zhdat), готово — галочка (marker-gotovo). Пузырь подпрыгивает и по тапу открывает панель здания.
    /// Состояние даёт <see cref="IgraKolonii.SostoyanieMarkera"/>.
    /// </summary>
    public sealed class MarkerZdaniya
    {
        public enum Rezhim { Skryt, Prostoy, Rabota, Gotovo }

        private readonly BuildingClickTarget _zdanie;
        public BuildingClickTarget Zdanie => _zdanie;
        private readonly IgraKolonii _igra;
        private readonly RectTransform _rt, _holst, _pusyr, _ikonka;
        private readonly Image _pusyrImg, _ikonkaImg;
        private readonly Text _tekst;
        private readonly Sprite _prostoy, _rabota, _gotovo;
        private readonly float _faza;

        public static MarkerZdaniya Sozdat(BuildingClickTarget zdanie, IgraKolonii igra, ElementyHolsta el)
        {
            var holst = GameObject.Find("interfeys");
            string imya = "marker-" + zdanie.gameObject.name;
            var staryy = holst.transform.Find(imya);
            if (staryy != null) Object.Destroy(staryy.gameObject);
            var go = new GameObject(imya, typeof(RectTransform));
            go.transform.SetParent(holst.transform, false);
            var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(96f, 120f); rt.pivot = new Vector2(0.5f, 0f);

            var pGo = new GameObject("pusyr", typeof(RectTransform)); pGo.transform.SetParent(rt, false);
            var pImg = pGo.AddComponent<Image>(); pImg.preserveAspect = true; pImg.raycastTarget = true;
            var pr = (RectTransform)pGo.transform; pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f); pr.pivot = new Vector2(0.5f, 0f); pr.anchoredPosition = new Vector2(0f, 24f); pr.sizeDelta = new Vector2(84f, 84f);
            var kn = pGo.AddComponent<Button>(); kn.targetGraphic = pImg; kn.transition = Selectable.Transition.None;
            var vybor = Object.FindFirstObjectByType<VyborZdaniy>();
            kn.onClick.AddListener(() => { if (vybor != null) vybor.SendMessage("Vybrat", zdanie, SendMessageOptions.DontRequireReceiver); });

            var iGo = new GameObject("ikonka", typeof(RectTransform)); iGo.transform.SetParent(pr, false);
            var iImg = iGo.AddComponent<Image>(); iImg.preserveAspect = true; iImg.raycastTarget = false;
            var ir = (RectTransform)iGo.transform; ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0.5f); ir.pivot = new Vector2(0.5f, 0.5f); ir.anchoredPosition = new Vector2(0f, 7f); ir.sizeDelta = new Vector2(52f, 52f);

            var tGo = new GameObject("tekst", typeof(RectTransform)); tGo.transform.SetParent(rt, false);
            var t = tGo.AddComponent<Text>(); t.alignment = TextAnchor.MiddleCenter; t.fontSize = 19; t.fontStyle = FontStyle.Bold; t.color = Color.white; t.raycastTarget = false;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ol = tGo.AddComponent<Outline>(); ol.effectColor = new Color(0.2f, 0.1f, 0.05f, 0.9f); ol.effectDistance = new Vector2(1.2f, -1.2f);
            var tr = (RectTransform)tGo.transform; tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f); tr.pivot = new Vector2(0.5f, 0f); tr.anchoredPosition = Vector2.zero; tr.sizeDelta = new Vector2(120f, 26f);
            rt.SetAsFirstSibling();
            return new MarkerZdaniya(zdanie, igra, rt, (RectTransform)holst.transform, pr, pImg, ir, iImg, t,
                el.Element("kartochka-podskazka"), el.Element("marker-zhdat"), el.Element("marker-gotovo"));
        }

        private MarkerZdaniya(BuildingClickTarget z, IgraKolonii i, RectTransform rt, RectTransform holst, RectTransform pusyr, Image pusyrImg, RectTransform ikonka, Image ikonkaImg, Text t, Sprite prostoy, Sprite rabota, Sprite gotovo)
        {
            _zdanie = z; _igra = i; _rt = rt; _holst = holst; _pusyr = pusyr; _pusyrImg = pusyrImg; _ikonka = ikonka; _ikonkaImg = ikonkaImg; _tekst = t;
            _prostoy = prostoy; _rabota = rabota; _gotovo = gotovo; _faza = Random.value * 6.28f;
        }

        private Rezhim _rezhim; private Sprite _ikonkaTek; private string _tekstTek;
        public Vector2 EkrannayaTochka { get; private set; }

        public void Skryt() { if (_rt != null && _rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); }

        /// <summary>Фаза 1: решить, нужен ли маркер, и посчитать экранную точку. Пустой пузырь (без иконки) запрещён — п.3.7.</summary>
        public bool Podgotovit(double now)
        {
            if (_zdanie == null || _rt == null) return false;
            _rezhim = _igra.SostoyanieMarkera(_zdanie, now, out _ikonkaTek, out _tekstTek);
            if (_rezhim == Rezhim.Prostoy && (_ikonkaTek == null || _prostoy == null)) _rezhim = Rezhim.Skryt;
            if (_rezhim == Rezhim.Gotovo && _gotovo == null) _rezhim = Rezhim.Skryt;
            if (_rezhim == Rezhim.Skryt) { Skryt(); return false; }
            var cam = _igra.Kamera; if (cam == null) { Skryt(); return false; }
            // Верх центра габарита, а не «самая верхняя вершина» (в изометрии это дальний угол, маркер уезжал от силуэта — Khan 06.09)
            var gb = _zdanie.ClickBounds; Vector3 ekran = cam.WorldToScreenPoint(new Vector3(gb.center.x, gb.max.y, gb.center.z) + Vector3.up * 0.3f);
            if (ekran.z < 0f) { Skryt(); return false; }
            EkrannayaTochka = ekran;
            return true;
        }

        /// <summary>Фаза 2: нарисовать (после отбора «не больше трёх»). Размер пузыря — 20 % высоты здания на экране, 40…70 px (п.3.5).</summary>
        public void Narisovat()
        {
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            var rezhim = _rezhim; Sprite ikonka = _ikonkaTek; string tekst = _tekstTek;
            var cam = _igra.Kamera;
            float razmer = 56f;
            if (cam != null)
            {
                var b = _zdanie.ClickBounds; float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var u = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float y = cam.WorldToScreenPoint(u).y; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
                var cv = _holst.GetComponent<Canvas>(); float k = cv != null && cv.scaleFactor > 0f ? 1f / cv.scaleFactor : 1f;   // экранные px → единицы холста
                razmer = Mathf.Clamp((maxY - minY) * 0.2f * k, 40f, 70f);
            }
            _pusyr.sizeDelta = new Vector2(razmer, razmer);
            _ikonka.sizeDelta = new Vector2(razmer * 0.62f, razmer * 0.62f);

            Sprite pusyr = rezhim == Rezhim.Prostoy ? _prostoy : rezhim == Rezhim.Rabota ? _rabota : _gotovo;
            if (_pusyrImg.sprite != pusyr) _pusyrImg.sprite = pusyr;
            _pusyrImg.enabled = pusyr != null;
            _pusyrImg.color = pusyr != null ? Color.white : new Color(1f, 0.95f, 0.8f, 0.9f);
            bool sIkonkoy = rezhim == Rezhim.Prostoy && ikonka != null;
            _ikonkaImg.enabled = sIkonkoy;
            if (sIkonkoy && _ikonkaImg.sprite != ikonka) _ikonkaImg.sprite = ikonka;
            _tekst.text = tekst ?? "";

            // Подпрыгивание: простой и готово — заметно, работа — тихо
            float amp = rezhim == Rezhim.Rabota ? 2f : 4f;
            float podskok = Mathf.Abs(Mathf.Sin(Time.time * 3.2f + _faza)) * amp;
            _pusyr.anchoredPosition = new Vector2(0f, 6f + podskok);   // ближе к макушке (Khan 06.09: «значки должны быть ближе к зданию»)
            _pusyr.localScale = Vector3.one * (rezhim == Rezhim.Gotovo ? 1f + 0.06f * Mathf.Sin(Time.time * 5f + _faza) : 1f);

            ObnovitPozitsiyu();
        }

        /// <summary>
        /// Экранная точка и позиция на холсте. Зовётся каждый кадр из LateUpdate, а не только в тике
        /// логики: камера с 08.09 ездит каждый кадр, а маркеры пересчитывались 4 раза в секунду —
        /// они отставали от здания и дёргались (Khan 08.09). LateUpdate — после движения камеры,
        /// иначе позиция считалась бы по прошлому кадру.
        /// </summary>
        public void ObnovitPozitsiyu()
        {
            if (_rt == null || _zdanie == null || _igra == null) return;
            var cam = _igra.Kamera; if (cam == null) return;
            var gb = _zdanie.ClickBounds;
            Vector3 ekran = cam.WorldToScreenPoint(new Vector3(gb.center.x, gb.max.y, gb.center.z) + Vector3.up * 0.3f);
            if (ekran.z < 0f) { Skryt(); return; }
            EkrannayaTochka = ekran;
            _rt.anchoredPosition = ElementyHolsta.EkranVHolst(_holst, ekran);
        }

        /// <summary>Кадровое обновление: класс не MonoBehaviour, поэтому его зовёт игра из своего LateUpdate.</summary>
        public void KadrovoeObnovlenie()
        {
            if (_rt == null || !_rt.gameObject.activeSelf) return;
            ObnovitPozitsiyu();
            // Подпрыгивание тоже здесь: иначе оно замирало между тиками логики
            if (_pusyr != null)
            {
                float amp = _rezhim == Rezhim.Rabota ? 2f : 4f;
                _pusyr.anchoredPosition = new Vector2(0f, 6f + Mathf.Abs(Mathf.Sin(Time.time * 3.2f + _faza)) * amp);
                _pusyr.localScale = Vector3.one * (_rezhim == Rezhim.Gotovo ? 1f + 0.06f * Mathf.Sin(Time.time * 5f + _faza) : 1f);
            }
        }
    }
}
