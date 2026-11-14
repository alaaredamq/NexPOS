using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// The owner's phone queues a change, the shop PC collects it, applies it and reports back. These tests cover the cloud half
/// over HTTP (sign-in, queueing, isolation between clients, long-poll, redelivery) and the shop-PC half (the polling service).
/// </summary>
public class CommandChannelTests : IClassFixture<CloudFactory>
{
    private const string Password = "pass-1234-ok";
    private readonly CloudFactory _cloud;

    public CommandChannelTests(CloudFactory cloud) => _cloud = cloud;

    private sealed record Owner(SeededSite Site, HttpClient Http);

    private async Task<Owner> SeedOwnerAsync(string email, string clientName = "Corner Shop", int months = 1)
    {
        var site = await _cloud.SeedSiteAsync(months, clientName);
        await _cloud.WithControlDbAsync(async db =>
        {
            db.OwnerUsers.Add(new OwnerUser
            {
                ClientId = site.ClientId, Email = email, DisplayName = "Layla",
                PasswordHash = PasswordHasher.Hash(Password), CreatedAt = _cloud.Clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var http = _cloud.CreateClient();
        var login = await http.PostAsJsonAsync("/owner/v1/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Owner(site, http);
    }

    private static HttpRequestMessage Post(string url, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static readonly object NewCustomer = new { firstName = "Nadine", lastName = "Fares", phoneNumber = "71234567" };

    // ---- sign in ----

    [Fact]
    public async Task Signing_in_gives_a_token_and_a_wrong_password_gives_one_generic_answer()
    {
        var owner = await SeedOwnerAsync("signin@shop.test");
        var me = await owner.Http.GetFromJsonAsync<JsonElement>("/owner/v1/me");
        Assert.Equal("Layla", me.GetProperty("displayName").GetString());
        Assert.Equal("Corner Shop", me.GetProperty("clientName").GetString());

        var wrong = await _cloud.CreateClient().PostAsJsonAsync("/owner/v1/auth/login", new { email = "signin@shop.test", password = "nope" });
        var unknown = await _cloud.CreateClient().PostAsJsonAsync("/owner/v1/auth/login", new { email = "nobody@shop.test", password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_owner_call_needs_a_token_and_a_signed_out_token_stops_working()
    {
        var anonymous = _cloud.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/owner/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/owner/v1/commands")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Post("/owner/v1/customers", NewCustomer, "key-12345678"))).StatusCode);

        // A device (till) token must not open the owner API either.
        var site = await _cloud.SeedSiteAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cloud.DeviceClient(site.Token).GetAsync("/owner/v1/me")).StatusCode);

        var owner = await SeedOwnerAsync("signout@shop.test");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.PostAsync("/owner/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Http.GetAsync("/owner/v1/me")).StatusCode);
    }

    // ---- queueing ----

    [Fact]
    public async Task A_change_is_accepted_and_queued_and_the_same_key_never_queues_it_twice()
    {
        var owner = await SeedOwnerAsync("queue@shop.test");

        var first = await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "customer-nadine-1"));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var queued = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("queued", queued.GetProperty("status").GetString());
        Assert.Equal("customer.create", queued.GetProperty("type").GetString());
        Assert.Equal("Layla", queued.GetProperty("requestedBy").GetString());

        var retry = await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "customer-nadine-1"));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(queued.GetProperty("id").GetGuid(), (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        var count = await _cloud.WithControlDbAsync(db => db.SiteCommands.CountAsync(c => c.SiteId == owner.Site.SiteId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_change_without_a_proper_key_or_body_is_refused_before_it_is_queued()
    {
        var owner = await SeedOwnerAsync("refuse@shop.test");

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "short"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.SendAsync(Post("/owner/v1/customers", new[] { 1, 2 }, "array-body-1"))).StatusCode);

        Assert.Equal(0, await _cloud.WithControlDbAsync(db => db.SiteCommands.CountAsync(c => c.SiteId == owner.Site.SiteId)));
    }

    [Fact]
    public async Task One_clients_owner_can_never_see_or_withdraw_another_clients_change()
    {
        var alice = await SeedOwnerAsync("alice@shop.test", "Alice Mart");
        var bob = await SeedOwnerAsync("bob@shop.test", "Bob Mart");

        var made = await alice.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "alice-change-1"));
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync($"/owner/v1/commands/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.DeleteAsync($"/owner/v1/commands/{id}")).StatusCode);
        var bobsList = await bob.Http.GetFromJsonAsync<JsonElement>("/owner/v1/commands");
        Assert.Equal(0, bobsList.GetArrayLength());

        // And Bob's shop PC is never handed Alice's change.
        var forBob = await _cloud.DeviceClient(bob.Site.Token).GetAsync("/api/sync/commands");
        Assert.Equal(HttpStatusCode.NoContent, forBob.StatusCode);
    }

    [Fact]
    public async Task A_change_can_be_withdrawn_only_until_the_shop_pc_takes_it()
    {
        var owner = await SeedOwnerAsync("withdraw@shop.test");
        var a = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "withdraw-me-1"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var b = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "taken-already-1"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var discard = await owner.Http.DeleteAsync($"/owner/v1/commands/{a}");
        Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
        Assert.Equal("discarded", (await discard.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        // The shop PC collects: only the live one is handed out.
        var fetched = await (await _cloud.DeviceClient(owner.Site.Token).GetAsync("/api/sync/commands")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(b, fetched.GetProperty("commands")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, fetched.GetProperty("commands").GetArrayLength());

        Assert.Equal(HttpStatusCode.Conflict, (await owner.Http.DeleteAsync($"/owner/v1/commands/{b}")).StatusCode);
    }

    [Fact]
    public async Task Changes_wait_while_the_online_plan_is_not_active()
    {
        var owner = await SeedOwnerAsync("expired@shop.test", months: 0); // no subscription at all
        var queued = await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "needs-plan-1"));
        Assert.Equal(HttpStatusCode.PaymentRequired, queued.StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await _cloud.DeviceClient(owner.Site.Token).GetAsync("/api/sync/commands")).StatusCode);
    }

    // ---- the shop PC's side of the wire ----

    [Fact]
    public async Task The_shop_pc_collects_a_change_and_the_owner_sees_the_result()
    {
        var owner = await SeedOwnerAsync("collect@shop.test");
        var till = _cloud.DeviceClient(owner.Site.Token);
        var made = await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "collect-me-0001"));
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var batch = await (await till.GetAsync("/api/sync/commands")).Content.ReadFromJsonAsync<JsonElement>();
        var command = batch.GetProperty("commands")[0];
        Assert.Equal("customer.create", command.GetProperty("type").GetString());
        Assert.Equal("Nadine", command.GetProperty("payload").GetProperty("firstName").GetString());
        Assert.Equal("delivered", (await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}")).GetProperty("status").GetString());

        // Nothing more to hand out while it is out for delivery.
        Assert.Equal(HttpStatusCode.NoContent, (await till.GetAsync("/api/sync/commands")).StatusCode);

        var report = await till.PostAsJsonAsync($"/api/sync/commands/{id}/result", CommandResult.Ok(new { id = 42 }), SyncWire.Json);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var seen = await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}");
        Assert.Equal("applied", seen.GetProperty("status").GetString());
        Assert.Equal(42, seen.GetProperty("data").GetProperty("id").GetInt32());

        // A repeated report (the answer was lost on the way back) keeps the first answer.
        await till.PostAsJsonAsync($"/api/sync/commands/{id}/result", CommandResult.Reject("conflict", "late"), SyncWire.Json);
        Assert.Equal("applied", (await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_refusal_reaches_the_owner_with_the_reason()
    {
        var owner = await SeedOwnerAsync("refused@shop.test");
        var till = _cloud.DeviceClient(owner.Site.Token);
        var id = (await (await owner.Http.SendAsync(Post("/owner/v1/products", new { name = "Pita" }, "refuse-product-1"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await till.GetAsync("/api/sync/commands");

        await till.PostAsJsonAsync($"/api/sync/commands/{id}/result",
            CommandResult.Reject(CommandErrorCodes.Validation, "That barcode is already used.", new { errors = new { barcode = new[] { "In use" } } }), SyncWire.Json);

        var seen = await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}");
        Assert.Equal("rejected", seen.GetProperty("status").GetString());
        Assert.Equal("validation", seen.GetProperty("code").GetString());
        Assert.Equal("That barcode is already used.", seen.GetProperty("message").GetString());
        Assert.Equal("In use", seen.GetProperty("data").GetProperty("errors").GetProperty("barcode")[0].GetString());
    }

    [Fact]
    public async Task A_shop_pc_can_only_answer_its_own_changes()
    {
        var mine = await SeedOwnerAsync("mine@shop.test", "Mine");
        var other = await SeedSiteForTokenOnlyAsync();
        var id = (await (await mine.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "not-yours-0001"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var attempt = await _cloud.DeviceClient(other.Token).PostAsJsonAsync($"/api/sync/commands/{id}/result", CommandResult.Ok(), SyncWire.Json);
        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
    }

    private Task<SeededSite> SeedSiteForTokenOnlyAsync() => _cloud.SeedSiteAsync(clientName: "Someone Else");

    [Fact]
    public async Task A_change_handed_out_but_never_answered_is_handed_out_again_later()
    {
        var owner = await SeedOwnerAsync("redeliver@shop.test");
        var till = _cloud.DeviceClient(owner.Site.Token);
        await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "redeliver-0001"));
        var original = _cloud.Clock.Now;
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await till.GetAsync("/api/sync/commands")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await till.GetAsync("/api/sync/commands")).StatusCode);

            _cloud.Clock.Now = original.AddMinutes(3); // the shop PC lost power and came back
            Assert.Equal(HttpStatusCode.OK, (await till.GetAsync("/api/sync/commands")).StatusCode);
        }
        finally { _cloud.Clock.Now = original; }
    }

    [Fact]
    public async Task A_waiting_shop_pc_is_woken_the_moment_the_owner_queues_a_change()
    {
        var owner = await SeedOwnerAsync("wake@shop.test");
        var till = _cloud.DeviceClient(owner.Site.Token);

        var waiting = till.GetAsync("/api/sync/commands?wait=30");
        await Task.Delay(500);
        Assert.False(waiting.IsCompleted, "nothing is queued, so the request is held open");

        await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "wake-up-00001"));
        var response = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_held_request_with_nothing_to_do_ends_quietly()
    {
        var owner = await SeedOwnerAsync("quiet@shop.test");
        var response = await _cloud.DeviceClient(owner.Site.Token).GetAsync("/api/sync/commands?wait=1");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- the shop PC's service ----

    private sealed class FakeHandler(string type, Func<JsonElement, CommandContext, CommandResult> act) : IOnlineCommandHandler
    {
        public int Runs;
        public string Type => type;

        public Task<CommandResult> HandleAsync(JsonElement payload, CommandContext context, CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            return Task.FromResult(act(payload, context));
        }
    }

    private sealed class RouteToCloud(HttpMessageHandler cloud) : DelegatingHandler(cloud);

    private sealed class ShopPc : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required OnlineCommandService Service { get; init; }
        public required string Folder { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            try { Directory.Delete(Folder, true); } catch (IOException) { }
        }
    }

    private ShopPc StartShopPc(SeededSite site, params IOnlineCommandHandler[] handlers) => StartShopPc(site, null, handlers);

    private ShopPc StartShopPc(SeededSite site, DataKey? key, params IOnlineCommandHandler[] handlers)
    {
        var folder = Path.Combine(Path.GetTempPath(), "nexpos-cmd-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOnlineSync(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Path.Combine(folder, "online.json"));
        services.AddHttpClient(OnlineSyncService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new RouteToCloud(_cloud.Server.CreateHandler()));
        foreach (var handler in handlers) services.AddScoped<IOnlineCommandHandler>(_ => handler);
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<OnlineSettingsStore>().Save(new OnlineSettings
        {
            Enabled = true, CloudUrl = "http://localhost", DeviceToken = site.Token, CommandWaitSeconds = 0,
            DataKeyBase64 = key?.ToBase64() ?? "",
        });
        return new ShopPc
        {
            Provider = provider, Folder = folder,
            Service = provider.GetServices<IHostedService>().OfType<OnlineCommandService>().Single(),
        };
    }

    [Fact]
    public async Task The_shop_pc_applies_a_change_and_the_owner_sees_it_done()
    {
        var owner = await SeedOwnerAsync("service@shop.test");
        var handler = new FakeHandler("customer.create", (p, c) => CommandResult.Ok(new { id = 7, name = p.GetProperty("firstName").GetString(), by = c.RequestedBy }));
        await using var pc = StartShopPc(owner.Site, handler);

        var id = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "service-run-001"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(1, await pc.Service.PollOnceAsync(default));

        var seen = await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}");
        Assert.Equal("applied", seen.GetProperty("status").GetString());
        Assert.Equal("Nadine", seen.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("Layla", seen.GetProperty("data").GetProperty("by").GetString());
        Assert.Equal(0, await pc.Service.PollOnceAsync(default));
    }

    [Fact]
    public async Task A_kind_of_change_the_shop_pc_does_not_know_is_refused_politely()
    {
        var owner = await SeedOwnerAsync("unknown@shop.test");
        await using var pc = StartShopPc(owner.Site); // no handlers at all
        var id = (await (await owner.Http.SendAsync(Post("/owner/v1/suppliers", new { name = "X" }, "unknown-type-01"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await pc.Service.PollOnceAsync(default);

        var seen = await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}");
        Assert.Equal("rejected", seen.GetProperty("status").GetString());
        Assert.Equal("unknown_type", seen.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_crash_inside_a_handler_becomes_a_refusal_not_a_stuck_change()
    {
        var owner = await SeedOwnerAsync("crash@shop.test");
        var handler = new FakeHandler("customer.create", (_, _) => throw new InvalidOperationException("database is locked"));
        await using var pc = StartShopPc(owner.Site, handler);
        var id = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "crash-in-handler"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await pc.Service.PollOnceAsync(default);

        var seen = await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}");
        Assert.Equal("rejected", seen.GetProperty("status").GetString());
        Assert.Equal("server_error", seen.GetProperty("code").GetString());
        Assert.DoesNotContain("database is locked", seen.GetProperty("message").GetString()); // internals stay on the shop PC
    }

    [Fact]
    public async Task A_change_the_shop_pc_already_applied_is_never_applied_twice()
    {
        var owner = await SeedOwnerAsync("twice@shop.test");
        var handler = new FakeHandler("customer.create", (_, _) => CommandResult.Ok(new { id = 9 }));
        await using var pc = StartShopPc(owner.Site, handler);
        var id = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "apply-once-0001"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await pc.Service.PollOnceAsync(default);
        Assert.Equal(1, handler.Runs);

        // The answer never reached the cloud: it still believes the change is out for delivery, and hands it out again.
        await _cloud.WithControlDbAsync(async db =>
        {
            var command = await db.SiteCommands.FirstAsync(c => c.Id == id);
            command.Status = CommandStatus.Delivered;
            command.DeliveredAt = _cloud.Clock.GetUtcNow().UtcDateTime.AddMinutes(-5);
            command.CompletedAt = null;
            await db.SaveChangesAsync();
            return 0;
        });
        await pc.Service.PollOnceAsync(default);

        Assert.Equal(1, handler.Runs); // answered from the ledger, not run again
        Assert.Equal("applied", (await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}")).GetProperty("status").GetString());
    }

    // ---- sealed changes: the cloud carries ciphertext it can neither read nor forge ----

    /// <summary>What the owner's phone does: seal {k, body} to the kind of change and send it as {enc}.</summary>
    private static object SealedByPhone(DataKey key, string type, string idempotencyKey, object body)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(new { k = idempotencyKey, body });
        return new Dictionary<string, string> { [CommandSeal.Field] = key.Cipher().Seal(CommandSeal.CommandPurpose, type, plain) };
    }

    private static JsonElement OpenResult(DataKey key, Guid commandId, JsonElement view)
    {
        var enc = view.GetProperty("data").GetProperty(CommandSeal.Field).GetString()!;
        return JsonDocument.Parse(key.Cipher().Open(CommandSeal.ResultPurpose, commandId.ToString("D"), enc)).RootElement.Clone();
    }

    [Fact]
    public async Task A_sealed_change_is_applied_and_the_cloud_never_holds_the_name_in_the_clear()
    {
        var owner = await SeedOwnerAsync("sealed@shop.test");
        var key = DataKey.Generate();
        string? seenFirstName = null;
        var handler = new FakeHandler("customer.create", (p, _) =>
        {
            seenFirstName = p.GetProperty("firstName").GetString();
            return CommandResult.Ok(new { id = 7, name = "Nadine Fares" });
        });
        await using var pc = StartShopPc(owner.Site, key, handler);

        var sent = await owner.Http.SendAsync(Post("/owner/v1/customers", SealedByPhone(key, "customer.create", "sealed-0001", NewCustomer), "sealed-0001"));
        var id = (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await pc.Service.PollOnceAsync(default);

        Assert.Equal("Nadine", seenFirstName); // the handler got the real body
        var view = await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}");
        Assert.Equal("applied", view.GetProperty("status").GetString());
        var opened = OpenResult(key, id, view);
        Assert.Equal("Nadine Fares", opened.GetProperty("data").GetProperty("name").GetString());

        // What the provider can read: no name, no phone number, in the request or in the answer.
        var stored = await _cloud.WithControlDbAsync(db => db.SiteCommands.SingleAsync(c => c.Id == id));
        var everything = stored.PayloadJson + stored.ResultDataJson + stored.ResultMessage;
        Assert.DoesNotContain("Nadine", everything);
        Assert.DoesNotContain("Fares", everything);
        Assert.DoesNotContain("71234567", everything);
    }

    [Fact]
    public async Task A_change_that_is_not_sealed_is_refused_once_the_shop_has_a_key_so_the_cloud_cannot_forge_one()
    {
        var owner = await SeedOwnerAsync("forged@shop.test");
        var key = DataKey.Generate();
        var handler = new FakeHandler("customer.create", (_, _) => CommandResult.Ok());
        await using var pc = StartShopPc(owner.Site, key, handler);

        var id = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", NewCustomer, "forged-000001"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await pc.Service.PollOnceAsync(default);

        Assert.Equal(0, handler.Runs);
        var view = await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}");
        Assert.Equal("rejected", view.GetProperty("status").GetString());
        Assert.Equal("not_sealed", view.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_change_sealed_with_another_key_or_for_another_kind_of_change_is_refused()
    {
        var owner = await SeedOwnerAsync("wrongkey@shop.test");
        var key = DataKey.Generate();
        var handler = new FakeHandler("customer.create", (_, _) => CommandResult.Ok());
        var supplierHandler = new FakeHandler("supplier.create", (_, _) => CommandResult.Ok());
        await using var pc = StartShopPc(owner.Site, key, handler, supplierHandler);

        var otherKey = DataKey.Generate();
        var a = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", SealedByPhone(otherKey, "customer.create", "other-key-0001", NewCustomer), "other-key-0001"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // A sealed customer, posted to the supplier route: the cloud swapping what the change is for.
        var b = (await (await owner.Http.SendAsync(Post("/owner/v1/suppliers", SealedByPhone(key, "customer.create", "swapped-00001", NewCustomer), "swapped-00001"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await pc.Service.PollOnceAsync(default);

        Assert.Equal(0, handler.Runs + supplierHandler.Runs);
        foreach (var id in new[] { a, b })
            Assert.Equal("not_sealed", (await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}")).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_cloud_replaying_a_sealed_change_under_a_new_id_does_not_make_it_run_twice()
    {
        var owner = await SeedOwnerAsync("replay@shop.test");
        var key = DataKey.Generate();
        var handler = new FakeHandler("customer.create", (_, _) => CommandResult.Ok(new { id = 11 }));
        await using var pc = StartShopPc(owner.Site, key, handler);
        var sealedBody = SealedByPhone(key, "customer.create", "replay-key-0001", NewCustomer);

        // The same sealed bytes queued twice, under two different keys (what a dishonest cloud could do).
        var first = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", sealedBody, "replay-queue-01"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var second = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", sealedBody, "replay-queue-02"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await pc.Service.PollOnceAsync(default);

        Assert.Equal(1, handler.Runs);
        foreach (var id in new[] { first, second })
        {
            var view = await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}");
            Assert.Equal("applied", view.GetProperty("status").GetString());
            Assert.Equal(11, OpenResult(key, id, view).GetProperty("data").GetProperty("id").GetInt32()); // each answer sealed to its own id
        }
    }

    [Fact]
    public async Task A_refusal_of_a_sealed_change_keeps_its_reason_sealed_too()
    {
        var owner = await SeedOwnerAsync("sealedrefusal@shop.test");
        var key = DataKey.Generate();
        var handler = new FakeHandler("customer.create", (_, _) => CommandResult.Reject(CommandErrorCodes.Validation, "Only 3 in stock, cannot remove 8.", new { errors = new { lastName = new[] { "too short" } } }));
        await using var pc = StartShopPc(owner.Site, key, handler);

        var id = (await (await owner.Http.SendAsync(Post("/owner/v1/customers", SealedByPhone(key, "customer.create", "refusal-0001", NewCustomer), "refusal-0001"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await pc.Service.PollOnceAsync(default);

        var view = await owner.Http.GetFromJsonAsync<JsonElement>($"/owner/v1/commands/{id}");
        Assert.Equal("rejected", view.GetProperty("status").GetString());
        Assert.Equal("validation", view.GetProperty("code").GetString());
        Assert.DoesNotContain("in stock", view.GetProperty("message").GetString());
        var opened = OpenResult(key, id, view);
        Assert.Contains("in stock", opened.GetProperty("message").GetString());
        Assert.Equal("too short", opened.GetProperty("data").GetProperty("errors").GetProperty("lastName")[0].GetString());
    }

    [Fact]
    public async Task The_cloud_routes_for_product_edits_and_stock_changes_queue_the_right_kind()
    {
        var owner = await SeedOwnerAsync("kinds@shop.test");

        var edit = await owner.Http.SendAsync(Post("/owner/v1/product-edits", new { enc = "e1.x" }, "kind-edit-0001"));
        var stock = await owner.Http.SendAsync(Post("/owner/v1/stock-changes", new { enc = "e1.x" }, "kind-stock-001"));

        Assert.Equal("product.update", (await edit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString());
        Assert.Equal("stock.adjust", (await stock.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_shop_pc_that_is_not_connected_does_nothing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nexpos-cmd-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOnlineSync(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Path.Combine(folder, "online.json"));
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetServices<IHostedService>().OfType<OnlineCommandService>().Single();

        Assert.Equal(0, await service.PollOnceAsync(default));
        Assert.False(Directory.Exists(folder), "an unconnected till writes nothing extra");
    }
}
