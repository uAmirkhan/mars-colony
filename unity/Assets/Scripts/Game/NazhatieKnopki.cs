using UnityEngine;
using UnityEngine.EventSystems;

namespace MarsColony.Game
{
    /// <summary>
    /// Отклик на нажатие любой uGUI-кнопки (спека этапа 3, блок 1, правка A1,
    /// пункт 1): сжатие до 0.94 масштаба за 40 мс, возврат за 80 мс ease-out.
    /// Навешивается автоматически всем Button холста из
    /// `AnimatsiiInterfeysa.PodklyuchitKnopki` — руками добавлять не нужно.
    ///
    /// Базовый масштаб читается в Awake, а не жёстко Vector3.one: некоторые
    /// узлы уже приходят промасштабированными сборщиком.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class NazhatieKnopki : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        Vector3 _baza;
        Coroutine _anim;

        void Awake() { _baza = transform.localScale; }

        public void OnPointerDown(PointerEventData e)
        {
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(AnimatsiiInterfeysa.AnimatMasshtabKnopki(
                transform, transform.localScale, _baza * AnimatsiiInterfeysa.NAZHATIE_MASSHTAB,
                AnimatsiiInterfeysa.NAZHATIE_VNIZ_S));
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(AnimatsiiInterfeysa.AnimatMasshtabKnopki(
                transform, transform.localScale, _baza,
                AnimatsiiInterfeysa.NAZHATIE_VVERH_S));
        }
    }
}
