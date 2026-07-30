/** Детерминированный генератор: прогон симулятора обязан воспроизводиться по seed. */
export function makeRng(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function pick<T>(rng: () => number, items: readonly T[]): T {
  if (items.length === 0) throw new Error('pick: список пуст');
  // Клемп на случай, когда генератор вернет ровно 1.0 и индекс уедет за границу.
  const idx = Math.min(items.length - 1, Math.floor(rng() * items.length));
  return items[idx]!;
}
