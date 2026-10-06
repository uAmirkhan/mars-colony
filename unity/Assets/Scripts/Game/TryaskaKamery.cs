using System.Collections;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Короткая дрожь камеры — отклик на касание шасси шаттла (план
    /// "loop/ui/plan-animatsii-shattla.md", §3). Намеренно маленький и
    /// отдельный от <see cref="PolyotShattla"/> компонент: он трогает ТОЛЬКО
    /// <c>transform.position</c> камеры, никогда fieldOfView/aspect/rotation,
    /// и обязан вернуть камеру ровно в исходную точку по завершении — план
    /// прямо требует численного совпадения "после == до", иначе следующие
    /// кадры сборщика (`Shoot`) съедут.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TryaskaKamery : MonoBehaviour
    {
        private Coroutine _tekushchaya;

        /// <param name="tselevayaTochka">Мировая точка, к которой привязана
        /// тряска (шаттл/под) — нужна только чтобы прикинуть метр-на-пиксель
        /// на реальной глубине сцены, а не на произвольной константе.</param>
        /// <param name="amplitudaPx">Амплитуда в экранных пикселях (план: 2).</param>
        /// <param name="dlitelnostSek">Длительность (план: 0.12 с = 120 мс).</param>
        public void Vstryakhnut(Vector3 tselevayaTochka, float amplitudaPx, float dlitelnostSek)
        {
            if (_tekushchaya != null) StopCoroutine(_tekushchaya);
            _tekushchaya = StartCoroutine(Tryastis(tselevayaTochka, amplitudaPx, dlitelnostSek));
        }

        private IEnumerator Tryastis(Vector3 tselevayaTochka, float amplitudaPx, float dlitelnost)
        {
            var cam = GetComponent<Camera>();
            Vector3 ishodnaya = transform.position;
            if (cam == null || dlitelnost <= 0f)
            {
                yield break;
            }

            float glubina = Mathf.Max(1f, Vector3.Dot(tselevayaTochka - ishodnaya, transform.forward));
            // Метров на пиксель по вертикали кадра на глубине шаттла — тот же
            // расчёт перспективы, что и WorldToScreenPoint, но без обращения к
            // рендер-таргету: он не нужен для маленького локального смещения.
            float metrNaPixel = 2f * glubina * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)
                    / Mathf.Max(1f, Screen.height);
            float amplitudaM = amplitudaPx * metrNaPixel;

            float t = 0f;
            const float TSIKLOV = 2f; // "два затухающих цикла" — план §3
            while (t < dlitelnost)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / dlitelnost);
                float zatukhanie = 1f - u;
                float faza = u * TSIKLOV * 2f * Mathf.PI;
                float dx = Mathf.Sin(faza) * amplitudaM * zatukhanie;
                float dy = Mathf.Cos(faza * 1.3f) * amplitudaM * 0.6f * zatukhanie;
                transform.position = ishodnaya + transform.right * dx + transform.up * dy;
                yield return null;
            }

            // Возврат ТОЧНО в исходную точку — план требует численного равенства.
            transform.position = ishodnaya;
            _tekushchaya = null;
        }
    }
}
