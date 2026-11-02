namespace CampusMobileLogin;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? testOutput = args.Length == 2 && args[0] == "--self-test" ? args[1] : null;
        Application.Run(new LoginForm(testOutput));
    }
}
