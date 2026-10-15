using NexoDesk.App.Services;

namespace NexoDesk.Windows.Tests;

public class ViewportPanTests
{
    [Theory]
    [InlineData(24, 48, 0, 1040, 0)]
    [InlineData(24, 48, 1040, 1040, 48)]
    [InlineData(24, 48, 520, 1040, 24)]
    [InlineData(24, 48, -1, 1040, 24)]
    [InlineData(24, 48, 1041, 1040, 24)]
    [InlineData(0, 48, 0, 1040, 0)]
    [InlineData(48, 48, 1040, 1040, 48)]
    public void CameraMovesOnlyAtEdgesAndStaysInBounds(double offset, double maximum, double pointer, double viewport, double expected)
    { Assert.Equal(expected, ViewportPan.Advance(offset, maximum, pointer, viewport, 0.05), 5); }

    [Fact]
    public void LongDispatcherPauseIsLimitedAndInvalidGeometryDoesNotMoveCamera()
    {
        Assert.Equal(45, ViewportPan.Advance(0, 1000, 1000, 1000, 30));
        Assert.Equal(10, ViewportPan.Advance(10, 100, double.NaN, 1000, 1));
        Assert.Equal(0, ViewportPan.Advance(0, 0, 1000, 1000, 1));
    }
}
