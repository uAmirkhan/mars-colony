import { useEffect, useState } from 'react';
import './ui/theme.css';
import { useGame } from './state/gameStore';
import { DomeScreen, FactoryPanel, Hud, Toasts, WarehousePanel } from './ui/screens';
import { SimScreen } from './ui/sim-screen';

type Modal = null | 'warehouse' | 'factory';
type Mode = 'sim' | 'game';

const HUB: Array<{ id: 'dome' | 'warehouse' | 'factory'; label: string }> = [
  { id: 'dome', label: 'Купол' },
  { id: 'warehouse', label: 'Склад' },
  { id: 'factory', label: 'Фабрика' },
];

/**
 * Симулятор — первый экран. Игра открывается вторым и служит доказательством,
 * что модель не теория: те же числа, тот же доменный код, реально играется.
 */
export default function App() {
  const [mode, setMode] = useState<Mode>('sim');

  return (
    <div style={{ position: 'fixed', inset: 0, overflow: 'auto' }}>
      <div
        style={{
          position: 'sticky',
          top: 0,
          zIndex: 40,
          display: 'flex',
          gap: 10,
          alignItems: 'center',
          padding: '10px 16px',
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

      {mode === 'sim' ? <SimScreen /> : <GameScreen />}
    </div>
  );
}

function GameScreen() {
  const tick = useGame((s) => s.tick);
  const [modal, setModal] = useState<Modal>(null);

  // Единый шаг времени: домен считает по абсолютным меткам, UI только опрашивает.
  useEffect(() => {
    const id = setInterval(() => tick(Math.floor(Date.now() / 1000)), 500);
    return () => clearInterval(id);
  }, [tick]);

  return (
    <div
      style={{
        position: 'relative',
        minHeight: 'calc(100vh - 58px)',
        background:
          'linear-gradient(180deg, var(--world-sky-top) 0%, var(--world-sky) 26%, var(--world-ground-far) 42%, var(--world-ground) 100%)',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 16,
      }}
    >
      <Hud />
      <Toasts />

      <div style={{ marginTop: 40 }}>
        <DomeScreen />
      </div>

      <div style={{ position: 'absolute', bottom: 14, display: 'flex', gap: 10 }}>
        {HUB.map((item) => (
          <button
            type="button"
            key={item.id}
            className="btn btn-secondary"
            onClick={() => setModal(item.id === 'dome' ? null : item.id)}
          >
            {item.label}
          </button>
        ))}
      </div>

      {modal === 'warehouse' && <WarehousePanel onClose={() => setModal(null)} />}
      {modal === 'factory' && <FactoryPanel onClose={() => setModal(null)} />}
    </div>
  );
}
