<div align="center">

**简体中文** | [繁體中文](README.zh-TW.md) | [English](README.en.md) | [日本語](README.ja.md) | [Türkçe](README.tr.md) | [Español](README.es.md) | [Čeština](README.cs.md) | [Português (Brasil)](README.pt-BR.md)

![Banner](docs/images/DiscImageStudio_Banner_2.png)

<h2>全能的光盘盘面可见图像生成与刻录工具</h2>

Disc Image Studio 是一个多平台的光盘盘面图像生成与刻录工具，可在兼容的 DVD、CD 和单层蓝光盘上刻出肉眼可见的图像。

![.NET 版本](https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square)
![运行平台](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-0078d6?style=flat-square)
![开发语言](https://img.shields.io/badge/Language-C%23-68217A?style=flat-square)
![GitHub Repo size](https://img.shields.io/github/repo-size/JiaFeiMiao-K-Cat/DiscImageStudio?style=flat-square&color=3cb371)
![贡献欢迎](https://img.shields.io/badge/PRs-Welcome-brightgreen?style=flat-square)
[![许可证：GPL-3.0-or-later](https://img.shields.io/badge/License-GPL--3.0--or--later-blue?style=flat-square)](LICENSE)

</div>

---

## 关于本分支

本仓库 [MissPinkElfLady/DiscImageStudio-Chroma](https://github.com/MissPinkElfLady/DiscImageStudio-Chroma) 是 MissPinkElfLady 基于 JiaFeiMiao-K-Cat 及贡献者的 [Disc Image Studio](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio) 建立的分支，新增 **DVD-R 衍射光栅图像**，包括**彩色图像**；其余部分均为上游 Disc Image Studio。本分支与上游项目及下文致谢的项目作者无隶属或背书关系。

**下载**：本分支可直接运行的构建见其 [Releases 页面](https://github.com/MissPinkElfLady/DiscImageStudio-Chroma/releases)：Windows x64 选 `win-x64.exe`，Windows 11 ARM64 选 `win-arm64.exe`，Linux 与 macOS 另有实验性 CLI 压缩包。

### 本分支新增的功能

- **衍射图像引擎**（`diffract` 命令，`src/DvdImageSolver/Diffraction`）：写入多种周期的光栅，从不同方向看到不同的图：单张图、随倾斜切换的两帧图和立体图。输出与 DiscDiffractStudio 逐字节一致（见下文）。
- **彩色图像**（`"effect": "color"`）：一张彩色图，红、绿、蓝分别写在三条 91 字节载波串上（16T、13.93T、12.13T），含逐格周期补偿与白平衡；`"copies": 2` 在第二个倾角再写一份。
- **整盘彩色**（`"coverage": "wide"`）：一张彩色图写满整张光盘。一盏灯下显示为过中心的两个相对扇形，移动灯光或转动光盘即可扫过并看到其余部分（一个灯光位置无法照亮整圈螺旋，原因见 [docs/DIFFRACTION.md](docs/DIFFRACTION.md)）。
- **应用内（DVD 工程）**：
  - 设计页（色调面板上方）与导出页新增 **灰度｜彩色** 切换，随工程保存。
  - 选择**彩色**时，导出与刻录把设计写成一张整盘彩色图，位置与灰度图相同。
  - 色调面板隐藏抖动、亮度与 sRGB 线性化，色调预览显示彩色。
  - 实时预览的设计投影显示写入的颜色。
  - **输出读回直接从 ISO 字节解码彩色图**，并有自己的灰度｜彩色切换，按应用写在 ISO 旁的 `.iso.json` sidecar 自动识别。
- **命令行预览**：`diffract --preview disc.png` 绘制布局；`--preview-light 角度` 模拟灯在该钟点方向时的效果。

限制与已知问题：仅在空白单层 DVD-R 上试过；彩色光盘暂不能加入文件；多帧动画尚未移植；在色轮等合成测试图上可能出现淡淡的弯曲径向线，实际图片中未见。

详情见 [docs/DIFFRACTION.md](docs/DIFFRACTION.md) 与 [CHANGELOG.md](CHANGELOG.md)。

### 参考与移植的代码

- **[Disc Image Studio](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio)**（GPL-3.0-or-later）：本分支的基础。新增功能建立在其 EFMPlus 表、扰码器、ECC 编码器、色散写入器、标定几何与 WPF 应用之上，并在原处扩展。
- **[DiscDiffractStudio](https://github.com/Neo-626/DiscDiffractStudio)**，作者 Neo-626（GPL-3.0-or-later）：衍射引擎是其 Python 引擎的 C# 移植，包括 `dvdgrating/carriers.py`（单字节载波、桥接字节）、`dvdgrating/strings.py`（载波串）、`dvdgrating/viewing.py`（观察模型与白平衡）、`multiview.Picture`、`DiscLayout`、`BlockMapper`（逐圈分配与布局）及其彩色两帧切换。其载波串表 `data/string_carriers.json` 以 `src/DvdImageSolver/Diffraction/string_carriers.json.gz` 嵌入。`tools/DiffractionParity` 中的脚本从 DiscDiffractStudio 源码生成一致性测试向量。各移植文件均注明来源。
- **[Disc Color Studio](https://github.com/Neo-626/DiscColorStudio)**，作者 Neo-626（GPL-3.0）：仅作背景参考（实测颜色表，以及大幅彩色图为何在某些角度被截断），未复制任何代码或数据。
- **[CDImage](https://github.com/arduinocelentano/cdimage)**，作者 arduinocelentano（GPL-3.0-or-later）：上游已就 CD 交织代码致谢，本分支未改动该部分。

### AI 生成说明

本分支新增的全部内容——代码、测试、一致性脚本、文档以及本节——均由 AI（Anthropic 的 Claude，通过 Claude Code）编写。MissPinkElfLady 负责指导、测试构建并实际刻录光盘；代码并非手写。以这种方式完成的提交带有 `Co-Authored-By: Claude` 标注。上游 Disc Image Studio 与 DiscDiffractStudio 的成果归其各自作者（DiscDiffractStudio 的 README 说明其本身完全由 AI 创建）。

### 许可证

本分支沿用上游的 [GNU GPL v3.0 或更高版本](LICENSE)。移植的 DiscDiffractStudio 代码与数据采用 GPL-3.0-or-later，与之兼容；未从 Disc Color Studio 取用任何内容。分发二进制或修改版时须提供对应源代码并保留版权与许可声明，详见 [LICENSE-NOTICE.md](LICENSE-NOTICE.md)。

---

## 效果预览

<p align="center">
  <img src="docs/images/case1.jpg" alt="盘面效果示例" height="300">
  <a href="https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio/issues/61"><img src="docs/images/case-community-61.jpg" alt="社区提供的盘面效果示例（Issue #61）" height="300"></a>
  <img src="docs/images/case2.jpg" alt="盘面效果示例" height="300">
</p>

---

## 概述

Disc Image Studio 可将图片转换为适合 CD、DVD 与单层蓝光绘图的数据。

## 界面预览

![主页：模板与最近工程](docs/images/ui-home.png)

![设计页：图层画布与影调](docs/images/ui-design.png)

![导出页：浅色主题与输出镜像](docs/images/ui-export.png)

---

## 使用 Disc Image Studio

本分支只改动了[关于本分支](#关于本分支)所列内容。快速开始、功能介绍、标定教程、构建、命令行、源码结构与参考标准，请参阅原项目的文档：

**[JiaFeiMiao-K-Cat/DiscImageStudio](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio)**：[README](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio#readme)、[文档](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio/tree/main/docs)、[DVD 标定实操教程](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio/blob/main/docs/CALIBRATION_TUTORIAL.md)。

本仓库中的这些文档与分支建立时的上游版本一致；衍射与彩色功能见 [docs/DIFFRACTION.md](docs/DIFFRACTION.md)。

## 系统与设备要求

- x64 桌面版需要 Windows 10 2004（2004/20H1，19041）及以上，或 Windows 11；ARM64 桌面版需要 Windows 11。ARM64 的原生 CI 覆盖软件自检与 GUI 截图，尚未验证实盘刻录。
- 构建：.NET 10 SDK（`global.json` 锁定 10.0.x）；自行构建的非自包含 GUI 需要 .NET 10 Windows 桌面运行时。Release EXE 已包含运行时，无需另行安装 .NET。
- “即时刻录”需要 Windows 可识别的刻录机和兼容盘片。BD-R 需要支持顺序写入的单层空白盘。BD-RE 需要可验证的无备用区布局，首次格式化依赖光驱的 RENoSA 支持。

## 刻录与介质安全须知

- Disc Image Studio 不安装驱动或服务；“即时刻录”功能使用了 Windows 自带的 IMAPI2，拒绝非空白介质。
- “即时刻录”功能已有部分实盘反馈，对不同设备、介质与刻录入口的验证覆盖仍有限，属于实验性功能，请只使用可报废的测试介质。刻录中断、断电或生成速度不足仍可能使盘片报废！
- 实际可见效果取决于盘片、刻录机、固件与写入策略。
- 混合文件夹会在开始刻录前扫描目录并固定布局；期间源文件大小改变会安全终止任务，已开始写入的盘片仍可能报废。
- BD-R 顺序写入不提供备用区重映射；中断后同一盘片通常不能继续写入。

## 隐私与安全

- Disc Image Studio 除您在关于页手动检查更新时访问 GitHub Releases 外，不联网、不上传文件、不收集遥测，详见[隐私政策](PRIVACY.md)。
- 发现安全问题请不要在公开 Issue 中披露，按[安全政策](SECURITY.md)使用 GitHub 私密报告。

## 贡献

欢迎报告问题与提交代码。开发环境搭建、模块边界规则、测试要求与 Pull Request 流程见[贡献指南](CONTRIBUTING.md)。

## 许可证

项目采用 [GNU GPL v3.0 or later](LICENSE)（`GPL-3.0-or-later`）。CD 交织实现包含从 CDImage 移植并修改的代码，整体分发沿用兼容的 GPL 授权；分发二进制或修改版时须遵守对应源代码等要求。本分支新增的衍射引擎移植自 DiscDiffractStudio（GPL-3.0-or-later），见[关于本分支](#关于本分支)。第三方字体与组件遵循各自许可证，详见 [授权说明](LICENSE-NOTICE.md) 和 [第三方声明](THIRD-PARTY-NOTICES.txt)；应用"关于"页也可离线查看完整文本。

## 致谢

感谢 [arduinocelentano 的 CDImage 项目](https://github.com/arduinocelentano/cdimage)，它展示了通过生成 CD 音轨在盘面形成可见图像的方法。本项目的 CD 交织实现由其相关代码移植并继续修改，CD 盘面绘图思路也受其启发。CDImage 原作者及贡献者的工作为本项目提供了重要基础。

本分支还感谢 Neo-626 的 [DiscDiffractStudio](https://github.com/Neo-626/DiscDiffractStudio)（衍射与彩色图像引擎即移植自它）以及 [Disc Color Studio](https://github.com/Neo-626/DiscColorStudio)（为彩色工作提供了参考）。
