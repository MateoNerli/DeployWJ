using DeployWebJobs.Core.Packaging;

namespace DeployWebJobs.Tests;

public class WebJobNameGuesserTests
{
    [Theory]
    [InlineData(@"C:\Desarrollo\Webjob.CentralDeConsultaSql\bin\Release\net472\publish", "Webjob.CentralDeConsultaSql")]
    [InlineData(@"C:\x\WebJob.SGR.FacturacionComisiones\bin\Debug\net8.0\publish\", "WebJob.SGR.FacturacionComisiones")]
    [InlineData("/home/u/MiJob/BIN/Release/publish", "MiJob")]
    [InlineData(@"C:\publicaciones\ultima", null)]
    [InlineData(@"C:\bin\Release", null)]       // "bin" sin carpeta de proyecto válida antes
    [InlineData("", null)]
    public void Adivina_el_nombre_desde_la_carpeta(string folder, string? esperado) =>
        Assert.Equal(esperado, WebJobNameGuesser.Guess(folder));
}
