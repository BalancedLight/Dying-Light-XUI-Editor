using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Editing;
using XuiEditor.Core.Values;

namespace XuiEditor.Tests;

[TestClass]
public sealed class ElementAlignmentTests
{
    [TestMethod]
    public void AlignmentCalculationsUseTheImmediateParentBounds()
    {
        XuiVector2 parentSize = new(100, 60);
        XuiVector3 childPosition = new(17, 23, 7);
        XuiVector2 childSize = new(20, 10);

        AssertDelta(
            XuiElementAlignment.Left,
            parentSize,
            childPosition,
            childSize,
            -17,
            0);
        AssertDelta(
            XuiElementAlignment.Center,
            parentSize,
            childPosition,
            childSize,
            23,
            2);
        AssertDelta(
            XuiElementAlignment.Right,
            parentSize,
            childPosition,
            childSize,
            63,
            0);
        AssertDelta(
            XuiElementAlignment.Top,
            parentSize,
            childPosition,
            childSize,
            0,
            -23);
        AssertDelta(
            XuiElementAlignment.Bottom,
            parentSize,
            childPosition,
            childSize,
            0,
            27);
    }

    [TestMethod]
    public void AlignmentCalculationsRejectNonFiniteGeometry()
    {
        bool calculated =
            XuiElementAlignmentCalculator.TryGetPositionDelta(
                XuiElementAlignment.Center,
                new XuiVector2(100, 60),
                new XuiVector3(double.NaN, 0, 0),
                new XuiVector2(20, 10),
                out XuiVector2 delta);

        Assert.IsFalse(calculated);
        Assert.AreEqual(default, delta);
    }

    private static void AssertDelta(
        XuiElementAlignment alignment,
        XuiVector2 parentSize,
        XuiVector3 childPosition,
        XuiVector2 childSize,
        double expectedX,
        double expectedY)
    {
        bool calculated =
            XuiElementAlignmentCalculator.TryGetPositionDelta(
                alignment,
                parentSize,
                childPosition,
                childSize,
                out XuiVector2 delta);

        Assert.IsTrue(calculated);
        Assert.AreEqual(expectedX, delta.X, 0.0001);
        Assert.AreEqual(expectedY, delta.Y, 0.0001);
    }
}
