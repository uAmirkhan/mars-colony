/**
 * Подтверждение необратимой траты изотопов — общий компонент.
 *
 * Каркас, раздел 13 (UX-стандарт, обязателен для всех ТЗ): «Подтверждение
 * требуется только для действий с необратимой тратой изотопов; остальное —
 * без модалок». ТЗ шаттла называет это дважды для «Ускорить»: «после тапа —
 * подтверждающий попап с ценой (защита от случайного списания) перед
 * сервер-запросом» (6.3) и «"Ускорить" требует подтверждающего попапа с
 * ценой... снижает жалобы "случайно списал изотопы"» (11). Докупка отсека
 * (6.2) тратит изотопы так же необратимо (И-12: докупленное не изымается) —
 * то же правило раздела 13 покрывает ее без отдельной оговорки в ТЗ шаттла.
 *
 * **Решение исполнителя.** Ни ТЗ шаттла, ни каркас не задают дословный текст
 * и композицию попапа — только требование «попап с ценой». Взят тот же
 * паттерн, что уже принят консилиумом для диалога выброса заказа дрона
 * ([[tz-drone-mars]], решение «один универсальный confirm-диалог... тело
 * называет последствие... без развилки мягкий/жесткий диалог»): заголовок
 * называет действие, тело — последствие (что именно не отменится), кнопки
 * называют глагол решения игрока, а не «OK/Cancel».
 *
 * Файл отдельный от `kit.tsx`, чтобы стройка и дрон могли подключить этот же
 * компонент без правки чужого файла в процессе (сейчас в них работают другие
 * агенты) — задание оркестратора явно просит вынести компонент так, чтобы
 * они могли переиспользовать его подключением, не трогая свою верстку сейчас.
 */

import { useState } from 'react';
import { Button, ISOTOPE_GLYPH, Panel } from './kit';

export interface ConfirmSpendRequest {
  /** Заголовок попапа — что именно подтверждает игрок. */
  title: string;
  /** Тело — последствие траты, которое не отменить. */
  body: string;
  price: number;
  onConfirm: () => void;
}

function ConfirmSpendDialog({
  request,
  onCancel,
}: {
  request: ConfirmSpendRequest;
  onCancel: () => void;
}) {
  return (
    <div className="scrim" onClick={onCancel}>
      <div onClick={(e) => e.stopPropagation()}>
        <Panel
          title={request.title}
          onClose={onCancel}
          style={{ maxWidth: 340, width: '88vw' }}
        >
          <div style={{ textAlign: 'center', marginBottom: 16, color: 'var(--text-muted)' }}>
            {request.body}
          </div>
          <div style={{ display: 'grid', gap: 8 }}>
            <Button
              full
              onClick={() => {
                request.onConfirm();
                onCancel();
              }}
            >
              Потратить {request.price} {ISOTOPE_GLYPH}
            </Button>
            <Button kind="secondary" full onClick={onCancel}>
              Отмена
            </Button>
          </div>
        </Panel>
      </div>
    </div>
  );
}

/**
 * Хук на месте использования: экран запрашивает подтверждение через `ask`
 * вместо того, чтобы тратить изотопы прямо по тапу кнопки действия, и
 * рисует `dialog` где-нибудь в своем дереве (поверх остального, как любой
 * `.scrim`-попап каркаса).
 */
export function useConfirmSpend() {
  const [request, setRequest] = useState<ConfirmSpendRequest | null>(null);

  return {
    ask: (r: ConfirmSpendRequest) => setRequest(r),
    dialog: request ? (
      <ConfirmSpendDialog request={request} onCancel={() => setRequest(null)} />
    ) : null,
  };
}
