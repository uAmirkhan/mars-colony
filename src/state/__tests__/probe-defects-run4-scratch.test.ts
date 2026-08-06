/** Разведка прогона 4. Не доказательство, а замер. Файл временный. */

import fs from 'node:fs';
import { afterEach, describe, expect, it } from 'vitest';
import { moduleCapacity, moduleTotal } from '../../domain/construction';
import { totalQty } from '../../domain/warehouse';
import { applyDemoState, DEMO_LEVEL, DEMO_TRIP_MIN_LEFT } from '../demo';
import { useGame } from '../gameStore';

const original_random = Math.random;
afterEach(() => {
  Math.random = original_random;
});

function seeded(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

describe('разведка показа', () => {
  it('замер по 500 сидам', () => {
    const report = {
      failures: 0,
      over_capacity: 0,
      negative_credits: 0,
      reserved_gt_qty: 0,
      modules_over_cap: 0,
      max_total: 0,
      max_reserved: 0,
      min_credits: Number.POSITIVE_INFINITY,
      slot_counts: new Set<number>(),
      states: new Set<string>(),
      trip_mins: new Set<number>(),
      is_first: new Set<boolean>(),
      leftover_reserved: 0,
      floor_forced: 0,
      needed_delivered: 0,
      empty_order_board: 0,
      worst_seeds: [] as number[],
    };

    for (let seed = 1; seed <= 500; seed++) {
      Math.random = seeded(seed);
      const ok = applyDemoState();
      const s = useGame.getState();
      if (!ok) {
        report.failures += 1;
        report.worst_seeds.push(seed);
      }

      const w = s.warehouse;
      const total = totalQty(w);
      if (total > report.max_total) {
        report.max_total = total;
      }
      if (total > w.capacity) report.over_capacity += 1;
      if (s.credits < 0) report.negative_credits += 1;
      report.min_credits = Math.min(report.min_credits, s.credits);
      for (const [, c] of Object.entries(w.cells)) {
        if (c.reserved > c.qty) report.reserved_gt_qty += 1;
        report.max_reserved = Math.max(report.max_reserved, c.reserved);
        if (c.reserved > 0) report.leftover_reserved += 1;
      }
      if (moduleTotal(s.construction.stock) > moduleCapacity(s.construction))
        report.modules_over_cap += 1;
      report.slot_counts.add(s.shuttle?.slots.length ?? -1);
      report.states.add(s.shuttle?.state ?? 'null');
      report.trip_mins.add(s.shuttle?.trip_min ?? -1);
      report.is_first.add(s.shuttle?.is_first_trip ?? false);
      if (s.shuttle?.slots.some((sl) => sl.floor_forced)) report.floor_forced += 1;
      if (s.shuttle?.slots.some((sl) => sl.reward === 'panel' || sl.reward === 'frame'))
        report.needed_delivered += 1;
      if (s.orders.length === 0) report.empty_order_board += 1;
    }

    fs.writeFileSync(
      'probe-out.json',
      JSON.stringify(
        {
          ...report,
          slot_counts: [...report.slot_counts],
          states: [...report.states],
          trip_mins: [...report.trip_mins],
          is_first: [...report.is_first],
        },
        null,
        2,
      ),
    );
    expect(DEMO_LEVEL).toBe(7);
    expect(DEMO_TRIP_MIN_LEFT).toBe(2);
  });
});
