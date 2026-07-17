namespace PrismWave_WinUI.Infrastructure.Navigation;

public enum ShellNavigationKind
{
    Initial,
    Primary,
    Nested,
    Back
}

public readonly record struct ShellNavigationRequest(
    string Route,
    ShellNavigationKind Kind);
