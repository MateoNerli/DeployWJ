namespace DeployWebJobs.Core.Models;

/// <summary>Cómo se sube el paquete al App Service.</summary>
public enum DeployMethod
{
    /// <summary>Zip Deploy contra la API Kudu (HTTPS). Método recomendado.</summary>
    ZipDeploy,
    /// <summary>FTPS (FTP sobre TLS). Reservado: se implementa solo si hace falta.</summary>
    Ftps
}

/// <summary>Tipo de WebJob en App Service.</summary>
public enum WebJobType
{
    /// <summary>Corre siempre (carpeta app_data/jobs/continuous).</summary>
    Continuous,
    /// <summary>Se dispara a demanda o por cron (carpeta app_data/jobs/triggered).</summary>
    Triggered
}

/// <summary>
/// Qué se espera del WebJob en Azure. Sirve para evitar errores de nombre: si se dice que ya existe,
/// tiene que existir con ESE nombre; si se dice que es nuevo, no puede existir ya.
/// </summary>
public enum WebJobExistence
{
    /// <summary>No se verifica (comportamiento anterior).</summary>
    Any,
    /// <summary>Se está actualizando uno existente: si el nombre no coincide exacto, no se sube nada.</summary>
    MustExist,
    /// <summary>Es un WebJob nuevo: si ya existe uno con ese nombre, no se sube nada.</summary>
    MustBeNew
}

/// <summary>Pasos visibles, por cliente, durante un deploy.</summary>
public enum DeployStep
{
    Validando,
    PreparandoPaquete,
    Conectando,
    DeteniendoJob,
    Subiendo,
    IniciandoJob,
    Verificando,
    Finalizado
}

/// <summary>Resultado final de un cliente.</summary>
public enum DeployOutcome
{
    Ok,
    Error,
    Omitido,
    Cancelado,
    Simulado
}

public static class EnumTexts
{
    /// <summary>Texto en español para mostrar al usuario.</summary>
    public static string Describe(this DeployStep step) => step switch
    {
        DeployStep.Validando => "Validando configuración",
        DeployStep.PreparandoPaquete => "Preparando paquete",
        DeployStep.Conectando => "Conectando",
        DeployStep.DeteniendoJob => "Deteniendo WebJob",
        DeployStep.Subiendo => "Subiendo archivos",
        DeployStep.IniciandoJob => "Iniciando WebJob",
        DeployStep.Verificando => "Verificando",
        DeployStep.Finalizado => "Finalizado",
        _ => step.ToString()
    };

    public static string Describe(this DeployOutcome outcome) => outcome switch
    {
        DeployOutcome.Ok => "OK",
        DeployOutcome.Error => "Error",
        DeployOutcome.Omitido => "Omitido",
        DeployOutcome.Cancelado => "Cancelado",
        DeployOutcome.Simulado => "Simulado",
        _ => outcome.ToString()
    };

    public static string Describe(this WebJobExistence existence) => existence switch
    {
        WebJobExistence.MustExist => "Ya existe en Azure (actualizar)",
        WebJobExistence.MustBeNew => "Es nuevo",
        _ => "Sin verificar"
    };

    public static string Describe(this WebJobType type) => type switch
    {
        WebJobType.Continuous => "Continuo",
        WebJobType.Triggered => "Programado/Triggered",
        _ => type.ToString()
    };
}
