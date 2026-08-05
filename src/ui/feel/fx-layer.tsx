/**
 * Слой эффектов и выключатель звука.
 *
 * Слой один на все приложение и стоит поверх экранов: цифра, вылетевшая из
 * грядки, не должна обрезаться границей панели, из которой она вылетела.
 * Клики он не перехватывает — `pointer-events: none` в стилях.
 */

import { type CSSProperties, useSyncExternalStore } from 'react';
import { FLOAT_MS, FLY_MS, type Fx, getFx, subscribeFx } from './fx';
import { isMuted, setMuted, subscribeMute } from './sfx';

const NO_FX: Fx[] = [];

/** Снимок для серверного рендера: эффектов там нет по определению. */
function noFx(): Fx[] {
  return NO_FX;
}

function notMuted(): boolean {
  return false;
}

export function FxLayer() {
  const items = useSyncExternalStore(subscribeFx, getFx, noFx);

  return (
    <div className="fx-layer" aria-hidden="true">
      {items.map((it) =>
        it.kind === 'float' ? (
          <div
            key={it.id}
            className={`fx-float fx-${it.tone}`}
            style={{ left: it.x, top: it.y, animationDuration: `${FLOAT_MS}ms` }}
          >
            {it.text}
          </div>
        ) : (
          <div
            key={it.id}
            className={`fx-fly fx-${it.tone}`}
            style={
              {
                left: it.x,
                top: it.y,
                animationDuration: `${FLY_MS}ms`,
                animationDelay: `${it.delay_ms}ms`,
                // Цель полета передается стилю: кейфрейм один на все точки.
                '--fx-dx': `${it.dx}px`,
                '--fx-dy': `${it.dy}px`,
              } as CSSProperties
            }
          />
        ),
      )}
    </div>
  );
}

/**
 * Выключатель звука. Стоит в углу и всегда на экране: игрок, которому звук
 * помешал, ищет выключатель первые пять секунд, а не в настройках.
 *
 * Подпись словом, а не иконкой динамика: глиф эмодзи — это ровно тот ресурс,
 * который в этом проекте уже молча не доезжал до сборки.
 */
export function SoundToggle() {
  const muted = useSyncExternalStore(subscribeMute, isMuted, notMuted);

  return (
    <button
      type="button"
      className={`btn ${muted ? 'btn-secondary' : 'btn-primary'} sound-toggle`}
      data-testid="sound-toggle"
      aria-pressed={muted}
      title={muted ? 'Включить звук' : 'Выключить звук'}
      onClick={() => setMuted(!muted)}
    >
      {muted ? 'Тихо' : 'Звук'}
    </button>
  );
}
