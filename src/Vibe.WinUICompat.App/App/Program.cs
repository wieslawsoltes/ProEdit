using Uno.UI.Hosting;

namespace Vibe.WinUICompat.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        UnoPlatformHostBuilder
            .Create()
            .App(static () => new App())
            .UseMacOS()
            .Build()
            .Run();
    }
}
