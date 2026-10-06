using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MarsColony.Domain;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Мост в страницу: плеер выкладывает в `window.marsColony` рамки своих
    /// кликабельных объектов и короткую сводку состояния.
    ///
    /// Зачем это вообще нужно. Внутри канваса Unity нет DOM: браузерная проверка
    /// не может ни найти объект, ни узнать, куда целиться. Без моста ей остается
    /// зашитая доля экрана — а она разъедется от первой же правки раскладки, и
    /// проверка начнет падать на художнике вместо кодера.
    ///
    /// Чем это НЕ является. Сводка состояния — не доказательство работы. Плеер,
    /// рапортующий о себе, — ровно тот канал, по которому в проект четыре раза
    /// приходил ложный успех. Доказательством в `check-interaction.mjs` служит
    /// сравнение кадров; сводка только называет, что именно изменилось.
    ///
    /// Соглашение о координатах: доли ширины и высоты экрана, начало в ЛЕВОМ
    /// ВЕРХНЕМ углу, как в браузере (в Unity ось Y смотрит вверх — здесь она
    /// переворачивается один раз, тут). У целей публикуется ЦЕНТР и размер, у
    /// HUD — левый верхний угол и размер.
    /// </summary>
    public sealed class BrowserBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void MarsPublish(string json);
#endif

        /// <summary>Как часто обновлять сводку без событий. Клик публикуется сразу.</summary>
        private const float PUBLISH_PERIOD_SEC = 0.25f;

        private float _next_publish_at;

        [Serializable]
        private sealed class RectDto
        {
            public float x;
            public float y;
            public float w;
            public float h;
        }

        [Serializable]
        private sealed class TargetDto
        {
            public string id;
            public string label;
            public float x;
            public float y;
            public float w;
            public float h;
        }

        [Serializable]
        private sealed class FieldDto
        {
            public int idx;
            public string state;
            public string good_id;
            public int remaining_sec;
        }

        [Serializable]
        private sealed class StateDto
        {
            public int credits;
            public int xp;
            public int warehouse_qty;
            public int warehouse_capacity;
            public FieldDto[] fields;
        }

        [Serializable]
        private sealed class PayloadDto
        {
            public bool ready;
            public TargetDto[] targets;
            public RectDto hud;
            public StateDto state;
        }

        public void Publish(
            ColonyGame game,
            IList<ClickTarget> targets,
            Hud hud,
            Camera cam,
            double now,
            bool force
        )
        {
            if (!force && Time.unscaledTime < _next_publish_at)
                return;
            _next_publish_at = Time.unscaledTime + PUBLISH_PERIOD_SEC;

            var payload = new PayloadDto
            {
                ready = true,
                targets = Targets(targets, cam),
                hud = hud != null ? ToRectDto(hud.ScreenRectNormalized()) : null,
                state = StateOf(game, now),
            };

            string json = JsonUtility.ToJson(payload);
#if UNITY_WEBGL && !UNITY_EDITOR
            MarsPublish(json);
#endif
        }

        private static TargetDto[] Targets(IList<ClickTarget> targets, Camera cam)
        {
            var list = new List<TargetDto>();
            if (cam == null || Screen.width == 0 || Screen.height == 0)
                return list.ToArray();

            foreach (ClickTarget target in targets)
            {
                Bounds b = target.ClickBounds;
                Vector3 p0 = cam.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, 0f));
                Vector3 p1 = cam.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, 0f));

                float x0 = Mathf.Min(p0.x, p1.x);
                float x1 = Mathf.Max(p0.x, p1.x);
                float y0 = Mathf.Min(p0.y, p1.y);
                float y1 = Mathf.Max(p0.y, p1.y);

                list.Add(
                    new TargetDto
                    {
                        id = target.TargetId,
                        label = target.Label,
                        x = (x0 + x1) * 0.5f / Screen.width,
                        y = 1f - (y0 + y1) * 0.5f / Screen.height,
                        w = (x1 - x0) / Screen.width,
                        h = (y1 - y0) / Screen.height,
                    }
                );
            }
            return list.ToArray();
        }

        private static StateDto StateOf(ColonyGame game, double now)
        {
            ColonyState state = game.State;
            var fields = new FieldDto[state.fields.Count];
            for (int i = 0; i < state.fields.Count; i++)
            {
                FieldSlot slot = state.fields[i];
                fields[i] = new FieldDto
                {
                    idx = slot.idx,
                    state = slot.state.ToString(),
                    good_id = slot.good_id ?? string.Empty,
                    remaining_sec = FieldView.RemainingSec(slot, now),
                };
            }

            return new StateDto
            {
                credits = state.credits,
                xp = state.xp,
                warehouse_qty = Warehouse.TotalQty(state.warehouse),
                warehouse_capacity = state.warehouse.capacity,
                fields = fields,
            };
        }

        private static RectDto ToRectDto(Rect r) =>
            new RectDto
            {
                x = r.x,
                y = r.y,
                w = r.width,
                h = r.height,
            };
    }
}
