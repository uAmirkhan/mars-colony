import { useEffect, useState } from 'react';
import './ui/theme.css';
import { useGame } from './state/gameStore';
import { ConstructionPanel } from './ui/construction';
import { DemoBadge } from './ui/demo-badge';
import { DroneBoard } from './ui/drone-board';
import { Feel } from './ui/feel';
import { GoalBar, type GoalHub, GoalScope, useFirstGoal } from './ui/first-goal';
import { useHubBadge } from './ui/hub-badge';
import { DomeScreen, FactoryPanel, Hud, Toasts, WarehousePanel } from './ui/screens';
import { ShuttleStation } from './ui/shuttle-station';
import { SimScreen } from './ui/sim-screen';

type HubId = 'dome' | 'warehouse' | 'factory' | 'drone' | 'shuttle' | 'construction';
type Modal = Exclude<HubId, 'dome'> | null;
type Mode = 'sim' | 'game';

/**
 * Хаб внимания. Порядок — приоритет бейджей каркаса (раздел 13): транспорт
 * выше производства, потому что именно он назначает следующую сессию.
 */
const HUB: Array<{ id: HubId; label: string }> = [
  { id: 'dome', label: 'Купол' },
  { id: 'warehouse', label: 'Склад' },
  { id: 'factory', label: 'Фабрика' },
  { id: 'drone', label: 'Дрон' },
  { id: 'shuttle', label: 'Шаттл' },
  { id: 'construction', label: 'Стройка' },
];

/**
 * Игра — первый экран. Симулятор рядом, второй кнопкой.
 *
 * Порядок менялся: сначала первым стоял балансный симулятор, и довод был
 * весомый — он доказывает, что за игрой стоит модель, а не догадки.
 * **Решение владельца 2026-08-06: предъявлять играбельность.** Довод сильнее:
 * играбельности в июне не было вовсе, именно ее и не хватило, а таблица с
 * ползунками в первую секунду отвечает не на тот вопрос, с которым человек
 * открывает ссылку. Симулятор никуда не делся и остается доказательством —
 * просто перестает быть первым впечатлением.
 */
export default function App() {
  const [mode, setMode] = useState<Mode>('game');

  return (
    // Колонка на всю высоту окна. Игровой экран забирает ровно тот остаток,
    // который не заняли шапка и подпись показа, — вместо жестко вычтенных 58
    // точек. Из-за вычитания константы экран уезжал вниз ровно на высоту
    // подписи, и хаб из шести кнопок оказывался ниже края телефона: чтобы
    // открыть склад, надо было догадаться прокрутить.
    <div
      style={{
        position: 'fixed',
        inset: 0,
        overflow: 'auto',
        display: 'flex',
        flexDirection: 'column',
      }}
    >
      {/* Отклик на нажатие, вылетающие цифры и звук — одна точка монтирования. */}
      <Feel />
      <div
        style={{
          position: 'sticky',
          top: 0,
          zIndex: 40,
          display: 'flex',
          flexWrap: 'wrap',
          gap: 10,
          alignItems: 'center',
          // Справа держим место под выключатель звука: он висит поверх экрана
          // в правом верхнем углу, и на узком экране накрывал собой кнопку
          // «Играть» — то есть главный вход в игру был физически не нажимаем.
          padding: '10px 96px 10px 16px',
          background: 'var(--panel)',
          borderBottom: '3px solid var(--panel-border)',
        }}
      >
        <strong style={{ color: 'var(--title)', marginRight: 6 }}>Mars Colony</strong>
        <button
          type="button"
          className={`btn ${mode === 'sim' ? 'btn-primary' : 'btn-secondary'}`}
          style={{ padding: '6px 14px', fontSize: 13 }}
          onClick={() => setMode('sim')}
        >
          Балансный симулятор
        </button>
        <button
          type="button"
          className={`btn ${mode === 'game' ? 'btn-primary' : 'btn-secondary'}`}
          style={{ padding: '6px 14px', fontSize: 13 }}
          onClick={() => setMode('game')}
        >
          Играть
        </button>
      </div>

      {mode === 'game' && <DemoBadge />}
      {mode === 'sim' ? <SimScreen /> : <GameScreen />}
    </div>
  );
}

function GameScreen() {
  const tick = useGame((s) => s.tick);
  const [modal, setModal] = useState<Modal>(null);
  // Первая цель: одна на экран, указателем, и она кончается ([[first-goal]]).
  const { goal, dismiss } = useFirstGoal();
  // Бейдж хаба внимания (Н-15, каркас 13): не более одного индикатора сразу,
  // логика приоритета — чистая функция в ui/hub-badge.ts.
  const hub_badge = useHubBadge();

  // Единый шаг времени: домен считает по абсолютным меткам, UI только опрашивает.
  useEffect(() => {
    const id = setInterval(() => tick(Math.floor(Date.now() / 1000)), 500);
    return () => clearInterval(id);
  }, [tick]);

  return (
    <GoalScope goal={goal}>
      <div
        style={{
          position: 'relative',
          flex: '1 1 auto',
          minHeight: 0,
          background:
            'linear-gradient(180deg, var(--world-sky-top) 0%, var(--world-sky) 26%, var(--world-ground-far) 42%, var(--world-ground) 100%)',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          padding: 16,
        }}
      >
        <Hud />
        <Toasts />

        {/*
          Мир забирает весь остаток высоты, а низ экрана стоит под ним в
          потоке. Раньше хаб был приклеен к низу абсолютом, и грядки, стоявшие
          по центру всего экрана, наезжали на него снизу: на телефоне кнопки
          ускорения нижних грядок оказывались под хабом и не нажимались. Теперь
          наехать физически нечему — низ занимает свою высоту, купол получает
          остальное.
        */}
        <div
          style={{
            flex: '1 1 auto',
            minHeight: 0,
            width: '100%',
            display: 'grid',
            placeItems: 'center',
            overflow: 'auto',
            // Отступ был 40 при абсолютном HUD — то есть на глаз и все равно
            // мало. Счетчики теперь занимают свою высоту сами (Д-31), и запас
            // тут нужен только на воздух между ними и куполом.
            paddingTop: 12,
          }}
        >
          <DomeScreen />
        </div>

        {/*
        Низ экрана: плашка цели и хаб одним столбцом.
        Хаб растянут по ширине экрана и переносится по строкам.
        Дефект Д-23: без `left/right` и переноса блок вставал по статической
        позиции — по центру и одной строкой, — а шесть кнопок в строке шире
        телефона. На экране 412 точек левый край хаба уезжал на x = -149, и
        кнопки «Купол» и «Склад» оказывались за краем экрана: склад с телефона
        было не открыть вовсе. Проверка `mobile-fit.spec.ts` держит это числами.
      */}
        <div
          style={{
            flex: '0 0 auto',
            width: '100%',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            // Зазор больше обычного: в нем стоит стрелка указателя над кнопкой
            // хаба. При зазоре в 10 точек плашка цели накрывала бы стрелку,
            // которую сама же и объясняет.
            gap: 26,
            padding: '10px 12px 0',
          }}
        >
          {/*
          Плашка цели стоит НАД хабом и в одном с ним блоке: она указывает на
          кнопку хаба, и разъехаться им нельзя. Под открытой модалкой плашка не
          рисуется — там цель уже на экране, и ее ведет кольцо на кнопке.
        */}
          {goal !== null && modal === null && (
            <GoalBar
              goal={goal}
              onOpen={(hub: Exclude<GoalHub, 'dome'>) => setModal(hub)}
              onDismiss={dismiss}
            />
          )}

          <div
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              justifyContent: 'center',
              // Между рядами воздуха больше, чем между кнопками в ряду: на
              // телефоне хаб переносится, и в промежуток между рядами встает
              // стрелка указателя. Тесный промежуток делал ее ничьей (Д-32).
              gap: '16px 10px',
            }}
          >
            {HUB.map((item) => {
              // Кольцо цели стоит на кнопке хаба, но НИКОГДА на «Куполе»:
              // купол и так на экране, цель в нем показывает стрелка на самой
              // грядке, а кольцо на кнопке уводило бы от нее. Плюс кнопка
              // «Купол» лежит в 44 точках от «Склада», и лишняя пульсация
              // ломала опорный замер покоя в проверке ощущения.
              const has_goal_ring =
                goal !== null && modal === null && goal.hub !== 'dome' && goal.hub === item.id;
              // Бейдж и кольцо не спорят за одну кнопку (Н-15): если на кнопке
              // уже горит кольцо первой цели, точка на ней не рисуется — это
              // тот же индикатор «сюда» другим способом, второй поверх первого
              // только шумит. Купол бейджа не несет вовсе — см. hub-badge.ts.
              const has_badge = item.id !== 'dome' && item.id === hub_badge && !has_goal_ring;

              return (
                <div key={item.id} style={{ position: 'relative', display: 'flex' }}>
                  <button
                    type="button"
                    className={`btn btn-secondary${has_goal_ring ? ' goal-point' : ''}`}
                    onClick={() => setModal(item.id === 'dome' ? null : item.id)}
                  >
                    {item.label}
                  </button>
                  {has_badge && <span className="hub-badge-dot" aria-hidden="true" />}
                </div>
              );
            })}
          </div>
        </div>

        {modal === 'warehouse' && (
          <WarehousePanel
            onClose={() => setModal(null)}
            onOpenConstruction={() => setModal('construction')}
          />
        )}
        {modal === 'factory' && <FactoryPanel onClose={() => setModal(null)} />}
        {modal === 'drone' && <DroneBoard onClose={() => setModal(null)} />}
        {modal === 'shuttle' && <ShuttleStation onClose={() => setModal(null)} />}
        {modal === 'construction' && <ConstructionPanel onClose={() => setModal(null)} />}
      </div>
    </GoalScope>
  );
}
