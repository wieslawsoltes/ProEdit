using Uno.UI.Hosting;

namespace ProEdit.Controls.Skia.Uno.Sample;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var builder = UnoPlatformHostBuilder
            .Create()
            .App(static () => new App());

#if HAS_UNO_SKIA_MACOS || __UNO_SKIA_MACOS__
        builder = builder.UseMacOS();
#endif

        builder.Build().Run();
    }
}
