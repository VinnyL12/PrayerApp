using System.Net;
using System.Net.Http.Headers;
using PrayerApp.Shared.Contracts.Admin;
using PrayerApp.Shared.Contracts.Auth;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class AdminTests(ApiFactory factory)
{
    [Fact]
    public async Task Normal_users_are_not_admins_and_cannot_reach_admin_endpoints()
    {
        var user = await factory.RegisterAsync();
        var other = await factory.RegisterAsync();

        Assert.False(user.User.IsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.GetAsync("/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.PutAsync($"/admin/users/{other.User.Id}/admin", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.DeleteAsync($"/admin/users/{other.User.Id}/admin")).StatusCode);
    }

    [Fact]
    public async Task A_promoted_user_gets_admin_access_at_once_and_sees_it_on_next_sign_in()
    {
        var admin = await AdminAsync();
        var user = await factory.RegisterAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await Promote(admin, user)).StatusCode);

        // Same token as before the promotion: the policy reads the database, not the token.
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/admin/users")).StatusCode);
        Assert.True((await SignInAsync(user)).User.IsAdmin);

        // Idempotent.
        Assert.Equal(HttpStatusCode.NoContent, (await Promote(admin, user)).StatusCode);
    }

    [Fact]
    public async Task A_demoted_admin_loses_access_on_their_very_next_request()
    {
        var admin = await AdminAsync();
        var demoted = await AdminAsync();

        // A token issued while still an admin.
        var token = (await SignInAsync(demoted)).AccessToken;
        demoted.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await demoted.Client.GetAsync("/admin/users")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Demote(admin, demoted)).StatusCode);

        // That token is still valid for 15 minutes, but it no longer opens admin endpoints.
        Assert.Equal(HttpStatusCode.Forbidden, (await demoted.Client.GetAsync("/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await demoted.Client.GetAsync("/communities")).StatusCode);
    }

    [Fact]
    public async Task Admins_cannot_demote_themselves()
    {
        var admin = await AdminAsync();

        var response = await Demote(admin, admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync("/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Owners_from_configuration_are_admins_and_cannot_be_demoted()
    {
        var owner = await factory.RegisterAsync("Owner", ApiFactory.OwnerEmail);
        var admin = await AdminAsync();

        // No role row was ever written for the owner; configuration alone makes it an admin.
        Assert.True(owner.User.IsAdmin);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/admin/users")).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Demote(admin, owner)).StatusCode);

        var listed = await ListAsync(admin, ApiFactory.OwnerEmail);
        var row = Assert.Single(listed);
        Assert.True(row.IsOwner);
        Assert.True(row.IsAdmin);
    }

    [Fact]
    public async Task Two_admins_demoting_each_other_at_once_leave_one_admin_standing()
    {
        var a = await AdminAsync();
        var b = await AdminAsync();

        // Without the shared lock, both could pass the "am I still an admin?" check before
        // either commits, and both would succeed.
        var responses = await Task.WhenAll(Demote(a, b), Demote(b, a));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Forbidden));

        var stillAdmin = await Task.WhenAll(new[] { a, b }.Select(async u =>
            (await u.Client.GetAsync("/admin/users")).StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, stillAdmin.Count(ok => ok));
    }

    [Fact]
    public async Task The_user_list_searches_and_puts_admins_first()
    {
        var admin = await AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var plain = await factory.RegisterAsync($"Aaron {tag}");
        var promoted = await factory.RegisterAsync($"Zelda {tag}");
        await factory.MakeAdminAsync(promoted.User.Id);

        var listed = await ListAsync(admin, tag);

        Assert.Equal([promoted.User.Id, plain.User.Id], listed.Select(u => u.Id));
        Assert.True(listed[0].IsAdmin);
        Assert.False(listed[1].IsAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.PutAsync($"/admin/users/{Guid.NewGuid()}/admin", null)).StatusCode);
    }

    private async Task<TestUser> AdminAsync()
    {
        var user = await factory.RegisterAsync("Admin");
        await factory.MakeAdminAsync(user.User.Id);
        return user;
    }

    private async Task<AuthResponse> SignInAsync(TestUser user)
    {
        var response = await factory.CreateApiClient().PostJsonAsync("/auth/login",
            new LoginRequest { Email = user.User.Email, Password = TestUsers.Password });
        return await response.ReadAsync<AuthResponse>();
    }

    private static Task<HttpResponseMessage> Promote(TestUser admin, TestUser target) =>
        admin.Client.PutAsync($"/admin/users/{target.User.Id}/admin", null);

    private static Task<HttpResponseMessage> Demote(TestUser admin, TestUser target) =>
        admin.Client.DeleteAsync($"/admin/users/{target.User.Id}/admin");

    private static async Task<List<AdminUserDto>> ListAsync(TestUser admin, string search) =>
        await (await admin.Client.GetAsync($"/admin/users?search={Uri.EscapeDataString(search)}")).ReadAsync<List<AdminUserDto>>();
}
