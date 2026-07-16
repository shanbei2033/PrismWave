using System.Xml.Linq;
using Xunit;

namespace PrismWave_WinUI.Tests;

public sealed class SettingsOnlineAccountUiTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void OnlinePivot_ContainsQualitySelectorAndBothAccountCards()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "PrismWave.WinUI",
            "Views",
            "Settings",
            "SettingsPage.xaml"));

        Assert.Contains("OnlineQualityOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("OnlineQualityPreference", xaml, StringComparison.Ordinal);
        Assert.Contains("NeteaseAccount", xaml, StringComparison.Ordinal);
        Assert.Contains("QqAccount", xaml, StringComparison.Ordinal);
        Assert.Contains("ScanLogin_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("SignOut_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("Streaming accounts", xaml, StringComparison.Ordinal);
        Assert.True(
            xaml.IndexOf("Streaming accounts", StringComparison.Ordinal)
                < xaml.IndexOf("BETA / Online mode", StringComparison.Ordinal),
            "Account login must be the first section in the Online tab.");
        Assert.DoesNotContain(
            "Content=\"Scan login\" Click=\"ScanLogin_Click\" IsEnabled=\"{Binding OnlineAccounts.IsLoginEnabled}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordBox", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie", xaml, StringComparison.OrdinalIgnoreCase);

        var code = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "PrismWave.WinUI",
            "Views",
            "Settings",
            "SettingsPage.xaml.cs"));
        Assert.Contains("Loaded += SettingsPage_Loaded", code, StringComparison.Ordinal);
        Assert.Contains("RefreshAccountsAsync", code, StringComparison.Ordinal);
        Assert.Contains("_accountInitializationCancellation?.Cancel()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ReferencesPinnedQRCoderPackage()
    {
        var projectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "PrismWave.WinUI",
            "PrismWave.WinUI.csproj");
        var project = XDocument.Load(projectPath);
        var qrcode = project
            .Descendants("PackageReference")
            .SingleOrDefault(node => string.Equals(
                node.Attribute("Include")?.Value,
                "QRCoder",
                StringComparison.Ordinal));

        Assert.NotNull(qrcode);
        Assert.Equal("1.8.0", qrcode!.Attribute("Version")?.Value);
    }

    [Fact]
    public void LoginDialog_UsesCoordinatorAndNeverAcceptsSecrets()
    {
        var dialogDirectory = Path.Combine(
            RepositoryRoot,
            "src",
            "PrismWave.WinUI",
            "Views",
            "Dialogs");
        var xamlPath = Path.Combine(dialogDirectory, "OnlineLoginDialog.xaml");
        var codePath = Path.Combine(dialogDirectory, "OnlineLoginDialog.xaml.cs");

        Assert.True(File.Exists(xamlPath));
        Assert.True(File.Exists(codePath));
        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("OnlineLoginDialogCoordinator", code, StringComparison.Ordinal);
        Assert.Contains("QrImage", xaml, StringComparison.Ordinal);
        Assert.Contains("RefreshQr_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordBox", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TextBox", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Console.Write", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupLog", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AppServices_InjectsSharedOnlineAccountServiceIntoSettings()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "PrismWave.WinUI",
            "Infrastructure",
            "AppServices.cs"));

        Assert.Contains("new SettingsViewModel(settingsService, libraryFolders, playbackService, themeService, developerLogService, onlineAccountService)", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "new OnlineAccountService("));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "PrismWave.WinUI")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the PrismWave repository root.");
    }
}
