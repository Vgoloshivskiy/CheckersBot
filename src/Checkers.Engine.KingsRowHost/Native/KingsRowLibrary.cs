using System.Runtime.InteropServices;
using System.Text;

namespace Checkers.Engine.KingsRowHost.Native;

/// <summary>
/// Kingsrow64.dll, loaded and called through the CheckerBoard engine interface it exports:
/// <c>int getmove(int board[8][8], int color, double maxtime, char str[1024], int *playnow, int info, int moreinfo, CBmove *move)</c>
/// and <c>int enginecommand(char command[256], char reply[1024])</c>.
/// </summary>
internal sealed unsafe class KingsRowLibrary : IDisposable
{
    /// <summary>Size of the status text buffer. CheckerBoard's interface defines it as 1024 bytes.</summary>
    public const int TextSize = 1024;

    private const int CommandSize = 256;

    // CheckerBoard's CBmove struct is a few hundred bytes. For English checkers KingsRow does not need it,
    // but a valid, zeroed buffer is passed so it can never write through a null pointer.
    private const int MoveStructSize = 512;

    // getmove's "info" argument: bit 1 makes KingsRow stop exactly at the time limit instead of using
    // anywhere from half to three times the nominal time. The API's time limits depend on this.
    private const int ExactTime = 2;

    private readonly nint _module;
    private readonly delegate* unmanaged[Stdcall]<int*, int, double, byte*, int*, int, int, byte*, int> _getMove;
    private readonly delegate* unmanaged[Stdcall]<byte*, byte*, int> _engineCommand;

    private readonly byte[] _moveStruct = GC.AllocateArray<byte>(MoveStructSize, pinned: true);
    private readonly byte[] _commandBuffer = GC.AllocateArray<byte>(CommandSize, pinned: true);
    private readonly byte[] _replyBuffer = GC.AllocateArray<byte>(TextSize, pinned: true);
    private readonly object _commandGate = new();

    private KingsRowLibrary(nint module, nint getMove, nint engineCommand)
    {
        _module = module;
        _getMove = (delegate* unmanaged[Stdcall]<int*, int, double, byte*, int*, int, int, byte*, int>)getMove;
        _engineCommand = (delegate* unmanaged[Stdcall]<byte*, byte*, int>)engineCommand;
    }

    /// <exception cref="FileNotFoundException">The DLL is not there.</exception>
    /// <exception cref="DllNotFoundException">The DLL, or egdb64.dll that it imports, could not be loaded.</exception>
    public static KingsRowLibrary Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"KingsRow DLL not found: {path}", path);
        }

        nint module = NativeLibrary.Load(path);
        try
        {
            nint getMove = NativeLibrary.GetExport(module, "getmove");
            nint engineCommand = NativeLibrary.GetExport(module, "enginecommand");
            return new KingsRowLibrary(module, getMove, engineCommand);
        }
        catch
        {
            NativeLibrary.Free(module);
            throw;
        }
    }

    /// <summary>
    /// Sends an engine command such as "set book 0" or "name". Returns the engine's reply, or null when the
    /// engine did not recognize the command.
    /// </summary>
    public string? Command(string command)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(command);
        if (bytes.Length >= CommandSize)
        {
            throw new ArgumentException($"Engine commands are limited to {CommandSize - 1} bytes.", nameof(command));
        }

        lock (_commandGate)
        {
            Array.Clear(_commandBuffer);
            Array.Clear(_replyBuffer);
            bytes.CopyTo(_commandBuffer, 0);

            int handled;
            string reply;
            fixed (byte* commandPointer = _commandBuffer)
            fixed (byte* replyPointer = _replyBuffer)
            {
                handled = _engineCommand(commandPointer, replyPointer);
                reply = Marshal.PtrToStringAnsi((nint)replyPointer) ?? string.Empty;
            }

            return handled != 0 ? reply : null;
        }
    }

    /// <summary>
    /// Searches the position in <paramref name="cells"/> (8x8 ints, rewritten with the position after the engine's move)
    /// and writes its status line to <paramref name="status"/>. Setting <c>*playNow</c> to non-zero from another thread
    /// makes the search return its best move so far.
    /// </summary>
    public int GetMove(int* cells, int color, double seconds, byte* status, int* playNow)
    {
        fixed (byte* move = _moveStruct)
        {
            return _getMove(cells, color, seconds, status, playNow, ExactTime, 0, move);
        }
    }

    public void Dispose() => NativeLibrary.Free(_module);
}
