using System.Net;
using System.Net.Http.Headers;

namespace DeployWebJobs.Core.Deploy;

/// <summary>
/// Contenido HTTP que envía un archivo (el ZIP) en bloques e informa cuántos bytes lleva enviados.
/// Así se puede dibujar la barra de progreso de subida.
/// </summary>
internal sealed class ProgressFileContent : HttpContent
{
    private const int BufferSize = 81920;

    private readonly string _path;
    private readonly IProgress<long>? _progress;
    private readonly long _length;

    public ProgressFileContent(string path, IProgress<long>? progress)
    {
        _path = path;
        _progress = progress;
        _length = new FileInfo(path).Length;
        Headers.ContentType = new MediaTypeHeaderValue("application/zip");
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[BufferSize];
        long sent = 0;
        int read;
        while ((read = await file.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            sent += read;
            _progress?.Report(sent);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }
}
