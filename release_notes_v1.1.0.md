- 新增 Inno Setup 安装向导，替代便携版 zip 解压方式，安装至 Program Files 并自动创建开始菜单快捷方式
- 安装程序启动时自动检测 .NET 10 Desktop Runtime，缺失时弹窗引导下载安装
- 安装程序自动检测 Windows App Runtime 2.2，缺失时在向导内静默下载安装
- 剔除未使用的 AI 相关组件（onnxruntime / DirectML / Microsoft.ML.OnnxRuntime / Windows AI Platform），安装包体积从 120MB 降至约 42MB
- 部署模式改为 Framework-Dependent，不再捆绑运行时 DLL
- 新增一键打包脚本 `tools/build_installer.ps1` 和安装脚本 `tools/setup.iss`
- 自本版本起，运行 PrismWave 需要系统预装 .NET 10 Desktop Runtime（安装程序会自动检测并提示安装）

SHA-256: B69EB7878A5ADB74D6F312EDDC571A9A3DC5FFFC636B148CDA05F226076D3D8D
