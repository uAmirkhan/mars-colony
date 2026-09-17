/**
 * Первая цель: куда нажать следующим действием.
 *
 * Зачем это есть. Ссылка открывается не каноническим стартом, а серединой
 * чужой сессии: колония седьмого уровня, шесть кнопок хаба, четыре счетчика,
 * грядки с таймерами. Все это одинаково громкое, и человек, открывший ссылку
 * впервые, не знает, что из этого сейчас важно. Экран, на котором важно все,
 * не объясняет ничего.
 *
 * Правило одно: в любой момент на экране ровно ОДНА цель, и она показана
 * указателем, а не абзацем. Кольцо и стрелка стоят на той самой кнопке, по
 * которой надо нажать; строка рядом говорит, что происходит, а не как играть.
 *
 * Цвет указателя синий (`--xp`), а не зеленый. Зеленым уже пульсирует
 * созревшая грядка, и если цель красить тем же цветом, два разных смысла
 * сольются в один: «здесь награда» и «сюда нажми» перестанут различаться.
 *
 * Порядок целей задан состоянием, а не режимом входа. Есть рейс шаттла — цепь
 * идет по нему (он и есть центральная вещь среза, и он на подлете). Рейса нет
 * (канонический старт, шаттл открывается только на пятом уровне) — цепь
 * каноническая: посеять, дождаться, собрать, продать.
 *
 * И главное: цепь КОНЧАЕТСЯ. Пройден последний шаг — указателя больше нет и он
 * не возвращается. Подсказка, которая не отпускает, читается как недоверие к
 * игроку; крестик на плашке снимает ее в любой момент досрочно.
 */

import { createContext, type ReactNode, useContext, useRef, useState } from 'react';
import { type BuildKind, CONSTRUCTION_RECIPE } from '../domain/config/modules';
import { missingFor } from '../domain/construction';
import { occupiedGoods } from '../domain/warehouse';
import { useGame } from '../state/gameStore';
import { Timer } from './kit';

/** Экран, к которому ведет цель. `dome` всегда на виду и кнопки не требует. */
export type GoalHub = 'shuttle' | 'construction' | 'warehouse' | 'dome';

/** Что подсветить внутри уже открытого экрана. */
export type GoalSpot = 'skip' | 'containers' | 'build' | 'sell' | 'field';

export interface Goal {
  /** Ключ шага. Уходит в разметку — по нему проверка видит, куда указывает игра. */
  id: string;
  /** Одна короткая строка. Подсказка, которую надо читать, уже не подсказка. */
  text: string;
  hub: GoalHub;
  spot: GoalSpot;
  /** Грядка, на которую показывает стрелка, если цель в куполе. */
  field_idx: number | null;
  /** Стройка, на которую показывает стрелка, если цель на стройке. */
  build_kind: BuildKind | null;
  /** Цель ждет времени, а не игрока: вместо кнопки таймер. */
  wait_sec: number | null;
}

type Snapshot = ReturnType<typeof useGame.getState>;

interface Step {
  /** Шаг пройден. Курсор идет дальше и назад уже не возвращается. */
  done: (s: Snapshot) => boolean;
  goal: (s: Snapshot) => Goal;
}

/** Стройка, на которую комплект модулей уже собран целиком. */
function readyBuild(s: Snapshot): BuildKind | null {
  for (const build of s.construction.builds) {
    if (build.state !== 'AVAILABLE') continue;
    if (Object.keys(missingFor(build, s.construction.stock)).length === 0) return build.kind;
  }
  return null;
}

/* --- Цепь показа: рейс уже в воздухе, и он центральная вещь среза. --- */

const FLIGHT: Step = {
  done: (s) => s.shuttle === null || s.shuttle.state === 'ARRIVED',
  goal: (s) => {
    const trip = s.shuttle;
    if (trip !== null && trip.state === 'IN_TRANSIT') {
      return {
        id: 'flight',
        text: 'Шаттл на подлете',
        hub: 'shuttle',
        spot: 'skip',
        field_idx: null,
        build_kind: null,
        wait_sec: Math.max(0, trip.arrives_at - s.now),
      };
    }
    return {
      id: 'load',
      text: 'Шаттл ждет груз',
      hub: 'shuttle',
      spot: 'skip',
      field_idx: null,
      build_kind: null,
      wait_sec: null,
    };
  },
};

const CARGO: Step = {
  done: (s) => s.shuttle === null || s.shuttle.state !== 'ARRIVED',
  goal: () => ({
    id: 'cargo',
    text: 'Шаттл прибыл, заберите груз',
    hub: 'shuttle',
    spot: 'containers',
    field_idx: null,
    build_kind: null,
    wait_sec: null,
  }),
};

const BUILD: Step = {
  done: (s) => readyBuild(s) === null,
  goal: (s) => {
    const kind = readyBuild(s);
    return {
      id: 'build',
      text:
        kind === null ? 'Стройка ждет' : `Хватает модулей: ${CONSTRUCTION_RECIPE[kind].name}`,
      hub: 'construction',
      spot: 'build',
      field_idx: null,
      build_kind: kind,
      wait_sec: null,
    };
  },
};

/* --- Каноническая цепь: посеять, дождаться, собрать, продать. --- */

const PLANT: Step = {
  done: (s) => s.fields.some((f) => f.state !== 'EMPTY'),
  goal: (s) => ({
    id: 'plant',
    text: 'Нажмите на грядку и посейте',
    hub: 'dome',
    spot: 'field',
    field_idx: s.fields.find((f) => f.state === 'EMPTY')?.idx ?? null,
    build_kind: null,
    wait_sec: null,
  }),
};

const HARVEST: Step = {
  // Первый урожай на складе — шаг пройден. Не «грядка пуста»: она пустеет и от
  // сбора, и от того, что игрок ничего не посеял, а это разные события.
  done: (s) => occupiedGoods(s.warehouse).length > 0,
  goal: (s) => {
    const ready = s.fields.find((f) => f.state === 'READY');
    if (ready) {
      return {
        id: 'harvest',
        text: 'Урожай созрел, соберите',
        hub: 'dome',
        spot: 'field',
        field_idx: ready.idx,
        build_kind: null,
        wait_sec: null,
      };
    }
    const growing = s.fields
      .filter((f) => f.state === 'GROWING')
      .sort((a, b) => a.ends_at - b.ends_at)[0];
    return {
      id: 'grow',
      text: 'Урожай зреет',
      hub: 'dome',
      spot: 'field',
      field_idx: growing?.idx ?? null,
      build_kind: null,
      wait_sec: growing ? Math.max(0, growing.ends_at - s.now) : null,
    };
  },
};

const SELL: Step = {
  done: (s) => occupiedGoods(s.warehouse).length === 0,
  goal: () => ({
    id: 'sell',
    text: 'Продайте урожай на складе',
    hub: 'warehouse',
    spot: 'sell',
    field_idx: null,
    build_kind: null,
    wait_sec: null,
  }),
};

const SHUTTLE_CHAIN: Step[] = [FLIGHT, CARGO, BUILD];
const FIRST_CROP_CHAIN: Step[] = [PLANT, HARVEST, SELL];

/**
 * Текущая цель и способ ее закрыть.
 *
 * Курсор живет в ref и двигается только вперед. Без памяти обойтись нельзя:
 * «посеять» снова становится невыполненным сразу после сбора урожая, и цепь
 * без курсора вернула бы человека на шаг назад ровно в тот момент, когда он
 * шагнул вперед.
 */
export function useFirstGoal(): { goal: Goal | null; dismiss: () => void } {
  const state = useGame();
  const [dismissed, setDismissed] = useState(false);
  const chain = useRef<Step[] | null>(null);
  const cursor = useRef(0);

  // Цепь выбирается один раз за сессию. Иначе левелап до пятого уровня выдал бы
  // шаттл посреди канонической цепи, и курсор указал бы в чужой список шагов.
  if (chain.current === null) {
    chain.current = state.shuttle !== null ? SHUTTLE_CHAIN : FIRST_CROP_CHAIN;
  }
  const steps = chain.current;

  for (let step = steps[cursor.current]; step !== undefined; step = steps[cursor.current]) {
    if (!step.done(state)) break;
    cursor.current += 1;
  }

  const step = steps[cursor.current];
  return {
    goal: dismissed || step === undefined ? null : step.goal(state),
    dismiss: () => setDismissed(true),
  };
}

const GoalContext = createContext<Goal | null>(null);

export function GoalScope({ goal, children }: { goal: Goal | null; children: ReactNode }) {
  return <GoalContext.Provider value={goal}>{children}</GoalContext.Provider>;
}

/** Стоит ли указатель на этом элементе. Читают экраны, которые он подсвечивает. */
export function useGoalSpot(spot: GoalSpot): boolean {
  return useContext(GoalContext)?.spot === spot;
}

/**
 * Грядка, на которую показывает стрелка, или `null`.
 *
 * Отдает номер, а не ответ «эта ли грядка»: купол рисует грядки списком, а
 * хук из цикла не вызвать. Спрашивать один раз на экран и сравнивать номера —
 * единственный способ, не разбивая купол на компонент на каждую грядку.
 */
export function useGoalFieldIdx(): number | null {
  const goal = useContext(GoalContext);
  if (goal === null || goal.spot !== 'field') return null;
  return goal.field_idx;
}

/** Стройка, на которую показывает стрелка, или `null`. Тот же случай, что грядка. */
export function useGoalBuild(): BuildKind | null {
  const goal = useContext(GoalContext);
  if (goal === null || goal.spot !== 'build') return null;
  return goal.build_kind;
}

const OPEN_LABEL: Record<Exclude<GoalHub, 'dome'>, string> = {
  shuttle: 'Открыть шаттл',
  construction: 'Открыть стройку',
  warehouse: 'Открыть склад',
};

/**
 * Плашка цели. Одна строка, один таймер, одна кнопка.
 *
 * Кнопки нет, когда цель в куполе: грядка и так на экране, и дублировать тап
 * по ней кнопкой значит учить не тому месту.
 */
export function GoalBar({
  goal,
  onOpen,
  onDismiss,
}: {
  goal: Goal;
  onOpen: (hub: Exclude<GoalHub, 'dome'>) => void;
  onDismiss: () => void;
}) {
  // Отдельная константа, чтобы сузился тип: внутри обработчика проверка
  // `goal.hub !== 'dome'` из разметки уже не действует.
  const hub = goal.hub;

  return (
    <div
      className="goal-bar"
      data-testid="goal-bar"
      data-goal={goal.id}
      data-goal-hub={goal.hub}
    >
      <span className="goal-bar-text">{goal.text}</span>
      {goal.wait_sec !== null && <Timer remaining_sec={goal.wait_sec} />}
      {/*
        Кольца на кнопке ниже нет намеренно. Указатель на экране один, и он
        стоит на кнопке хаба: она объясняет, ГДЕ в игре живет шаттл. Кольцо
        еще и здесь превратило бы указатель в два, а стрелка над кнопкой
        перекрыла бы собственный текст плашки.
      */}
      {hub !== 'dome' && (
        <button
          type="button"
          className="btn btn-primary"
          data-testid="goal-open"
          onClick={() => onOpen(hub)}
        >
          {OPEN_LABEL[hub]}
        </button>
      )}
      <button
        type="button"
        className="btn btn-secondary goal-bar-hide"
        onClick={onDismiss}
        aria-label="Скрыть подсказку"
        title="Скрыть подсказку"
      >
        ✕
      </button>
    </div>
  );
}
