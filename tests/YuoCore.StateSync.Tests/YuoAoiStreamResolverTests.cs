namespace YuoCore.StateSync.Tests;

public sealed class YuoAoiStreamResolverTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Resolver_rejects_invalid_cell_size(float cellSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YuoGridAoiStreamResolver(cellSize));
    }

    [Fact]
    public void Resolve_stream_id_uses_map_phase_and_grid_cell()
    {
        var resolver = new YuoGridAoiStreamResolver(cellSize: 10);

        var streamId = resolver.ResolveStreamId(
            1001,
            new YuoAoiPosition(25, 0, 39),
            "normal");

        Assert.Equal("map:1001:aoi:normal.cell_2_3", streamId);
    }

    [Fact]
    public void Resolve_stream_id_handles_negative_coordinates()
    {
        var resolver = new YuoGridAoiStreamResolver(cellSize: 10);

        var cell = resolver.ResolveCell(1001, new YuoAoiPosition(-1, 0, -11));
        var streamId = resolver.ResolveStreamId(1001, new YuoAoiPosition(-1, 0, -11));

        Assert.Equal(new YuoAoiStreamCell(1001, -1, -2, "default"), cell);
        Assert.Equal("map:1001:aoi:default.cell_-1_-2", streamId);
    }

    [Fact]
    public void Resolve_stream_id_can_use_observer_phase()
    {
        var resolver = new YuoGridAoiStreamResolver(cellSize: 8);
        var observer = new YuoAoiObserver(
            1,
            new YuoAoiPosition(16, 0, 31),
            ViewRadius: 20,
            Phase: "dungeon-1");

        var streamId = resolver.ResolveStreamId(2002, observer);

        Assert.Equal("map:2002:aoi:dungeon-1.cell_2_3", streamId);
    }

    [Fact]
    public void Try_parse_stream_cell_restores_generated_stream_id()
    {
        Assert.True(YuoGridAoiStreamResolver.TryParseStreamCell(
            "map:1001:aoi:default.cell_-1_2",
            out var cell));

        Assert.Equal(new YuoAoiStreamCell(1001, -1, 2, "default"), cell);
    }

    [Theory]
    [InlineData("")]
    [InlineData("map:1001")]
    [InlineData("map:1001:aoi:default")]
    [InlineData("map:1001:aoi:default.cell_a_1")]
    [InlineData("player:4e58d47532b14f8d81a562f23ca4f401")]
    public void Try_parse_stream_cell_rejects_non_grid_stream_id(string streamId)
    {
        Assert.False(YuoGridAoiStreamResolver.TryParseStreamCell(streamId, out _));
    }

    [Fact]
    public void Resolve_stream_id_rejects_invalid_phase_segment()
    {
        var resolver = new YuoGridAoiStreamResolver(cellSize: 10);

        Assert.Throws<ArgumentException>(() =>
            resolver.ResolveStreamId(1001, new YuoAoiPosition(0, 0, 0), "phase:bad"));
    }

    [Fact]
    public void Resolve_cell_rejects_negative_map_id()
    {
        var resolver = new YuoGridAoiStreamResolver(cellSize: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.ResolveCell(-1, new YuoAoiPosition(0, 0, 0)));
    }
}
