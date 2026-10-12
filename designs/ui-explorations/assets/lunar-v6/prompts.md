# 夜航月景联动 · v6 素材与来源

## 范围

独立月景交互预览；未替换正式应用、未改其它三套主题。旧版图片与预览保留。

## 生成图片

使用内置 image_gen 的图片编辑模式，不使用 CLI/API fallback。

- 编辑目标：`../lunar-v2/night-sky.png`
- 生成素材：`night-ambient.png`，2172 × 724，无月、较弱的云缘照明和水面反射。
- 原始生成副本：`C:/Users/excel/.codex/generated_images/01a0d3d1-7e48-7b81-9038-acf7308f4abb/exec-ebd847b4-eba6-41fc-a7e7-9c84930e13b1.png`
- 内联预览只做 WebP 编码；未另行用脚本重绘或修饰生成图片。

### 实际提示词

```text
Use case: lighting-weather. Input image 1 is the EDIT TARGET. Create an ambient-only unlit background plate for the SAME animated moonlit landscape. Keep exactly the existing 3:1 composition, camera, mountains, trees, lake horizon, cloud shapes and their positions, fine painterly ink textures, blue-green palette, and empty dark left side. Change ONLY the lighting: remove the strong localized moon illumination from the upper-right sky and cloud edges, and suppress the bright vertical turquoise/silver reflection in the lake below it. The empty sky at approximately 76 percent of width and 36 percent of height should be smooth quiet deep teal, without a circular halo, black circular patch, moon, or stars. Clouds remain visible at low contrast under soft diffuse night-sky light, not outlined with bright rims. Lake keeps its ripples and gentle diffuse blue reflections but no bright moonlight column. Preserve readable mountain silhouettes and beautiful nuanced mist, do not crush everything into black. The scene should feel like the same painting before the moon rises; cinematic painterly atmosphere, not a different painting. No new objects, no moon, no text, no UI, no watermark. Output only the full landscape artwork.
```

## 可选真实月面材质

来源与署名：NASA's Scientific Visualization Studio，Ernie Wright；LRO/LROC 与 LOLA 数据团队。

- [CGI Moon Kit](https://svs.gsfc.nasa.gov/4720/)
- `lroc-color-2k.jpg`：[2025 颜色贴图](https://svs.gsfc.nasa.gov/vis/a000000/a004700/a004720/lroc_color_2k.jpg)，原件 2048 × 1024，预览编码为 1024 宽。
- `lola-elevation-preview.jpg`：[8-bit 高程浏览图](https://svs.gsfc.nasa.gov/vis/a000000/a004700/a004720/ldem_3_8bit.jpg)。仅用于近似法线扰动，不用于科学测量。

限制：NASA CGI 颜色图本身经过美术优化。此预览还做了场景色调匹配；没有星历、真实日期/所在地、地形投射阴影或大气散射求解，不称为天文级仿真。

## 合成方式

- 背景不含固定月亮，月面独立绘制；可整体轻微视差，不会露出旧月亮。
- 环境明度随进度连续混合较暗底图与既有明亮无月背景。属于艺术指导的场景重照明，不是光线追踪。
- 保留“固定满月背景”对照，月面使用相同几何和材质，以隔离环境光是否协调的影响。
- 原画笔触沿用 `../lunar-v3/night-soft-full.png`；保持原始尺寸内联编码，未先降到 1440 宽。
- 月面暗侧不透明，理想球面受光面积跟随示例进度。悬停不改月相和进度。
- 前景薄云从无月背景采样，独立于月面作轻微反向位移。
- 全部相位，包括满月，都保留悬停/键盘聚焦反馈。数字只在提示信息中显示。
- 交互结束停止 Canvas RAF；显式演示只运行一次，页面隐藏后停止；减少动态效果/关闭动效时禁用位移和补间。

## 风格判断

纸月的低反差、共同纸张笔触与远山/雾层，玄石的冷灰材质、暗背景和独立景窗，都更容易容纳月相。玄石岩壁并未遮住月面，不以遮挡解释其协调感。
