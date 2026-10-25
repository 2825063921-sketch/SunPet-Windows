# 太对了桌宠 · Windows

基于 [Ganzhe2028/SunPet](https://github.com/Ganzhe2028/SunPet) 的非官方 Windows 复刻，使用原头像和全部 28 条原声。点击说话、拖动换位置，三个角色是各自独立的程序。

## 下载和安装

需要 Windows 10/11 x64、.NET Framework 4.8 和可用的音频输出设备。成品无需 Python、Node.js、API Key 或管理员权限。

1. 从 [Releases](https://github.com/2825063921-sketch/SunPet-Windows/releases/latest) 下载 ZIP 并**完整解压**。
2. 双击 SunPet.exe。Assets 文件夹必须与程序保存在一起。
3. 如需桌面和开始菜单快捷方式，在解压目录运行 install.ps1；或在 PowerShell 中运行：

~~~powershell
& .\install.ps1 -Launch
~~~

安装到当前用户的 %LOCALAPPDATA%\Programs\SunPet-Windows。不会修改开机启动项或系统安全设置。程序未购买代码签名；若系统策略阻止运行，请遵循自己的设备策略。也可只使用解压目录。

## 操作

- 左键点击：随机播放一句；同一轮不重复，加载/播放期间的重复点击不排队、不叠音。
- 按住人物拖动：改变位置，松开后保存，拖动不触发语音。
- 右键菜单：挑一句、固定/随机、启用语音、音量、大小、联动开关、停止、移回默认位置、退出。
- 音量默认 65%。薄肌默认启用8句，右键可开启其余29句；SunPet28句、好事11句默认全开。
- 位置、音量、大小、固定句、启用语音与联动开关保存在 %LOCALAPPDATA%\MemePetsWindows\sun\settings.json。

三个程序在同一个 Windows 用户会话运行时：

~~~text
点击薄肌 → 薄肌 → 太对了 → 好事 → 太对了 → 自动停止
点击好事 → 好事 → 太对了 → 自动停止
点击太对了 → 只播放自己
~~~

没有运行的同伴会被跳过并终止该链；忙碌的同伴不会插话。Windows 版在前一句实际播放结束后接话，节奏与 macOS 原版略有区别。停止、关闭联动或退出会取消本角色后续回应。与 macOS App 之间不互通；没有机身加速度计摇一摇功能。程序只读本地素材，通过同一 Windows 会话内的消息接话，运行时不联网。

## 从源码构建和检查

构建使用 Windows 自带的 C# / WPF 编译器，不需要 Visual Studio、.NET SDK 或第三方包。在仓库根目录运行：

~~~powershell
& .\tests\Test-Core.ps1
& .\tests\verify-assets.ps1
& .\build.ps1 -Package
~~~

输出为 dist\SunPet-Windows\SunPet.exe；ZIP 和 SHA256 在 outputs\。逐条验证真实音频解码与播放结束（测试静音，不修改偏好）：

~~~powershell
& .\dist\SunPet-Windows\SunPet.exe --self-test "$PWD\docs\validation\native-playback.json" | Out-Null
~~~

仅开发诊断时可追加 --diagnostics，将事件写入上述设置目录的 diagnostics.jsonl。默认不记录诊断日志。诊断模式会临时显示任务栏入口，方便测试工具定位窗口。构建工作流检查源码、素材与编译；真实音频和桌面交互测试在本机进行，结果见 docs/validation/。

卸载时先右键退出，再移除安装目录及两个快捷方式；如需清除偏好，再移除对应的设置目录。升级前也请先退出同一角色。

## 来源与许可

代码 MIT；头像和原声属于第三方，**不在 MIT 授权范围内**。使用或再分发前请阅读 [NOTICE.md](NOTICE.md)。来源版本与素材哈希见 [source-manifest.json](source-manifest.json)。