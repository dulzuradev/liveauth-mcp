using FluentAssertions;
using Xunit;

namespace LiveAuthCore.Tests.CoreData;

public class PowDifficultyTests
{
    [Theory]
    [InlineData(0, 0xFF, 0xFF, 0xFF)]
    [InlineData(1, 0x7F, 0xFF, 0xFF)]
    [InlineData(8, 0x00, 0xFF, 0xFF)]
    [InlineData(12, 0x00, 0x0F, 0xFF)]
    [InlineData(16, 0x00, 0x00, 0xFF)]
    public void TargetFromBits_CalculatesExpectedLeadingTargetBytes(
        int bits,
        byte first,
        byte second,
        byte third)
    {
        var target = global::PowDifficulty.TargetFromBits(bits);

        target.Should().HaveCount(32);
        target[0].Should().Be(first);
        target[1].Should().Be(second);
        target[2].Should().Be(third);
    }

    [Fact]
    public void TargetFromBits_WithMaximumDifficulty_ReturnsAllZeroTarget()
    {
        var target = global::PowDifficulty.TargetFromBits(256);

        target.Should().OnlyContain(value => value == 0x00);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(257)]
    public void TargetFromBits_WithOutOfRangeDifficulty_Throws(int bits)
    {
        var act = () => global::PowDifficulty.TargetFromBits(bits);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("bits");
    }

    [Fact]
    public void IsValid_ReturnsTrueWhenHashIsLowerThanTarget()
    {
        var target = global::PowDifficulty.TargetFromBits(8);
        var hash = Enumerable.Repeat((byte)0x00, 32).ToArray();

        global::PowDifficulty.IsValid(hash, target).Should().BeTrue();
    }

    [Fact]
    public void IsValid_ReturnsTrueWhenHashEqualsTarget()
    {
        var target = global::PowDifficulty.TargetFromBits(12);

        global::PowDifficulty.IsValid(target.ToArray(), target).Should().BeTrue();
    }

    [Fact]
    public void IsValid_ReturnsFalseWhenHashIsGreaterThanTarget()
    {
        var target = global::PowDifficulty.TargetFromBits(8);
        var hash = target.ToArray();
        hash[0] = 0x01;

        global::PowDifficulty.IsValid(hash, target).Should().BeFalse();
    }
}
