/**
 * Докрутка счетчика. Число не прыгает скачком, а добегает до цели за долю
 * секунды: скачок глаз не ловит вовсе, а движение ловит боковым зрением, даже
 * когда смотришь на грядку, а не на HUD.
 *
 * Длительность из [[ux-motion-spec]] раздел 2, «инкремент-тик счетчика»:
 * 300-400 мс, ease-out.
 */

import { useEffect, useRef, useState } from 'react';

const COUNT_MS = 340;
/**
 * Разница меньше этой докручивается мгновенно. Ползущая на 340 мс двойка
 * выглядит не мягко, а медленно — это ровно тот случай, где анимация мешает.
 */
const MIN_STEP = 3;

export function useCountUp(target: number | null): number | null {
  const [shown, setShown] = useState(target);
  const shown_ref = useRef(target);

  useEffect(() => {
    if (target === null) {
      shown_ref.current = null;
      setShown(null);
      return;
    }

    const start = shown_ref.current;
    const animatable =
      start !== null &&
      Math.abs(target - start) >= MIN_STEP &&
      typeof requestAnimationFrame === 'function';

    if (!animatable) {
      shown_ref.current = target;
      setShown(target);
      return;
    }

    const t0 = performance.now();
    let raf = requestAnimationFrame(function step(t: number) {
      const k = Math.min(1, (t - t0) / COUNT_MS);
      // ease-out cubic: быстрый старт, мягкое торможение к цели.
      const eased = 1 - (1 - k) ** 3;
      const value = Math.round(start + (target - start) * eased);
      shown_ref.current = value;
      setShown(value);
      if (k < 1) raf = requestAnimationFrame(step);
    });

    return () => cancelAnimationFrame(raf);
  }, [target]);

  return shown;
}
