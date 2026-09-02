# Quest shader variants: standard / vegetation / glass

These are mobile (Android/Quest and iOS) counterparts. The PC shaders and every existing
material remain untouched; build-time material copies should point at the new
shader names below.

| PC source shader | Quest shader |
|---|---|
| `you5248/dhk Standard (Bicubic Lightmap)` | `you5248/Quest/dhk Standard` |
| `you5248/LeafCover Standard` | `you5248/Quest/LeafCover Standard` |
| `you5248/LeafCover StandardCutout` | `you5248/Quest/LeafCover StandardCutout` |
| `Mochie/Glass (Two Pass)` | `you5248/Quest/Glass NoGrab` |
| `Mochie/Glass (Two Pass) LeafCover NoGrab` | `you5248/Quest/Glass LeafCover NoGrab` |
| `Mochie/Glass (Two Pass) SnowCover NoGrab` | `you5248/Quest/Glass SnowCover NoGrab` |
| `Mochie/Glass SnowCover NoGrab` | `you5248/Quest/Glass SnowCover NoGrab` |

## Deliberate Quest reductions

- Mobile world builds always force non-directional lightmaps. Bakery `*_L1`
  references are removed from both `LightmapSettings` and
  `ftLightmapsStorage`; this is a fixed project policy, not a config toggle.
- Before the mobile scene is serialized, any remaining material with MonoSH
  enabled is replaced by an in-memory Quest copy with MonoSH/specular keywords
  disabled. PC material assets are not modified.
- `dhk Standard`: shader model 3.5; standard bilinear lightmap sampling;
  Bicubic and MonoSH disabled; no ForwardAdd, realtime-shadow, deferred, or
  prepass variants.
- `LeafCover`: shader model 3.5; MonoSH and realtime-light/shadow passes removed;
  the PC maximum of 27 leaf candidates per fragment is approximated with four.
  `_Layers` still increases density but no longer multiplies the sample loop.
- Glass: shader model 3.5 and one double-sided premultiplied pass. There is no
  GrabPass, refraction, blur, SSR, rain, AreaLit, LTCGI, ForwardAdd, or shadow
  pass. A baked reflection-probe sample and cheap Fresnel silhouette remain.
- Quest glass adds `_QuestTint`, `_QuestOpacity`, and
  `_QuestFresnelStrength`. Tune these only on the new Quest material copies.
- Vegetation wind is disabled on the in-memory Quest material copies. The
  Soyogi and BK vegetation shaders also compile the vertex-wind calculations
  out under `SHADER_API_MOBILE`, so zero visual motion also means zero wind
  vertex cost on Quest.

Use `you5248 > Quest Port > Audit MonoSH and Vegetation Wind` to list effective
scene materials that need the build-time policy enforcement and to count
serialized Bakery `_L1` references before a build.

## Material-copy notes

Create new Quest materials by copying the PC materials, then change only the
copy's shader to the mapped Quest shader. Matching property names preserve the
albedo/PBR/leaf/snow values. Register source-to-copy pairs in
`QuestPortConfig.materialSwaps`; never point a PC material itself at a Quest
shader.

Recommended glass starting values are `_QuestOpacity = 0.16` and
`_QuestFresnelStrength = 0.55`. Transparent glass is fill-rate sensitive on
Quest, so keep its screen coverage and overlap low.
