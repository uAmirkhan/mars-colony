import { xpToNext, cumulativeXpToReach } from '../src/domain/config/levels';

// Зеркало Unity: mars-unity/Assets/Scripts/Domain/Config/Progress.cs, BAZA=12, SHAG=1.5
const BAZA = 12, SHAG = 1.5;
function unityCum(uroven: number): number {
  if (uroven <= 1) return 0;
  let summa = 0, shag = BAZA;
  for (let i = 1; i < uroven; i++) { summa += shag; shag *= SHAG; }
  return Math.trunc(summa);
}

console.log('=== ДВЕ РАЗНЫЕ КРИВЫЕ XP НА ОДНИХ И ТЕХ ЖЕ unlock_level ===');
console.log('TS  levels.ts:  xpToNext(N) = round(120 * N^1.35 / 10) * 10');
console.log('C#  Progress.cs: BAZA 12, SHAG 1.5 (геометрическая)');
console.log('');
console.log('ур | TS кумулятив | C# кумулятив | во сколько раз TS дороже');
for (let n = 2; n <= 21; n++) {
  const ts = cumulativeXpToReach(n);
  const cs = unityCum(n);
  console.log(
    String(n).padStart(2) + ' | ' + String(ts).padStart(12) + ' | ' + String(cs).padStart(12) +
    ' | ' + (cs === 0 ? '-' : (ts / cs).toFixed(2) + 'x')
  );
}
console.log('');
console.log('шаг на уровень:');
for (let n = 1; n <= 21; n++) {
  console.log('  ур.' + String(n).padStart(2) + ' -> ' + String(n + 1).padStart(2) + ': TS ' + String(xpToNext(n)).padStart(5) + ', C# ' + String(unityCum(n + 1) - unityCum(n)).padStart(6));
}
