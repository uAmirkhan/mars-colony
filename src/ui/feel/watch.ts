/**
 * Наблюдатель состояния: превращает изменение чисел игры в цифру на экране и
 * звук. Подписка на стор, ни одной записи в него — стор про это ничего не
 * знает и знать не должен.
 *
 * Почему наблюдатель, а не вызов эффекта из каждого обработчика: начислений в
 * игре двенадцать штук (сбор, продажа, отправка дрона, левелап, награда за
 * уровень, скидка изотопов на пяти кнопках...), и половина из них живет
 * внутри стора, куда интерфейсу хода нет. Разница чисел до и после — единственный
 * канал, который видит их все и не разъедется с новыми.
 *
 * Гейт по жесту обязателен. Без него загрузка сейва (кредиты 0 -> 4200) и
 * подстановка состояния в браузерной проверке выплевывали бы фейерверк цифр
 * на пустом месте.
 */

import { useGame } from '../../state/gameStore';
import { ISOTOPE_GLYPH } from '../kit';
import { anchor, spawnFloat } from './fx';
import { withinGesture } from './press';
import { play, type SfxName } from './sfx';

/** Один звук на одно действие: начисление за отправку дрона трогает три числа. */
const PRIORITY: Record<SfxName, number> = { deny: 3, reward: 2, success: 1, press: 0 };

let pending: SfxName | null = null;

function queue(name: SfxName): void {
  if (pending !== null && PRIORITY[pending] >= PRIORITY[name]) return;
  const first = pending === null;
  pending = name;
  if (!first) return;
  queueMicrotask(() => {
    if (pending) play(pending);
    pending = null;
  });
}

function signed(delta: number, unit: string): string {
  return `${delta > 0 ? '+' : '-'}${Math.abs(delta)} ${unit}`;
}

export function installFeelWatch(): () => void {
  return useGame.subscribe((s, prev) => {
    if (!withinGesture()) return;

    const seen = prev.toasts.reduce((max, t) => (t.id > max ? t.id : max), 0);
    const fresh = s.toasts.filter((t) => t.id > seen);
    const denied = fresh.some((t) => t.kind === 'warn');
    const celebrated = fresh.some((t) => t.kind === 'reward');

    const d_credits = s.credits - prev.credits;
    const d_isotopes = s.isotopes - prev.isotopes;
    const leveled = s.level > prev.level;
    const d_xp = leveled ? 0 : s.xp_into_level - prev.xp_into_level;

    if (d_credits !== 0) {
      spawnFloat(signed(d_credits, 'кр'), d_credits > 0 ? 'gain' : 'cost', anchor('credits'));
    }
    if (d_isotopes !== 0) {
      spawnFloat(
        signed(d_isotopes, ISOTOPE_GLYPH),
        d_isotopes > 0 ? 'gain' : 'cost',
        anchor('isotopes'),
      );
    }
    if (d_xp > 0) spawnFloat(`+${d_xp} XP`, 'xp', anchor('xp'));
    if (leveled) spawnFloat(`Уровень ${s.level}`, 'level', anchor('xp'));

    if (denied) queue('deny');
    else if (leveled || celebrated) queue('reward');
    else if (d_credits > 0 || d_isotopes > 0 || d_xp > 0) queue('success');
  });
}
