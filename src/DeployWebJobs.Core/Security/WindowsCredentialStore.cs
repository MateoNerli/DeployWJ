using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DeployWebJobs.Core.Security;

/// <summary>
/// Guarda las credenciales en el Administrador de credenciales de Windows (Credential Manager),
/// cifradas por el sistema y atadas al usuario de Windows actual. Se pueden ver en:
/// Panel de control > Administrador de credenciales > Credenciales de Windows > "DeployWebJobs/...".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const string Prefix = "DeployWebJobs/";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public ClientCredential? Get(string clientId)
    {
        if (!CredRead(Prefix + clientId, CredTypeGeneric, 0, out var ptr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new InvalidOperationException($"No se pudo leer la credencial de '{clientId}' (error de Windows {error}).");
        }

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            var password = cred.CredentialBlobSize > 0
                ? Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2)
                : "";
            return new ClientCredential(cred.UserName ?? "", password ?? "");
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public void Set(string clientId, ClientCredential credential)
    {
        var bytes = Encoding.Unicode.GetBytes(credential.Password);
        if (bytes.Length > 2560)
            throw new ArgumentException("La contraseña es demasiado larga para el Administrador de credenciales.");

        var blob = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            if (bytes.Length > 0) Marshal.Copy(bytes, 0, blob, bytes.Length);

            var cred = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = Prefix + clientId,
                UserName = credential.UserName,
                CredentialBlob = blob,
                CredentialBlobSize = (uint)bytes.Length,
                Persist = CredPersistLocalMachine
            };

            if (!CredWrite(ref cred, 0))
                throw new InvalidOperationException(
                    $"No se pudo guardar la credencial de '{clientId}' (error de Windows {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            // Limpia el buffer con la contraseña antes de liberarlo.
            for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
            Marshal.FreeHGlobal(blob);
            Array.Clear(bytes);
        }
    }

    public void Delete(string clientId)
    {
        if (!CredDelete(Prefix + clientId, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                throw new InvalidOperationException($"No se pudo borrar la credencial de '{clientId}' (error de Windows {error}).");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
