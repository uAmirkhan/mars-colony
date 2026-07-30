/**
 * Компонентный кит по [[ux-component-library]]: панель, кнопка, слот, счетчик, таймер.
 * Ни один компонент не знает про игровые правила — только про отображение.
 */

import type { CSSProperties, ReactNode } from 'react';

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
      <div style={{ position: 'relative' }}>
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
      {children}
    </div>
  );
}

export function Button({
  kind = 'primary',
  disabled,
  onClick,
  children,
  full,
}: {
  kind?: 'primary' | 'secondary';
  disabled?: boolean;
  onClick?: () => void;
  children: ReactNode;
  full?: boolean;
}) {
  return (
    <button
      type="button"
      className={`btn btn-${kind}`}
      disabled={disabled}
      onClick={onClick}
      style={full ? { width: '100%' } : undefined}
    >
      {children}
    </button>
  );
}

export type CurrencyKind = 'credits' | 'isotopes' | 'xp';

const SHAPE: Record<CurrencyKind, string> = {
  credits: 'coin',
  isotopes: 'hex',
  xp: 'star',
};

/** Валюты различаются формой носителя, а не только цветом — раздел 1 арт-языка. */
export function Currency({ kind, value }: { kind: CurrencyKind; value: number | string }) {
  return (
    <div className="currency">
      <div className={SHAPE[kind]} />
      <span>{value}</span>
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

export function ProgressBar({ value, max }: { value: number; max: number }) {
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
          background: 'linear-gradient(180deg,#6ec4f0,var(--xp))',
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
