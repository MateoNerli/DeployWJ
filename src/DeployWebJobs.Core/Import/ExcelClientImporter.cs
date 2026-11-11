using ClosedXML.Excel;
using DeployWebJobs.Core.Configuration;
using DeployWebJobs.Core.Models;
using DeployWebJobs.Core.Security;

namespace DeployWebJobs.Core.Import;

public sealed record ImportResult(
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Errors);

/// <summary>
/// Migración única desde el ConexionesFTP.xlsx de la versión anterior:
/// pasa los clientes a config.json (sin secretos) y las contraseñas al Administrador de credenciales.
/// Columnas esperadas (igual que antes): 1 Cliente, 2 Publicar, 3 Host FTP, 4 Usuario, 5 Contraseña.
/// </summary>
public static class ExcelClientImporter
{
    public static ImportResult Import(string xlsxPath, ConfigService config, ICredentialStore credentials, bool overwrite = false)
    {
        var imported = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();

        // FileShare.ReadWrite: permite leerlo aunque esté abierto en Excel.
        using var stream = new FileStream(xlsxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);

        foreach (var row in sheet.RowsUsed().Skip(1)) // saltea encabezados
        {
            var rowNumber = row.RowNumber();
            try
            {
                var name = row.Cell(1).GetString().Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var publish = row.Cell(2).GetString().Trim();
                var ftpHost = row.Cell(3).GetString().Trim();
                var user = row.Cell(4).GetString().Trim();
                var password = row.Cell(5).GetString();

                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
                {
                    errors.Add($"Fila {rowNumber} ({name}): falta usuario o contraseña.");
                    continue;
                }

                var id = ClientConfig.Slugify(name);
                if (config.FindClient(id) is not null && !overwrite)
                {
                    skipped.Add($"{name} (ya existía)");
                    continue;
                }

                var client = new ClientConfig
                {
                    Id = id,
                    Name = name,
                    Enabled = publish.Equals("SI", StringComparison.OrdinalIgnoreCase),
                    SiteName = ClientConfig.SiteNameFromUser(user),
                    FtpHost = string.IsNullOrEmpty(ftpHost) ? null : ftpHost
                };

                config.AddOrUpdateClient(client);
                credentials.Set(client.Id, new ClientCredential(user, password));
                imported.Add(name);
            }
            catch (Exception ex)
            {
                // Nunca se incluye la contraseña en el mensaje.
                errors.Add($"Fila {rowNumber}: {ex.Message}");
            }
        }

        return new ImportResult(imported, skipped, errors);
    }
}
