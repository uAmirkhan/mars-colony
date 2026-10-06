using System.Collections;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Экран склада, виток "ночь-2" ночного цикла (`loop/ui/spec-sklad.md`).
    /// Только открытие/закрытие целиком узла — содержимое (сетка товаров,
    /// лента, крестик) строит `InterfeysBuilder.PostroitEkranSklada`
    /// один раз при сборке, здесь нет ничего живого сверх SetActive.
    ///
    /// Два входа на открытие по спеке (раздел 3): кнопка меню (тут —
    /// `Perekluchit`, единственный пункт меню в срезе) и тап по 3D-модели
    /// склада в сцене — второй вход не входит в этот виток (нет содержимого,
    /// пока не собран сам экран, см. отчет витка).
    ///
    /// Закрытие — крестик и тап по затемнению, оба вызывают `Zakryt`, не
    /// `Perekluchit`: тап по scrim никогда не должен ОТКРЫВАТЬ экран.
    ///
    /// Единственная копия в сцене, кладется сборщиком интерфейса как прямой
    /// ребенок холста ("interfeys/ekran-sklada").
    /// </summary>
    public sealed class EkranSklada : MonoBehaviour
    {
        // Виток "ночь-4": критик кадра поймал два зелёных акцента разом —
        // карточка цели с «ПОСЕЯТЬ» осталась полной яркости под открытым
        // складом рядом с зелёной «РАСШИРИТЬ» (`OTCHET-nochi-2.md`, виток 3,
        // пункт 1). Решение — прятать карточку цели (и её тень) на всё время,
        // пока склад открыт, возвращать при закрытии.
        //
        // Оба узла — прямые соседи этого объекта на холсте "interfeys":
        // сборщик кладёт "ekran-sklada" прямым ребёнком холста (см. докстринг
        // класса), а "kartochka-tseli"/"kartochka-tseli-ten" — тоже прямые
        // дети того же холста (`InterfeysBuilder.KartochkaTseli`). Поэтому
        // ищем через `transform.parent`, а не отдельным `GameObject.Find` по
        // всей сцене.
        private Transform _kartochka;
        private Transform _kartochkaTen;
        private bool _iskaliKartochku;

        // Этап 3, блок 1, правка A1, пункт 2: масштаб 0.96->1 и альфа 0->1 на
        // открытии (120 мс), обратно за 90 мс на закрытии. CanvasGroup
        // добавляется рантаймом сюда же (InterfeysBuilder.cs этот виток не
        // трогает), RectTransform у "ekran-sklada" уже есть — узел растянут.
        private RectTransform _rt;
        private CanvasGroup _cg;
        private Coroutine _anim;
        private bool _gotovKAnimatsii;

        void Podgotovit()
        {
            if (_gotovKAnimatsii) return;
            _gotovKAnimatsii = true;
            _rt = GetComponent<RectTransform>();
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        }

        public void Perekluchit()
        {
            if (gameObject.activeSelf) Zakryt(); else Otkryt();
        }

        void Otkryt()
        {
            Podgotovit();
            gameObject.SetActive(true);
            PokazatKartochkuTseli(false);
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(AnimatsiiInterfeysa.AnimatPanel(
                _rt, _cg, AnimatsiiInterfeysa.SKLAD_MASSHTAB_START, 1f, 0f, 1f,
                AnimatsiiInterfeysa.SKLAD_OTKRYTIE_S, perelet: true));
        }

        public void Zakryt()
        {
            Podgotovit();
            PokazatKartochkuTseli(true);
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(ZakrytPlavno());
        }

        IEnumerator ZakrytPlavno()
        {
            yield return AnimatsiiInterfeysa.AnimatPanel(
                _rt, _cg, 1f, AnimatsiiInterfeysa.SKLAD_MASSHTAB_START, 1f, 0f,
                AnimatsiiInterfeysa.SKLAD_ZAKRYTIE_S, perelet: false);
            gameObject.SetActive(false);
        }

        private void PokazatKartochkuTseli(bool pokazat)
        {
            NaytiKartochkuTseli();
            if (_kartochka != null) _kartochka.gameObject.SetActive(pokazat);
            if (_kartochkaTen != null) _kartochkaTen.gameObject.SetActive(pokazat);
        }

        // Лениво и один раз: холст статичен, узлы никуда не переезжают, а
        // Awake() этого компонента не гарантированно отработал (объект мог
        // прийти уже активным прямо из сохранённой сцены без входа в Play).
        private void NaytiKartochkuTseli()
        {
            if (_iskaliKartochku)
                return;
            _iskaliKartochku = true;

            Transform holst = transform.parent;
            if (holst == null)
            {
                Debug.LogWarning("[sklad] у 'ekran-sklada' нет родителя-холста — карточку цели скрыть не удастся");
                return;
            }

            _kartochka = holst.Find("kartochka-tseli");
            _kartochkaTen = holst.Find("kartochka-tseli-ten");
            if (_kartochka == null)
                Debug.LogWarning("[sklad] 'kartochka-tseli' не найдена рядом на холсте 'interfeys'");
        }
    }
}
