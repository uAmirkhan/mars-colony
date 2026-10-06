using UnityEngine;
using UnityEngine.EventSystems;

namespace MarsColony.Game
{
    /// <summary>
    /// Свободное управление камерой (Khan 08.09): тянуть мир пальцем или мышью, стрелки и WASD.
    /// Камера двигается только по XZ, наклон и высота неизменны — тот же изометрический вид.
    /// Точка земли под центром экрана держится в прямоугольнике колонии, чтобы не уезжать за ландшафт.
    ///
    /// Перетаскивание «за землю»: точка мира под указателем в момент нажатия остаётся под указателем,
    /// пока кнопка нажата. Нажатие над интерфейсом не начинает перетаскивание.
    /// Подвод камеры списком объектов и обучением (`IgraKolonii.PodvestiKameru`) идёт через тот же зажим.
    /// </summary>
    public sealed class UpravlenieKameroy : MonoBehaviour
    {
        /// <summary>
        /// Границы точки под центром экрана, м. Замерены 08.09 рендером с небом в маджента: внутри этого
        /// прямоугольника ни на одном краю кадра не видно неба за краем земли (Khan: «закрыть дыры по краям»).
        /// Земля -68..68, но с юга и запада край входит в кадр раньше из-за наклона камеры.
        /// </summary>
        public static readonly Rect GRANITSY = Rect.MinMaxRect(-27f, -55f, 53f, 31f);
        private const float SKOROST_KLAVISH = 28f;     // м/с
        private const float POROG_PERETASKIVANIYA = 10f;   // px: меньше — это тап, а не перетаскивание

        private Camera _cam;
        private bool _tyanem, _nazhatoNadUI;
        private Vector3 _tochkaZahvata;   // точка земли под указателем в момент нажатия
        private Vector2 _ekranNazhatiya;
        private float _ploskostY;

        /// <summary>Сколько px прошёл указатель с момента нажатия; выбор зданий не срабатывает после перетаскивания.</summary>
        public static float SmeshchenieUkazatelya { get; private set; }
        public static bool Peretaskivanie { get; private set; }

        public static UpravlenieKameroy Obespechit(Camera cam)
        {
            if (cam == null) return null;
            var est = cam.GetComponent<UpravlenieKameroy>();
            if (est == null) est = cam.gameObject.AddComponent<UpravlenieKameroy>();
            est._cam = cam;
            return est;
        }

        /// <summary>Позиция камеры, при которой точка под центром экрана не выходит за границы.</summary>
        public static Vector3 Zazhat(Camera cam, Vector3 pozitsiya)
        {
            var luch = new Ray(pozitsiya, cam.transform.forward);
            var pl = new Plane(Vector3.up, Vector3.zero);
            if (!pl.Raycast(luch, out float d)) return pozitsiya;
            Vector3 tsentr = luch.GetPoint(d);
            float x = Mathf.Clamp(tsentr.x, GRANITSY.xMin, GRANITSY.xMax), z = Mathf.Clamp(tsentr.z, GRANITSY.yMin, GRANITSY.yMax);
            return pozitsiya + new Vector3(x - tsentr.x, 0f, z - tsentr.z);
        }

        private bool TochkaZemli(Vector2 ekran, float y, out Vector3 tochka)
        {
            var luch = _cam.ScreenPointToRay(ekran);
            var pl = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            if (pl.Raycast(luch, out float d)) { tochka = luch.GetPoint(d); return true; }
            tochka = Vector3.zero; return false;
        }

        private void Update()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) return;

            // Клавиатура: стрелки и WASD, по осям экрана, спроецированным на землю
            Vector3 vpered = _cam.transform.forward; vpered.y = 0f; vpered.Normalize();
            Vector3 vpravo = _cam.transform.right; vpravo.y = 0f; vpravo.Normalize();
            float gx = (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) ? 1f : 0f);
            float gz = (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S) ? 1f : 0f);
            if (gx != 0f || gz != 0f)
                _cam.transform.position = Zazhat(_cam, _cam.transform.position + (vpravo * gx + vpered * gz).normalized * SKOROST_KLAVISH * Time.unscaledDeltaTime);

            // Указатель: мышь или первый палец
            bool nazhato = Input.GetMouseButton(0) || Input.touchCount > 0;
            Vector2 ekran = Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
            bool nachalo = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);

            if (nachalo)
            {
                _nazhatoNadUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                _ekranNazhatiya = ekran; SmeshchenieUkazatelya = 0f; Peretaskivanie = false;
                _ploskostY = 0f;
                _tyanem = !_nazhatoNadUI && TochkaZemli(ekran, _ploskostY, out _tochkaZahvata);
            }
            else if (nazhato && _tyanem)
            {
                SmeshchenieUkazatelya = Vector2.Distance(_ekranNazhatiya, ekran);
                if (SmeshchenieUkazatelya >= POROG_PERETASKIVANIYA) Peretaskivanie = true;
                if (Peretaskivanie && TochkaZemli(ekran, _ploskostY, out Vector3 tochka))
                {
                    Vector3 sdvig = _tochkaZahvata - tochka; sdvig.y = 0f;
                    _cam.transform.position = Zazhat(_cam, _cam.transform.position + sdvig);
                }
            }
            else if (!nazhato)
            {
                _tyanem = false;
            }
        }
    }
}
