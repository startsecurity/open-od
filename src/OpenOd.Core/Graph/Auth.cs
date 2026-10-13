using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Kiota.Abstractions.Authentication;

namespace OpenOd.Core.Graph;

// Microsoft account sign-in via MSAL (device code flow: open a URL in the
// browser and enter a code). The token is stored in the system keyring
// (libsecret) or, when no keyring is available, in a file readable only by you.
public sealed class Auth : IAccessTokenProvider
{
    // The project's "open-od" app registration in Entra ID. Not a secret:
    // desktop apps are public clients and do not use a client secret.
    public const string DefaultClientId = "b369d8b9-f994-44c5-a502-107689ab42dc";

    public static readonly string[] Scopes = ["Files.Read"];

    private readonly IPublicClientApplication _app;

    private Auth(IPublicClientApplication app) => _app = app;

    public AllowedHostsValidator AllowedHostsValidator { get; } = new(["graph.microsoft.com"]);

    public static async Task<Auth> CreateAsync(string clientId)
    {
        var app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority("https://login.microsoftonline.com/common")
            .WithDefaultRedirectUri()
            .Build();

        var cacheHelper = await CreateCacheHelperAsync();
        cacheHelper.RegisterCache(app.UserTokenCache);
        return new Auth(app);
    }

    public async Task LoginAsync()
    {
        var result = await _app.AcquireTokenWithDeviceCode(Scopes, code =>
        {
            Console.WriteLine();
            Console.WriteLine(code.Message);
            Console.WriteLine();
            return Task.CompletedTask;
        }).ExecuteAsync();

        Console.WriteLine($"Conectado como {result.Account.Username}.");
    }

    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
    {
        var account = (await _app.GetAccountsAsync()).FirstOrDefault()
            ?? throw new InvalidOperationException("No account signed in. Run: open-od login");

        try
        {
            var result = await _app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            throw new InvalidOperationException("The session expired. Run: open-od login");
        }
    }

    public async Task LogoutAsync()
    {
        foreach (var account in await _app.GetAccountsAsync())
            await _app.RemoveAsync(account);
    }

    private static async Task<MsalCacheHelper> CreateCacheHelperAsync()
    {
        var dir = Paths.CacheDir;
        Directory.CreateDirectory(dir);

        var keyring = new StorageCreationPropertiesBuilder("msal.cache", dir)
            .WithLinuxKeyring(
                "open-od.tokencache", MsalCacheHelper.LinuxKeyRingDefaultCollection, "open-od token cache",
                new KeyValuePair<string, string>("Version", "1"),
                new KeyValuePair<string, string>("ProductGroup", "open-od"))
            .Build();

        var helper = await MsalCacheHelper.CreateAsync(keyring);
        try
        {
            helper.VerifyPersistence();
            return helper;
        }
        catch (MsalCachePersistenceException)
        {
            Log.Warn("system keyring unavailable; storing the token in a plain file instead");
            var file = new StorageCreationPropertiesBuilder("msal.cache", dir)
                .WithLinuxUnprotectedFile()
                .Build();
            return await MsalCacheHelper.CreateAsync(file);
        }
    }
}
