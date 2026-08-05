/**
 * Отклик на нажатие. Один делегированный слушатель на документ вместо правки
 * каждой кнопки: кнопок и слотов в игре больше сорока, и любая новая обязана
 * отзываться, не вспоминая про это.
 *
 * Анимация ставится через Web Animations API, а не классом. Причина
 * конкретная: слот грядки после сбора меняет `className` (уходит `slot-ready`),
 * React переписывает атрибут и стер бы класс отклика ровно в том действии,
 * ради которого отклик и нужен. `element.animate()` живет вне реконсиляции и
 * пережить перерисовку не может помешать никто.
 */

import { ensureAudio, play } from './sfx';

/**
 * Микро-отклик на тап: [[ux-motion-spec]] раздел 2, 80-120 мс, scale 1→0.92→1.
 *
 * Разложен на две половины — нажать и отпустить, — и между ними кнопка стоит
 * продавленной. Быстрый тап от спеки не отличается ничем: палец уходит раньше,
 * чем кончается первая половина, и глаз видит ровно 1→0.93→1 за две сотни
 * миллисекунд. Разница появляется только когда палец задержался: цельная
 * анимация в этом случае возвращала кнопку в исходный вид ПОД пальцем, и
 * удержание переставало быть видимым вовсе.
 */
const PRESS_DOWN_MS = 90;
const PRESS_UP_MS = 120;
const PRESS_SCALE = 0.93;
/** Отказ: короткий сдвиг, не тряска. Спека 4.7 запрещает наказывать за отказ. */
const NUDGE_MS = 180;

let last_gesture_at = 0;

/** Когда игрок последний раз трогал экран. Эффекты без жеста не рождаются. */
export function lastGestureAt(): number {
  return last_gesture_at;
}

/** Изменение состояния считается ответом на действие только рядом с жестом. */
export const GESTURE_WINDOW_MS = 1200;

export function withinGesture(): boolean {
  return Date.now() - last_gesture_at < GESTURE_WINDOW_MS;
}

function canAnimate(el: Element): el is HTMLElement {
  return typeof (el as HTMLElement).animate === 'function';
}

/**
 * Кто сейчас продавлен. Ровно один: мультитач в этой игре не нужен, а забытый
 * в продавленном виде элемент — самый заметный вид залипшего интерфейса.
 */
let held: { el: HTMLElement; anim: Animation } | null = null;

/** Нажатие: кнопка уходит вниз и ОСТАЕТСЯ там, пока палец не поднят. */
export function pressPop(el: Element | null): void {
  if (!el || !canAnimate(el)) return;
  releasePress();
  const anim = el.animate([{ transform: 'scale(1)' }, { transform: `scale(${PRESS_SCALE})` }], {
    duration: PRESS_DOWN_MS,
    easing: 'cubic-bezier(0.33, 1, 0.68, 1)',
    fill: 'forwards',
  });
  held = { el, anim };
}

/**
 * Отпускание: кнопка возвращается из того положения, в котором ее застали.
 * Читается текущий кадр анимации, а не константа, — иначе быстрый тап давал бы
 * скачок вниз на полпути вверх.
 */
export function releasePress(): void {
  if (!held) return;
  const { el, anim } = held;
  held = null;

  const style = getComputedStyle(el).transform;
  anim.cancel();
  el.animate(
    [
      { transform: style === 'none' ? `scale(${PRESS_SCALE})` : style },
      { transform: 'scale(1)' },
    ],
    { duration: PRESS_UP_MS, easing: 'cubic-bezier(0.16, 1, 0.3, 1)' },
  );
}

/** Реакция на отказ: сдвиг и просадка цвета. Заметно, но без красных вспышек. */
export function denyNudge(el: Element | null): void {
  if (!el || !canAnimate(el)) return;
  el.animate(
    [
      { transform: 'translateX(0)', filter: 'saturate(1)' },
      { transform: 'translateX(-6px)', filter: 'saturate(0.45)', offset: 0.25 },
      { transform: 'translateX(5px)', offset: 0.6 },
      { transform: 'translateX(0)', filter: 'saturate(1)' },
    ],
    { duration: NUDGE_MS, easing: 'cubic-bezier(0.65, 0, 0.35, 1)' },
  );
}

function onPointerDown(event: Event): void {
  last_gesture_at = Date.now();
  // Звуковой контекст разблокируется только внутри жеста. Здесь единственное
  // место в игре, где мы гарантированно внутри него.
  ensureAudio();

  const target = event.target;
  if (!(target instanceof Element)) return;
  const el = target.closest('.btn, .slot, [data-press]');
  if (!el) return;
  if (el instanceof HTMLButtonElement && el.disabled) return;

  pressPop(el);
  play('press');
}

function onPointerUp(): void {
  releasePress();
}

let installed = false;

/**
 * Ставится один раз на весь документ. Повторный вызов ничего не делает.
 *
 * Подъем слушается на окне, а не на элементе: палец нередко уходит с кнопки до
 * того, как поднялся, и слушатель на самой кнопке этого не увидел бы — кнопка
 * осталась бы продавленной навсегда. `pointercancel` — тот же случай, только
 * его объявляет браузер, начав скролл.
 */
export function installPress(): () => void {
  if (installed || typeof document === 'undefined') return () => {};
  installed = true;
  document.addEventListener('pointerdown', onPointerDown, { capture: true });
  window.addEventListener('pointerup', onPointerUp, { capture: true });
  window.addEventListener('pointercancel', onPointerUp, { capture: true });
  return () => {
    document.removeEventListener('pointerdown', onPointerDown, { capture: true });
    window.removeEventListener('pointerup', onPointerUp, { capture: true });
    window.removeEventListener('pointercancel', onPointerUp, { capture: true });
    installed = false;
  };
}
