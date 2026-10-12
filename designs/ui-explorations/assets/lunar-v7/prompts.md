# 夜航晴空满月原画 · v7

## 范围与结果

按用户要求重新生成完整带月亮的静态原画，不再在此图上叠加月相遮罩、透明暗面或前景云。只生成新素材，不替换正式应用或已有交互预览。

- 使用技能：imagegen；方式：内置 image_gen 工具，未使用 CLI/API fallback。
- 风格参考：`../lunar-v3/night-soft-full.png`，仅参考色彩和山水笔触，不锁定旧云层布局。
- 最终素材：[night-clear-full.png](night-clear-full.png)，实际 2172 × 724。
- 月亮缩小并置于右上晴空；低云与月轮之间留有明显间隔。
- 已目视检查：单个月亮，完整可见且无云穿过月面，无界面文字；月亮、山水和湖面直接生成于同一张图。
- 不宣称天文精确性；本轮不生成动态效果。
- 原始生成副本：`C:/Users/excel/.codex/generated_images/01a0d3d1-7e48-7b81-9038-acf7308f4abb/exec-46041ac0-5e38-4705-8e06-26342e449d02.png`。

## 实际生成提示词

```text
Use case: stylized-concept. Input image 1 is a STYLE AND PALETTE REFERENCE, not a layout to copy literally. Generate a new complete original panoramic painting for the same moonlit mountain-lake interface, with a better sky composition. Output one finished 3:1 landscape, approximately 3072x1024, no interface or text. Retain the reference's exquisite blue-green ink-painting atmosphere, layered mountain silhouettes, finely painted pines, luminous mist in the valleys and restrained rippling lake reflections. Leave the left half dark and visually quiet for future interface text. CRITICAL COMPOSITION: a single fully opaque FULL MOON is painted directly into the upper-right clear sky, center around 76% of canvas width and 31% of height, diameter about 10% of canvas width, smaller and more balanced than the reference. Its entire circular disk and all of its boundary must be unobstructed. Keep every cloud, cloud wisp, fog streak and mountain completely outside the moon and its immediate surroundings: leave at least half a lunar radius of clear blue-black sky around the whole moon. Put all distinct sky clouds into loose low horizontal banks near the mountain horizon, well BELOW the moon, with a generous obvious gap; do not arrange clouds into a circular hole or ring. Absolutely no clouds in front of the lunar disk, no clouds visible through it, and no moon overlapping a cloud bank. MOON MATERIAL: coherent silver-celadon natural lunar markings with fine painted texture, an opaque solid visible lunar surface, luminous but not chalk white; match the landscape's brushwork and color grading. No transparent parts, no glass sphere, no dark crescent mask, no eclipse, no planetary rim lighting, no white contour stroke, no bright circular edge band, no decorative ring. Any atmospheric moon glow must be broad, very faint and smoothly dispersed into the sky, never a tight halo. Light the low clouds, distant ridges and water gently and consistently, avoiding a glaring vertical light pillar. Make this feel like a single beautiful original painting, not an isolated moon pasted into a background. No extra objects, people, buildings, spacecraft, symbols, labels, UI, watermark or stars crowding the moon.
```
