# License notice

Disc Image Studio 采用 **GNU General Public License v3.0 or later（GPL-3.0-or-later）**，许可证全文见 [LICENSE](LICENSE)。本项目可按 GPL 第 3 版或任何后续版本使用、修改和分发；软件不提供担保。

Copyright (c) 2026 Disc Image Studio contributors。

CD 交织实现包含从 [CDImage](https://github.com/arduinocelentano/cdimage) 移植并修改的代码，其原作者版权为 Copyright (C) 2008-2022 arduinocelentano，上游采用 GPL 第 3 版或后续版本。因此本项目整体分发采用兼容的 GPL-3.0-or-later。相关文件保留来源与修改说明。

本分支（[MissPinkElfLady/DiscImageStudio-Chroma](https://github.com/MissPinkElfLady/DiscImageStudio-Chroma)）的 DVD 衍射图像引擎（`src/DvdImageSolver/Diffraction`、`tools/DiffractionParity`）包含从 [DiscDiffractStudio](https://github.com/Neo-626/DiscDiffractStudio)（作者 Neo-626）移植并修改的代码，以及其载波串数据表 `data/string_carriers.json`（以 `string_carriers.json.gz` 嵌入）。上游采用 GPL-3.0-or-later，与本项目授权兼容；各移植文件注明来源模块。[Disc Color Studio](https://github.com/Neo-626/DiscColorStudio)（GPL-3.0）仅作参考，未复制其代码或数据。本分支新增部分由 AI（Anthropic 的 Claude，通过 Claude Code）编写，详见 [README](README.md#关于本分支)。

This fork's DVD diffraction engine (`src/DvdImageSolver/Diffraction`, `tools/DiffractionParity`) contains code ported and modified from [DiscDiffractStudio](https://github.com/Neo-626/DiscDiffractStudio) by Neo-626, and its carrier-string table `data/string_carriers.json` (embedded as `string_carriers.json.gz`). That project is GPL-3.0-or-later, which is compatible with this project's licence, and each ported file names its source module. [Disc Color Studio](https://github.com/Neo-626/DiscColorStudio) (GPL-3.0) was consulted only; no code or data was copied from it. The fork's additions were written by AI (Anthropic's Claude, via Claude Code); see the [README](README.en.md#about-this-fork).

分发修改版或二进制程序时，应遵守 GPL 对版权声明、许可证、修改说明及对应源代码的要求。发布 EXE/MSIX 时需提供与该构建对应的完整源代码及构建说明；仅提供与二进制无关的最新开发源码不够。

第三方组件和独立资源继续适用其原有许可证：

- CommunityToolkit.Mvvm、MahApps.Metro.IconPacks 及其依赖：详见 [第三方声明](THIRD-PARTY-NOTICES.txt)。Lucide 图标另包含 ISC 和继承自 Feather 的 MIT 条款。
- 自包含 EXE/MSIX 携带 .NET 10 与 Windows 桌面运行时，继续适用其 MIT 许可及上游第三方条款，完整文本保留在同一份[第三方声明](THIRD-PARTY-NOTICES.txt)中。
- HarmonyOS Sans SC 字体：作为独立字体资源适用 [HarmonyOS Sans 字体许可协议](src/DiscImageStudio.App/Assets/Fonts/HarmonyOS-Sans-LICENSE.txt)，本项目不改变其授权。
- 引用的 ECMA / ISO 标准文档与用户自行导入的图片不因本项目授权而改变其权利归属。

应用“关于”页可离线查看项目许可证、第三方声明和字体协议；这些文本嵌入程序集，随单文件 EXE 和 MSIX 一起分发。重新分发时须保留适用的第三方声明。
