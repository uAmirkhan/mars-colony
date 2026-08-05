import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './App.tsx';
import { chooseEntryState } from './state/entry';

// Состояние старта выбирается ДО первой отрисовки: сейв поднимается синхронно
// при импорте стора, и подстановка показа позже дала бы видимую вспышку чужой
// колонии на первом кадре.
chooseEntryState();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
