using Checkers.Engine.Protocol;
using Xunit;

namespace Checkers.Tests;

public class ProtocolTests
{
    [Fact]
    public void Requests_round_trip_as_single_lines_of_camel_case_json()
    {
        var request = new HostRequest(7, HostMessageTypes.Search, Search: new SearchRequest(250, 12));

        string json = ProtocolJson.Serialize(request);

        Assert.DoesNotContain('\n', json);
        Assert.Contains("\"moveTimeMs\":250", json);
        Assert.DoesNotContain("position", json);   // nulls are left out
        Assert.Equal(request, ProtocolJson.Deserialize<HostRequest>(json));
    }

    [Fact]
    public void Responses_carry_the_search_result()
    {
        var answer = new SearchResponse("22-18", new[] { "22-18", "5-9" }, -14, 12, 153201, false);
        var response = new HostResponse(3, true, Search: answer);

        var back = ProtocolJson.Deserialize<HostResponse>(ProtocolJson.Serialize(response));

        Assert.NotNull(back);
        Assert.True(back!.Ok);
        Assert.Equal("22-18", back.Search!.BestMove);
        Assert.Equal(new[] { "22-18", "5-9" }, back.Search.Pv);
        Assert.Equal(153201, back.Search.Nodes);
    }

    [Fact]
    public void The_ready_message_has_id_zero_and_engine_info()
    {
        var ready = new HostResponse(0, true, Info: new HostInfo("Kingsrow-English", "Using the Chinook 8-piece WLD endgame database", 8, "board[x][y], White at y=0"));

        var back = ProtocolJson.Deserialize<HostResponse>(ProtocolJson.Serialize(ready));

        Assert.Equal(0, back!.Id);
        Assert.Equal(8, back.Info!.DatabasePieces);
    }
}
