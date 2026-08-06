/**
 * Компонентный кит по [[ux-component-library]]: панель, кнопка, слот, счетчик, таймер.
 * Ни один компонент не знает про игровые правила — только про отображение.
 */

import type { CSSProperties, ReactNode } from 'react';
import { useCountUp } from './feel/count-up';

export function Panel({
  title,
  onClose,
  children,
  style,
}: {
  title: string;
  onClose?: () => void;
  children: ReactNode;
  style?: CSSProperties;
}) {
  return (
    <div className="panel" style={{ padding: '32px 22px 20px', minWidth: 320, ...style }}>
      {/* Шапка не уезжает: она вне прокручиваемой области, а значит крестик
          доступен при любой высоте содержимого. */}
      <div style={{ position: 'relative', flex: '0 0 auto' }}>
        <div className="panel-title">{title}</div>
        {onClose && (
          <button
            type="button"
            className="btn btn-close"
            onClick={onClose}
            style={{ position: 'absolute', right: -12, top: -34 }}
            aria-label="Закрыть"
          >
            ✕
          </button>
        )}
      </div>
      {/* `minHeight: 0` обязателен: без него флекс-элемент отказывается быть
          меньше своего содержимого, прокрутка не включается, и панель снова
          вырастает выше экрана — ровно тот дефект, ради которого все это. */}
      <div style={{ flex: '1 1 auto', minHeight: 0, overflowY: 'auto', overflowX: 'hidden' }}>
        {children}
      </div>
    </div>
  );
}

export function Button({
  kind = 'primary',
  disabled,
  onClick,
  children,
  full,
  title,
  pointer,
}: {
  kind?: 'primary' | 'secondary';
  disabled?: boolean;
  onClick?: () => void;
  children: ReactNode;
  full?: boolean;
  /** Подпись причины, по которой кнопка недоступна: ТЗ требует ее у ряда состояний. */
  title?: string;
  /**
   * На кнопке стоит указатель первой цели ([[first-goal]]): кольцо и стрелка.
   *
   * Флагом, а не свободным `className`: подсветка «нажми сюда» в игре одна, и
   * второй способ ее нарисовать сразу же разъехался бы с первым по цвету.
   */
  pointer?: boolean;
}) {
  return (
    <button
      type="button"
      className={`btn btn-${kind}${pointer ? ' goal-point' : ''}`}
      disabled={disabled}
      onClick={onClick}
      title={title}
      style={full ? { width: '100%' } : undefined}
    >
      {children}
    </button>
  );
}

/**
 * Единственное допустимое обозначение изотопов на кнопках и в подписях —
 * UX-стандарт каркаса раздел 13: «Обозначение изотопов — только иконка ⚛».
 *
 * Константа, а не литерал в каждой кнопке: одна валюта уже разъехалась по
 * экранам на два разных глифа (⚛ у грядки, ⬡ у стройки, дрона и шаттла), и
 * заметить это можно было только сравнив четыре файла глазами.
 */
export const ISOTOPE_GLYPH = '⚛';

export type CurrencyKind = 'credits' | 'isotopes' | 'xp';

const SHAPE: Record<CurrencyKind, string> = {
  credits: 'coin',
  isotopes: 'hex',
  xp: 'star',
};

/**
 * Валюты различаются формой носителя, а не только цветом — раздел 1 арт-языка.
 *
 * Число докручивается, а не подменяется: прыжок счетчика глаз не ловит.
 * `data-fx-anchor` — точка, в которую летит вылетающая цифра начисления; без
 * нее эффект не знает, где находится кошелек.
 */
export function Currency({ kind, value }: { kind: CurrencyKind; value: number | string }) {
  const shown = useCountUp(typeof value === 'number' ? value : null);

  return (
    <div className="currency" data-fx-anchor={kind}>
      <div className={SHAPE[kind]} />
      <span>{shown ?? value}</span>
    </div>
  );
}

export function formatTime(seconds: number): string {
  const s = Math.max(0, Math.ceil(seconds));
  const m = Math.floor(s / 60);
  const rest = s % 60;
  if (m >= 60) {
    const h = Math.floor(m / 60);
    return `${h}ч ${String(m % 60).padStart(2, '0')}м`;
  }
  return `${m}м ${String(rest).padStart(2, '0')}с`;
}

export function Timer({ remaining_sec }: { remaining_sec: number }) {
  return (
    <span style={{ fontWeight: 800, fontSize: 13, color: 'var(--title)' }}>
      {formatTime(remaining_sec)}
    </span>
  );
}

/**
 * Порог, при котором капасити-бар красится предупреждающим цветом — ТЗ
 * производства 9.2: «цвет бара меняется на предупреждающий (акцент) при
 * заполнении >= 90%». Число не из конфиг-таблицы ТЗ (раздел 7 его не знает) —
 * это UI-порог экрана, а не тюнимая экономика; общая константа вместо двух
 * литералов в двух местах (склад, бейдж хаба того же склада).
 */
export const WAREHOUSE_WARN_RATIO = 0.9;

export function ProgressBar({
  value,
  max,
  warn,
}: {
  value: number;
  max: number;
  /** Красит бар предупреждающим цветом — capacity-бар склада у порога (9.2). */
  warn?: boolean;
}) {
  const pct = max > 0 ? Math.min(100, (value / max) * 100) : 0;
  return (
    <div
      style={{
        height: 14,
        background: 'rgba(122,83,48,.28)',
        borderRadius: 999,
        overflow: 'hidden',
        border: '2px solid var(--panel-border)',
      }}
    >
      <div
        style={{
          width: `${pct}%`,
          height: '100%',
          background: warn
            ? 'linear-gradient(180deg,#ffb37a,var(--close))'
            : 'linear-gradient(180deg,#6ec4f0,var(--xp))',
          transition: 'width 320ms ease',
        }}
      />
    </div>
  );
}

/** Заглушка ассета: цветной прямоугольник с подписью. Картинки подставим на 4-й день. */
export function GoodIcon({ name, size = 34 }: { name: string; size?: number }) {
  const hue = [...name].reduce((a, c) => a + c.charCodeAt(0), 0) % 360;
  return (
    <div
      style={{
        width: size,
        height: size,
        borderRadius: 10,
        background: `hsl(${hue} 55% 62%)`,
        border: '2px solid rgba(0,0,0,.18)',
        display: 'grid',
        placeItems: 'center',
        fontSize: size * 0.42,
        fontWeight: 800,
        color: '#fff',
        textShadow: '0 1px 0 rgba(0,0,0,.3)',
      }}
    >
      {name.slice(0, 1).toUpperCase()}
    </div>
  );
}
