# PrismWave v1.1.0 - 发布单 exe 安装包（自动依赖安装）

> 2026-08-21 · [下载链接](https://github.com/shanbei2033/PrismWave/releases/tag/v1.1.0)

---

## 🚀 新版本亮点

**🎉 一键安装向导上线！**
- **Setup.exe 替代便携版**: 用户不再需要手动解压缩，直接双击安装
- **自动检测依赖**: 启动时自动检查 .NET 10 Desktop Runtime，缺失时弹窗引导下载
- **Runtime 自安装**: Windows App Runtime 2.2 缺失时，向导内静默下载安装（约 40 MB）
- **体积大幅缩小**: 剔除 AI 组件后安装包仅 **41.7 MB**（原便携版 zip 120MB+，缩小 65%）
- **标准安装体验**: Program Files 目录、开始菜单快捷方式、控制面板卸载

> ⚠️ **重要更新**: 自 v1.1.0 版本起，运行 PrismWave 需要系统预装 **.NET 10 Desktop Runtime**。安装程序会自动检测并引导安装，无需用户手动操作。

---

## 📝 版本更新详情

### 新增功能

- **单 exe 安装包**: Inno Setup 中文向导，支持自动检测和安装运行依赖（.NET 10 + WinApp Runtime 2.2）
- **AI 组件剔除**: 移除不必要的 onnxruntime/DirectML 等 AI 相关库，安装包体积从 120MB 降至 41.7MB
- **Framework-Dependent 部署**: 采用 FDD 模式，减少运行时捆绑，保持安装包小巧

### 技术改进

- 统一 csproj Version/AssemblyVersion/FileVersion → 1.1.0
- setup.iss 加入.NET 和 WinAppRuntime 双路径枚举检测逻辑
- build_installer.ps1 完善打包流程与错误处理

---

## 🔍 SHA-256 校验

```
PrismWave-Setup-1.1.0.exe: <SHA256 将在上传后自动计算>
```

---

## ⚙️ 系统要求

| 依赖 | 要求 |
|------|------|
| 操作系统 | Windows 10 1809+ (x64) |
| .NET 运行时 | .NET 10 Desktop Runtime **(安装程序自动检测并提示安装)** |
| Windows App Runtime | Microsoft.WindowsAppRuntime.2.2 **(安装程序自动检测并静默安装)** |

> 💡 **提示**: 如果网络受限无法自动下载依赖，可访问以下页面手动安装：
> - [.NET 10 下载中心](https://dotnet.microsoft.com/download/dotnet/10.0)
> - [Windows App Runtime 2.2](https://aka.ms/windowsappsdk/2.2/latest)

---

## 📦 下载

[**v1.1.0 Setup** (41.7 MB)](https://github.com/shanbei2033/PrismWave/releases/download/v1.1.0/PrismWave-Setup-1.1.0.exe)

---

*感谢所有贡献者和测试用户！如有疑问请查看官方文档或提交 Issue。*
