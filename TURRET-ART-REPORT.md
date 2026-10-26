# Turret art report (pass 4, 2026-10-26)

Scope: the nine turrets' 3D assets for the chosen evolution design (TURRET-UPGRADE-DESIGN.md): base → A/B at Lv3 → four
independent finals A1/A2/B1/B2 at Lv9. Nothing else touched (no weapons, scenes, FX, version, Thunderstore).

## What was built

- **81 low-poly models** in `HtfFortress/Models/`: for each turret `<T>Body` (its own plinth + chassis, the game's "Head"),
  `<T>Default` (Lv1-2 head), `<T>A` / `<T>B` (Lv3: the first mechanism), `<T>A1` / `<T>A2` / `<T>B1` / `<T>B2` (Lv9: the
  A or B mechanism kept, plus the parts the design names) and `<T>Barrel` (the extra barrel the game hangs for every extra
  shot/target: now one exists for every turret; before, seven turrets fell back to a brown cube).
- **Generator**: `tools/make_turrets_v4.py` (one palette, shared helpers; `A1 = A's parts + its own`). Run:
  `Blender -b HtfFortress/Models/fortress_models.blend --python tools/make_turrets_v4.py` (`ONLY=GunA,GunB` to rebuild some).
- **Previews**: `tools/turret_preview.py` renders whole turrets as the game stacks them (TurretBase + body + head + extra
  barrels, head grown by level). `preview/turrets_final/<turret>.png` = before (classic head + generic BranchX ornament)
  over after, 3/4 and front views, columns Lv1 / A / B / A1 / A2 / B1 / B2; the `.txt` beside each lists heights and tris.
- **Icons**: `HtfFortress/Icons/Piece<T>.png` re-rendered from Body + Default (`tools/make_icons.py` updated).

Bases (design doc §0): Gun = pick 7 (tall square stone plinth, twin barrels, side ammo box), Cannon = pick 1 (round
stone/wood carriage, one big muzzle), the other seven as self-described there.

## Code changes (smallest that attach the heads by branch)

- `Catalog.ClassicHeads` → `false` (and the line forcing it in `FortressPlugin` removed): the existing modular path
  (`BodyModel` / `ModuleModel` / `AttachHeadModule`) now drives every turret, so A/B/A1/A2/B1/B2 are whole heads per
  turret, not the one generic `BranchA`… ornament. `<T>Head.obj` and `Branch*.obj` stay on disk (GearNetCannon's icon uses
  CannonHead) but are no longer spawned.
- `Catalog.BarrelMount` now gives height and forward offset per turret (it put every extra barrel 0.08 m under the head
  pivot, at the foot); `<T>Barrel` models point +Z from their rear so the mount is their base.
- `tests/Headless/Tests.cs` (buildings): every turret has its nine models on disk, names distinct, ≤2500 tris and
  ≤5 materials each, barrel mount on the head.

## Budget (triangles / materials per model)

| Turret | Body | Default | A | B | A1 | A2 | B1 | B2 | Barrel |
|---|---|---|---|---|---|---|---|---|---|
| Gun | 236 / 4 | 328 / 3 | 444 / 4 | 248 / 3 | 952 / 4 | 868 / 5 | 636 / 5 | 480 / 4 | 176 / 3 |
| Cannon | 616 / 4 | 312 / 3 | 468 / 4 | 448 / 4 | 916 / 4 | 772 / 5 | 888 / 5 | 1432 / 5 | 200 / 3 |
| Harpoon | 236 / 4 | 126 / 4 | 234 / 5 | 178 / 5 | 426 / 5 | 844 / 5 | 372 / 5 | 274 / 5 | 62 / 3 |
| Mortar | 414 / 4 | 216 / 3 | 406 / 5 | 366 / 4 | 616 / 5 | 690 / 5 | 670 / 4 | 518 / 4 | 124 / 3 |
| Tesla | 344 / 5 | 336 / 4 | 560 / 5 | 548 / 5 | 804 / 5 | 1256 / 5 | 748 / 5 | 892 / 5 | 196 / 3 |
| Flame | 348 / 4 | 272 / 5 | 452 / 5 | 236 / 5 | 680 / 5 | 536 / 5 | 472 / 5 | 480 / 5 | 80 / 3 |
| Sniper | 204 / 4 | 268 / 4 | 328 / 5 | 340 / 5 | 500 / 5 | 470 / 5 | 488 / 5 | 480 / 5 | 120 / 3 |
| Bubble | 424 / 4 | 244 / 5 | 340 / 5 | 484 / 5 | 548 / 5 | 768 / 5 | 676 / 5 | 650 / 5 | 52 / 3 |
| Mender | 212 / 5 | 168 / 4 | 356 / 4 | 292 / 5 | 628 / 5 | 524 / 5 | 548 / 5 | 580 / 5 | 64 / 4 |

Largest model: 1432 tris. Whole turret in view at Lv9 (TurretBase + body + head + extra barrels): see the `.txt` files.

## Progression

- Lv1-2: body + plain head. Lv3: A or B head (major mechanism: shroud + drum / long rail barrel, three-tube rack / recoil
  cylinders, ice spear, fuel tank, transformer, bipod…). Lv9: the A1/A2/B1/B2 head keeps that mechanism and adds the
  design's unique parts (carousel, coolant jackets + frost ring, rails + coils + arc, brake + counterweight; mortar rack,
  napalm tank + hoses, shield + big wheels, blast ring + springs; …). Head scale 1.0 → 1.16 → 1.40 is the game's (unchanged).
- A side glows amber, B side cyan; Flame B2 is blue/ice; Harpoon B ice.

## Tests

`./tests/run.sh`: 124175 checks passed, 0 failed (dotnet build clean).

## Remaining / notes

- Not deployed, not packed, no version bump.
- `TurretBase.obj` (shared foot, 12252 tris) was left alone: out of this pass's scope, and its brass lamp pads double the
  lamp sockets BuildSystem.SetLamps builds. Worth a slim rebuild later.
- Extra barrels for the widest volleys (Cannon A1 / Harpoon A1 / Mortar B1 at Lv9: five extra) spread ±0.79 m from
  BuildSystem's fixed spacing; those modules carry a wide rack bar so the outer ones sit on something, but a per-turret
  spread cap in SetTurretLook would look tidier.
- Owen's main checkout reportedly has uncommitted classic `<T>Head.obj` and `Piece*.png` edits (round 1); the icons here
  will conflict on merge — keep these (they render the new bodies).
- In-game check still to do (headless only here): aiming pivot, extra-barrel placement at Lv5/10/15, lamp ring vs the new
  bodies' footprints.
