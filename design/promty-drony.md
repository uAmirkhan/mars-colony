# Дрон-курьер: три тира

Редакция 4, по эталону `design/etalons/V1/`.

**Что было не так в третьей.** Убирая остекление кабины, я выкинул заодно
обтекаемый корпус и заменил его открытой рамой-порталом. Рама читается
строительными лесами с ящиком внутри, а не курьером. Привлекательность
аппарата держал именно гладкий вытянутый корпус.

**Что взято из эталона.** Гладкий кремовый корпус, винты в толстых кольцах
на коротких пилонах вплотную к телу, ящик снизу на захватах, и спереди
**круглая камера с бирюзовым свечением** — объектив, а не окно.

**Тир задан пропорцией и навеской, а не другой машиной:**

| Тир | Корпус | Винты | Опоры | Груз |
|---|---|---|---|---|
| 1 | короткий, почти яйцо | 4 малых кольца вплотную | 4 тумбы | куб, 2 захвата |
| 2 | вдвое длиннее, спинной обтекатель | 4 широких на пилонах | 2 лыжи | широкий ящик, 4 захвата |
| 3 | длинный, стреловидные выносы | 4 больших на выносах | 4 стойки | капсула, 6 захватов |

---

## Порядок работы

**Шаг 1.** Строй, промпт 0 — только там задаётся соотношение размеров.
В одиночном кадре «больше» не имеет точки отсчёта.

**Шаг 2.** В том же чате вынуть каждого:

```
Keep the exact same three designs, style, colours, lighting and white background. Show only the middle drone now — one machine alone, centered in a square frame, same three-quarter view from above, with a clear margin on every side. Remove the other two entirely.
```

**Шаг 3.** Одиночные промпты ниже — если строй уже утверждён.

---

## 0. Строй: три тира в одном кадре

```
SUBJECT. Three cargo drones of one upgrade family standing in a row on an empty white background, seen together in one wide frame, drawn to true relative scale against each other so the growth is obvious at a glance.

LEFT, the small one — a short compact almost egg-like hull, four small ducted fans on very short stub pylons hugging the body, four stubby pads underneath, one small cube container held by two grip arms. The smallest of the three.

MIDDLE, the mid-tier — a hull about twice as long as it is wide with equipment blisters and a dorsal spine fairing, four wider ducted fans on short angled pylons, two flat landing skids, one wide rectangular container held by four grip arms, clearly larger than the small drone's box.

RIGHT, the heavy-lift — a long deep hull with swept forward-raked shoulder booms carrying four large ducted fans out clear of the body so it reads as winged, side equipment pods, four thick straight struts with flat pads, one large rounded capsule container held in a cradle of six grip arms. Clearly the biggest of the three.

All three share the same cream hull, charcoal fan rings with orange accent stripes, and one round teal camera lens at the nose — a camera, never a window. All three rest on the same invisible level line.

CAMERA AND FRAME. Isometric three-quarter view from above, camera pitched down about 40 degrees, all three seen from the same front-left angle. Wide 16:9 frame. All three fit fully inside with an even margin, evenly spaced, nothing cropped by any edge.

FAMILY. These drones belong to one upgrade family and share one design language: a smooth cream hull, ducted fans in thick rings on stub pylons, a round teal camera eye at the nose, and one detachable cargo container gripped beneath the belly. Tier is told by PROPORTION and EQUIPMENT, not by a different kind of machine: the hull grows longer and deeper, the fan rings grow wider, more equipment blisters and pods appear on the hull, the grip arms grow heavier, and the container grows markedly bigger.

RENDER. Clean stylised 3D game asset render, smooth matte surfaces, soft even shading with gentle ambient occlusion in the panel seams. No visible brushwork, no canvas texture, no sketch marks, no heavy black outline around the silhouette — shapes separate through shading and colour.

FORM. A smooth streamlined machine, not a crate and not a scaffold. The hull is one flowing rounded volume with softly tapered ends, its surface broken by shallow panel seams, small equipment blisters and vent slots. Ducted fans are thick solid rings with real depth, carried on short stub pylons that hug the hull rather than sticking far out on long spindly arms. Every strut and support is at least as thick as a person's arm at this machine's scale.

THE EYE. At the front of the hull sits one large round camera lens in a slightly protruding housing — a dark glass eye with a teal glow, the single most prominent feature of the nose. This is a camera, never a window: no windscreen, no glazed band, no passenger glass, no cockpit, no pilot, no door anywhere on the machine.

FEET. The machine rests on simple fixed supports only — short stubby pads, straight struts or flat skids as named in its own line. No articulated legs, no bent knee joints, no spidery or insect limbs, no tentacles.

COLOUR. Cream-white #EFE2CB hull reads as the main mass. Charcoal #3E4248 on fan rings, hubs, pylons, clamps, supports and mechanical fittings. Saturated orange #E8722C as an accent stripe running along the hull and around the fan rings. Teal #4FBFC4 on the camera lens and the cargo status panel — the only glowing elements in the image. No other colours, no weathering, no rust, no dirt, no hazard chevrons.

MARKINGS. Bare painted surfaces with only mechanical detailing — panel seams, hinges, vents, bolts. No text, no numbers, no logos, no decals.

LIGHT. Soft even light from a large surrounding source. Gentle two-step shading, lighter where a surface faces the light, darker where it turns away, soft occlusion in seams. No hard cast shadow, no contact shadow, no specular hotspot. The teal glow reads as emissive, lighting nothing around it.

SCENE. Seamless pure white #FFFFFF void, floating product photography, the white continuing directly underneath with no seam where a floor would be. No floor, no ground, no horizon, no table, no pedestal, no stand, no shadow, no reflection.
```

## 1. Тир 1, лёгкий

короткий компактный корпус, четыре малых кольца, маленький куб на двух захватах

`dron-t1`

```
A small light courier drone. A short compact rounded hull, barely longer than it is wide, almost egg-like, with a plain smooth surface and only one or two small equipment blisters. FOUR small ducted fans on very short stub pylons close around the hull. A round teal camera lens in a small housing at the nose. It rests on FOUR short stubby pads directly under the hull. Beneath the belly, held by TWO simple grip arms, ONE small cube-shaped cargo container with a thin teal status strip. Compact and light overall — the smallest machine of the family.

FAMILY. These drones belong to one upgrade family and share one design language: a smooth cream hull, ducted fans in thick rings on stub pylons, a round teal camera eye at the nose, and one detachable cargo container gripped beneath the belly. Tier is told by PROPORTION and EQUIPMENT, not by a different kind of machine: the hull grows longer and deeper, the fan rings grow wider, more equipment blisters and pods appear on the hull, the grip arms grow heavier, and the container grows markedly bigger.

CARGO. The container hangs clearly below the hull with a visible gap between them, held by grip arms that bridge that gap. It is a separate box with square corners and its own flat faces, contrasting against the hull's rounded forms, and it carries a glowing teal status strip. Keep it large — no smaller than a third of the hull's own volume.

CAMERA AND FRAME. Isometric three-quarter view from above, camera pitched down about 40 degrees, seen from the front-left so the fans, the camera eye and the slung container all read at once. Square 1:1 frame. Exactly one machine in the frame. The whole machine including fans, supports and container fits inside with a clear even margin of at least eight percent on every side — never cropped by any edge.

RENDER. Clean stylised 3D game asset render, smooth matte surfaces, soft even shading with gentle ambient occlusion in the panel seams. No visible brushwork, no canvas texture, no sketch marks, no heavy black outline around the silhouette — shapes separate through shading and colour.

FORM. A smooth streamlined machine, not a crate and not a scaffold. The hull is one flowing rounded volume with softly tapered ends, its surface broken by shallow panel seams, small equipment blisters and vent slots. Ducted fans are thick solid rings with real depth, carried on short stub pylons that hug the hull rather than sticking far out on long spindly arms. Every strut and support is at least as thick as a person's arm at this machine's scale.

THE EYE. At the front of the hull sits one large round camera lens in a slightly protruding housing — a dark glass eye with a teal glow, the single most prominent feature of the nose. This is a camera, never a window: no windscreen, no glazed band, no passenger glass, no cockpit, no pilot, no door anywhere on the machine.

FEET. The machine rests on simple fixed supports only — short stubby pads, straight struts or flat skids as named in its own line. No articulated legs, no bent knee joints, no spidery or insect limbs, no tentacles.

COLOUR. Cream-white #EFE2CB hull reads as the main mass. Charcoal #3E4248 on fan rings, hubs, pylons, clamps, supports and mechanical fittings. Saturated orange #E8722C as an accent stripe running along the hull and around the fan rings. Teal #4FBFC4 on the camera lens and the cargo status panel — the only glowing elements in the image. No other colours, no weathering, no rust, no dirt, no hazard chevrons.

MARKINGS. Bare painted surfaces with only mechanical detailing — panel seams, hinges, vents, bolts. No text, no numbers, no logos, no decals.

LIGHT. Soft even light from a large surrounding source. Gentle two-step shading, lighter where a surface faces the light, darker where it turns away, soft occlusion in seams. No hard cast shadow, no contact shadow, no specular hotspot. The teal glow reads as emissive, lighting nothing around it.

SCENE. Seamless pure white #FFFFFF void, floating product photography, the white continuing directly underneath with no seam where a floor would be. No floor, no ground, no horizon, no table, no pedestal, no stand, no shadow, no reflection.
```

## 2. Тир 2, средний

корпус вдвое длиннее, кольца шире, навеска на спине, широкий ящик на четырёх захватах

`dron-t2`

```
A mid-tier cargo drone. A longer hull, about twice as long as it is wide, tapering at both ends, with a row of equipment blisters, vent slots and a low dorsal spine fairing along its back. FOUR ducted fans, noticeably wider in ring diameter than the small drone's, on short angled pylons. A larger round teal camera lens in a protruding turret housing at the nose, with a small secondary sensor beside it. It rests on TWO flat landing skids running front to back under the hull. Beneath the belly, held by FOUR grip arms, ONE wide rectangular cargo container roughly twice the volume of the small one, its front face carrying a glowing teal status panel framed by orange corner brackets.

FAMILY. These drones belong to one upgrade family and share one design language: a smooth cream hull, ducted fans in thick rings on stub pylons, a round teal camera eye at the nose, and one detachable cargo container gripped beneath the belly. Tier is told by PROPORTION and EQUIPMENT, not by a different kind of machine: the hull grows longer and deeper, the fan rings grow wider, more equipment blisters and pods appear on the hull, the grip arms grow heavier, and the container grows markedly bigger.

CARGO. The container hangs clearly below the hull with a visible gap between them, held by grip arms that bridge that gap. It is a separate box with square corners and its own flat faces, contrasting against the hull's rounded forms, and it carries a glowing teal status strip. Keep it large — no smaller than a third of the hull's own volume.

CAMERA AND FRAME. Isometric three-quarter view from above, camera pitched down about 40 degrees, seen from the front-left so the fans, the camera eye and the slung container all read at once. Square 1:1 frame. Exactly one machine in the frame. The whole machine including fans, supports and container fits inside with a clear even margin of at least eight percent on every side — never cropped by any edge.

RENDER. Clean stylised 3D game asset render, smooth matte surfaces, soft even shading with gentle ambient occlusion in the panel seams. No visible brushwork, no canvas texture, no sketch marks, no heavy black outline around the silhouette — shapes separate through shading and colour.

FORM. A smooth streamlined machine, not a crate and not a scaffold. The hull is one flowing rounded volume with softly tapered ends, its surface broken by shallow panel seams, small equipment blisters and vent slots. Ducted fans are thick solid rings with real depth, carried on short stub pylons that hug the hull rather than sticking far out on long spindly arms. Every strut and support is at least as thick as a person's arm at this machine's scale.

THE EYE. At the front of the hull sits one large round camera lens in a slightly protruding housing — a dark glass eye with a teal glow, the single most prominent feature of the nose. This is a camera, never a window: no windscreen, no glazed band, no passenger glass, no cockpit, no pilot, no door anywhere on the machine.

FEET. The machine rests on simple fixed supports only — short stubby pads, straight struts or flat skids as named in its own line. No articulated legs, no bent knee joints, no spidery or insect limbs, no tentacles.

COLOUR. Cream-white #EFE2CB hull reads as the main mass. Charcoal #3E4248 on fan rings, hubs, pylons, clamps, supports and mechanical fittings. Saturated orange #E8722C as an accent stripe running along the hull and around the fan rings. Teal #4FBFC4 on the camera lens and the cargo status panel — the only glowing elements in the image. No other colours, no weathering, no rust, no dirt, no hazard chevrons.

MARKINGS. Bare painted surfaces with only mechanical detailing — panel seams, hinges, vents, bolts. No text, no numbers, no logos, no decals.

LIGHT. Soft even light from a large surrounding source. Gentle two-step shading, lighter where a surface faces the light, darker where it turns away, soft occlusion in seams. No hard cast shadow, no contact shadow, no specular hotspot. The teal glow reads as emissive, lighting nothing around it.

SCENE. Seamless pure white #FFFFFF void, floating product photography, the white continuing directly underneath with no seam where a floor would be. No floor, no ground, no horizon, no table, no pedestal, no stand, no shadow, no reflection.
```

## 3. Тир 3, тяжёлый

длинный корпус со стреловидными выносами, кольца на пилонах в стороны, капсула на шести захватах

`dron-t3`

```
A heavy-lift cargo drone, the top of the family. A long deep hull with a swept forward-raked shoulder boom on each side carrying the fans out clear of the body, so the machine reads as winged in plan view. FOUR large ducted fans, the widest rings of the family, mounted at the ends of those booms. Two side equipment pods faired into the flanks, extra blisters and vents along the spine. A large round teal camera lens in a heavy turret at the nose with a sensor cluster around it. It rests on FOUR short thick straight struts with wide flat pads. Beneath the belly, held in a heavy cradle of SIX thick grip arms, ONE large rounded-corner capsule container roughly twice the volume of the mid-tier one, with a long glowing teal status strip along its lower edge. Clearly the biggest and most equipped machine of the three.

FAMILY. These drones belong to one upgrade family and share one design language: a smooth cream hull, ducted fans in thick rings on stub pylons, a round teal camera eye at the nose, and one detachable cargo container gripped beneath the belly. Tier is told by PROPORTION and EQUIPMENT, not by a different kind of machine: the hull grows longer and deeper, the fan rings grow wider, more equipment blisters and pods appear on the hull, the grip arms grow heavier, and the container grows markedly bigger.

CARGO. The container hangs clearly below the hull with a visible gap between them, held by grip arms that bridge that gap. It is a separate box with square corners and its own flat faces, contrasting against the hull's rounded forms, and it carries a glowing teal status strip. Keep it large — no smaller than a third of the hull's own volume.

CAMERA AND FRAME. Isometric three-quarter view from above, camera pitched down about 40 degrees, seen from the front-left so the fans, the camera eye and the slung container all read at once. Square 1:1 frame. Exactly one machine in the frame. The whole machine including fans, supports and container fits inside with a clear even margin of at least eight percent on every side — never cropped by any edge.

RENDER. Clean stylised 3D game asset render, smooth matte surfaces, soft even shading with gentle ambient occlusion in the panel seams. No visible brushwork, no canvas texture, no sketch marks, no heavy black outline around the silhouette — shapes separate through shading and colour.

FORM. A smooth streamlined machine, not a crate and not a scaffold. The hull is one flowing rounded volume with softly tapered ends, its surface broken by shallow panel seams, small equipment blisters and vent slots. Ducted fans are thick solid rings with real depth, carried on short stub pylons that hug the hull rather than sticking far out on long spindly arms. Every strut and support is at least as thick as a person's arm at this machine's scale.

THE EYE. At the front of the hull sits one large round camera lens in a slightly protruding housing — a dark glass eye with a teal glow, the single most prominent feature of the nose. This is a camera, never a window: no windscreen, no glazed band, no passenger glass, no cockpit, no pilot, no door anywhere on the machine.

FEET. The machine rests on simple fixed supports only — short stubby pads, straight struts or flat skids as named in its own line. No articulated legs, no bent knee joints, no spidery or insect limbs, no tentacles.

COLOUR. Cream-white #EFE2CB hull reads as the main mass. Charcoal #3E4248 on fan rings, hubs, pylons, clamps, supports and mechanical fittings. Saturated orange #E8722C as an accent stripe running along the hull and around the fan rings. Teal #4FBFC4 on the camera lens and the cargo status panel — the only glowing elements in the image. No other colours, no weathering, no rust, no dirt, no hazard chevrons.

MARKINGS. Bare painted surfaces with only mechanical detailing — panel seams, hinges, vents, bolts. No text, no numbers, no logos, no decals.

LIGHT. Soft even light from a large surrounding source. Gentle two-step shading, lighter where a surface faces the light, darker where it turns away, soft occlusion in seams. No hard cast shadow, no contact shadow, no specular hotspot. The teal glow reads as emissive, lighting nothing around it.

SCENE. Seamless pure white #FFFFFF void, floating product photography, the white continuing directly underneath with no seam where a floor would be. No floor, no ground, no horizon, no table, no pedestal, no stand, no shadow, no reflection.
```

---

## Приёмка

1. **Корпус гладкий и обтекаемый**, а не рама и не ящик. Получилась
   коробка на стойках — брак, дальше не смотреть.
2. Спереди круглый объектив со свечением. Окон и остекления нет ни у одного.
3. Ног-щупалец нет. Тумбы, лыжи, прямые стойки.
4. Три корпуса заметно разной длины: короткий, вдвое длиннее, длинный с выносами.
5. Ящик отделён от корпуса зазором, между ними видны захваты.
6. Груз растёт от тира к тиру: куб, широкий ящик, капсула.
7. Кольца винтов толстые, с глубиной, а не проволочные обручи.
8. Под машиной пусто, обводки нет, ничего не срезано рамкой.
