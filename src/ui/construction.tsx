/**
 * Экран стройки — [[tz-production-mars]] 9.4, класс Б.
 *
 * Чек-лист «владею/нужно» стоит прямо на карточке, а не прячется за тапом:
 * именно эта строка отвечает на вопрос «чего мне не хватает», ради которого
 * игрок сюда и заходит. Она же — вход инвариантов И-7 и И-11: дроп шаттла
 * читает ровно этот дефицит, и игрок должен видеть то же, что видит роллер.
 */

import { constructionSpeedupCost } from '../domain/config/economy';
import { ALL_MODULE_IDS, CONSTRUCTION_RECIPE, MODULES } from '../domain/config/modules';
import {
  type BuildSlot,
  missingFor,
  moduleCapacity,
  modulePurchasePrice,
  moduleTotal,
} from '../domain/construction';
import { useGame } from '../state/gameStore';
import { useGoalBuild } from './first-goal';
import { Button, ISOTOPE_GLYPH, Panel, Timer } from './kit';

const TIER_COLOR = {
  basic: 'var(--panel-border)',
  rare: 'var(--secondary-dark)',
  gated: 'var(--xp)',
} as const;

/** Склад модулей: что есть в наличии. Пустые типы не прячем — их отсутствие информативно. */
function ModuleStock() {
  const construction = useGame((s) => s.construction);
  const stock = construction.stock;

  return (
    <div style={{ marginBottom: 14 }}>
      <div style={{ fontSize: 12, color: 'var(--text-muted)', marginBottom: 6 }}>
        {/* Потолок растет вместе с товарным: расширение склада поднимает оба
            лимита (каркас 6). Показывать здесь стартовую константу значило бы
            врать игроку после первой же постройки. */}
        Склад модулей: {moduleTotal(stock)} из {moduleCapacity(construction)}
      </div>
      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
        {ALL_MODULE_IDS.map((id) => {
          const module = MODULES[id];
          const qty = stock[id] ?? 0;
          return (
            <div
              key={id}
              style={{
                padding: '4px 8px',
                borderRadius: 8,
                border: `2px solid ${TIER_COLOR[module.tier]}`,
                opacity: qty > 0 ? 1 : 0.4,
                fontSize: 12,
                fontWeight: 800,
                color: 'var(--title)',
              }}
            >
              {module.name} {qty}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function BuildCard({ build }: { build: BuildSlot }) {
  const {
    construction,
    now,
    isotopes,
    level,
    startConstruction,
    buyModulesFor,
    speedupConstruction,
  } = useGame();
  const def = CONSTRUCTION_RECIPE[build.kind];
  const missing = missingFor(build, construction.stock);
  const enough = Object.keys(missing).length === 0;
  // Указатель первой цели стоит на той стройке, комплект которой уже собран, —
  // ровно на одной, а не на всех сразу.
  const point = useGoalBuild() === build.kind && enough && build.state === 'AVAILABLE';

  return (
    <div className="slot" style={{ padding: 12, gap: 8, alignItems: 'stretch' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span style={{ fontWeight: 800, color: 'var(--title)' }}>
          {def.name}
          {def.repeatable && build.tier > 0 ? ` · тир ${build.tier + 1}` : ''}
        </span>
        <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
          {def.build_time_min} мин
        </span>
      </div>

      {build.state === 'LOCKED' && (
        <div style={{ color: 'var(--text-muted)', fontSize: 13 }}>
          Открывается на уровне {def.unlock_level}. Сейчас {level}-й.
        </div>
      )}

      {build.state === 'DONE' && (
        <div style={{ color: 'var(--action-dark)', fontWeight: 800 }}>Построено</div>
      )}

      {build.state === 'IN_PROGRESS' && (
        <>
          <Timer remaining_sec={Math.max(0, build.ends_at - now)} />
          <Button
            kind="secondary"
            full
            disabled={constructionSpeedupCost(build.ends_at - now) > isotopes}
            onClick={() => speedupConstruction(build.kind)}
          >
            Ускорить за {constructionSpeedupCost(build.ends_at - now)} {ISOTOPE_GLYPH}
          </Button>
        </>
      )}

      {build.state === 'AVAILABLE' && (
        <>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {(Object.entries(def.recipe) as Array<[keyof typeof MODULES, number]>).map(
              ([id, need]) => {
                // Докупленное считается наравне со складским: игрок за него
                // заплатил, и чек-лист, который этого не видит, требует
                // купить второй раз.
                const have = (construction.stock[id] ?? 0) + (build.purchased[id] ?? 0);
                const ok = have >= need;
                return (
                  <span
                    key={id}
                    style={{
                      fontSize: 12,
                      fontWeight: 800,
                      color: ok ? 'var(--action-dark)' : 'var(--text-muted)',
                    }}
                  >
                    {MODULES[id].name} {Math.min(have, need)}/{need}
                  </span>
                );
              },
            )}
          </div>

          {/*
            Второй канал получения модулей, разрешенный инвариантом И-1: шаттл
            или изотопы. До этой кнопки канал существовал только в конфиге —
            цена лежала в `MODULE_PRICE_ISOTOPES` и читалась одной формулой
            ожидаемой ценности отсека, а купить модуль было нельзя вовсе.
            Игроку, которому не хватало двух панелей, оставалось только ждать
            рейса, и ровно на этом месте казуальный ситибилдер зарабатывает.

            Глагол «Докупить», а не «Открыть» — по UX-стандарту каркаса
            (раздел 13): это разовый расходник под конкретную стройку, а не
            постоянное расширение. Цена стоит НА кнопке: каркас запрещает
            кнопку траты изотопов без числа.
          */}
          {(Object.entries(missing) as Array<[keyof typeof MODULES, number]>).map(
            ([id, short]) => {
              const price = modulePurchasePrice(id, short);
              return (
                <Button
                  key={id}
                  kind="secondary"
                  full
                  disabled={price > isotopes}
                  onClick={() => buyModulesFor(build.kind, id)}
                >
                  Докупить {short} {MODULES[id].name.toLowerCase()} за {price} {ISOTOPE_GLYPH}
                </Button>
              );
            },
          )}

          <Button
            full
            disabled={!enough}
            pointer={point}
            onClick={() => startConstruction(build.kind)}
          >
            {enough ? 'Строить' : 'Не хватает модулей'}
          </Button>
        </>
      )}
    </div>
  );
}

export function ConstructionPanel({ onClose }: { onClose: () => void }) {
  const builds = useGame((s) => s.construction.builds);

  return (
    <div className="scrim" onClick={onClose}>
      <div onClick={(e) => e.stopPropagation()}>
        <Panel title="Стройка" onClose={onClose} style={{ maxWidth: 460, width: '92vw' }}>
          <ModuleStock />
          <div style={{ display: 'grid', gap: 10 }}>
            {builds.map((build) => (
              <BuildCard key={build.kind} build={build} />
            ))}
          </div>
        </Panel>
      </div>
    </div>
  );
}
