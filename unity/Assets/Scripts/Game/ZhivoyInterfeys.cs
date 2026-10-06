using System.Collections.Generic;
using MarsColony.Domain;
using MarsColony.Domain.Config;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Единственный рантайм интерфейса: связывает холст, собранный
    /// InterfeysBuilder, с состоянием колонии.
    ///
    /// Зачем он заменил Hud и ZhivoyHud. Их было два, и каждый обслуживал свой
    /// холст: Hud держал прямые ссылки на надписи кодового холста, ZhivoyHud
    /// искал по именам узлы рукописного. Пока холстов было два, это работало;
    /// как только холст остался один, два рантайма стали гарантией, что
    /// появится третий. Числа спеки говорят прямо: свести в один, иначе разъезд
    /// повторится.
    ///
    /// Узлы ищутся по именам — это договор со сборщиком. Не найденный узел
    /// молча не обновляется и не роняет игру: интерфейс может собираться
    /// частями, а падать из-за отсутствующей надписи он не должен.
    /// </summary>
    public sealed class ZhivoyInterfeys : MonoBehaviour
    {
        /// <summary>Сколько живет всплывающее число, секунд.</summary>
        public float zhizn_vsplyvashki = 1.1f;

        /// <summary>На сколько единиц холста оно поднимается за свою жизнь.</summary>
        public float podyem = 42f;

        Text _kredity, _opyt, _uroven, _telo, _nagrada, _cta;
        Text _izotopy;
        /// <summary>Изотопы игрока — вторая валюта; игра кладёт сюда число перед Obnovit (живут в Igra, не в ColonyState). Khan 06.09: «не вижу, сколько у меня изотопов».</summary>
        public int Izotopy;
        /// <summary>Поселенцы и энергия (спека 08.09): два счётчика слева под опытом, задаёт игра перед Obnovit.</summary>
        public int Poselentsy; public double Energiya;
        Text _poselentsy, _energiya;
        Image _zapolnenie;
        RectTransform _kartochka;
        int _bylo_kreditov, _bylo_opyta;
        bool _pervyy = true;

        // Карточка имени здания (виток UI-3): всплывает у выбранного объекта,
        // а не в углу — см. обоснование в `InterfeysBuilder.KartochkaImeni`.
        RectTransform _kartochkaImeni;
        Text _imyaZdaniya;
        RectTransform _holst;

        // Этап 3, блок 1, правка A1, пункт 5: анимация появления плашки
        // идёт только на переходе неактивна->активна, а не каждый кадр,
        // пока выбор держится (метод вызывается каждый кадр — см. докстринг
        // PokazatImyaZdaniya).
        Coroutine _animPlashka;
        bool _plashkaAnimiruetsya;

        /// <summary>Поле визора вокруг текста слева и справа, px. Приемка попытки 1: было
        /// 92 px при заявленных 16 — визор читался полосой, а не ярлыком, и лишнее
        /// поле как раз и накрывало соседний бейдж.</summary>
        const float POLE_KARTOCHKI_IMENI_X = 16f;

        /// <summary>Нижняя и верхняя граница ширины визора, px. Нижняя — чтобы «Склад» не
        /// схлопнулся в точку, верхняя — разумный потолок на случай неожиданно длинной строки
        /// (используется и в `VyborZdaniy` как запас при поиске мешающих бейджей).</summary>
        public const float MIN_SHIRINA_KARTOCHKI = 120f;
        public const float MAX_SHIRINA_KARTOCHKI = 420f;

        readonly List<Vsplyvashka> _zhivye = new List<Vsplyvashka>();

        sealed class Vsplyvashka
        {
            public RectTransform rt;
            public Text tekst;
            public float rodilas;
            public Vector2 start;
        }

        void Awake() { Sobrat(); }

        void Sobrat()
        {
            if (_holst == null) _holst = GetComponent<RectTransform>();

            foreach (var t in GetComponentsInChildren<Text>(true))
            {
                string rod = t.transform.parent != null ? t.transform.parent.name : "";

                if (t.name == "znachenie" && Predok(t.transform, "schetchik-kredity")) _kredity = t;
                if (Predok(t.transform, "schetchik-opyt"))
                {
                    if (t.name == "znachenie") _opyt = t;
                    if (t.name == "uroven") _uroven = t;
                }
                if (t.name == "telo") _telo = t;
                if (t.name == "nagrada") _nagrada = t;
                if (rod == "knopka-deystviya" && t.name == "tekst") _cta = t;
                if (t.name == "imya" && Predok(t.transform, "kartochka-imeni")) _imyaZdaniya = t;
            }
            ObespechitSchetchikIzotopov();
            _poselentsy = ObespechitSchetchikSleva("schetchik-poselentsy", 0, "UI/Ikonki/znachok-poselenets");
            _energiya = ObespechitSchetchikSleva("schetchik-energiya", 1, "UI/Ikonki/znachok-uskorit");

            foreach (var im in GetComponentsInChildren<Image>(true))
                if (im.name == "zapolnenie") _zapolnenie = im;

            foreach (var rt in GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name == "kartochka-tseli") _kartochka = rt;
                if (rt.name == "kartochka-imeni") _kartochkaImeni = rt;
            }
        }

        /// <summary>
        /// Что рантайм реально нашел на холсте. Нужно потому, что не найденный
        /// узел молча не обновляется: интерфейс при этом выглядит собранным, а
        /// числа на нем остаются краской. Именно так в сцене и завелся когда-то
        /// второй рантайм — поломку заметили только глазами на кадре.
        /// Возвращает пустую строку, если найдено все.
        /// </summary>
        public string ChegoNet()
        {
            Sobrat();
            var net = new List<string>();
            if (_kredity == null) net.Add("кредиты");
            if (_opyt == null) net.Add("опыт");
            if (_uroven == null) net.Add("уровень");
            if (_zapolnenie == null) net.Add("полоса опыта");
            if (_telo == null) net.Add("тело задания");
            if (_cta == null) net.Add("надпись кнопки");
            if (_kartochkaImeni == null) net.Add("карточка имени");
            if (_imyaZdaniya == null) net.Add("текст имени здания");
            return net.Count == 0 ? "" : string.Join(", ", net);
        }

        static bool Predok(Transform t, string imya)
        {
            while (t != null) { if (t.name == imya) return true; t = t.parent; }
            return false;
        }

        /// <summary>
        /// Показывает карточку имени над экранной точкой-якорем и переставляет
        /// ее туда же каждый вызов. Заодно подгоняет ширину визора под сам
        /// текст (приемка попытки 1, находка 4: было 380 px фиксированных
        /// при 196 px текста — визор читался полосой поперек сцены, а лишнее
        /// поле как раз и накрывало соседний бейдж).
        ///
        /// Точка приходит уже ЭКРАННОЙ, не мировой — ее считает `VyborZdaniy`
        /// проекцией всех восьми углов габарита здания (см. там), а не одной
        /// точки: у одной точки нет понятия "мешающий рядом бейдж", а у
        /// экранного прямоугольника есть. Дважды переводить мир в экран тоже
        /// незачем — числа могут разойтись только от круглой ошибки, а не от
        /// разного смысла.
        ///
        /// Вызывается из `VyborZdaniy` каждый кадр, пока что-то выбрано, а не
        /// только на клике: сцена сейчас статична, но правило то же, что у
        /// `ClickTarget.ClickBounds` — не кэшировать то, что рантайм не
        /// гарантирует неизменным.
        /// </summary>
        public void PokazatImyaZdaniya(string imya, Vector2 ekrannayaTochka, Camera cam)
        {
            if (_kartochkaImeni == null || _imyaZdaniya == null || _holst == null || cam == null)
                return;

            Vector2 lokalnaya = ElementyHolsta.EkranVHolst(_holst, ekrannayaTochka);

            _imyaZdaniya.text = imya;

            // preferredWidth считается в единицах ХОЛСТА (уже с учетом
            // CanvasScaler), поэтому складывается с полем без пересчета
            // масштаба — тот же холст, та же линейка.
            float shirina = Mathf.Clamp(_imyaZdaniya.preferredWidth + POLE_KARTOCHKI_IMENI_X * 2f,
                                        MIN_SHIRINA_KARTOCHKI, MAX_SHIRINA_KARTOCHKI);
            var razmer = _kartochkaImeni.sizeDelta;
            razmer.x = shirina;
            _kartochkaImeni.sizeDelta = razmer;

            // Клэмп у края холста (спека раздел 2.2, "поведение при выходе за
            // край экрана"). Камера рубежа 1 статична и не роняет карточку за
            // границу ни на одном из 13 зданий, поэтому клэмп сегодня не
            // подтвержден кадром — но он бесплатен и правильно готовит код к
            // движению камеры (рубеж 4). Считаем по РЕАЛЬНОМУ прямоугольнику
            // холста (_holst.rect), а не по константе 1600: на более узком,
            // чем 16:9, экране видимая ширина холста меньше (раздел 10).
            const float ZAZOR_KRAYA = 16f;
            float polShiriny = shirina * 0.5f;
            float minX = _holst.rect.xMin + polShiriny + ZAZOR_KRAYA;
            float maxX = _holst.rect.xMax - polShiriny - ZAZOR_KRAYA;
            if (minX <= maxX)
                lokalnaya.x = Mathf.Clamp(lokalnaya.x, minX, maxX);

            bool byloNeaktivno = !_kartochkaImeni.gameObject.activeSelf;
            _kartochkaImeni.gameObject.SetActive(true);

            if (byloNeaktivno && !_plashkaAnimiruetsya)
            {
                var cg = _kartochkaImeni.GetComponent<CanvasGroup>();
                if (cg == null) cg = _kartochkaImeni.gameObject.AddComponent<CanvasGroup>();
                if (_animPlashka != null) StopCoroutine(_animPlashka);
                _plashkaAnimiruetsya = true;
                _animPlashka = StartCoroutine(PoyavitPlashkuIObnulit(cg, lokalnaya));
            }
            else if (!_plashkaAnimiruetsya)
            {
                // Уже видна и не анимируется — позиция просто следует за
                // мишенью кадр в кадр, как и раньше до этой правки.
                _kartochkaImeni.anchoredPosition = lokalnaya;
            }
        }

        System.Collections.IEnumerator PoyavitPlashkuIObnulit(CanvasGroup cg, Vector2 finalPos)
        {
            yield return AnimatsiiInterfeysa.AnimatPoyavleniePlashki(_kartochkaImeni, cg, finalPos);
            _plashkaAnimiruetsya = false;
        }

        /// <summary>
        /// Фактическая ширина карточки имени в единицах холста, 0 если карточки
        /// нет. Нужна тому, кто решает, что карточка накрывает: считать по
        /// потолку MAX_SHIRINA_KARTOCHKI неверно — он вдвое шире короткой
        /// подписи, и под «накрытое» попадает лишнее.
        /// </summary>
        public float ShirinaKartochkiImeni =>
            _kartochkaImeni != null ? _kartochkaImeni.sizeDelta.x : 0f;

        /// <summary>
        /// Холст интерфейса. Нужен маяку цели (`MayakTseli`, ведёт его
        /// `VyborZdaniy`) для того же перевода экранной точки в локальные
        /// координаты, каким уже пользуется карточка имени в
        /// <see cref="PokazatImyaZdaniya"/> — общая линейка, не вторая копия.
        /// </summary>
        public RectTransform Holst => _holst;

        /// <summary>Прячет карточку имени. Вызывается при снятии выбора.</summary>
        public void SkrytImyaZdaniya()
        {
            if (_kartochkaImeni != null)
                _kartochkaImeni.gameObject.SetActive(false);
            _plashkaAnimiruetsya = false; // следующий показ должен снова анимироваться
        }

        /// <summary>Всплывающее "+N" над капсулой кредитов для пути без `ColonyGame`
        /// (`SkladSostoyanie.Prodat` пишет текст HUD напрямую и не проходит
        /// через `Obnovit`, поэтому автоматическое сравнение было/стало там
        /// не срабатывает — см. правку A1, пункт 4).</summary>
        public void PokazatPribavkuKreditov(RectTransform nadKem, int summa)
        {
            if (nadKem == null) return;
            Rodit(nadKem, "+" + summa);
        }

        /// <summary>Карточка ЦЕЛЬ прячется, пока открыта панель здания (геймдизайнер, п.3.9).</summary>
        public IgraKolonii.Tsel Tsel;
        Image _ikonkaTseli; Sprite _ikonkaTseliIshodnaya; string _ikonkaTseliId;

        /// <summary>Иконка на карточке — товар текущей цели (посеять/собрать/загрузить); без товара — исходная картинка карточки.</summary>
        void ObnovitIkonkuTseli(ColonyState state)
        {
            if (_kartochka == null) return;
            if (_ikonkaTseli == null)
            {
                foreach (var img in _kartochka.GetComponentsInChildren<Image>(true)) if (img.name == "miniatyura") { _ikonkaTseli = img; _ikonkaTseliIshodnaya = img.sprite; break; }
                if (_ikonkaTseli == null) return;
            }
            string id = Tsel.vidna ? Tsel.ikonka : null;
            if (id == _ikonkaTseliId) return;
            _ikonkaTseliId = id;
            Sprite spr = null;
            if (id != null) { var el = ElementyHolsta.Sobrat(_kartochka.parent); spr = el.Ikonka(id); }
            _ikonkaTseli.sprite = spr != null ? spr : _ikonkaTseliIshodnaya;
        }

        public void PokazatKartochkuTseli(bool vidna)
        {
            if (_kartochka != null && _kartochka.gameObject.activeSelf != vidna) _kartochka.gameObject.SetActive(vidna);
            var ten = _kartochka != null && _kartochka.parent != null ? _kartochka.parent.Find("kartochka-tseli-ten") : null;
            if (ten != null && ten.gameObject.activeSelf != vidna) ten.gameObject.SetActive(vidna);
        }

        /// <summary>Всплеск левелапа (ТЗ 5.3): «Уровень N!» над медальоном уровня, «+кредиты» над монетой, «+изотопы» под ней.</summary>
        public void PokazatLevelap(int uroven, int kredity, int izotopy)
        {
            // Медальоны стоят у верхней кромки: всплывашка над ними уходит за экран, поэтому рождаем ниже и в два яруса.
            if (_uroven != null) Rodit(_uroven.rectTransform, "Уровень " + uroven + "!", new Vector2(0f, -80f), TextAnchor.MiddleLeft);   // медальон у левого края: текст растёт вправо
            if (_kredity != null && kredity > 0) Rodit(_kredity.rectTransform, "+" + kredity, new Vector2(0f, -80f));
            if (_kredity != null && izotopy > 0) Rodit(_kredity.rectTransform, "+" + izotopy + " изотопов", new Vector2(0f, -120f));
        }

        /// <summary>
        /// Счётчики слева под опытом (спека 08.09): копия капсулы кредитов, ряд 0 — поселенцы, ряд 1 — энергия.
        /// Иконка поселенца ждёт генерации заказчика: пока медальон пустой, своих заглушек не рисуем.
        /// </summary>
        private Text ObespechitSchetchikSleva(string imya, int ryad, string putIkonki)
        {
            if (_kredity == null) return null;
            var kr = _kredity.transform; while (kr.parent != null && kr.name != "schetchik-kredity") kr = kr.parent;
            if (kr.name != "schetchik-kredity") return null;
            var opyt = kr.parent.Find("schetchik-opyt") as RectTransform;
            var sushch = kr.parent.Find(imya);
            GameObject go = sushch != null ? sushch.gameObject : Instantiate(kr.gameObject, kr.parent);
            go.name = imya;
            var rt = (RectTransform)go.transform; var krt = (RectTransform)kr;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f); rt.sizeDelta = krt.sizeDelta;
            float x = opyt != null ? opyt.anchoredPosition.x : 28f, y0 = opyt != null ? opyt.anchoredPosition.y - opyt.sizeDelta.y : -96f;
            rt.anchoredPosition = new Vector2(x, y0 - 10f - ryad * (krt.sizeDelta.y + 10f));
            var spr = putIkonki != null ? Resources.Load<Sprite>(putIkonki) : null;
            foreach (var img in go.GetComponentsInChildren<Image>(true))
                if (img.name == "medalyon" || (img.transform.parent != null && img.transform.parent.name == "medalyon"))
                {
                    if (img.name == "korpus") continue;
                    if (spr != null) img.sprite = spr; else img.enabled = false;
                }
            Text znach = null;
            foreach (var t in go.GetComponentsInChildren<Text>(true)) if (t.name == "znachenie") znach = t;
            go.SetActive(true);
            return znach;
        }

        /// <summary>Счётчик изотопов — копия счётчика кредитов слева от него, с иконкой изотопов из Resources/UI/Ikonki.</summary>
        private void ObespechitSchetchikIzotopov()
        {
            if (_kredity == null) return;
            var kr = _kredity.transform; while (kr.parent != null && kr.name != "schetchik-kredity") kr = kr.parent;
            if (kr.name != "schetchik-kredity") return;
            var sushch = kr.parent.Find("schetchik-izotopy");
            GameObject go = sushch != null ? sushch.gameObject : Instantiate(kr.gameObject, kr.parent);
            go.name = "schetchik-izotopy";
            var rt = (RectTransform)go.transform; var krt = (RectTransform)kr;
            rt.anchorMin = krt.anchorMin; rt.anchorMax = krt.anchorMax; rt.pivot = krt.pivot; rt.sizeDelta = krt.sizeDelta;
            rt.anchoredPosition = krt.anchoredPosition + new Vector2(-(krt.rect.width + 14f), 0f);
            var spr = Resources.Load<Sprite>("UI/Ikonki/znachok-izotopy");
            foreach (var img in go.GetComponentsInChildren<Image>(true)) if (img.name == "medalyon" || (img.transform.parent != null && img.transform.parent.name == "medalyon")) { if (spr != null && img.sprite != null && img.name != "korpus") img.sprite = spr; }
            foreach (var t in go.GetComponentsInChildren<Text>(true)) if (t.name == "znachenie") _izotopy = t;
            go.SetActive(true);
        }

        /// <summary>Обновляет числа и надписи. Вызывается каждый кадр из ColonyGame.</summary>
        public void Obnovit(ColonyState state, double now)
        {
            if (_kredity != null) _kredity.text = state.credits.ToString();
            if (_izotopy != null) _izotopy.text = Izotopy.ToString();
            if (_poselentsy != null) _poselentsy.text = Poselentsy.ToString();
            if (_energiya != null) _energiya.text = Energiya.ToString("0.0");   // дробная часть, чтобы число шевелилось

            if (_opyt != null || _zapolnenie != null || _uroven != null)
            {
                // Полоса показывает прогресс ВНУТРИ уровня, а не общий опыт:
                // Progress хранит накопительные пороги, и без вычитания нижнего
                // порога полоса на пятом уровне выглядела бы почти полной всегда.
                int uroven = Progress.UrovenPoOpytu(state.xp);
                int nizhniy = Progress.VsegoDlyaUrovnya(uroven);
                int verhniy = Progress.VsegoDlyaUrovnya(uroven + 1);
                int nado = Mathf.Max(1, verhniy - nizhniy);
                int est = Mathf.Clamp(state.xp - nizhniy, 0, nado);

                if (_opyt != null) _opyt.text = est + "/" + nado;
                if (_uroven != null) _uroven.text = uroven.ToString();
                if (_zapolnenie != null) _zapolnenie.fillAmount = (float)est / nado;
            }

            // Текст цели задаёт игра (цепочка первых действий, IgraKolonii.VychislitTsel); старый расчёт по состоянию — запасной
            if (_cta != null) _cta.text = Tsel.vidna ? Tsel.glagol : Glagol(state, now);
            if (_telo != null) _telo.text = Tsel.vidna ? Tsel.tekst : Zadanie(state, now);
            ObnovitIkonkuTseli(state);

            // Всплывающее число рождается на начислении, а не по таймеру: в этой
            // экономике ничего не капает само, и тикер «+X/сек» показывал бы
            // несуществующий доход.
            if (!_pervyy)
            {
                if (state.credits != _bylo_kreditov && _kredity != null)
                    Rodit(_kredity.rectTransform, (state.credits - _bylo_kreditov > 0 ? "+" : "")
                          + (state.credits - _bylo_kreditov));
                if (state.xp != _bylo_opyta && _opyt != null)
                    Rodit(_opyt.rectTransform, "+" + (state.xp - _bylo_opyta) + " XP");
            }
            _bylo_kreditov = state.credits; _bylo_opyta = state.xp; _pervyy = false;
        }

        /// <summary>
        /// Всплывашки двигаются своим кадром, а не из Obnovit: в сцене MAIN нет ColonyGame,
        /// и «+48» после продажи навек зависало поверх числа кредитов (серия кадров prodat-*, этап 3 блок 1).
        /// </summary>
        void LateUpdate()
        {
            if (_zhivye.Count > 0) Dvigat(Time.time);
        }

        /// <summary>
        /// Надпись на кнопке — глагол текущего состояния, а не название экрана.
        /// Одна кнопка в пяти состояниях читается быстрее пяти кнопок.
        /// </summary>
        static string Glagol(ColonyState state, double now)
        {
            int idx = FieldView.TekushchayaCelPolya(state);
            if (idx < 0) return "СТРОИТЬ";
            FieldSlot slot = state.PoleTeplitsy(idx);
            if (slot.state == FieldState.READY) return "СОБРАТЬ";
            if (slot.state == FieldState.GROWING) return "ЖДАТЬ";
            return "ПОСЕЯТЬ";
        }

        static string Zadanie(ColonyState state, double now)
        {
            int idx = FieldView.TekushchayaCelPolya(state);
            if (idx < 0) return "Построить\nтеплицу";
            FieldSlot slot = state.PoleTeplitsy(idx);

            if (slot.state == FieldState.GROWING && slot.good_id != null)
            {
                int left = FieldView.RemainingSec(slot, now);
                return Goods.Of(slot.good_id).name + " растут\n"
                     + string.Format("{0:00}:{1:00}", left / 60, left % 60);
            }
            if (slot.state == FieldState.READY && slot.good_id != null)
                return "Собрать урожай\n" + Goods.Of(slot.good_id).name;

            string good_id = CropChoice.CheapestUnlocked(state.level);
            if (good_id == null) return "Построить\nтеплицу";
            return "Посеять " + Goods.Of(good_id).name + "\nв теплице";
        }

        void Rodit(RectTransform rodom, string text, Vector2 smeshchenie = default, TextAnchor vyravnivanie = TextAnchor.MiddleRight)
        {
            var go = new GameObject("vsplyvashka", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(rodom.parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rodom.anchorMin; rt.anchorMax = rodom.anchorMax; rt.pivot = rodom.pivot;
            rt.sizeDelta = rodom.sizeDelta;
            rt.anchoredPosition = rodom.anchoredPosition + smeshchenie;

            var t = go.GetComponent<Text>();
            var obrazets = rodom.GetComponent<Text>();
            t.font = obrazets != null ? obrazets.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = obrazets != null ? obrazets.fontSize : 26;
            t.fontStyle = FontStyle.Bold;
            t.alignment = vyravnivanie;
            t.color = new Color(0.18f, 0.62f, 0.18f);
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            _zhivye.Add(new Vsplyvashka { rt = rt, tekst = t, rodilas = Time.time, start = rt.anchoredPosition });
        }

        void Dvigat(double now)
        {
            for (int i = _zhivye.Count - 1; i >= 0; i--)
            {
                var v = _zhivye[i];
                float k = (Time.time - v.rodilas) / zhizn_vsplyvashki;
                if (k >= 1f || v.rt == null)
                {
                    if (v.rt != null) Destroy(v.rt.gameObject);
                    _zhivye.RemoveAt(i);
                    continue;
                }
                v.rt.anchoredPosition = v.start + new Vector2(0f, podyem * k);
                var c = v.tekst.color; c.a = 1f - k * k; v.tekst.color = c;
            }
        }
    }
}
