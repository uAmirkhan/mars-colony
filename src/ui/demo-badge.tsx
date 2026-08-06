/**
 * Подпись показа: откуда взялись числа на экране.
 *
 * Нужна по одной причине. Колония седьмого уровня с тысячей изотопов — это
 * либо честная экономика, либо подкрученный сейв, и по экрану одно от другого
 * не отличить. Молчание тут читается как второе. Поэтому граница проведена
 * вслух: что начислила игра, что выдано показу.
 *
 * Свернуть можно и нужно: подпись объясняет старт, а не сопровождает игру.
 */

import { useState } from 'react';
import { skipPrice } from '../domain/shuttle';
import { DEMO_ISOTOPES, DEMO_LEVEL } from '../state/demo';
import { entryMode, startFresh } from '../state/entry';
import { useGame } from '../state/gameStore';
import { ISOTOPE_GLYPH } from './kit';

export function DemoBadge() {
  const [open, setOpen] = useState(true);
  const trip = useGame((s) => s.shuttle);
  const now = useGame((s) => s.now);

  if (entryMode() !== 'demo') return null;

  const skip = trip !== null ? skipPrice(trip, now) : 0;

  if (!open) {
    return (
      <button
        type="button"
        className="btn btn-secondary demo-badge-mini"
        onClick={() => setOpen(true)}
        title="Откуда взялись числа"
      >
        Показ
      </button>
    );
  }

  return (
    <div className="panel demo-badge" data-testid="demo-badge">
      <div className="demo-badge-text">
        <strong>Колония {DEMO_LEVEL} уровня — начало показа.</strong> Уровень, опыт и кредиты
        начислила экономика игры за семь уровней. Выданы, чтобы не ждать: {DEMO_ISOTOPES}{' '}
        {ISOTOPE_GLYPH}, запас на складе и модули стройки.
        {skip > 0 && (
          <>
            {' '}
            Рейс на подлете, ускорить целиком — {skip} {ISOTOPE_GLYPH}.
          </>
        )}
      </div>
      <div className="demo-badge-actions">
        <button type="button" className="btn btn-secondary" onClick={startFresh}>
          Начать с нуля
        </button>
        <button
          type="button"
          className="btn btn-secondary"
          onClick={() => setOpen(false)}
          aria-label="Свернуть подпись"
        >
          Свернуть
        </button>
      </div>
    </div>
  );
}
