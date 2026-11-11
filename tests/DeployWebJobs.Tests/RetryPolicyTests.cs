using DeployWebJobs.Core.Deploy;

namespace DeployWebJobs.Tests;

public class RetryPolicyTests
{
    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task Reintenta_errores_pasajeros_hasta_que_funciona()
    {
        var calls = 0;
        var retries = 0;

        await RetryPolicy.ExecuteAsync(
            (attempt, _) =>
            {
                calls++;
                if (attempt < 3) throw new DeployException("red caída", isTransient: true);
                return Task.CompletedTask;
            },
            maxAttempts: 4, baseDelay: TimeSpan.FromSeconds(1),
            onRetry: (_, _, _) => retries++, CancellationToken.None, NoDelay);

        Assert.Equal(3, calls);
        Assert.Equal(2, retries);
    }

    [Fact]
    public async Task No_reintenta_errores_definitivos()
    {
        var calls = 0;

        await Assert.ThrowsAsync<DeployException>(() => RetryPolicy.ExecuteAsync(
            (_, _) => { calls++; throw new DeployException("credenciales", isTransient: false); },
            maxAttempts: 5, baseDelay: TimeSpan.Zero, onRetry: null, CancellationToken.None, NoDelay));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Se_rinde_al_agotar_los_intentos()
    {
        var calls = 0;

        await Assert.ThrowsAsync<DeployException>(() => RetryPolicy.ExecuteAsync(
            (_, _) => { calls++; throw new DeployException("siempre falla", isTransient: true); },
            maxAttempts: 3, baseDelay: TimeSpan.Zero, onRetry: null, CancellationToken.None, NoDelay));

        Assert.Equal(3, calls);
    }

    [Fact]
    public void La_espera_se_duplica_y_tiene_tope()
    {
        var baseDelay = TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.FromSeconds(2), RetryPolicy.ComputeDelay(baseDelay, 1));
        Assert.Equal(TimeSpan.FromSeconds(4), RetryPolicy.ComputeDelay(baseDelay, 2));
        Assert.Equal(TimeSpan.FromSeconds(30), RetryPolicy.ComputeDelay(baseDelay, 10));
    }
}
