using YuoCore.Ecs;

namespace YuoCore.StateSync.Tests;

public sealed class YuoAoiTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Grid_query_rejects_invalid_cell_size(float cellSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YuoGridAoiQuery(cellSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => YuoGridAoiTargetIndex.Create(cellSize, []));
    }

    [Fact]
    public void Grid_query_filters_by_radius_and_phase()
    {
        var query = new YuoGridAoiQuery(cellSize: 10);
        var observer = new YuoAoiObserver(
            1,
            new YuoAoiPosition(0, 0, 0),
            ViewRadius: 12,
            Phase: "normal");

        var visible = query.QueryVisible(
            observer,
            [
                new YuoAoiTarget(1, new YuoAoiPosition(0, 0, 0), "normal"),
                new YuoAoiTarget(2, new YuoAoiPosition(9, 0, 0), "normal"),
                new YuoAoiTarget(3, new YuoAoiPosition(20, 0, 0), "normal"),
                new YuoAoiTarget(4, new YuoAoiPosition(5, 0, 0), "stealth")
            ]);

        var entity = Assert.Single(visible);
        Assert.Equal(2, entity.NetworkId);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Grid_query_returns_empty_for_non_finite_view_radius(float viewRadius)
    {
        var query = new YuoGridAoiQuery(cellSize: 10);
        var visible = query.QueryVisible(
            new YuoAoiObserver(1, new YuoAoiPosition(0, 0, 0), viewRadius),
            [new YuoAoiTarget(2, new YuoAoiPosition(1, 0, 1))]);

        Assert.Empty(visible);
    }

    [Fact]
    public void Grid_query_returns_empty_for_non_positive_view_radius()
    {
        var query = new YuoGridAoiQuery(cellSize: 10);
        var visible = query.QueryVisible(
            new YuoAoiObserver(1, new YuoAoiPosition(0, 0, 0), 0),
            [new YuoAoiTarget(2, new YuoAoiPosition(0, 0, 0))]);

        Assert.Empty(visible);
    }

    [Fact]
    public void Grid_query_handles_negative_coordinates()
    {
        var query = new YuoGridAoiQuery(cellSize: 10);
        var observer = new YuoAoiObserver(
            10,
            new YuoAoiPosition(-9, 0, -9),
            ViewRadius: 3);

        var visible = query.QueryVisible(
            observer,
            [
                new YuoAoiTarget(11, new YuoAoiPosition(-10, 0, -10)),
                new YuoAoiTarget(12, new YuoAoiPosition(-13, 0, -13))
            ]);

        var entity = Assert.Single(visible);
        Assert.Equal(11, entity.NetworkId);
    }

    [Fact]
    public void Grid_target_index_can_be_reused_by_multiple_observers()
    {
        var index = YuoGridAoiTargetIndex.Create(
            cellSize: 10,
            [
                new YuoAoiTarget(1, new YuoAoiPosition(0, 0, 0), "normal"),
                new YuoAoiTarget(2, new YuoAoiPosition(8, 0, 0), "normal"),
                new YuoAoiTarget(3, new YuoAoiPosition(30, 0, 0), "normal"),
                new YuoAoiTarget(4, new YuoAoiPosition(31, 0, 0), "normal")
            ]);

        var first = index.QueryVisible(new YuoAoiObserver(1, new YuoAoiPosition(0, 0, 0), 12, "normal"));
        var second = index.QueryVisible(new YuoAoiObserver(3, new YuoAoiPosition(30, 0, 0), 5, "normal"));

        Assert.Equal([2L], first.Select(entity => entity.NetworkId));
        Assert.Equal([4L], second.Select(entity => entity.NetworkId));
    }

    [Fact]
    public void Grid_target_index_can_count_visible_without_building_sorted_result()
    {
        var index = YuoGridAoiTargetIndex.Create(
            cellSize: 10,
            [
                new YuoAoiTarget(1, new YuoAoiPosition(0, 0, 0), "normal"),
                new YuoAoiTarget(2, new YuoAoiPosition(8, 0, 0), "normal"),
                new YuoAoiTarget(3, new YuoAoiPosition(12, 0, 0), "normal"),
                new YuoAoiTarget(4, new YuoAoiPosition(8, 0, 0), "stealth"),
                new YuoAoiTarget(5, new YuoAoiPosition(30, 0, 0), "normal")
            ]);

        var observer = new YuoAoiObserver(1, new YuoAoiPosition(0, 0, 0), 13, "normal");

        Assert.Equal(2, index.CountVisible(observer));
        Assert.Equal(index.QueryVisible(observer).Count, index.CountVisible(observer));
    }

    [Fact]
    public void Grid_target_index_count_returns_zero_for_invalid_radius()
    {
        var index = YuoGridAoiTargetIndex.Create(
            cellSize: 10,
            [new YuoAoiTarget(2, new YuoAoiPosition(0, 0, 0))]);

        Assert.Equal(0, index.CountVisible(new YuoAoiObserver(1, new YuoAoiPosition(0, 0, 0), 0)));
        Assert.Equal(0, index.CountVisible(new YuoAoiObserver(1, new YuoAoiPosition(0, 0, 0), float.NaN)));
    }

    [Fact]
    public void Entity_query_reads_aoi_projection_components()
    {
        var world = new YuoWorld();
        world.OnInit(setAsDefault: false);

        var observer = CreateAoiEntity(world, 100, 0, 0, 0, radius: 15);
        var visible = CreateAoiEntity(world, 101, 3, 0, 4, radius: 0);
        var hidden = CreateAoiEntity(world, 102, 40, 0, 0, radius: 0);

        var query = new YuoEntityAoiQuery(new YuoGridAoiQuery(cellSize: 10));
        var result = query.QueryVisible(observer, [observer, visible, hidden]);

        var entity = Assert.Single(result);
        Assert.Equal(101, entity.NetworkId);
        Assert.Equal(25, entity.DistanceSquared);
    }

    [Fact]
    public void Entity_query_returns_empty_when_observer_cannot_observe()
    {
        var world = new YuoWorld();
        world.OnInit(setAsDefault: false);

        var observer = CreateAoiEntity(world, 200, 0, 0, 0, radius: 15);
        observer.GetComponent<YuoAoiComponent>().CanObserve = false;
        var visible = CreateAoiEntity(world, 201, 1, 0, 1, radius: 0);

        var query = new YuoEntityAoiQuery(new YuoGridAoiQuery(cellSize: 10));
        var result = query.QueryVisible(observer, [observer, visible]);

        Assert.Empty(result);
    }

    private static YuoEntity CreateAoiEntity(
        YuoWorld world,
        long networkId,
        float x,
        float y,
        float z,
        float radius)
    {
        var entity = new YuoEntity(world);
        entity.AddComponent<YuoNetworkIdentityComponent>().NetworkId = networkId;
        var aoi = entity.AddComponent<YuoAoiComponent>();
        aoi.SetPosition(x, y, z);
        aoi.ViewRadius = radius;
        return entity;
    }
}
