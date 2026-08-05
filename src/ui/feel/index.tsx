/**
 * Корень ощущения. Одна точка монтирования на все приложение: слой эффектов,
 * делегированный отклик на нажатие, наблюдатель начислений и выключатель звука.
 *
 * Собрано в один компонент нарочно — чтобы подключение стоило одну строку в
 * `App.tsx` и чтобы забыть половину было нельзя.
 */

import { useEffect } from 'react';
import { FxLayer, SoundToggle } from './fx-layer';
import { installPress } from './press';
import { installFeelWatch } from './watch';

export function Feel() {
  useEffect(() => {
    const off_press = installPress();
    const off_watch = installFeelWatch();
    return () => {
      off_press();
      off_watch();
    };
  }, []);

  return (
    <>
      <FxLayer />
      <SoundToggle />
    </>
  );
}

export { actWithFx } from './act';
export { useCountUp } from './count-up';
export { anchor, centerOf, spawnFloat, spawnFly } from './fx';
export { denyNudge, pressPop } from './press';
export { isMuted, play, setMuted } from './sfx';
