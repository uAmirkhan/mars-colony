using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Маяк текущей цели: плавающая экранная метка над зданием, на которое
    /// указывает <see cref="QuestMarker"/> — тот же источник, что уже читает
    /// карточка цели снизу экрана (виток UI-10). Не контур, не стрелка, не
    /// затемнение: два независимых критика назвали одной и той же бедой то,
    /// что игрок ищет купол глазами по всей сцене, пока карточка называет его
    /// словом.
    ///
    /// Компонент — только точки крепления: <see cref="Rect"/> и
    /// <see cref="Ikonka"/> ставит художник интерфейса своей геометрией и
    /// анимацией дальше, здесь заложена только логика "показать в точке /
    /// спрятать", которую ведёт <see cref="VyborZdaniy"/> каждый кадр.
    ///
    /// Ровно 0 или 1 в кадре — потому что экземпляр всего один (его строит
    /// `InterfeysBuilder` вместе с холстом), а не потому что кто-то считает
    /// маяки и гасит лишние.
    /// </summary>
    public sealed class MayakTseli : MonoBehaviour
    {
        /// <summary>Корневой прямоугольник маяка на холсте. Позицию каждый кадр ставит `VyborZdaniy`.</summary>
        public RectTransform Rect;

        /// <summary>
        /// Иконка внутри маяка. Обязана ссылаться на ТОТ ЖЕ `Sprite`, что и
        /// миниатюра карточки цели (`InterfeysBuilder.Ikonka(0)`) — совпадение
        /// проверяется по ссылке при сборке, не "похоже".
        /// </summary>
        public Image Ikonka;

        /// <summary>
        /// Прячет маяк на время выделения циановым контуром именно ТОГО
        /// здания, на которое он указывает, и возвращает при снятии
        /// выделения — так подсказка не спорит взглядом с контуром.
        /// </summary>
        private bool _skryt_vydeleniem;

        /// <summary>
        /// Отступ низа маяка над точкой-якорем, ЭКРАННЫЕ px холста, не метры
        /// мира. Приёмка попытки 1 поймала: `marker_height` в мировых 0.30 м
        /// на экране такого масштаба даёт почти ноль, и маяк лёг прямо на
        /// макушку купола вместо того, чтобы висеть над ней. То же число 12,
        /// каким уже поднята карточка имени над силуэтом (`VyborZdaniy.
        /// OTSTUP_PX`) — тот же визуальный зазор, тот же прием.
        /// </summary>
        private const float OTSTUP_PX = 12f;

        /// <summary>Период вертикального дыхания маяка, сек (виток 8 ночного цикла).</summary>
        private const float PERIOD_DYHANIYA_SEK = 1.6f;

        /// <summary>Амплитуда вертикального дыхания маяка, px холста.</summary>
        private const float AMPLITUDA_DYHANIYA_PX = 8f;

        /// <summary>Длительность гашения/появления при смене состояния, сек.</summary>
        private const float ZATUKHANIE_SEK = 0.12f;

        private CanvasGroup _canvasGroup;
        private Vector2 _bazovayaPozitsiya;
        private float _faza_dyhaniya;
        private float _tekushchayaTselevayaAlfa = -1f;
        private Coroutine _fadeKoroutina;

        public void PriVydeleniiZdaniya(bool eto_tsel_vydelena)
        {
            _skryt_vydeleniem = eto_tsel_vydelena;
            if (eto_tsel_vydelena)
                Skryt();
        }

        /// <summary>
        /// Переводит экранную точку-якорь (макушка купола) в локальные
        /// координаты холста и ставит маяк НАД ней — тем же переводом, каким
        /// уже пользуется карточка имени (`ZhivoyInterfeys.PokazatImyaZdaniya`).
        /// </summary>
        public void Pokazat(RectTransform holst, Vector2 ekrannayaTochka, Camera cam)
        {
            if (Rect == null || holst == null || cam == null || _skryt_vydeleniem)
            {
                Skryt();
                return;
            }

            PokazatVHolste(ElementyHolsta.EkranVHolst(holst, ekrannayaTochka));
        }

        /// <summary>
        /// Ставит маяк уже готовой точкой-якорем в координатах холста
        /// (`anchoredPosition` того же `Canvas`), минуя
        /// `ScreenPointToLocalPointInRectangle`. Нужна диагностике кадра
        /// приёмки: в редакторе этот перевод считает под разрешение окна
        /// Game, а не рендер-таргета кадра (та же ловушка, что уже описана у
        /// `CeliDiag`), и число расходится с тем, что реально уйдёт в PNG.
        /// В Play разницы нет — там оба пути дают одну точку.
        ///
        /// Точка — это МАКУШКА купола, не центр маяка: нижний край маяка
        /// встаёт над ней на <see cref="OTSTUP_PX"/>, а не сам маяк садится
        /// на неё. Пивот у `Rect` центральный, поэтому подъём — это
        /// половина высоты плюс отступ.
        /// </summary>
        public void PokazatVHolste(Vector2 tochkaHolsta)
        {
            if (Rect == null || _skryt_vydeleniem)
            {
                Skryt();
                return;
            }

            float polvysoty = Rect.rect.height * 0.5f;
            _bazovayaPozitsiya = new Vector2(tochkaHolsta.x, tochkaHolsta.y + OTSTUP_PX + polvysoty);
            Rect.gameObject.SetActive(true);
            NachatZatukhanie(1f, false);
        }

        /// <summary>
        /// Вертикальное "дыхание" маяка синусом вокруг базовой позиции — не
        /// геометрия и не цвет, только `anchoredPosition.y`; амплитуда и
        /// период заданы витком 8 ночного цикла (mars-colony-7h0).
        /// </summary>
        private void Update()
        {
            if (Rect == null || !Rect.gameObject.activeSelf)
                return;

            _faza_dyhaniya += Time.deltaTime;
            float smeshenie = Mathf.Sin(_faza_dyhaniya * (2f * Mathf.PI / PERIOD_DYHANIYA_SEK)) * AMPLITUDA_DYHANIYA_PX;
            Rect.anchoredPosition = new Vector2(_bazovayaPozitsiya.x, _bazovayaPozitsiya.y + smeshenie);
        }

        /// <summary>Ленивое создание `CanvasGroup` на объекте маяка — гашение не трогает цвет/геометрию пина, только альфу.</summary>
        private void UbeditCanvasGroup()
        {
            if (_canvasGroup != null || Rect == null)
                return;
            _canvasGroup = Rect.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = Rect.gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>
        /// Запускает fade к целевой альфе за <see cref="ZATUKHANIE_SEK"/>;
        /// повторный вызов с той же целью — no-op, иначе `PriVydeleniiZdaniya`/
        /// `Pokazat`, вызываемые каждый кадр, рестартовали бы таймер и fade
        /// никогда бы не завершался.
        /// </summary>
        private void NachatZatukhanie(float tselevayaAlfa, bool skrytPoOkonchanii)
        {
            if (Mathf.Approximately(_tekushchayaTselevayaAlfa, tselevayaAlfa))
                return;
            _tekushchayaTselevayaAlfa = tselevayaAlfa;
            UbeditCanvasGroup();
            if (_fadeKoroutina != null)
                StopCoroutine(_fadeKoroutina);
            _fadeKoroutina = StartCoroutine(ZatukhanieDo(tselevayaAlfa, skrytPoOkonchanii));
        }

        private IEnumerator ZatukhanieDo(float tselevayaAlfa, bool skrytPoOkonchanii)
        {
            float start = _canvasGroup.alpha;
            float t = 0f;
            while (t < ZATUKHANIE_SEK)
            {
                t += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(start, tselevayaAlfa, Mathf.Clamp01(t / ZATUKHANIE_SEK));
                yield return null;
            }
            _canvasGroup.alpha = tselevayaAlfa;
            if (skrytPoOkonchanii)
                Rect.gameObject.SetActive(false);
        }

        /// <summary>
        /// Гасит маяк. Вызывается, когда цели нет (`QuestMarker.HasTarget`
        /// ложно) или слот цели ушёл из EMPTY — на его месте встаёт полоса
        /// роста здания, отдельной анимации исчезновения маяку не нужно.
        /// </summary>
        public void Skryt()
        {
            if (Rect == null || !Rect.gameObject.activeSelf)
                return;
            NachatZatukhanie(0f, true);
        }

        /// <summary>Виден ли маяк прямо сейчас.</summary>
        public bool Vidim => Rect != null && Rect.gameObject.activeSelf;

        /// <summary>
        /// Приёмка числом: сколько маяков в кадре видимы одновременно.
        /// Экземпляр всего один, поэтому число обязано быть 0 или 1 —
        /// метод существует, чтобы это было проверяемым фактом, а не
        /// утверждением на словах.
        /// </summary>
        public static int AktivnyhVKadre()
        {
            var vse = Object.FindObjectsByType<MayakTseli>(FindObjectsSortMode.None);
            int n = 0;
            foreach (var m in vse)
                if (m.Vidim)
                    n++;
            return n;
        }
    }
}
