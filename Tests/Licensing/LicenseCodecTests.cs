using Common.Licensing;
using Xunit;

namespace Tests.Licensing;

public class LicenseCodecTests
{
    private static readonly DateOnly Issued = new(2026, 10, 17);
    private static readonly DateOnly Until = new(2027, 10, 17);

    [Fact]
    public void Encode_then_Decode_returns_every_field()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Provisional, TestMachines.ShopPc, Issued, Until, "Alaa Mini Market", 0xDEADBEEF);

        var result = LicenseCodec.Decode(key, issuer.PublicKeySpki);

        Assert.True(result.Success);
        var payload = result.Payload!;
        Assert.Equal(LicenseKind.Provisional, payload.Kind);
        Assert.Equal(Issued, payload.IssuedOn);
        Assert.Equal(Until, payload.ValidUntil);
        Assert.Equal(0xDEADBEEFu, payload.Serial);
        Assert.Equal("DEADBEEF", payload.SerialText);
        Assert.Equal("Alaa Mini Market", payload.Customer);
        Assert.True(payload.Machine.IsSameMachineAs(TestMachines.ShopPc));
        Assert.True(payload.IsTimeLimited);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(255)]
    public void Seats_round_trip_and_leave_the_other_fields_alone(int seats)
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, "Alaa Mini Market", 0xDEADBEEF, seats);

        var payload = LicenseCodec.Decode(key, issuer.PublicKeySpki).Payload!;

        Assert.Equal(seats, payload.Seats);
        Assert.Equal("Alaa Mini Market", payload.Customer);
        Assert.Equal(0xDEADBEEFu, payload.Serial);
        Assert.Equal(Until, payload.ValidUntil);
        Assert.True(payload.Machine.IsSameMachineAs(TestMachines.ShopPc));
    }

    [Fact]
    public void A_key_without_seats_covers_one_pc()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until);

        Assert.Equal(1, LicenseCodec.Decode(key, issuer.PublicKeySpki).Payload!.Seats);
    }

    [Fact]
    public void A_single_seat_key_is_still_written_in_the_original_format_so_older_builds_accept_it()
    {
        using var issuer = new LicenseTestIssuer();

        Assert.Equal(1, FormatVersionOf(issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, seats: 1)));
        Assert.Equal(2, FormatVersionOf(issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, seats: 2)));
    }

    [Fact]
    public void Seats_cannot_be_raised_by_editing_the_key()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, "Shop", 1, seats: 2);

        // Re-encode the raw bytes with the seat byte changed: the signature no longer matches.
        Assert.True(Base32.TryDecode(string.Concat(key.Split('-').Skip(1)), out var raw));
        raw[raw.Length - 64 - 1] = 200;
        var tampered = LicenseCodec.Canonicalize(Base32.Encode(raw));

        var result = LicenseCodec.Decode(tampered, issuer.PublicKeySpki);

        Assert.False(result.Success);
        Assert.Equal(LicenseDecodeError.BadSignature, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(256)]
    public void A_license_cannot_be_issued_for_an_impossible_seat_count(int seats)
    {
        using var issuer = new LicenseTestIssuer();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, seats: seats));
    }

    private static int FormatVersionOf(string key)
    {
        Assert.True(Base32.TryDecode(string.Concat(key.Split('-').Skip(1)), out var raw));
        return raw[0];
    }

    [Fact]
    public void Arabic_customer_names_survive()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, "سوبرماركت الأمل");

        var result = LicenseCodec.Decode(key, issuer.PublicKeySpki);

        Assert.True(result.Success);
        Assert.Equal("سوبرماركت الأمل", result.Payload!.Customer);
    }

    [Fact]
    public void Key_text_starts_with_the_prefix_and_is_grouped()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until);

        Assert.StartsWith("NXL1-", key);
        Assert.All(key.Split('-').Skip(1), group => Assert.InRange(group.Length, 1, 5));
    }

    [Fact]
    public void Decode_tolerates_whitespace_line_breaks_lowercase_and_invisible_characters()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until);

        var messy = "  " + key.ToLowerInvariant().Replace("-", " - ").Replace("-", "-\r\n") + "​ \t";

        Assert.True(LicenseCodec.Decode(messy, issuer.PublicKeySpki).Success);
    }

    [Fact]
    public void Canonicalize_gives_the_same_text_however_the_key_was_pasted()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until);

        Assert.Equal(key, LicenseCodec.Canonicalize(key.ToLowerInvariant().Replace("-", "  ")));
    }

    [Fact]
    public void A_changed_character_is_rejected()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until);

        // Flip a character inside the signed payload (after the "NXL1-" prefix).
        var chars = key.ToCharArray();
        var index = 12;
        while (chars[index] == '-') index++;
        chars[index] = chars[index] == 'A' ? 'B' : 'A';

        Assert.False(LicenseCodec.Decode(new string(chars), issuer.PublicKeySpki).Success);
    }

    [Fact]
    public void A_key_signed_by_someone_else_is_rejected_with_BadSignature()
    {
        using var realIssuer = new LicenseTestIssuer();
        using var forger = new LicenseTestIssuer();
        var forged = forger.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until);

        var result = LicenseCodec.Decode(forged, realIssuer.PublicKeySpki);

        Assert.False(result.Success);
        Assert.Equal(LicenseDecodeError.BadSignature, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("NXL1-")]
    [InlineData("NXL1-AAAAA-BBBBB")]
    [InlineData("XXXX-AAAAA-BBBBB-CCCCC-DDDDD")]
    public void Garbage_is_Malformed(string? text)
    {
        using var issuer = new LicenseTestIssuer();

        var result = LicenseCodec.Decode(text, issuer.PublicKeySpki);

        Assert.False(result.Success);
        Assert.Equal(LicenseDecodeError.Malformed, result.Error);
    }

    [Fact]
    public void A_truncated_key_is_Malformed()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until);

        var result = LicenseCodec.Decode(key[..^12], issuer.PublicKeySpki);

        Assert.False(result.Success);
    }

    [Fact]
    public void A_key_with_extra_characters_is_rejected()
    {
        using var issuer = new LicenseTestIssuer();
        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until);

        Assert.False(LicenseCodec.Decode(key + "-AAAAA", issuer.PublicKeySpki).Success);
    }

    [Fact]
    public void Long_customer_names_are_cut_without_splitting_a_character()
    {
        using var issuer = new LicenseTestIssuer();
        var longArabic = string.Concat(Enumerable.Repeat("مرحبا", 20)); // 2 bytes per letter, far over 60 bytes

        var key = issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, Issued, Until, longArabic);
        var customer = LicenseCodec.Decode(key, issuer.PublicKeySpki).Payload!.Customer;

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(customer) <= LicenseCodec.MaxCustomerBytes);
        Assert.StartsWith(customer, longArabic);
        Assert.DoesNotContain('�', customer);
    }

    [Fact]
    public void A_license_cannot_be_issued_for_an_empty_machine_id()
    {
        using var issuer = new LicenseTestIssuer();
        var empty = MachineId.FromBytes(new byte[MachineId.Length]);

        Assert.Throws<ArgumentException>(() => issuer.Issue(LicenseKind.Full, empty, Issued, Until));
    }

    [Fact]
    public void Dates_before_2024_cannot_be_issued()
    {
        using var issuer = new LicenseTestIssuer();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            issuer.Issue(LicenseKind.Full, TestMachines.ShopPc, new DateOnly(2023, 12, 31), Until));
    }

    [Fact]
    public void Base32_round_trips_every_length()
    {
        var random = new Random(42);
        for (var length = 0; length <= 64; length++)
        {
            var bytes = new byte[length];
            random.NextBytes(bytes);

            Assert.True(Base32.TryDecode(Base32.Encode(bytes), out var decoded));
            Assert.Equal(bytes, decoded);
        }
    }

    [Fact]
    public void Base32_reads_the_characters_people_mistype()
    {
        Assert.True(Base32.TryDecode("0", out var zero));
        Assert.True(Base32.TryDecode("O", out var letterO));
        Assert.Equal(letterO, zero);

        Assert.False(Base32.TryDecode("A?B", out _));
    }
}
