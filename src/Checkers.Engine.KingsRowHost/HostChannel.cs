using Checkers.Engine.Protocol;

namespace Checkers.Engine.KingsRowHost;

/// <summary>Writes protocol messages to standard output, one JSON object per line. Safe to call from several threads.</summary>
internal sealed class HostChannel
{
    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HostChannel(TextWriter writer)
    {
        _writer = writer;
    }

    public async Task SendAsync(HostResponse response)
    {
        string line = ProtocolJson.Serialize(response);

        await _gate.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(line);
            await _writer.FlushAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}
