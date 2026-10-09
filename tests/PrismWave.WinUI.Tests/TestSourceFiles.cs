namespace PrismWave_WinUI.Tests;

/// <summary>
/// 服务层源码在 Phase 0 迁入 PrismWave.Core；UI 层源码仍在 PrismWave.WinUI。
/// 结构测试读取源码时按两个工程依次解析，避免路径耦合。
/// </summary>
internal static class TestSourceFiles
{
    public static string Read(params string[] segments) => File.ReadAllText(Resolve(segments));

    public static string Resolve(params string[] segments)
    {
        // 兼容旧调用带 "src", "PrismWave.WinUI" 前缀的 segments
        if (segments.Length >= 2 && segments[0] == "src"
            && segments[1] is "PrismWave.WinUI" or "PrismWave.Core")
        {
            segments = segments[2..];
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var winUi = Path.Combine(
                new[] { directory.FullName, "src", "PrismWave.WinUI" }.Concat(segments).ToArray());
            if (File.Exists(winUi))
            {
                return winUi;
            }

            var core = Path.Combine(
                new[] { directory.FullName, "src", "PrismWave.Core" }.Concat(segments).ToArray());
            if (File.Exists(core))
            {
                return core;
            }

            // 仓库根级文件（native dll、解决方案文件等）
            var repoRoot = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(repoRoot))
            {
                return repoRoot;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository source file: " + string.Join("/", segments));
    }
}
