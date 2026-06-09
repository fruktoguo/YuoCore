using YuoCore.Abstractions;

namespace YuoCore.Abstractions.Tests;

public sealed class YuoResultTests
{
    [Fact]
    public void Fail_RejectsNullError()
    {
        Assert.Throws<ArgumentNullException>(() => YuoResult.Fail(null!));
    }

    [Fact]
    public void GenericFail_RejectsNullError()
    {
        Assert.Throws<ArgumentNullException>(() => YuoResult<int>.Fail(null!));
    }

    [Fact]
    public void Fail_WithError_ReturnsFailedResult()
    {
        var error = YuoError.Invalid("bad input");

        var result = YuoResult.Fail(error);
        var genericResult = YuoResult<int>.Fail(error);

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.False(genericResult.IsSuccess);
        Assert.Same(error, genericResult.Error);
    }

    [Fact]
    public void Ok_ReturnsSuccessfulResult()
    {
        var result = YuoResult.Ok();
        var genericResult = YuoResult<int>.Ok(10);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.True(genericResult.IsSuccess);
        Assert.Null(genericResult.Error);
        Assert.Equal(10, genericResult.Value);
    }
}
