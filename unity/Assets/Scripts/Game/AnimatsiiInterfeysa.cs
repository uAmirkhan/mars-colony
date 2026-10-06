using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Этап 3, блок 1, правка A1 (`loop/ui/SPISOK-pravok-blok1.md`) — базовые
    /// анимации отклика ("mars-juice"). Единственное место с длительностями:
    /// остальные компоненты (`NazhatieKnopki`, `EkranSklada`,
    /// `SkladSostoyanie`, `ZhivoyInterfeys`) зовут статические корутины
    /// отсюда, числа не разбегаются по файлам.
    ///
    /// Мост Unity недоступен в этом витке — правка кодовая, проверка кадрами
    /// (`check-interaction.mjs`) остаётся за оркестратором.
    /// </summary>
    public sealed class AnimatsiiInterfeysa : MonoBehaviour
    {
        // --- Длительности (сек) и величины — единственное место правки ---
        public const float NAZHATIE_VNIZ_S = 0.04f;
        public const float NAZHATIE_VVERH_S = 0.08f;
        public const float NAZHATIE_MASSHTAB = 0.94f;

        public const float SKLAD_OTKRYTIE_S = 0.12f;
        public const float SKLAD_ZAKRYTIE_S = 0.09f;
        public const float SKLAD_MASSHTAB_START = 0.96f;

        public const float POPUP_POYAVLENIE_S = 0.10f;
        public const float POPUP_MASSHTAB_START = 0.9f;

        public const float MONETA_POLET_S = 0.35f;
        public const float MONETA_DUGA_VYSOTA = 90f;

        public const float PLASHKA_IMENI_S = 0.10f;
        public const float PLASHKA_IMENI_PODYEM = 6f;

        public static AnimatsiiInterfeysa Instance { get; private set; }

        void Awake() { Instance = this; }

        // Автосоздание себя и навешивание отклика на кнопки холста — без
        // правки InterfeysBuilder.cs (файл занят другим витком). Тот же
        // приём, что уже используют EkranSklada/ZhivoyInterfeys: Awake на
        // холсте, а не ручная проводка в сборщике.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Zapustit()
        {
            if (Instance != null) return;
            var holst = GameObject.Find("interfeys");
            if (holst == null)
            {
                Debug.LogWarning("[juice] холст 'interfeys' не найден на старте — отклик кнопок не подключен");
                return;
            }
            var go = new GameObject("animatsii-interfeysa");
            var a = go.AddComponent<AnimatsiiInterfeysa>();
            a.PodklyuchitKnopki(holst.transform);
        }

        /// <summary>Навешивает `NazhatieKnopki` на все Button холста, включая неактивные —
        /// попап продажи собран сборщиком заранее и просто выключен, поэтому
        /// найдётся тем же проходом.</summary>
        void PodklyuchitKnopki(Transform holst)
        {
            var knopki = holst.GetComponentsInChildren<Button>(true);
            foreach (var k in knopki)
                if (k.GetComponent<NazhatieKnopki>() == null)
                    k.gameObject.AddComponent<NazhatieKnopki>();
        }

        // --- Кривые ---
        static float EaseOutCubic(float k) => 1f - Mathf.Pow(1f - k, 3f);

        // Перелёт на появлении (см. правило "движение" — рост из нуля с
        // перелётом, уход без него), стандартная back-out кривая.
        static float EaseOutBack(float k)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float x = k - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        /// <summary>Масштаб + альфа панели (склад, пузырь продажи). `cg` может быть null —
        /// тогда анимируется только масштаб (пузырь продажи альфу не меняет).</summary>
        public static IEnumerator AnimatPanel(RectTransform rt, CanvasGroup cg,
            float fromScale, float toScale, float fromAlpha, float toAlpha,
            float dlitelnostS, bool perelet)
        {
            if (rt == null) yield break;
            rt.localScale = Vector3.one * fromScale;
            if (cg != null) cg.alpha = fromAlpha;
            float t = 0f;
            while (t < dlitelnostS)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dlitelnostS);
                float masshtabK = perelet ? EaseOutBack(k) : EaseOutCubic(k);
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(fromScale, toScale, masshtabK);
                if (cg != null) cg.alpha = Mathf.Lerp(fromAlpha, toAlpha, EaseOutCubic(k));
                yield return null;
            }
            rt.localScale = Vector3.one * toScale;
            if (cg != null) cg.alpha = toAlpha;
        }

        /// <summary>Масштаб кнопки при нажатии/отпускании — без альфы, ease-out.</summary>
        public static IEnumerator AnimatMasshtabKnopki(Transform t, Vector3 ot, Vector3 do_, float dlitelnostS)
        {
            if (t == null) yield break;
            float tt = 0f;
            while (tt < dlitelnostS)
            {
                tt += Time.unscaledDeltaTime;
                float k = EaseOutCubic(Mathf.Clamp01(tt / dlitelnostS));
                t.localScale = Vector3.Lerp(ot, do_, k);
                yield return null;
            }
            t.localScale = do_;
        }

        /// <summary>Плашка имени: альфа 0→1 и подъём на `PLASHKA_IMENI_PODYEM` px.</summary>
        public static IEnumerator AnimatPoyavleniePlashki(RectTransform rt, CanvasGroup cg, Vector2 finalPos)
        {
            if (rt == null) yield break;
            Vector2 start = finalPos - new Vector2(0f, PLASHKA_IMENI_PODYEM);
            rt.anchoredPosition = start;
            if (cg != null) cg.alpha = 0f;
            float t = 0f;
            while (t < PLASHKA_IMENI_S)
            {
                t += Time.unscaledDeltaTime;
                float k = EaseOutCubic(Mathf.Clamp01(t / PLASHKA_IMENI_S));
                rt.anchoredPosition = Vector2.Lerp(start, finalPos, k);
                if (cg != null) cg.alpha = k;
                yield return null;
            }
            rt.anchoredPosition = finalPos;
            if (cg != null) cg.alpha = 1f;
        }

        /// <summary>Полёт монеты-иконки по дуге от кнопки "Продать" к капсуле кредитов.
        /// Клонирует УЖЕ существующую иконку (`obrazets`, "znachok-kredity" 28px из
        /// попапа) — тот же спрайт, что смонтирован в сборке, а не новая загрузка
        /// ресурса рантаймом (см. правило про звук/шрифт, которые тихо не доезжали
        /// до сборки — спрайт тем же приёмом гарантированно есть).</summary>
        public static IEnumerator LetetMonetaKKreditam(RectTransform obrazets, RectTransform tsel, Transform sloy)
        {
            if (obrazets == null || tsel == null || sloy == null) yield break;

            var klon = Object.Instantiate(obrazets.gameObject, sloy, false);
            klon.name = "moneta-polet";
            var rt = (RectTransform)klon.transform;
            rt.SetAsLastSibling();
            rt.localScale = Vector3.one;
            var cg = klon.GetComponent<CanvasGroup>();
            if (cg == null) cg = klon.AddComponent<CanvasGroup>();
            var img = klon.GetComponent<Image>();
            if (img != null) img.raycastTarget = false; // летит поверх интерфейса, клики не перехватывает

            // Координаты холста, не мира: холст Screen Space - Camera с масштабом ~0.0005,
            // в мировых единицах дуга 90 уносила монету за экран на весь полёт (серия prodat2, этап 3 блок 1).
            Vector3 start = sloy.InverseTransformPoint(obrazets.position);
            Vector3 finish = sloy.InverseTransformPoint(tsel.position);
            Vector3 verh = (start + finish) * 0.5f + Vector3.up * MONETA_DUGA_VYSOTA;

            float t = 0f;
            while (t < MONETA_POLET_S)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / MONETA_POLET_S);
                Vector3 a = Vector3.Lerp(start, verh, k);
                Vector3 b = Vector3.Lerp(verh, finish, k);
                rt.localPosition = Vector3.Lerp(a, b, k);
                if (k > 0.7f) cg.alpha = 1f - (k - 0.7f) / 0.3f; // исчезновение в конце полёта
                yield return null;
            }
            Object.Destroy(klon);
        }
    }
}
