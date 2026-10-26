# Pending (not released)

Last public release: **0.2.9**.

Changes made after 0.2.9 go here while test builds stay on the released version.
The next public version is decided by Owen.

## 0.3.0 work (gun upgrade, not released)

- Per-weapon GunSpec display profile: longer Rocket/Gatling/heavy weapons, non-pistol Boomerang/Whistle/Conch/Puffer grips, per-weapon muzzle points, drop sizes restored.
- SharkWhistle now really spawns `_sharks` that hunt and bite; game-native gun drops now show their native mesh.
- Boomerang held 90° correction shared with its icon. Headless tests at 124,050 before the model pass.
- Next: actual mesh redesign for Rocket/Gatling/Hook/WaterCannon/NetLauncher/Boomerang/SharkWhistle/LobsterClaw/Absorber; image-reference pass via ChatGPT.
- Gun Round 2: Rocket/Gatling/Hook/WaterCannon/NetLauncher/Boomerang/SharkWhistle/LobsterClaw/Absorber/Whirlpool/StarThrower meshes and icons rebuilt; all fortress guns now <=1500 tris and <=6 materials; Rocket/Gatling/Hook/WC/Net/Boomerang/Whistle previews in preview/weapons/.
- Round 2 leaves projectile scale, ichirality, grip pose, and in-game held/ADS/drop verification for the next pass.
