/**
 * Звук интерфейса. Четыре сигнала: нажатие, успех, отказ, награда.
 *
 * Синтез осциллятором, а не файлы. Причина не в бюджете (хотя его нет), а в
 * канале отказа: в этом проекте ресурс дважды молча не доезжал до сборки —
 * шейдер интерфейса и кириллические глифы шрифта, оба раза без единой ошибки
 * в консоли. Звуковой файл — ровно такой же кандидат: 404 на `.mp3` игра не
 * заметит, а тишину владелец спишет на то, что звука и не делали. У кода
 * такого режима отказа нет: если модуль попал в бандл, сигнал звучит.
 *
 * Осталась одна проверяемая дыра — синтез, дающий тишину (перепутанная
 * огибающая, нулевой гейн). Поэтому наружу выставлен `renderSfx`: он гоняет
 * ровно те же голоса через `OfflineAudioContext` и возвращает пик и RMS
 * отрендеренного буфера. Браузерная проверка меряет их числом, а не ухом.
 *
 * Громкость по умолчанию тихая (моушен-спека 3: «еле слышный UI-клик»), и
 * есть выключатель — вкладку закроют раньше, чем найдут его, если начать
 * громко.
 */

export type SfxName = 'press' | 'success' | 'deny' | 'reward';

/** Ключ выключателя. Отдельно от сейва игры: это настройка железа, не прогресс. */
const MUTE_KEY = 'mars-colony-sound-muted';

/**
 * Общая громкость. Инженерная константа отображения, не игровое число: на
 * экономику не влияет и в `domain/config` ей делать нечего.
 */
const MASTER_GAIN = 0.22;

/** Один и тот же сигнал чаще этого интервала — дребезг, а не отклик. */
const REPEAT_GUARD_MS = 45;

/**
 * Затухающий тон. Огибающая экспоненциальная: линейный срез дает щелчок на
 * конце, который слышно отчетливее самого сигнала.
 */
function blip(
  ctx: BaseAudioContext,
  out: AudioNode,
  t0: number,
  type: OscillatorType,
  from_hz: number,
  to_hz: number,
  dur: number,
  peak: number,
): void {
  const osc = ctx.createOscillator();
  const gain = ctx.createGain();
  osc.type = type;
  osc.frequency.setValueAtTime(from_hz, t0);
  osc.frequency.exponentialRampToValueAtTime(Math.max(1, to_hz), t0 + dur);
  gain.gain.setValueAtTime(0.0001, t0);
  gain.gain.exponentialRampToValueAtTime(peak, t0 + Math.min(0.012, dur * 0.25));
  gain.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
  osc.connect(gain);
  gain.connect(out);
  osc.start(t0);
  osc.stop(t0 + dur + 0.02);
}

interface Voice {
  /** Полная длительность голоса — нужна офлайн-рендеру, чтобы не срезать хвост. */
  dur: number;
  build: (ctx: BaseAudioContext, out: AudioNode, t0: number) => void;
}

/**
 * Голоса. Отказ отличается от успеха тембром и направлением высоты, а не
 * громкостью: успех — чистая синусоида вверх, отказ — низкая квадратная вниз.
 * Различие по громкости на слух читается как «то же самое, но тише», то есть
 * не читается вовсе.
 */
const VOICES: Record<SfxName, Voice> = {
  press: {
    dur: 0.07,
    build: (c, o, t) => blip(c, o, t, 'triangle', 940, 620, 0.05, 0.34),
  },
  success: {
    dur: 0.2,
    build: (c, o, t) => {
      blip(c, o, t, 'sine', 620, 660, 0.09, 0.6);
      blip(c, o, t + 0.055, 'sine', 930, 960, 0.12, 0.5);
    },
  },
  deny: {
    dur: 0.26,
    build: (c, o, t) => {
      blip(c, o, t, 'square', 196, 116, 0.2, 0.26);
      blip(c, o, t + 0.02, 'square', 98, 74, 0.2, 0.15);
    },
  },
  reward: {
    dur: 0.5,
    build: (c, o, t) => {
      const chord = [523.25, 659.25, 783.99, 1046.5];
      chord.forEach((f, i) => {
        blip(c, o, t + i * 0.075, 'triangle', f, f, 0.22, 0.4);
      });
    },
  },
};

export interface SfxRecord {
  name: SfxName;
  at: number;
  /** Сигнал реально ушел в звуковой граф, а не только записался в журнал. */
  audible: boolean;
}

/** Журнал воспроизведения. Существует для браузерной проверки, не для игры. */
const played: SfxRecord[] = [];

type Ctor = new () => AudioContext;

let ctx: AudioContext | null = null;
let master: GainNode | null = null;
let muted = readMuted();
const mute_listeners = new Set<() => void>();
const last_at = new Map<SfxName, number>();

function readMuted(): boolean {
  try {
    return globalThis.localStorage?.getItem(MUTE_KEY) === '1';
  } catch {
    return false; // приватный режим запретил хранилище — не повод молчать
  }
}

function audioCtor(): Ctor | null {
  if (typeof window === 'undefined') return null;
  const w = window as unknown as { AudioContext?: Ctor; webkitAudioContext?: Ctor };
  return w.AudioContext ?? w.webkitAudioContext ?? null;
}

/**
 * Создание и разблокировка контекста. Браузер держит звук выключенным до
 * первого жеста игрока, поэтому вызывается из обработчика нажатия, а не при
 * загрузке модуля: контекст, созданный на старте, остается `suspended`, и
 * первый же звук уходит в тишину без единой ошибки.
 */
export function ensureAudio(): void {
  const Ctor = audioCtor();
  if (!Ctor) return;
  if (!ctx) {
    ctx = new Ctor();
    master = ctx.createGain();
    master.gain.value = muted ? 0 : MASTER_GAIN;
    master.connect(ctx.destination);
  }
  if (ctx.state === 'suspended') void ctx.resume();
}

export function play(name: SfxName): void {
  const now = Date.now();
  const last = last_at.get(name) ?? 0;
  if (now - last < REPEAT_GUARD_MS) return;
  last_at.set(name, now);

  let audible = false;
  if (!muted) {
    ensureAudio();
    if (ctx && master && ctx.state === 'running') {
      VOICES[name].build(ctx, master, ctx.currentTime + 0.001);
      audible = true;
    }
  }

  played.push({ name, at: now, audible });
  if (played.length > 120) played.splice(0, played.length - 120);
}

export function isMuted(): boolean {
  return muted;
}

export function setMuted(value: boolean): void {
  muted = value;
  if (master) master.gain.value = value ? 0 : MASTER_GAIN;
  try {
    globalThis.localStorage?.setItem(MUTE_KEY, value ? '1' : '0');
  } catch {
    // Хранилище запрещено: выключатель работает на текущую вкладку.
  }
  for (const fn of mute_listeners) fn();
}

export function subscribeMute(fn: () => void): () => void {
  mute_listeners.add(fn);
  return () => {
    mute_listeners.delete(fn);
  };
}

export interface SfxLevel {
  peak: number;
  rms: number;
}

/**
 * Офлайн-рендер голоса. Тот же `build`, что и в живом воспроизведении, —
 * проверка меряет боевой код, а не свою копию.
 */
export async function renderSfx(name: SfxName): Promise<SfxLevel> {
  const w = window as unknown as {
    OfflineAudioContext?: new (ch: number, len: number, rate: number) => OfflineAudioContext;
  };
  const Offline = w.OfflineAudioContext;
  if (!Offline) return { peak: 0, rms: 0 };

  const rate = 44100;
  const voice = VOICES[name];
  const off = new Offline(1, Math.ceil(rate * (voice.dur + 0.1)), rate);
  const gain = off.createGain();
  gain.gain.value = MASTER_GAIN;
  gain.connect(off.destination);
  voice.build(off, gain, 0);

  const buffer = await off.startRendering();
  const data = buffer.getChannelData(0);
  let peak = 0;
  let sum = 0;
  for (const v of data) {
    const a = Math.abs(v);
    if (a > peak) peak = a;
    sum += v * v;
  }
  return { peak, rms: Math.sqrt(sum / data.length) };
}

/**
 * Шов для браузерной проверки. Тот же режим доступности, что у шва стора:
 * в настоящем деплое ветка вырезается сборщиком.
 */
if (
  (import.meta.env.DEV || import.meta.env.VITE_E2E === '1') &&
  typeof window !== 'undefined'
) {
  (
    window as unknown as {
      __sfx?: {
        played: SfxRecord[];
        names: SfxName[];
        render: (name: SfxName) => Promise<SfxLevel>;
        state: () => string;
        isMuted: () => boolean;
        setMuted: (v: boolean) => void;
      };
    }
  ).__sfx = {
    played,
    names: Object.keys(VOICES) as SfxName[],
    render: renderSfx,
    state: () => ctx?.state ?? 'none',
    isMuted,
    setMuted,
  };
}
