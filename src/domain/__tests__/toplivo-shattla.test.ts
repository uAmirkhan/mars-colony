/**
 * Сторож цены топлива шаттла (правило Khan 08.09: шаттл не взлетает без метана,
 * кислорода и энергии).
 *
 * Зачем машиной. Первые числа — 5 метана, 3 кислорода, 100 энергии — были взяты
 * на глаз, и каждое из них съедало БОЛЬШЕ, чем производство выдаёт за один цикл
 * рейса: метановая буровая даёт 5 за 75 минут, энергии копится 81. Шаттл —
 * главный контур прогресса, и такая цена останавливает игру насовсем, причём
 * молча: игрок видит полные отсеки и не понимает, почему рейс стоит.
 *
 * Метрика та же, что в ТЗ ребаланса экономики (`tz-rebalans-ekonomiki.md`,
 * раздел 2): не «сколько стоит штука», а «какую долю потока за час занятости
 * узкого места это забирает». Узкое место топлива — один цикл рейса.
 *
 * Правило самого топлива живёт только в C# (`Uluchsheniya.cs`): добыча и ангар
 * тоже только там, зеркалить нечего. Поэтому проверка читает файл — тем же
 * приёмом, что и `cs-mirror-drift`.
 */

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { flightTimerMin } from '../config/economy';
import { GOODS } from '../config/goods';
import { CS_MIRROR_ROOT, siblingExists } from './sibling';

const mirror_here = siblingExists(CS_MIRROR_ROOT, 'C#-зеркало Unity-трека');
const uluchsheniya_cs = mirror_here
  ? readFileSync(join(CS_MIRROR_ROOT, 'Uluchsheniya.cs'), 'utf8')
  : '';
const igra_cs = mirror_here
  ? readFileSync(join(CS_MIRROR_ROOT, '../Game/IgraKolonii.cs'), 'utf8')
  : '';

/** `public const int METANA_NA_REYS = 2, KISLORODA_NA_REYS = 2;` и подобные. */
function constOf(text: string, name: string): number {
  const m = text.match(new RegExp(`${name}\\s*=\\s*(-?\\d+(?:\\.\\d+)?)`));
  if (!m) throw new Error(`константа ${name} не найдена в C#`);
  return Number(m[1]);
}

/**
 * Солнечных плит в срезе — девять (замер сцены 08.09). Число сценическое, а не
 * конфигурационное, поэтому стоит здесь и с датой: если плит станет меньше,
 * проверка обязана покраснеть, а не тихо считать по старому.
 */
const PLIT_V_SREZE = 9;

/** Доля потока, которую топливо имеет право забрать за один цикл рейса. */
const PREDEL_DOLI = 0.5;

describe.skipIf(!mirror_here)('топливо взлёта по силам производству', () => {
  const metana = constOf(uluchsheniya_cs, 'METANA_NA_REYS');
  const kisloroda = constOf(uluchsheniya_cs, 'KISLORODA_NA_REYS');
  const energii = constOf(uluchsheniya_cs, 'ENERGII_NA_REYS');

  /** Цикл рейса на уровне, с которого товар вообще требуется. */
  const ciklChasov = (uroven: number) => flightTimerMin(uroven) / 60;

  it('метана за цикл рейса добывается вдвое больше, чем он стоит', () => {
    const metan = GOODS.methane;
    const zaChas = 3600 / metan.prod_time_sec;
    const zaCikl = zaChas * ciklChasov(metan.unlock_level);
    expect(metana).toBeLessThanOrEqual(zaCikl * PREDEL_DOLI);
  });

  it('кислорода за цикл рейса производится вдвое больше, чем он стоит', () => {
    const ballon = GOODS.oxygen_tank;
    const zaChas = 3600 / ballon.prod_time_sec;
    const zaCikl = zaChas * ciklChasov(ballon.unlock_level);
    expect(kisloroda).toBeLessThanOrEqual(zaCikl * PREDEL_DOLI);
  });

  it('руды на кислород хватает: вход не становится вторым узким местом', () => {
    const ballon = GOODS.oxygen_tank;
    const ruda = GOODS.iron_ore;
    const vhodov = ballon.inputs.find((i) => i.good_id === 'iron_ore')?.qty ?? 0;
    const rudyZaChas = 3600 / ruda.prod_time_sec;
    const rudyZaCikl = rudyZaChas * ciklChasov(ballon.unlock_level);
    expect(kisloroda * vhodov).toBeLessThanOrEqual(rudyZaCikl * PREDEL_DOLI);
  });

  it('энергии за цикл рейса набегает вдвое больше, чем он стоит', () => {
    const stantsiya = constOf(igra_cs, 'ENERGIYA_STANTSIYA_V_CHAS');
    const plita = constOf(igra_cs, 'ENERGIYA_PLITA_V_CHAS');
    const vChas = stantsiya + plita * PLIT_V_SREZE;
    // Энергия требуется с первого уровня — цикл берётся самый короткий из таблицы.
    const zaCikl = vChas * ciklChasov(1);
    expect(energii).toBeLessThanOrEqual(zaCikl * PREDEL_DOLI);
  });

  it('приход энергии считается по времени каркаса, а не по реальным часам', () => {
    // Все таймеры домена умножаются на time_scale; приход энергии обязан делиться на него,
    // иначе при ускорении x10 рейс идёт 7,5 реальных минут, а топливо на него копится 28 —
    // бюджет из этого файла считается в часах каркаса и молча перестаёт что-либо охранять.
    expect(igra_cs).toMatch(
      /NachislitEnergiyu[\s\S]*?EnergiyaVChas \* dt \/ 3600\.0 \/ masshtab/,
    );
  });

  it('энергия рейса влезает в хранилище: копить на рейс не дольше одного цикла', () => {
    const potolok = constOf(igra_cs, 'ENERGIYA_POTOLOK');
    expect(energii).toBeLessThanOrEqual(potolok);
  });

  it('топливо требуется не раньше, чем товар открыт игроку', () => {
    // Правило гейта — в самом C#: пороги читаются из конфига товаров, а не
    // переписаны числом. Проверяется именно это, потому что при переписывании
    // числом расхождение с goods.ts проходит молча.
    expect(uluchsheniya_cs).toMatch(
      /ToplivoReysa[\s\S]*?Goods\.Of\(TOPLIVO_METAN\)\.unlock_level[\s\S]*?Goods\.Of\(TOPLIVO_KISLOROD\)\.unlock_level/,
    );
  });

  it('шаттл у новичка не заперт: до восьмого уровня топливо — только энергия', () => {
    // Метан открывается на 10, баллон на 8. Если пороги в конфиге уедут ниже
    // первого уровня, гейт превратится в тупик для новой игры.
    expect(GOODS.methane.unlock_level).toBeGreaterThan(1);
    expect(GOODS.oxygen_tank.unlock_level).toBeGreaterThan(1);
  });
});
