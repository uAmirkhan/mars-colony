# Шаттл: промпт генерации

По вашему эталону `design/etalons/V1/`. Шаттл-орбитер с **открытым грузовым
отсеком вдоль хребта**: створки распахнуты, внутри видно три ячейки. Это и есть
«вагоны» — отсеки видно и их можно посчитать, при этом силуэт остаётся цельным
кораблём.

Формат кадра 1:1.

---

```
A stylised space shuttle orbiter cargo craft. Cream-white fuselage with a dark charcoal underbelly and charcoal wing leading edges, a blunt orange nose cap, a wide teal-glass cockpit windscreen wrapping the nose, swept delta wings, a tall vertical tail fin, a cluster of three engine bells with two side manoeuvring pods at the tail, landing gear extended — one nose wheel and two main wheels.

CARGO BAY — the point of the vehicle. A long payload bay runs down the spine of the fuselage behind the cockpit, and both hinged bay doors are swung fully open along its sides, standing up and outward. Inside, the open hold is divided by visible cross frames into three equal cargo cells of the same size, empty and open to the sky, so the number of loading slots can be counted at a glance from outside. The bay runs at least half the length of the fuselage, and nothing else on the craft is larger than one cell.

RENDER. Clean stylised 3D game asset render, smooth matte surfaces, soft even shading with gentle ambient occlusion in the panel seams and inside the bay. No visible brushwork, no canvas texture, no sketch marks, no heavy black outline around the silhouette — shapes separate through shading and colour.

FORM. Chunky, simplified, toy-solid proportions — the nose blunt and rounded, the wings thick with rounded edges, every corner softened. Panel seams are shallow grooves, not drawn lines. Readable as a shuttle in one glance at thumbnail size.

COLOUR. Cream-white #EFE2CB body reads as the main mass. Charcoal #3E4248 on the underbelly, wing leading edges and tail fin trim. One saturated orange #E8722C accent on the nose cap and a short stripe near the cockpit. Teal glass #4FBFC4 in the windows. Brushed steel-grey inside the cargo bay and on the landing gear. No other colours, no weathering, no rust, no dirt, no hazard chevrons.

MARKINGS. Bare painted surfaces with only mechanical detailing — panel seams, hinges, latches. No text, no numbers, no logos, no decals, no flags.

LIGHT. Soft even light from a large surrounding source. Gentle two-step shading, lighter where a surface faces the light, darker where it turns away, soft occlusion in seams and in the open bay. No hard cast shadow, no contact shadow, no specular hotspot.

CAMERA AND FRAME. Isometric three-quarter view from above, camera pitched down about 40 degrees, the craft seen from the front-left so the open bay, the wing and the cockpit all read at once. Square 1:1 frame. The whole craft fits inside with a clear even margin of at least eight percent on every side — never cropped by any edge.

SCENE. The craft is suspended weightless in a seamless pure white #FFFFFF void, floating product photography, the white continuing directly underneath it with no seam where a floor would be. No floor, no ground, no horizon, no table, no pedestal, no stand, no shadow, no reflection beneath the craft.
```

---

## Две оговорки

**Обводки в промпте нет намеренно.** У вашей второй картинки эталона по силуэту
идёт тёмная линия. Она запекается в текстуру и лезет в кадр Unity чёрной каймой,
а Tripo по обведённому силуэту хуже читает глубину. Если обводка нужна как стиль
игры, её правильнее делать шейдером в движке, а не в модели.

**Фон белый, а не зелёный.** Зелёный на эталоне был под композит макета. Для
восстановления в 3D нужен белый: на зелёном Tripo подмешает зелёный в края
текстуры.

## Что ещё не сгенерено

Кроме шаттла в наборе не хватает:

- **атмосферная станция** — в партии её не оказалось, промпт есть в
  `promty-gotovye.md`, пункт 10;
- **жилой барак** — стоит в сцене пятью копиями и тянет больше всех по весу;
- **гриб-купол** — две копии;
- **текстильная фабрика**;
- **десять предметов декора** — `promty-dekor.md`;
- **шесть предметов ледяного биома** — `promty-led.md`.
