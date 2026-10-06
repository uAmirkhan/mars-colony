using System.Collections.Generic;
using MarsColony.Domain;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Единственный обработчик ввода в сборке: держит состояние колонии,
    /// принимает нажатие, отдает его объекту сцены и обновляет картинку.
    ///
    /// Домен ничего не знает ни про Unity, ни про мышь. Здесь только три вещи:
    /// куда попал курсор, чей это объект и что показать после.
    ///
    /// Чего в этом срезе сознательно нет:
    ///   — сохранения: состояние живет до перезагрузки страницы;
    ///   — часов реального времени: отсчет идет от старта плеера
    ///     (`Time.timeAsDouble`). Оба места чинятся вместе, когда появится
    ///     сохранение: домен уже считает время числом секунд и ему все равно,
    ///     от чего оно отсчитано.
    /// </summary>
    public sealed class ColonyGame : MonoBehaviour
    {
        public ColonyState State { get; private set; }

        private readonly List<ClickTarget> _targets = new List<ClickTarget>();
        private readonly BrowserBridge _bridge = new BrowserBridge();
        private Camera _cam;
        private Hud _hud;
        private ZhivoyInterfeys _zhivoy;

        private void Awake()
        {
            State = ColonyState.CreateNew();
            _cam = Camera.main;
            _hud = FindFirstObjectByType<Hud>();
            _zhivoy = FindFirstObjectByType<ZhivoyInterfeys>();
            _targets.AddRange(FindObjectsByType<ClickTarget>(FindObjectsSortMode.None));
        }

        private void Update()
        {
            double now = Time.timeAsDouble;

            // Готовность выводится из времени, а не хранится: одно правило,
            // одно место. Тот же договор, что в `production.ts`.
            foreach (FieldSlot slot in State.fields)
                Production.RefreshField(slot, now);

            bool clicked = false;
            if (Input.GetMouseButtonDown(0))
            {
                ClickTarget target = Pick(Input.mousePosition);
                if (target != null)
                {
                    target.OnClicked(this, now);
                    clicked = true;
                }
            }

            foreach (ClickTarget target in _targets)
                target.Render(this, now);

            if (_hud != null)
                _hud.Render(this, now);

            // Единственный рантайм интерфейса. Прежде их было два — `Hud` под
            // кодовый холст и `ZhivoyHud` под рукописный; холст остался один,
            // и рантайм обязан быть один, иначе появится третий.
            if (_zhivoy != null)
                _zhivoy.Obnovit(State, now);

            _bridge.Publish(this, _targets, _hud, _cam, now, clicked);
        }

        /// <summary>
        /// Кого мы нажали. Луч из камеры через точку курсора, из всех задетых
        /// габаритов берётся ближний.
        ///
        /// Раньше здесь экранная точка переводилась в мир через
        /// `ScreenToWorldPoint` и сравнивалась по X и Y. В плоском срезе это
        /// работало, в трёхмерной сцене — нет: `ScreenToWorldPoint` без
        /// заданной глубины возвращает точку на ближнем отсечении, а здания
        /// стоят в десятках метров дальше. Попадание не срабатывало никогда, и
        /// при этом ничего не падало и ничего не писалось в консоль.
        /// </summary>
        private ClickTarget Pick(Vector3 screen_point)
        {
            if (_cam == null)
                return null;

            Ray luch = _cam.ScreenPointToRay(screen_point);
            ClickTarget best = null;
            float blizhayshee = float.MaxValue;
            foreach (ClickTarget target in _targets)
            {
                float rasstoyanie;
                if (!target.Popadanie(luch, out rasstoyanie))
                    continue;
                if (rasstoyanie < blizhayshee)
                {
                    blizhayshee = rasstoyanie;
                    best = target;
                }
            }
            return best;
        }
    }
}
