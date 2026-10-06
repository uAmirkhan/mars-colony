# Лицензия — Kenney Space Kit (2.0)

Источник: https://kenney.nl/assets/space-kit
Автор: Kenney (www.kenney.nl)
Скачано: прямая ссылка https://kenney.nl/media/pages/assets/space-kit/20874c75ac-1677698978/kenney_space-kit.zip (без регистрации, без оплаты)
Дата скачивания: 2026-08-06

## Текст лицензии (из License.txt внутри архива)

```
Space Kit (2.0)

Created/distributed by Kenney (www.kenney.nl)
Creation date: 27-08-2020 14:20

------------------------------

License: (Creative Commons Zero, CC0)
http://creativecommons.org/publicdomain/zero/1.0/

This content is free to use in personal, educational and commercial projects.
Support us by crediting Kenney or www.kenney.nl (this is not mandatory)

------------------------------

Donate:   http://support.kenney.nl
Request:  http://request.kenney.nl
Patreon:  http://patreon.com/kenney/
```

Полный текст CC0: http://creativecommons.org/publicdomain/zero/1.0/

## Состав

153 модели в форматах FBX, GLTF, OBJ, DAE, STL (папка `extracted/Models/<формат> format/`).
Оригинальный zip лежит рядом (`kenney_space-kit.zip`), полный дамп лицензии — файл
`extracted/License.txt`.

## Важно для интеграции (не проверено на диске, требует замера)

- Модели идут БЕЗ текстурного атласа — только preview/sample картинки. Kenney красит
  low-poly модели этого набора вершинными цветами (vertex colors), а не UV-текстурой.
  Материал "Standard" с одним атласом (как у KayKit в `SceneBuilder3D.cs`) их не покрасит —
  нужен шейдер, читающий vertex color, либо перекраска материалом с фиксированным цветом.
- Масштаб и пивот НЕ измерены. Перед использованием в раскладке — прогнать по этим моделям
  такой же замер, как `AuditKit()` в `SceneBuilder3D.cs` (габариты, min_y), а не полагаться
  на глаз: правило проекта "масштаб не берется на веру".
