using ClosedXML.Excel;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Import;
using DeployWebJobs.Core.Security;

namespace DeployWebJobs.Tests;

public class ExcelImporterTests
{
    [Fact]
    public void Importa_clientes_guarda_credenciales_aparte_y_reporta_filas_malas()
    {
        using var dir = new TempDir();
        var xlsx = Path.Combine(dir.Path, "ConexionesFTP.xlsx");

        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Hoja1");
            ws.Cell(1, 1).Value = "Cliente"; ws.Cell(1, 2).Value = "Publicar"; ws.Cell(1, 3).Value = "Host FTP";
            ws.Cell(1, 4).Value = "Usuario"; ws.Cell(1, 5).Value = "Contraseña";

            ws.Cell(2, 1).Value = "Aval OC"; ws.Cell(2, 2).Value = "SI"; ws.Cell(2, 3).Value = "waws-prod-xx-000.ftp.azurewebsites.windows.net";
            ws.Cell(2, 4).Value = @"aval-oc\$aval-oc"; ws.Cell(2, 5).Value = "clave-uno";

            ws.Cell(3, 1).Value = "Sin clave"; ws.Cell(3, 2).Value = "SI"; ws.Cell(3, 4).Value = @"x\$x";

            wb.SaveAs(xlsx);
        }

        var creds = new InMemoryCredentialStore();
        var config = new ConfigService(Path.Combine(dir.Path, "config.json"), creds);

        var result = ExcelClientImporter.Import(xlsx, config, creds);

        Assert.Equal(new[] { "Aval OC" }, result.Imported.ToArray());
        Assert.Single(result.Errors);
        Assert.DoesNotContain("clave-uno", string.Join(" ", result.Errors));

        var client = Assert.Single(config.Clients);
        Assert.Equal("aval-oc", client.SiteName);
        Assert.Equal("clave-uno", creds.Get("aval-oc")!.Password);
        Assert.DoesNotContain("clave-uno", File.ReadAllText(config.FilePath));
    }
}
