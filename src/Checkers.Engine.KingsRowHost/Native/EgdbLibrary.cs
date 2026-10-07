using System.Runtime.InteropServices;
using System.Text;

namespace Checkers.Engine.KingsRowHost.Native;

/// <summary>
/// egdb64.dll, the endgame database driver KingsRow uses. The host only asks it which database is in a folder and how many
/// pieces it covers, so the API knows when an endgame database can answer. The lookups themselves happen inside KingsRow.
/// Loading it first, by full path, also guarantees Kingsrow64.dll finds this copy when it imports egdb64.dll.
/// </summary>
internal sealed unsafe class EgdbLibrary : IDisposable
{
    private readonly nint _module;
    private readonly delegate* unmanaged[Cdecl]<byte*, int*, int*, int> _identify;

    private EgdbLibrary(nint module, nint identify)
    {
        _module = module;
        _identify = (delegate* unmanaged[Cdecl]<byte*, int*, int*, int>)identify;
    }

    /// <exception cref="FileNotFoundException">The DLL is not there.</exception>
    public static EgdbLibrary Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"egdb64.dll not found: {path}", path);
        }

        nint module = NativeLibrary.Load(path);
        try
        {
            return new EgdbLibrary(module, NativeLibrary.GetExport(module, "egdb_identify"));
        }
        catch
        {
            NativeLibrary.Free(module);
            throw;
        }
    }

    /// <summary>
    /// Checks whether <paramref name="directory"/> holds an endgame database (KingsRow, Cake or Chinook format).
    /// Returns false when it does not; otherwise the database type code and the largest piece count it covers.
    /// </summary>
    public bool TryIdentify(string directory, out int databaseType, out int maxPieces)
    {
        byte[] path = Encoding.UTF8.GetBytes(directory + '\0');
        int type = 0;
        int pieces = 0;
        int status;

        fixed (byte* pathPointer = path)
        {
            status = _identify(pathPointer, &type, &pieces);
        }

        databaseType = type;
        maxPieces = pieces;
        return status == 0;
    }

    public void Dispose() => NativeLibrary.Free(_module);
}
