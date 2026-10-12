# 夜航月色融合 · v3

## 生成方式

使用内置 image_gen 图片编辑工具，单次定向修整。未调用 CLI 或外部图像生成 API。

- 编辑目标：[v2 夜航原画](../lunar-v2/night-refined-full.png)
- 生成原件：[night-soft-full.png](night-soft-full.png)（2172 × 724）
- 预览压缩图：[night-soft-full.jpg](night-soft-full.jpg)（1440 × 480；仅尺寸与编码压缩）

旧版素材保持不变。新图只在夜航的“柔光去边”和“薄雾融月”效果中使用；“旧版对照”仍使用 v2 原图。

## 实际生成提示词

```text
Use case: precise-object-edit. Input 1 is the EDIT TARGET. Refine ONLY the existing moon and its immediate atmosphere. Preserve its center, diameter, circular silhouette, all mountains, lake, clouds outside its immediate vicinity, overall 3:1 framing and blue-green night palette. The moon currently has a bright white continuous rim that reads like a sticker. Completely remove that white outline and bright perimeter band: the edge should never be brighter than the lunar face next to it. Paint the lunar surface right to a softly atmospheric boundary with subtle lost-and-found edges, slightly lower chalk-white highlights, silver-celadon light sharing the scene's blue-green haze. Keep readable painterly lunar markings, recognizable light and visual presence; do NOT simply blur or fade the whole moon. Add only a very thin broken translucent blue-gray cloud wisp across the lower quarter of the moon, continuous with existing neighboring wisps, so some contour is briefly lost behind atmosphere. Do not cover most of the moon. No uniform glowing ring, no drop shadow, no embossed edge, no radiating beams, no new props, no UI/text. Keep the lake reflection and composition unchanged. Output the full artwork, not an isolated moon.
```

## 预览中的后续处理

- 月相通过原画纹理与 Canvas 透明遮罩合成，不另画实色圆盘。
- 新模式仅羽化外缘约 2–4 个显示像素，保留月面细节。
- “薄雾融月”在下缘叠加来自同一背景的局部薄云，幅度很小的慢速位移；月亮不浮动、不缩放。
- 悬停/键盘聚焦用云缘与水面局部光感响应，具体任务数仍通过原生提示浮层展示。
- 水面叠加光感随月相轻微变化，不宣称物理真实模拟。
