# Промпт: клуб пыли для посадки шаттла (билборд-спрайт), 2026-09-06

Причина: ParticleSystem в сцене MAIN не рендерится (ни в RenderTexture, ни в бэкбуфере окна Game — проверено 4-метровыми белыми частицами и SetParticles), пыль сферами читается мячами. Нужен спрайт клуба, который кладём на квад-билборд и анимируем масштабом и альфой кодом (PolyotShattla, флаг PylMeshami → PylSpraytami).

Файл: pyl-klub (квадрат 1:1), Gemini, фон маджента для вырезки (ikonka_rembg --bez-obvodki --ne-kvadrat --model birefnet-general-lite).

```
Single soft puff of Martian dust, a rounded cumulus-like cloud shape seen from the side, matte dusty orange-tan color like Mars regolith, soft feathered edges fading to nothing, a slightly lighter warm top and a slightly darker underside, stylized casual game look like Township smoke puffs, flat vector render with gentle shading, solid uniform bright magenta-pink background #FF00AA, centered, generous empty margin, square crop.
```

Нужны 2-3 варианта формы (kлуб-1, -2, -3) для разнообразия.
