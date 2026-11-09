using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>The owner's side of private Online: the recovery code, the key on the PC, and the confirm step (PRIVACY-KEYS.md, step 2).</summary>
public class SealingFlowTests
{
    private static byte[] FixedKey() => Enumerable.Range(0, 32).Select(i => (byte)(i * 7 + 3)).ToArray();

    // ---- the recovery code ----

    [Fact]
    public void A_recovery_code_is_14_groups_of_4_and_reads_back_to_the_same_key()
    {
        for (var i = 0; i < 200; i++)
        {
            var key = RowSealer.NewKey();
            var groups = RecoveryCode.Groups(key);

            Assert.Equal(14, groups.Length);
            Assert.All(groups, g => Assert.Matches("^[A-Z2-7]{4}$", g));
            Assert.True(RecoveryCode.TryParse(RecoveryCode.Format(key), out var back));
            Assert.Equal(key, back);
        }
    }

    [Fact]
    public void A_code_typed_by_hand_is_forgiven_for_case_spacing_and_look_alike_characters()
    {
        var key = FixedKey();
        var code = RecoveryCode.Format(key);

        Assert.True(RecoveryCode.TryParse(code.ToLowerInvariant(), out var lower)); Assert.Equal(key, lower);
        Assert.True(RecoveryCode.TryParse(code.Replace('-', ' '), out var spaced)); Assert.Equal(key, spaced);
        Assert.True(RecoveryCode.TryParse(code.Replace("-", ""), out var joined)); Assert.Equal(key, joined);
        Assert.True(RecoveryCode.TryParse("  " + code.Replace("-", "\n") + "  ", out var lines)); Assert.Equal(key, lines);
        Assert.True(RecoveryCode.TryParse(code.Replace('O', '0').Replace('I', '1').Replace('B', '8'), out var lookAlike)); Assert.Equal(key, lookAlike);
    }

    [Fact]
    public void A_mistyped_or_incomplete_code_is_refused_instead_of_making_a_wrong_key()
    {
        var code = RecoveryCode.Format(FixedKey()).Replace("-", "");
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        // Every single wrong character, anywhere, is caught.
        for (var i = 0; i < code.Length; i++)
        {
            var other = alphabet.First(c => c != code[i]);
            var typo = code.Remove(i, 1).Insert(i, other.ToString());
            Assert.False(RecoveryCode.TryParse(typo, out _), $"a wrong character at {i} was accepted");
        }

        Assert.False(RecoveryCode.TryParse(code[..^1], out _));        // a character short
        Assert.False(RecoveryCode.TryParse(code + "A", out _));        // a character over
        Assert.False(RecoveryCode.TryParse("", out _));
        Assert.False(RecoveryCode.TryParse(null, out _));
        Assert.False(RecoveryCode.TryParse("not a recovery code at all, not even close to one!!", out _));

        var swapped = code[4..8] + code[..4] + code[8..];               // two groups swapped
        Assert.False(RecoveryCode.TryParse(swapped, out _));
    }

    // ---- the key file: restore ----

    private static string TempKeyPath() => Path.Combine(Path.GetTempPath(), "nexpos-key-" + Guid.NewGuid().ToString("N"), "online.key");

    [Fact]
    public void A_key_from_a_slip_is_put_on_a_new_pc_and_is_the_same_key()
    {
        var original = new SealKeyStore(TempKeyPath());
        var key = original.ExportKey();

        var newPc = new SealKeyStore(TempKeyPath());
        var restored = newPc.Import(key);

        Assert.Equal(new RowSealer(key).KeyId, restored.KeyId);
        Assert.Equal(key, newPc.ExportKey());
        Assert.Equal(restored.KeyId, newPc.TryLoad()!.KeyId);
    }

    [Fact]
    public void A_pc_that_already_has_a_different_key_refuses_a_restore_and_keeps_its_key()
    {
        var store = new SealKeyStore(TempKeyPath());
        var mine = store.ExportKey();
        var before = File.ReadAllBytes(store.Path);

        Assert.Throws<InvalidOperationException>(() => store.Import(RowSealer.NewKey()));
        Assert.Equal(before, File.ReadAllBytes(store.Path));
        Assert.Equal(mine, store.ExportKey());
        store.Import(mine); // the same key again is fine
    }

    // ---- the confirm step ----

    private sealed class Rig
    {
        public required OnlineSettingsStore Store { get; init; }
        public required SealKeyStore Keys { get; init; }
        public required OnlineSealing Sealing { get; init; }

        public static Rig Create(bool connected)
        {
            var folder = Path.Combine(Path.GetTempPath(), "nexpos-flow-" + Guid.NewGuid().ToString("N"));
            var store = new OnlineSettingsStore(Path.Combine(folder, "online.json"), new OnlineSettings());
            if (connected) store.Save(store.Current with { Enabled = true, CloudUrl = "https://cloud.example", DeviceToken = "token" });
            var keys = new SealKeyStore(Path.Combine(folder, "online.key"));
            return new Rig { Store = store, Keys = keys, Sealing = new OnlineSealing(store, keys, new OnlineSignal(), TimeProvider.System) };
        }
    }

    [Fact]
    public void The_slip_shows_the_key_that_is_on_the_pc_and_makes_it_once()
    {
        var rig = Rig.Create(connected: true);
        Assert.False(rig.Keys.Exists);

        var slip = rig.Sealing.GetSlip();
        var again = rig.Sealing.GetSlip();

        Assert.True(rig.Keys.Exists);
        Assert.Equal(slip.Code, again.Code);
        Assert.Equal(14, slip.Groups.Count);
        Assert.Equal(rig.Keys.TryLoad()!.KeyId, slip.KeyId);
        Assert.True(RecoveryCode.TryParse(slip.Code, out var key));
        Assert.Equal(rig.Keys.ExportKey(), key);
    }

    [Fact]
    public void Confirming_needs_a_connection_and_a_key_and_then_turns_sealing_on()
    {
        var offline = Rig.Create(connected: false);
        offline.Keys.LoadOrCreate();
        Assert.Equal(OnlineSealingCodes.NotConnected, Assert.Throws<OnlineSealingException>(offline.Sealing.Confirm).Code);

        var noKey = Rig.Create(connected: true);
        Assert.Equal(OnlineSealingCodes.NoKey, Assert.Throws<OnlineSealingException>(noKey.Sealing.Confirm).Code);
        Assert.False(noKey.Store.Current.SealData);

        var ready = Rig.Create(connected: true);
        ready.Sealing.GetSlip();
        ready.Sealing.Confirm();
        Assert.True(ready.Store.Current.SealData);
        Assert.NotNull(ready.Store.Current.SealConfirmedAtUtc);
    }

    [Fact]
    public void Restoring_a_key_reports_a_bad_code_a_different_key_and_success()
    {
        var rig = Rig.Create(connected: true);
        var key = RowSealer.NewKey();

        Assert.Equal(RestoreOutcome.InvalidCode, rig.Sealing.RestoreKey("nonsense"));
        Assert.False(rig.Keys.Exists);

        Assert.Equal(RestoreOutcome.Restored, rig.Sealing.RestoreKey(RecoveryCode.Format(key)));
        Assert.Equal(key, rig.Keys.ExportKey());

        Assert.Equal(RestoreOutcome.DifferentKeyHere, rig.Sealing.RestoreKey(RecoveryCode.Format(RowSealer.NewKey())));
        Assert.Equal(key, rig.Keys.ExportKey());
    }

    [Theory]
    [InlineData(false, false, false, "off")]
    [InlineData(false, true, true, "off")]
    [InlineData(true, false, false, "needsSlip")]
    [InlineData(true, false, true, "needsSlip")]
    [InlineData(true, true, false, "keyMissing")]
    [InlineData(true, true, true, "on")]
    public void The_screen_step_follows_the_settings_and_whether_the_key_is_here(bool sealData, bool confirmed, bool keyExists, string expected)
    {
        var settings = new OnlineSettings { SealData = sealData, SealConfirmedAtUtc = confirmed ? DateTime.UtcNow : null };
        Assert.Equal(expected, SealingStates.Of(settings, keyExists));
    }

    [Fact]
    public void Every_new_connection_is_sealed_by_default()
    {
        Assert.True(new OnlineSettings().SealOnConnect);
        Assert.False(new OnlineSettings().SealData);
    }
}
