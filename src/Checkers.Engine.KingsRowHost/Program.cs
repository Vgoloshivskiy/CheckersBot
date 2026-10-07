using System.Text;
using Checkers.Engine.Protocol;

namespace Checkers.Engine.KingsRowHost;

/// <summary>
/// Hosts KingsRow in its own process. The API starts one of these per worker and talks to it over standard input
/// and output: one JSON object per line, requests in, replies out. The first line out is the "ready" message.
/// Standard error carries diagnostics only.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Standard output belongs to the protocol. Anything else that prints is sent to standard error instead.
        var protocolOut = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        Console.SetOut(Console.Error);
        var channel = new HostChannel(protocolOut);

        HostSession session;
        try
        {
            var options = HostOptions.Parse(args);
            HostSettings.Apply(options);
            session = await HostSession.StartAsync(options, channel);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"fatal: {ex}");
            await channel.SendAsync(new HostResponse(0, false, $"{ex.GetType().Name}: {ex.Message}"));
            return 2;
        }

        await channel.SendAsync(new HostResponse(0, true, Info: session.Info));

        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        string? line;
        while ((line = await input.ReadLineAsync()) is not null)
        {
            if (line.Length == 0) continue;

            HostRequest? request;
            try
            {
                request = ProtocolJson.Deserialize<HostRequest>(line);
            }
            catch (System.Text.Json.JsonException ex)
            {
                await channel.SendAsync(new HostResponse(0, false, $"Bad request: {ex.Message}"));
                continue;
            }

            if (request is null) continue;

            if (request.Type == HostMessageTypes.Stop)
            {
                session.Stop();
                continue;
            }

            // Run it on its own task so this loop keeps reading and can pass on a stop message while it works.
            HostRequest work = request;
            _ = Task.Run(() => session.HandleAsync(work));
        }

        // The API closed our input: stop whatever is running and exit.
        session.Stop();
        return 0;
    }
}
