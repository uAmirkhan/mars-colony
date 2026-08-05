/**
 * Шина эффектов: вылетающие цифры и летящие к счетчику точки.
 *
 * Живет вне React-дерева игры сознательно. Эффект рождается в обработчике
 * действия и должен пережить перерисовку того элемента, из которого вылетел:
 * грядка после сбора меняет состояние и перемонтируется, а цифра «+4» обязана
 * долететь. Поэтому шина — модульный синглтон, а рисует ее один слой поверх
 * всего экрана.
 */

export type FxTone = 'gain' | 'cost' | 'xp' | 'level' | 'deny';

export interface FloatFx {
  id: number;
  kind: 'float';
  text: string;
  tone: FxTone;
  x: number;
  y: number;
}

export interface FlyFx {
  id: number;
  kind: 'fly';
  tone: FxTone;
  x: number;
  y: number;
  /** Смещение до цели в пикселях: анимация читает его из CSS-переменных. */
  dx: number;
  dy: number;
  /** Задержка старта — каскад из нескольких точек не должен идти залпом. */
  delay_ms: number;
}

export type Fx = FloatFx | FlyFx;

export interface Point {
  x: number;
  y: number;
}

/** Длительности из [[ux-motion-spec]] раздел 2. Не выдуманные. */
export const FLOAT_MS = 900;
export const FLY_MS = 460;
/** Шаг каскада иконок урожая — там же, 9.1: «задержка старта ~40 мс». */
export const FLY_STEP_MS = 40;
/** Больше пяти иконок за сбор не летит: спека 4.1 задает 3-5. */
const FLY_MAX = 5;

let seq = 0;
let items: Fx[] = [];
const listeners = new Set<() => void>();

function emit(): void {
  for (const fn of listeners) fn();
}

export function subscribeFx(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

/** Снимок для `useSyncExternalStore`: ссылка меняется только при изменении списка. */
export function getFx(): Fx[] {
  return items;
}

function drop(id: number): void {
  items = items.filter((it) => it.id !== id);
  emit();
}

function push(item: Fx, life_ms: number): void {
  items = [...items, item];
  emit();
  setTimeout(() => drop(item.id), life_ms);
}

/** Центр элемента в координатах вьюпорта. Слой эффектов — `position: fixed`. */
export function centerOf(el: Element | null): Point | null {
  if (!el) return null;
  const r = el.getBoundingClientRect();
  if (r.width === 0 && r.height === 0) return null;
  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
}

/**
 * Точка привязки по имени. Счетчики HUD помечены `data-fx-anchor`, а не
 * прокинуты рефами: эффект рождается в обработчике действия, до которого
 * реф пришлось бы тащить через четыре экрана.
 */
export function anchor(name: string): Point | null {
  if (typeof document === 'undefined') return null;
  return centerOf(document.querySelector(`[data-fx-anchor="${name}"]`));
}

export function spawnFloat(text: string, tone: FxTone, at: Point | null): void {
  if (!at || !text) return;
  push({ id: ++seq, kind: 'float', text, tone, x: at.x, y: at.y }, FLOAT_MS + 60);
}

/**
 * Каскад точек от источника к счетчику. Количество — не количество товара:
 * при qty=20 двадцать точек превращаются в кашу, спека 4.1 задает 3-5 штук
 * независимо от числа.
 */
export function spawnFly(from: Point | null, to: Point | null, tone: FxTone, count = 4): void {
  if (!from || !to) return;
  const n = Math.max(1, Math.min(FLY_MAX, count));
  for (let i = 0; i < n; i++) {
    // Разброс старта, иначе точки идут одной линией и читаются как одна.
    const jitter_x = (i - (n - 1) / 2) * 11;
    const jitter_y = (i % 2 === 0 ? -1 : 1) * 7;
    const x = from.x + jitter_x;
    const y = from.y + jitter_y;
    push(
      {
        id: ++seq,
        kind: 'fly',
        tone,
        x,
        y,
        dx: to.x - x,
        dy: to.y - y,
        delay_ms: i * FLY_STEP_MS,
      },
      FLY_MS + n * FLY_STEP_MS + 60,
    );
  }
}

/** Полная очистка. Нужна только тестам и смене экрана. */
export function clearFx(): void {
  items = [];
  emit();
}
