namespace DeployWebJobs.Core.Deploy;

/// <summary>Reintentos con espera exponencial ante errores pasajeros (red, timeouts, 5xx).</summary>
public static class RetryPolicy
{
    public static bool IsTransient(Exception ex) => ex switch
    {
        DeployException d => d.IsTransient,
        HttpRequestException => true,
        TimeoutException => true,
        IOException => true,
        OperationCanceledException => true, // solo llega acá si NO fue una cancelación del usuario (ver filtro en ExecuteAsync)
        _ => false
    };

    /// <param name="maxAttempts">Cantidad total de intentos (incluye el primero).</param>
    /// <param name="onRetry">(intento que falló, excepción, espera antes del próximo)</param>
    /// <param name="delayFunc">Solo para tests: reemplaza Task.Delay.</param>
    public static async Task ExecuteAsync(
        Func<int, CancellationToken, Task> action,
        int maxAttempts,
        TimeSpan baseDelay,
        Action<int, Exception, TimeSpan>? onRetry,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delayFunc = null)
    {
        if (maxAttempts < 1) maxAttempts = 1;

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await action(attempt, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts
                                       && !cancellationToken.IsCancellationRequested
                                       && IsTransient(ex))
            {
                var wait = ComputeDelay(baseDelay, attempt);
                onRetry?.Invoke(attempt, ex, wait);

                if (delayFunc is null) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                else await delayFunc(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static TimeSpan ComputeDelay(TimeSpan baseDelay, int attempt)
    {
        var seconds = baseDelay.TotalSeconds * Math.Pow(2, attempt - 1);
        return TimeSpan.FromSeconds(Math.Min(30, seconds));
    }
}
