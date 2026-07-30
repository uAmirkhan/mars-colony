/**
 * XP-кривая, разблокировки, награды за левелап.
 * Источник истины — [[mars-colony-frame]], раздел 6. Формула первична, таблицы вторичны.
 */

export const MAX_LEVEL_MVP = 21;

/**
 * Коэффициенты XP-кривой. Имена — из конфиг-таблицы [[tz-production-mars]] раздел 7.
 * Показатель снижен с 1.55 после пересчета приемки, см. каркас раздел 6.
 */
export const XP_CURVE_BASE_COEF = 120;
export const XP_CURVE_EXPONENT = 1.35;
export const XP_CURVE_ROUND_STEP = 10;

/** XP_to_next(N) = round(120 x N^1.35 / 10) x 10. */
export function xpToNext(level: number): number {
  const raw = XP_CURVE_BASE_COEF * Math.pow(level, XP_CURVE_EXPONENT);
  return Math.round(raw / XP_CURVE_ROUND_STEP) * XP_CURVE_ROUND_STEP;
}

/** Накопленный XP, нужный чтобы дойти с 1-го уровня до указанного. */
export function cumulativeXpToReach(level: number): number {
  let total = 0;
  for (let n = 1; n < level; n++) total += xpToNext(n);
  return total;
}

/** Уровни без нового контента: выдают изотопы и бесплатное расширение купола. */
export const EMPTY_LEVELS = [4, 14, 16, 18, 19, 21];

export const LEVEL_UP_ISOTOPES_BASE = 20;
export const LEVEL_UP_ISOTOPES_EMPTY_BONUS = 25;

export interface LevelUpReward {
  credits: number;
  isotopes: number;
  free_dome_expansion: boolean;
}

export const LEVEL_UP_CREDITS_COEF = 100;
export const LEVEL_UP_CREDITS_EXPONENT = 1.2;
export const LEVEL_UP_CREDITS_ROUND_STEP = 10;

/**
 * Награда за достижение уровня N: 100 x N^1.2 кредитов, округление к десяткам.
 * Показатель 1.2 намеренно ниже показателя стока расширений 1.5 — иначе кредиты
 * обесценятся к двадцатому уровню (каркас, раздел 6).
 */
export function levelUpReward(level: number): LevelUpReward {
  const is_empty = EMPTY_LEVELS.includes(level);
  const raw_credits = LEVEL_UP_CREDITS_COEF * Math.pow(level, LEVEL_UP_CREDITS_EXPONENT);
  return {
    credits:
      Math.round(raw_credits / LEVEL_UP_CREDITS_ROUND_STEP) * LEVEL_UP_CREDITS_ROUND_STEP,
    isotopes: LEVEL_UP_ISOTOPES_BASE + (is_empty ? LEVEL_UP_ISOTOPES_EMPTY_BONUS : 0),
    free_dome_expansion: is_empty,
  };
}

/**
 * Пожизненный бюджет изотопов неплатящего игрока за MVP-прогрессию.
 * Каркас обещает 550 — тест держит обещание.
 */
export function freeIsotopeBudget(max_level = MAX_LEVEL_MVP): number {
  let total = 0;
  for (let n = 2; n <= max_level; n++) total += levelUpReward(n).isotopes;
  return total;
}

/** Уровни открытия механик трио. */
export const MECHANIC_UNLOCK_LEVEL = {
  drone: 2,
  shuttle: 5,
  liner: 12,
} as const;
