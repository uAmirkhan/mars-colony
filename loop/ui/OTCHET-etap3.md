# Отчёт этапа 3 (спека loop/SPEC-ETAP-3.md), начат 2026-09-06 утром

## Блок 1 — полировка вида
- Аудит критика кадра записан: `loop/ui/AUDIT-polirovka-kritik.md`. Аудит арт-директора — в работе.
- Склад: элементы заказчика vkladka-neaktivnaya и kapsula-emkost подставлены (`etap3-sklad-elementy.png` = noch2-sklad-3.png на 11:24). Коммит mars-unity fcb313d.
- Пыль шаттла: три кандидата спрайт-листов (`loop/ui/pyl-kandidaty/`), выбран ohyhei Smoke Sprite Sheet (CC0), билборды-квады с флипбуком реализованы (`PolyotShattla.PylSpraytami`, спрайт-лист `Assets/UI/Effekty/pyl-klub-list.png`), но **невидимы**.
- **Системная находка:** в сцене MAIN не рендерится ни один прозрачный или alpha-clip 3D-материал (тесты квадов в Play: transparent Unlit, Sprites/Default, alpha-clip — невидимы; opaque Unlit с той же текстурой и красный куб — видны). Это причина и невидимых частиц. Depth priming выключен, маски -1, фич нет. Расследует `mars-shader-artist` (задача bd P1). До починки пыль шаттла ждёт.
