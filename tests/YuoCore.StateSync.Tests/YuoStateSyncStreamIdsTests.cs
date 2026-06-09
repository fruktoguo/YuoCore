namespace YuoCore.StateSync.Tests;

public sealed class YuoStateSyncStreamIdsTests
{
    [Fact]
    public void For_player_returns_stable_compact_guid_stream_id()
    {
        var playerId = Guid.Parse("4e58d475-32b1-4f8d-81a5-62f23ca4f401");

        var streamId = YuoStateSyncStreamIds.ForPlayer(playerId);

        Assert.Equal("player:4e58d47532b14f8d81a562f23ca4f401", streamId);
    }

    [Fact]
    public void For_map_and_aoi_return_explicit_scope_stream_ids()
    {
        Assert.Equal("map:1001", YuoStateSyncStreamIds.ForMap(1001));
        Assert.Equal("map:1001:aoi:cell_12-18", YuoStateSyncStreamIds.ForMapAoi(1001, " cell_12-18 "));
    }

    [Fact]
    public void For_interest_group_returns_generic_interest_scope()
    {
        var streamId = YuoStateSyncStreamIds.ForInterestGroup("team", "raid.alpha-1");

        Assert.Equal("interest:team:raid.alpha-1", streamId);
    }

    [Fact]
    public void Matches_or_empty_allows_legacy_empty_request_and_rejects_wrong_stream()
    {
        const string expected = "map:1001:aoi:cell_1";

        Assert.True(YuoStateSyncStreamIds.MatchesOrEmpty(expected, null));
        Assert.True(YuoStateSyncStreamIds.MatchesOrEmpty(expected, ""));
        Assert.True(YuoStateSyncStreamIds.MatchesOrEmpty(expected, "map:1001:aoi:cell_1"));
        Assert.False(YuoStateSyncStreamIds.MatchesOrEmpty(expected, "map:1001:aoi:cell_2"));
    }

    [Fact]
    public void Try_parse_map_aoi_extracts_map_and_aoi_id()
    {
        Assert.True(YuoStateSyncStreamIds.TryParseMapAoi(
            "map:1001:aoi:default.cell_1_2",
            out var mapId,
            out var aoiId));
        Assert.Equal(1001, mapId);
        Assert.Equal("default.cell_1_2", aoiId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("player:4e58d47532b14f8d81a562f23ca4f401")]
    [InlineData("map:1001")]
    [InlineData("map:-1:aoi:cell")]
    [InlineData("map:1001:interest:cell")]
    public void Try_parse_map_aoi_rejects_non_aoi_stream_ids(string value)
    {
        Assert.False(YuoStateSyncStreamIds.TryParseMapAoi(value, out var mapId, out var aoiId));
        Assert.Equal(0, mapId);
        Assert.Equal("", aoiId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("cell:1")]
    [InlineData("cell/1")]
    public void Aoi_segment_rejects_empty_or_ambiguous_delimiters(string value)
    {
        Assert.Throws<ArgumentException>(() => YuoStateSyncStreamIds.ForMapAoi(1001, value));
    }

    [Fact]
    public void Negative_map_id_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => YuoStateSyncStreamIds.ForMap(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => YuoStateSyncStreamIds.ForMapAoi(-1, "cell"));
    }
}
