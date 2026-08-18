# Готовые промпты Gemini: 13 моделей Mars Colony

Редакция 3. Что изменилось против второй: снят запрет на структуру и
заменён правилом толщины, цвет переведён в иерархию с гаммой по типу
объекта, из кадра убрано самопротиворечие, в дизайн-строках назван
рабочий узел вместо абстрактной формы.

Формат кадра в Gemini поставить 1:1. Копировать блок целиком.

**Помечено «уже годно»** — эти три опознались вслепую с прошлого
захода, перегенерировать не нужно, промпт оставлен на случай переделки.

## Приёмка картинки до отправки в 3D

1. Объект один, все части сращены, между ними не просвечивает фон.
2. Под объектом пусто: ни плиты, ни грунта, ни тени.
3. Ни одной буквы, цифры или логотипа.
4. Ничего не срезано рамкой, по краям есть поля.
5. Видно верх объекта — крышу, палубу, макушку.
6. Опознаётся вслепую: покажите картинку человеку, не говоря что это.
   Не угадал — брак промпта, не генератора.

---

## 1. sklad-bunkery  *(уже годно, перегенерация не нужна)*

```
A single large low storage depot building with thick outward-sloping walls and a shallow curved roof, one wide roll-up cargo shutter on the long side with a small human-sized personnel door beside it, two squat roof vent hoods with visible open louvre slots, a short stack of shipping canisters against the wall by the shutter.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 2. burovaya-02

```
A working oil-derrick style drilling tower: an open four-legged lattice mast of thick square girders, a boxy crown block with pulley sheaves at the top, a heavy drill pipe descending through the open middle of the mast into a rotary table on the deck below, the deck a solid low machine housing with a pipe rack of stacked drill rods along one edge.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 3. burovaya-03

```
A tracked auger drilling machine: a long horizontal drilling barrel carried on chunky treads, a real helical screw auger with deep open spiral flights projecting from the front end, a cuttings chute spilling to one side, the operator cab set back behind the barrel.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 4. burovaya-04

```
A rotary drill rig: a squat tracked body with a slewing turntable, a thick vertical drill mast standing on it, a heavy toothed rotary drill bit at the bottom of the mast biting down into the drilling position, a hydraulic ram arm bracing the mast to the body, a blocky counterweight at the rear.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 5. burovaya-05

```
A stationary core-drilling rig standing on three thick splayed truss legs, a large prominent drill head and thick drill stem hanging in the open gap between the legs and reaching well below them, a winch drum with heavy cable on the platform above, the platform a solid deck with a control cabinet.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 6. zhiloy-bashnya

```
A residential tower of four stacked cylindrical habitat decks with flat vertical walls and a band of square windows around each deck, a flat capped roof with a solar array and a squat water tank, a solid external stair-and-lift shaft running up one side, off-centre.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 7. zhiloy-kupol

```
A smooth solid opaque hemisphere habitat dome, low and wide, one human-sized airlock pod bulging off-centre on its curve, two round porthole windows, a low canopy over the airlock door.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 8. kupol-geodezicheskiy  *(уже годно, перегенерация не нужна)*

```
A large glazed greenhouse dome, one continuous glass shell divided into big flat crystalline facets with visible frame ribs, green planting beds showing through the glass, a wide loading gate set into it off-axis near the bottom.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 9. kupol-tunnel

```
A pressurised corridor module: a long low horizontal tube with one elbow bend where it changes direction, thick sealing collar flanges at both ends where it bolts onto other buildings, a row of small round viewport windows along its side, a service pipe bundle running along the top.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 10. stantsiya-atmosfernaya

```
An atmosphere processing plant: three upright cylindrical pressure tanks of different heights standing together, joined by a manifold of thick pipes with visible valve wheels and gauges, a finned condenser unit at the base, one tall intake stack with a flared hood rising off-centre.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 11. zavod-pishchevoy

```
A food processing factory: a large flat-roofed rectangular hall, a loading dock with two shutter doors and a canopy along one side, three tall silos with conical tops clustered at one corner, a covered conveyor bridge running from the silos into the roof of the hall.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 12. sklad-angar  *(уже годно, перегенерация не нужна)*

```
A large barrel-arch hangar with a tall recessed archway entrance set off-centre in one end face, the opening a deep shadowed tunnel with visible side walls and a ceiling curve inside it, a sliding door panel parked open to one side, roof ribs across the vault.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

## 13. ploshchadka-shattla

```
A shuttle landing pad: a wide circular deck on short thick legs, a painted landing target ring on its top surface, a ring of stubby landing lights around the rim with one taller approach mast, a folding boarding stair unit attached at the edge, blast deflector plates standing at the perimeter.

RENDER REGISTER. A clean CG product render of one object, smooth-shaded as if captured in a 3D engine viewport. This is a 3D render, not concept art, not an illustration. No ink outline, no contour stroke around any silhouette or seam, no comic linework, no visible brushwork, no canvas or paper texture, no painterly shading, no sketch marks. Shapes separate through shading and colour, never through a line.

SUBJECT. Exactly one object in the frame, centered and complete. Never a row, never a set, never duplicates or variants side by side, never a second copy in the background. Every part of the object is fused into one connected body — no separate piece merely leaning against another, no gap of background visible between two parts that should be joined.

SCALE. This is a full-size colony structure or machine, large enough for people to work inside it or operate it. It is never a tabletop prop, never a container, crate, chest, trunk, box, barrel or toy. Nothing in the frame may read as an object small enough to pick up.

TONE. Bright, clean, optimistic — a sunlit, well-run colony. Never a rugged industrial wasteland, never weathered realistic science fiction. If in doubt, render it friendlier and cleaner, not grittier.

FORM. One large primary shell carries the silhouette. Onto it are attached four to seven secondary volumes that give the object a job and an owner: a trim collar around a natural seam, a canopy over an entrance, an oversized working fixture on the roof or face, an off-centre bulging pod, a stack of cargo or canisters hugging the foot along one side, a thick stub of pipe where the object joins the rest of the colony. Never centre an attachment on a face — each sits off-axis, the way something bolted on afterwards would. No single attachment is larger than about a fifth of the primary shell, and together they cover well under half of it: they accent the shape, they never replace it. Each must look like something a colonist would use, service or load, never an ornament added for texture. The object still reads as one clear silhouette at thumbnail size — the shell dominant, the attachments legible as a handful of distinct bumps along its edge, never as fuzz.

STRUCTURE. The limit is thickness, not openness. Real structural framework is welcome — lattice masts, truss legs, girder frames, helical auger flights, pipe runs between tanks — provided every member is at least as thick as a person's arm at this object's scale, with its own visible depth and shading. Anything thinner than that is replaced by its solid equivalent: a low parapet instead of a railing, a solid ramp or chunky block steps instead of ladder rungs, a fat stub mast instead of a hairline antenna, a rigid pipe or a thick coiled spool instead of a loose cable. Never draw structure as flat painted stripes on a solid surface — if a truss or a screw thread belongs there, it is modelled as real geometry with real gaps, or it is left out entirely.

SURFACE. Every surface is a clean factory-fresh matte-to-satin painted finish, ceramic composite or painted metal, one uniform colour per panel, crisp moulded edges. The object is new and cared-for: intact smooth paint everywhere. A soft gentle sheen only on painted panels and brass trim, never a hard glossy hotspot, never a mirror reflection.

COLOUR. Colour is a hierarchy, not a paint job. The main body sits muted, at 30 to 45 percent saturation, and recedes. One functional part — the part that does the object's work — carries a fully saturated accent at 65 to 85 percent, and it is the first thing the eye finds. The saturated colour never spreads across the whole body. Hue family is locked to what the object is: drilling and hauling machinery in the rust and terracotta family (#C1432B, #C2643C), habitats and domes in the teal and sage family (#1E93A0, #7C9B63), production and storage in the mustard and brass family (#D6A02A, #C7A233). Metal fittings are warm brass #C7A233, matte to satin, used sparingly where a real fitting would sit, never as decorative bands wrapping the body. Never pure interface blue #2E9BE0, never yellow-and-black hazard chevrons, never khaki or battleship grey, never rust-orange streaking.

MARKINGS. Bare factory-fresh painted surfaces with only mechanical detailing — rivets, panel seams, bolts. Unmarked, as if photographed on the production line before any signage, decals, stencils, numbering or paint markings were applied.

LIGHT. Soft even light from a large surrounding source, like a bright overcast sky. Every surface carries a gentle two-step gradient, lighter where it faces the light, darker where it turns away, with soft ambient occlusion only in deep seams and inner corners. No hard directional key light, no cast shadow anywhere in the frame, no contact shadow beneath the object, no specular hotspot, no rim light, no coloured light.

CAMERA AND FRAME. Three-quarter view, camera pitched down about 40 degrees so the top surface of the object — roof, deck or crown — is clearly visible, not merely hinted at. Square 1:1 frame. The whole object fits inside the frame with a clear even margin of at least eight percent on every side. Never crop the top, bottom, left or right of the object: a smaller complete object is always better than a larger cropped one. If the object is tall or long, shrink it until it fits rather than pushing it against an edge.

SCENE. Floating product photography: the object is suspended weightless in a seamless white void, shot in the style of a levitation product campaign. Pure white #FFFFFF continues directly underneath the object with no line, seam or colour change where a floor would be. The object's own flat manufactured underside is its lowest visible surface, with empty white void below it. No floor, no ground, no terrain patch, no horizon, no table, no pedestal, no plinth, no display stand, no turntable, no base slab, no platform, no shadow, no reflection beneath the object.
```

---

## Правки в диалоге, если что-то вылезло

Дешевле перегенерации. Scope блокируется жёстко, нужное состояние
описывается утвердительно, а не запретом.

**Плита под объектом:**

```
Keep this exact object, camera angle, lighting and background unchanged. Remove the flat surface currently touching the bottom of the object — the object should end at its own flat underside with nothing else below it, floating in the same white void as the rest of the frame around it.
```

**Объектов вышло несколько:**

```
Keep the style, lighting, camera angle and background exactly as they are. Show only one single object, centered and complete, with a clear margin on every side. Remove every other copy from the image.
```

**Объект срезан рамкой:**

```
Keep the object, style, lighting and background exactly as they are. Zoom out so the entire object fits inside the frame with a clear even margin on all four sides — nothing touching or crossing any frame edge.
```

**Ракурс слишком низкий:**

```
Keep the object, style, lighting and background exactly as they are. Raise the camera so we look down on the object from about 40 degrees above and clearly see its whole top surface.
```
