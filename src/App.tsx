import { useEffect, useState } from 'react';
import './ui/theme.css';
import { useGame } from './state/gameStore';
import { DomeScreen, FactoryPanel, Hud, Toasts, WarehousePanel } from './ui/screens';

type Modal = null | 'warehouse' | 'factory';

const HUB: Array<{ id: 'dome' | 'warehouse' | 'factory'; label: string }> = [
  { id: 'dome', label: 'Купол' },
  { id: 'warehouse', label: 'Склад' },
  { id: 'factory', label: 'Фабрика' },
];

export default function App() {
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
        position: 'fixed',
        inset: 0,
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
