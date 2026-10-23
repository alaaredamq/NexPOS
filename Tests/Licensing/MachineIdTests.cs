using Common.Licensing;
using Xunit;

namespace Tests.Licensing;

public class MachineIdTests
{
    private static readonly string[] Facts =
    {
        "6F1A2B3C-0001-4D5E-8F90-AAAAAAAAAAAA",
        "4C4C4544-0050-3910-8052-B7C04F4B4E32",
        "BOARD-SERIAL-12345",
        "DISK-SERIAL-98765"
    };

    private static MachineId With(params string?[] components) => MachineId.FromComponents(components);

    [Fact]
    public void Format_and_TryParse_round_trip()
    {
        var id = With(Facts);

        Assert.Matches(@"^[A-Z2-7]{5}(-[A-Z2-7]{5}){3}$", id.Format());
        Assert.True(MachineId.TryParse(id.Format(), out var parsed));
        Assert.True(parsed.IsSameMachineAs(id));
    }

    [Fact]
    public void TryParse_accepts_lowercase_and_spaces_and_rejects_nonsense()
    {
        var id = With(Facts);
        var sloppy = id.Format().ToLowerInvariant().Replace("-", " ");

        Assert.True(MachineId.TryParse(sloppy, out _));
        Assert.False(MachineId.TryParse("", out _));
        Assert.False(MachineId.TryParse("ABCDE-ABCDE", out _));
        Assert.False(MachineId.TryParse("!!!!!-!!!!!-!!!!!-!!!!!", out _));
    }

    [Fact]
    public void The_same_facts_always_give_the_same_id()
    {
        Assert.Equal(With(Facts).Format(), With(Facts).Format());
    }

    [Fact]
    public void Different_facts_give_a_different_id()
    {
        Assert.NotEqual(With(Facts).Format(), TestMachines.OtherPc.Format());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Default string")]
    [InlineData("To be filled by O.E.M.")]
    [InlineData("None")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
    [InlineData("03000200-0400-0500-0006-000700080009")]
    [InlineData("0000000000")]
    public void Placeholder_values_leave_the_slot_empty(string? placeholder)
    {
        Assert.False(MachineId.IsUsableComponent(placeholder));
        Assert.True(With(placeholder, null, null, null).IsEmpty);
    }

    [Fact]
    public void The_same_pc_matches_itself()
    {
        Assert.True(With(Facts).IsSameMachineAs(With(Facts)));
    }

    [Fact]
    public void A_windows_reinstall_changes_one_fact_and_still_matches()
    {
        var licensed = With(Facts);
        var afterReinstall = With("NEW-MACHINE-GUID-AFTER-REINSTALL", Facts[1], Facts[2], Facts[3]);

        Assert.True(licensed.IsSameMachineAs(afterReinstall));
    }

    [Fact]
    public void A_replaced_disk_and_a_windows_reinstall_together_still_match_on_the_other_two_facts()
    {
        var licensed = With(Facts);
        var after = With("NEW-GUID-1234", Facts[1], Facts[2], "NEW-DISK-SERIAL");

        Assert.True(licensed.IsSameMachineAs(after));
    }

    [Fact]
    public void Two_changed_facts_out_of_four_is_the_limit_three_changed_does_not_match()
    {
        var licensed = With(Facts);
        var threeChanged = With("NEW-GUID-1234", "NEW-UUID-5678", "NEW-BOARD-9999", Facts[3]);

        Assert.False(licensed.IsSameMachineAs(threeChanged));
    }

    [Fact]
    public void A_different_pc_does_not_match()
    {
        Assert.False(TestMachines.ShopPc.IsSameMachineAs(TestMachines.OtherPc));
    }

    [Fact]
    public void A_pc_that_only_had_one_readable_fact_must_match_on_that_fact()
    {
        var licensed = With(Facts[0], null, null, null);

        Assert.True(licensed.IsSameMachineAs(With(Facts)));
        Assert.False(licensed.IsSameMachineAs(With("SOMETHING-ELSE-ENTIRELY", Facts[1], Facts[2], Facts[3])));
    }

    [Fact]
    public void A_license_with_an_empty_id_matches_nothing()
    {
        var empty = MachineId.FromBytes(new byte[MachineId.Length]);

        Assert.False(empty.IsSameMachineAs(With(Facts)));
        Assert.False(empty.IsSameMachineAs(empty));
    }
}
