/**
 * Интерактивный балансный симулятор — главный артефакт проекта.
 *
 * Смысл экрана: человек двигает ползунок и видит, как ломается экономика.
 * Не отчет о прогоне, а инструмент, в котором поломку можно нащупать руками.
 *
 * Работает на том же доменном слое, что и игра. Балансная модель не лежит
 * в таблице рядом с игрой — она и есть игра.
 */

import { useMemo, useState } from 'react';
import { DEFAULT_TUNING, TUNING_RANGES, type Tuning } from '../domain/tuning';
import { DEFAULT_SIM, type SimConfig, type SimResult, simulate } from '../sim/simulate';

interface PlayerModel {
  sessions_per_day: number;
  session_length_min: number;
  orders_per_session: number;
  days: number;
}

const DEFAULT_PLAYER: PlayerModel = {
  sessions_per_day: 4,
  session_length_min: 20,
  orders_per_session: 3,
  days: 30,
};

const PLAYER_RANGES: Record<
  keyof PlayerModel,
  { min: number; max: number; step: number; label: string }
> = {
  sessions_per_day: { min: 1, max: 8, step: 1, label: 'Заходов в день' },
  session_length_min: { min: 3, max: 40, step: 1, label: 'Длина захода, мин' },
  orders_per_session: { min: 0, max: 8, step: 1, label: 'Заказов за заход' },
  days: { min: 7, max: 60, step: 1, label: 'Дней прогона' },
};

/** Часы начала заходов раскидываются по бодрствующему дню равномерно. */
function sessionStarts(count: number): number[] {
  const first = 8 * 60;
  const last = 22 * 60;
  if (count <= 1) return [first];
  const gap = (last - first) / (count - 1);
  return Array.from({ length: count }, (_, i) => Math.round(first + gap * i));
}

function buildConfig(player: PlayerModel, tuning: Tuning, seed: number): SimConfig {
  return {
    days: player.days,
    session_starts_min: sessionStarts(player.sessions_per_day),
    session_length_min: player.session_length_min,
    orders_per_session: player.orders_per_session,
    seed,
    tuning,
  };
}

// --- Инварианты, которые проверяет панель --------------------------------

interface Verdict {
  name: string;
  ok: boolean;
  value: string;
  why: string;
}

function checkInvariants(result: SimResult, player: PlayerModel): Verdict[] {
  const last = result.rows.at(-1);
  const day5 = result.milestones[5] ?? null;
  const day12 = result.milestones[12] ?? null;
  const hours_per_week = (player.sessions_per_day * player.session_length_min * 7) / 60;

  return [
    {
      name: 'Игра запускается с нуля',
      ok: result.planting_starved === 0,
      value:
        result.planting_starved === 0
          ? 'да'
          : `нет, ${result.planting_starved} раз нечем сеять`,
      why: 'Посев стоит кредиты. Если их не хватает, а заработать нечем, играть не во что.',
    },
    {
      name: 'Анти-софтлок не понадобился',
      ok: result.softlock_rescues === 0,
      value: result.softlock_rescues === 0 ? 'да' : `сработал ${result.softlock_rescues} раз`,
      why: 'И-15 — страховка на крайний случай. Регулярные срабатывания значат, что экономика живет на грани.',
    },
    {
      name: 'Второй уровень в первый день',
      ok: (result.rows[0]?.level ?? 1) >= 2,
      value: `ур. ${result.rows[0]?.level ?? 1} к концу дня 1`,
      why: 'Дрон открывается на ур.2. Не дошел за день — первая сессия прошла без второй механики.',
    },
    {
      name: 'Пятый уровень за первую неделю',
      ok: day5 !== null && day5 <= 7,
      value: day5 === null ? 'не достигнут' : `день ${day5}`,
      why: 'На ур.5 открывается шаттл. Дольше недели наедине с дроном — слабый онбординг.',
    },
    {
      name: 'Двенадцатый уровень за месяц',
      ok: day12 !== null,
      value: day12 === null ? 'не достигнут' : `день ${day12}`,
      why: 'Каркас обещает 2-3 недели при 5-10 часах. Шаттл и лайнер в модели не учтены, поэтому оценка снизу.',
    },
    {
      name: 'Склад ощущается узким местом',
      ok: result.warehouse_blocks > 0 && result.warehouse_blocks < player.days * 20,
      value: `${result.warehouse_blocks} блокировок сбора`,
      why: 'Ноль — конверсионный узел не работает и апгрейд склада никому не нужен. Слишком много — игра превращается в мучение.',
    },
    {
      name: 'Кредиты не обесцениваются',
      ok: (last?.credits ?? 0) < 60_000,
      value: `${last?.credits ?? 0} на конец прогона`,
      why: 'Если мягкая валюта копится без предела, стоки не работают и покупки перестают быть решением.',
    },
    {
      name: 'Бюджет игрока правдоподобен',
      ok: hours_per_week >= 3 && hours_per_week <= 20,
      value: `${hours_per_week.toFixed(1)} ч/неделю`,
      why: 'Каркас целится в 5-10 часов. За пределами этого коридора выводы прогона к реальной аудитории не относятся.',
    },
  ];
}

// --- График --------------------------------------------------------------

interface Series {
  label: string;
  color: string;
  points: number[];
}

function LineChart({ series, height = 150 }: { series: Series[]; height?: number }) {
  const width = 480;
  const pad = { left: 44, right: 8, top: 10, bottom: 20 };
  const all = series.flatMap((s) => s.points);
  const max = Math.max(1, ...all);
  const count = Math.max(1, ...series.map((s) => s.points.length));

  const x = (i: number) =>
    pad.left + (i / Math.max(1, count - 1)) * (width - pad.left - pad.right);
  const y = (v: number) => height - pad.bottom - (v / max) * (height - pad.top - pad.bottom);

  const ticks = [0, max / 2, max];

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      style={{ width: '100%', height: 'auto', display: 'block' }}
      role="img"
      aria-label={series.map((s) => s.label).join(', ')}
    >
      <title>{series.map((s) => s.label).join(', ')}</title>
      {ticks.map((t) => (
        <g key={t}>
          <line
            x1={pad.left}
            x2={width - pad.right}
            y1={y(t)}
            y2={y(t)}
            stroke="rgba(122,83,48,.18)"
            strokeWidth={1}
          />
          <text x={4} y={y(t) + 4} fontSize={10} fill="var(--text-muted)">
            {t >= 1000 ? `${Math.round(t / 1000)}k` : Math.round(t)}
          </text>
        </g>
      ))}
      {series.map((s) => (
        <polyline
          key={s.label}
          fill="none"
          stroke={s.color}
          strokeWidth={2.5}
          strokeLinejoin="round"
          strokeLinecap="round"
          points={s.points.map((v, i) => `${x(i)},${y(v)}`).join(' ')}
        />
      ))}
    </svg>
  );
}

// --- Ползунок ------------------------------------------------------------

function Slider({
  label,
  value,
  min,
  max,
  step,
  changed,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step: number;
  changed: boolean;
  onChange: (v: number) => void;
}) {
  return (
    <label style={{ display: 'block', marginBottom: 10 }}>
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          fontSize: 12,
          fontWeight: 700,
          color: changed ? 'var(--secondary-dark)' : 'var(--text)',
          marginBottom: 2,
        }}
      >
        <span>{label}</span>
        <span>
          {value}
          {changed && ' •'}
        </span>
      </div>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        style={{ width: '100%', accentColor: 'var(--action)' }}
      />
    </label>
  );
}

// --- Экран ---------------------------------------------------------------

export function SimScreen() {
  const [player, setPlayer] = useState<PlayerModel>(DEFAULT_PLAYER);
  const [tuning, setTuning] = useState<Tuning>(DEFAULT_TUNING);
  const [seed, setSeed] = useState(DEFAULT_SIM.seed);

  const result = useMemo(
    () => simulate(buildConfig(player, tuning, seed)),
    [player, tuning, seed],
  );

  const verdicts = useMemo(() => checkInvariants(result, player), [result, player]);
  const broken = verdicts.filter((v) => !v.ok);

  const rows = result.rows;
  const reset = () => {
    setPlayer(DEFAULT_PLAYER);
    setTuning(DEFAULT_TUNING);
    setSeed(DEFAULT_SIM.seed);
  };
  const touched =
    JSON.stringify(tuning) !== JSON.stringify(DEFAULT_TUNING) ||
    JSON.stringify(player) !== JSON.stringify(DEFAULT_PLAYER);

  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'minmax(260px, 320px) 1fr',
        gap: 16,
        padding: 16,
        alignItems: 'start',
        maxWidth: 1180,
        margin: '0 auto',
      }}
    >
      {/* Ползунки */}
      <div className="panel" style={{ padding: 18 }}>
        <div style={{ fontWeight: 800, color: 'var(--title)', marginBottom: 10 }}>
          Модель игрока
        </div>
        {(Object.keys(PLAYER_RANGES) as Array<keyof PlayerModel>).map((key) => {
          const r = PLAYER_RANGES[key];
          return (
            <Slider
              key={key}
              label={r.label}
              value={player[key]}
              min={r.min}
              max={r.max}
              step={r.step}
              changed={player[key] !== DEFAULT_PLAYER[key]}
              onChange={(v) => setPlayer((p) => ({ ...p, [key]: v }))}
            />
          );
        })}

        <div
          style={{
            fontWeight: 800,
            color: 'var(--title)',
            margin: '16px 0 10px',
            borderTop: '3px dashed rgba(168,118,62,.35)',
            paddingTop: 14,
          }}
        >
          Параметры игры
        </div>
        {(Object.keys(TUNING_RANGES) as Array<keyof Tuning>).map((key) => {
          const r = TUNING_RANGES[key];
          return (
            <Slider
              key={key}
              label={r.label}
              value={tuning[key]}
              min={r.min}
              max={r.max}
              step={r.step}
              changed={tuning[key] !== DEFAULT_TUNING[key]}
              onChange={(v) => setTuning((t) => ({ ...t, [key]: v }))}
            />
          );
        })}

        <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 14 }}>
          <label style={{ fontSize: 12, fontWeight: 700, color: 'var(--text)' }}>
            Seed
            <input
              type="number"
              value={seed}
              onChange={(e) => setSeed(Number(e.target.value))}
              style={{
                width: 96,
                marginLeft: 6,
                padding: '4px 6px',
                border: '2px solid var(--panel-border)',
                borderRadius: 8,
                background: 'var(--panel-warm)',
                color: 'var(--title)',
                fontWeight: 700,
              }}
            />
          </label>
          <button
            type="button"
            className="btn btn-secondary"
            style={{ padding: '6px 12px', fontSize: 13 }}
            onClick={reset}
            disabled={!touched}
          >
            Сбросить
          </button>
        </div>
        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 8 }}>
          Один seed — один и тот же прогон. Всегда.
        </div>
      </div>

      {/* Результат */}
      <div style={{ display: 'grid', gap: 14 }}>
        <div
          className="panel"
          style={{
            padding: '12px 18px',
            display: 'flex',
            gap: 18,
            flexWrap: 'wrap',
            alignItems: 'center',
            background: broken.length ? '#ffd9c8' : 'var(--panel)',
          }}
        >
          <div style={{ fontWeight: 800, color: 'var(--title)', fontSize: 17 }}>
            {broken.length === 0 ? 'Экономика держится' : `Сломано пунктов: ${broken.length}`}
          </div>
          <div style={{ fontSize: 13 }}>
            уровень {result.final_level} за {player.days} дней · заказов {result.orders_done} ·
            кредитов {rows.at(-1)?.credits ?? 0}
          </div>
        </div>

        <div className="panel" style={{ padding: 16 }}>
          <div style={{ fontWeight: 800, color: 'var(--title)', marginBottom: 6 }}>
            Уровень по дням
          </div>
          <LineChart
            series={[
              { label: 'уровень', color: 'var(--xp)', points: rows.map((r) => r.level) },
            ]}
          />
          <div
            style={{
              fontWeight: 800,
              color: 'var(--title)',
              margin: '10px 0 6px',
            }}
          >
            Кредиты и опыт
          </div>
          <LineChart
            series={[
              {
                label: 'кредиты',
                color: 'var(--credits)',
                points: rows.map((r) => r.credits),
              },
              { label: 'XP', color: 'var(--action)', points: rows.map((r) => r.xp_total) },
            ]}
          />
        </div>

        <div className="panel" style={{ padding: 16 }}>
          <div style={{ fontWeight: 800, color: 'var(--title)', marginBottom: 10 }}>
            Инварианты
          </div>
          <div style={{ display: 'grid', gap: 6 }}>
            {verdicts.map((v) => (
              <div
                key={v.name}
                style={{
                  display: 'grid',
                  gridTemplateColumns: '18px 1fr auto',
                  gap: 10,
                  alignItems: 'baseline',
                  padding: '6px 8px',
                  borderRadius: 10,
                  background: v.ok ? 'transparent' : 'rgba(232,74,47,.10)',
                }}
              >
                <span style={{ color: v.ok ? 'var(--action-dark)' : 'var(--close)' }}>
                  {v.ok ? '✓' : '✕'}
                </span>
                <div>
                  <div style={{ fontWeight: 700, color: 'var(--title)', fontSize: 14 }}>
                    {v.name}
                  </div>
                  {!v.ok && <div style={{ fontSize: 12, color: 'var(--text)' }}>{v.why}</div>}
                </div>
                <span style={{ fontSize: 13, fontWeight: 700, whiteSpace: 'nowrap' }}>
                  {v.value}
                </span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
