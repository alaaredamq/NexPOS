using Sync;
using Xunit;

namespace Cloud.Tests;

public class EnrollmentLinkTests
{
    private const string Code = "7K4M-Q2PD-X9RT";

    [Theory]
    [InlineData("https://cloud.example.com/enroll/7K4M-Q2PD-X9RT")]
    [InlineData("  https://cloud.example.com/enroll/7K4M-Q2PD-X9RT  ")]
    [InlineData("https://cloud.example.com/enroll/7K4M-Q2PD-X9RT.")]                     // full stop from a sentence
    [InlineData("https://cloud.example.com/enroll/7k4m-q2pd-x9rt")]                      // lower case
    [InlineData("https://cloud.example.com/enroll/7K4MQ2PDX9RT")]                        // no dashes
    [InlineData("cloud.example.com/enroll/7K4M-Q2PD-X9RT")]                               // no scheme
    [InlineData("<https://cloud.example.com/enroll/7K4M-Q2PD-X9RT>")]                     // chat apps wrap links
    [InlineData("Coduo POS Online: paste this link and press Connect.\nلتفعيل Coduo POS Online: الصق الرابط\nhttps://cloud.example.com/enroll/7K4M-Q2PD-X9RT")] // the whole WhatsApp message
    [InlineData("https://cloud.example.com/enroll/7K4M-Q2PD-X9RT?utm=chat")]
    [InlineData("https://cloud.example.com/enroll/7K4M%2DQ2PD%2DX9RT")]
    public void Common_ways_of_pasting_all_work(string pasted)
    {
        Assert.Equal(EnrollmentLink.Problem.None, EnrollmentLink.TryParse(pasted, out var parsed));
        Assert.Equal("https://cloud.example.com/", parsed!.BaseUri.ToString());
        Assert.Equal("7K4MQ2PDX9RT", parsed.Code);
    }

    [Fact]
    public void A_cloud_under_a_sub_path_and_a_custom_port_keep_both()
    {
        Assert.Equal(EnrollmentLink.Problem.None, EnrollmentLink.TryParse($"https://host.example.com:8443/nexpos/enroll/{Code}", out var parsed));
        Assert.Equal("https://host.example.com:8443/nexpos/", parsed!.BaseUri.ToString());
        Assert.Equal("https://host.example.com:8443/nexpos/api/enroll", new Uri(parsed.BaseUri, "api/enroll").ToString());
    }

    [Theory]
    [InlineData("http://localhost:5100/enroll/7K4M-Q2PD-X9RT")]
    [InlineData("http://127.0.0.1:5100/enroll/7K4M-Q2PD-X9RT")]
    public void Plain_http_is_allowed_only_for_this_computer(string link)
        => Assert.Equal(EnrollmentLink.Problem.None, EnrollmentLink.TryParse(link, out _));

    [Theory]
    [InlineData("http://cloud.example.com/enroll/7K4M-Q2PD-X9RT")]
    [InlineData("http://192.168.1.20/enroll/7K4M-Q2PD-X9RT")]
    public void Plain_http_to_anywhere_else_is_refused_because_the_code_buys_a_credential(string link)
        => Assert.Equal(EnrollmentLink.Problem.Insecure, EnrollmentLink.TryParse(link, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("https://cloud.example.com/")]
    [InlineData("https://cloud.example.com/enroll/")]
    [InlineData("https://cloud.example.com/enroll/SHORT")]
    [InlineData("https://cloud.example.com/enroll/7K4M-Q2PD-X9RT-EXTRA")]
    [InlineData("ftp://cloud.example.com/enroll/7K4M-Q2PD-X9RT")]
    [InlineData("https://cloud.example.com/login/7K4M-Q2PD-X9RT")]
    public void Anything_else_is_not_a_link(string? pasted)
        => Assert.Equal(EnrollmentLink.Problem.NotALink, EnrollmentLink.TryParse(pasted, out _));
}

public class OnlineSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "nexpos-online-settings-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_folder, "online.json");

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    [Fact]
    public void No_file_means_not_enrolled()
    {
        var store = new OnlineSettingsStore(File_, new OnlineSettings());
        Assert.False(store.Current.Enabled);
        Assert.False(store.Current.IsConfigured);
        Assert.Null(store.LoadError);
        Assert.False(File.Exists(File_), "reading must not create the file");
    }

    [Fact]
    public void Saved_settings_survive_a_restart()
    {
        var first = new OnlineSettingsStore(File_, new OnlineSettings());
        first.Save(first.Current with
        {
            Enabled = true, CloudUrl = "https://cloud.example.com", DeviceToken = "nxp_secret", SiteName = "Main till", ClientName = "Corner Shop",
            ConnectedAtUtc = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc),
        });

        var second = new OnlineSettingsStore(File_, new OnlineSettings());
        Assert.True(second.Current.Enabled);
        Assert.True(second.Current.IsConfigured);
        Assert.Equal("nxp_secret", second.Current.DeviceToken);
        Assert.Equal("Corner Shop", second.Current.ClientName);
        Assert.Equal(new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc), second.Current.ConnectedAtUtc);
    }

    [Fact]
    public void A_hand_written_file_in_the_original_format_still_works()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(File_, """{ "Online": { "Enabled": true, "CloudUrl": "https://c.example.com", "DeviceToken": "nxp_abc" } }""");

        var store = new OnlineSettingsStore(File_, new OnlineSettings());
        Assert.True(store.Current.IsConfigured);
        Assert.Equal(30, store.Current.IntervalSeconds); // unspecified values keep their defaults
    }

    [Fact]
    public void A_damaged_file_switches_online_off_and_says_why_instead_of_crashing()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(File_, "{ this is not json");

        var store = new OnlineSettingsStore(File_, new OnlineSettings { Enabled = true });
        Assert.NotNull(store.LoadError);
        Assert.False(store.Current.IsConfigured);

        // Saving again repairs it.
        store.Save(new OnlineSettings());
        Assert.Null(store.LoadError);
    }

    [Fact]
    public void Saving_replaces_the_file_in_one_step_and_leaves_no_temp_file()
    {
        var store = new OnlineSettingsStore(File_, new OnlineSettings());
        store.Save(store.Current with { Enabled = true, CloudUrl = "https://a.example.com", DeviceToken = "nxp_1" });
        store.Save(store.Current with { DeviceToken = "nxp_2" });

        Assert.Equal(["online.json"], Directory.GetFiles(_folder).Select(Path.GetFileName).ToArray());
        Assert.Equal("nxp_2", new OnlineSettingsStore(File_, new OnlineSettings()).Current.DeviceToken);
    }
}
