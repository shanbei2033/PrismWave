using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.ViewModels.Settings;

public sealed record LocalizedOnlineQualityOption(
    OnlineQualityPreference Value,
    string DisplayName);

public sealed class SettingsText
{
    private readonly string _language;

    private SettingsText(string language)
    {
        _language = language;
    }

    public static SettingsText For(string language) => new(language);

    public string SettingsTitle => T("设置", "設定", "Settings");
    public string BasicTab => T("基本", "基本", "Basic");
    public string OnlineTab => T("在线", "線上", "Online");
    public string PlaybackTab => T("播放", "播放", "Playback");
    public string DeveloperTab => T("开发者", "開發者", "Developer");
    public string MusicFolders => T("音乐文件夹", "音樂資料夾", "Music folders");
    public string AddFolder => T("添加文件夹", "新增資料夾", "Add folder");
    public string Rescan => T("重新扫描", "重新掃描", "Rescan");
    public string LocalLibrary => T("本地曲库", "本機音樂庫", "Local library");
    public string LanguageAndTheme => T("语言与主题", "語言與主題", "Language and theme");
    public string Language => T("语言", "語言", "Language");
    public string LowEffects => T("低特效模式", "低特效模式", "Low effects");
    public string On => T("开", "開", "On");
    public string Off => T("关", "關", "Off");
    public string ThemeName => T("Fluent 深色", "Fluent 深色", "Fluent Dark");
    public string PreferenceMigration => T("Flutter 设置迁移", "Flutter 設定移轉", "Flutter preference migration");
    public string StreamingAccounts => T("流媒体账号", "串流媒體帳號", "Streaming accounts");
    public string StreamingAccountsDescription => T(
        "使用网易云音乐或 QQ 音乐扫码登录，以使用账号已有的播放权限。",
        "使用網易雲音樂或 QQ 音樂掃碼登入，以使用帳號已有的播放權限。",
        "Scan with NetEase Cloud Music or QQ Music to use the playback rights available to your account.");
    public string ScanLogin => T("扫码登录", "掃碼登入", "Scan login");
    public string SignOut => T("退出登录", "登出", "Sign out");
    public string BetaOnlineMode => T("BETA / 在线模式", "BETA / 線上模式", "BETA / Online mode");
    public string ExperimentalFeatures => T("实验功能", "實驗功能", "Experimental features");
    public string OnlineMode => T("在线模式", "線上模式", "Online mode");
    public string PreferredStreamingQuality => T("首选流媒体音质", "偏好串流音質", "Preferred streaming quality");
    public string ThirdPartyProviders => T("第三方音源", "第三方音源", "Third-party providers");
    public string ThirdPartyProvidersMessage => T(
        "在线音源属于实验功能，提供商策略或接口变化可能导致暂时不可用。",
        "線上音源屬於實驗功能，提供商策略或介面變化可能導致暫時無法使用。",
        "Online sources are experimental and can become unavailable when provider policies or endpoints change.");
    public string AudioOutput => T("音频输出", "音訊輸出", "Audio output");
    public string OutputMode => T("输出模式", "輸出模式", "Output mode");
    public string ActiveOutput => T("当前输出", "目前輸出", "Active output");
    public string OutputDeviceId => T("输出设备 ID", "輸出裝置 ID", "Output device id");
    public string WindowsDsdDevice => T("Windows DSD / ASIO 设备", "Windows DSD / ASIO 裝置", "Windows DSD / ASIO device");
    public string FadeInOut => T("淡入 / 淡出", "淡入 / 淡出", "Fade in / out");
    public string FadeDuration => T("淡化时长", "淡化時間", "Fade duration");
    public string DeveloperLogs => T("开发者日志", "開發者日誌", "Developer logs");
    public string Open => T("打开", "開啟", "Open");
    public string Entries => T("条记录", "筆記錄", "entries");
    public string Lossless => T("无损", "無損", "Lossless");
    public string HighQuality => T("高品质", "高音質", "High quality");
    public string StandardQuality => T("标准", "標準", "Standard");
    public string MpvName => T("MPV（自动）", "MPV（自動）", "MPV (automatic)");
    public string MpvDescription => T("由 MPV 自动选择可用的音频输出。", "由 MPV 自動選擇可用的音訊輸出。", "Let MPV select an available audio output automatically.");
    public string WasapiSharedName => T("WASAPI 共享", "WASAPI 共用", "WASAPI shared");
    public string WasapiSharedDescription => T("默认模式，可与其他应用同时播放。", "預設模式，可與其他應用程式同時播放。", "Default mode; other applications can play audio at the same time.");
    public string WasapiExclusiveName => T("WASAPI 独占", "WASAPI 獨佔", "WASAPI exclusive");
    public string WasapiExclusiveDescription => T("独占设备；失败后依次回退到共享和 MPV。", "獨佔裝置；失敗後依序回退到共用和 MPV。", "Use the device exclusively, then fall back to shared mode and MPV if needed.");

    private string T(string simplified, string traditional, string english) => _language switch
    {
        "zh-TW" => traditional,
        "en-US" => english,
        _ => simplified
    };
}
