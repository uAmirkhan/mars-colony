# Отчёт этапа 3 (спека loop/SPEC-ETAP-3.md), начат 2026-09-06 утром

## Блок 1 — полировка вида
- Аудит критика кадра записан: `loop/ui/AUDIT-polirovka-kritik.md`. Аудит арт-директора — в работе.
- Склад: элементы заказчика vkladka-neaktivnaya и kapsula-emkost подставлены (`etap3-sklad-elementy.png` = noch2-sklad-3.png на 11:24). Коммит mars-unity fcb313d.
- Пыль шаттла: три кандидата спрайт-листов (`loop/ui/pyl-kandidaty/`), выбран ohyhei Smoke Sprite Sheet (CC0), билборды-квады с флипбуком реализованы (`PolyotShattla.PylSpraytami`, спрайт-лист `Assets/UI/Effekty/pyl-klub-list.png`), но **невидимы**.
- **Системная находка:** в сцене MAIN не рендерится ни один прозрачный или alpha-clip 3D-материал (тесты квадов в Play: transparent Unlit, Sprites/Default, alpha-clip — невидимы; opaque Unlit с той же текстурой и красный куб — видны). Это причина и невидимых частиц. Depth priming выключен, маски -1, фич нет. Расследует `mars-shader-artist` (задача bd P1). До починки пыль шаттла ждёт.
- Аудит арт-директора записан (`AUDIT-polirovka-artdir.md`), оба аудита сведены в `SPISOK-pravok-blok1.md` (P1–P14, A1).
- Прозрачность: гипотезы шейдер-художника проверены в Play — ось сортировки (→Perspective), Volume/пост-обработка (off), copyDepthMode (→AfterOpaques) + intermediate (Auto): ни одна не вернула прозрачный квад; настройки возвращены. В сцене 22 рендерера с queue≥2450 (знаки cutout, выстилки Lit transparent) — проверить, видны ли они. Задача `mars-colony-4y9` с фактами. P12/P14 заблокированы.
- Виток UI-30 (P1+P2+P4: медальоны 1.65×, капсула опыта к краю, лента и кнопка карточки) — программист; A1 анимации — mars-juice параллельно (файлы не пересекаются).
