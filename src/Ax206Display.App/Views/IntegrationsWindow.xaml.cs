using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Ax206Display.Config.Models;
using Ax206Display.Config.Secrets;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.Http;
using Ax206Display.DataSources.PiHole;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.DataSources.UniFi;

namespace Ax206Display.App.Views;

/// <summary>
/// Lets the user configure the Proxmox, Pi-hole, and UniFi integrations
/// without editing config.json or the secret store by hand - each on its own
/// tab. Each section is independently testable/saveable/removable; a saved
/// password/API token is never redisplayed - the field stays blank and
/// "Test &amp; Save" reuses the existing secret unless a new value is entered.
/// </summary>
public partial class IntegrationsWindow : Window
{
    private const string ProxmoxKind = "proxmox";
    private const string PiHoleKind = "pihole";
    private const string UniFiKind = "unifi";

    private readonly ConfigService _configService;
    private readonly SecretStore _secretStore;

    public IntegrationsWindow(ConfigService configService, SecretStore secretStore)
    {
        InitializeComponent();
        Theme.DarkTitleBar.Apply(this);
        _configService = configService;
        _secretStore = secretStore;

        Loaded += async (_, _) => await LoadExistingAsync();
    }

    private async Task LoadExistingAsync()
    {
        try
        {
            var config = await _configService.LoadAsync();

            if (config.Integrations.FirstOrDefault(i => i.Kind == ProxmoxKind) is { } proxmox)
            {
                ProxmoxUrlTextBox.Text = proxmox.BaseUrl;
                ProxmoxUsernameTextBox.Text = proxmox.Username ?? string.Empty;
                ProxmoxRealmTextBox.Text = proxmox.Realm ?? "pam";
                ProxmoxThumbprintTextBox.Text = proxmox.PinnedCertificateSha256Thumbprint ?? string.Empty;
                SetProxmoxStatus("Configured. Leave the password blank to keep the saved one.");
            }

            if (config.Integrations.FirstOrDefault(i => i.Kind == PiHoleKind) is { } pihole)
            {
                PiHoleUrlTextBox.Text = pihole.BaseUrl;
                PiHoleThumbprintTextBox.Text = pihole.PinnedCertificateSha256Thumbprint ?? string.Empty;
                SetPiHoleStatus("Configured. Leave the token blank to keep the saved one.");
            }

            if (config.Integrations.FirstOrDefault(i => i.Kind == UniFiKind) is { } unifi)
            {
                UniFiUrlTextBox.Text = unifi.BaseUrl;
                UniFiUsernameTextBox.Text = unifi.Username ?? string.Empty;
                UniFiSiteTextBox.Text = unifi.Site ?? "default";
                UniFiThumbprintTextBox.Text = unifi.PinnedCertificateSha256Thumbprint ?? string.Empty;
                SetUniFiStatus("Configured. Leave the password blank to keep the saved one.");
            }
        }
        catch (Exception ex)
        {
            SetProxmoxStatus("Could not load existing settings: " + ex.Message);
        }
    }

    private async void OnProxmoxTestAndSaveClick(object sender, RoutedEventArgs e)
    {
        SetProxmoxStatus("Testing...");
        try
        {
            var baseUrl = ProxmoxUrlTextBox.Text.Trim();
            var username = ProxmoxUsernameTextBox.Text.Trim();
            var realm = string.IsNullOrWhiteSpace(ProxmoxRealmTextBox.Text) ? "pam" : ProxmoxRealmTextBox.Text.Trim();
            var thumbprint = string.IsNullOrWhiteSpace(ProxmoxThumbprintTextBox.Text) ? null : ProxmoxThumbprintTextBox.Text.Trim();

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(username))
            {
                SetProxmoxStatus("Host URL and username are required.");
                return;
            }

            var config = await _configService.LoadAsync();
            var existing = config.Integrations.FirstOrDefault(i => i.Kind == ProxmoxKind);

            var password = await ResolveSecretAsync(ProxmoxPasswordBox.Password, existing);
            if (string.IsNullOrEmpty(password))
            {
                SetProxmoxStatus("Password is required.");
                return;
            }

            var integrationId = existing?.Id ?? Guid.NewGuid().ToString("N");
            var secretKey = existing?.SecretKey ?? $"integration.{integrationId}";

            var testConfig = new IntegrationConfig
            {
                Id = integrationId,
                Kind = ProxmoxKind,
                BaseUrl = baseUrl,
                Username = username,
                Realm = realm,
                SecretKey = secretKey,
                PinnedCertificateSha256Thumbprint = thumbprint,
            };

            using var httpClient = IntegrationHttpClientFactory.Create(testConfig, enableCookies: false);
            var client = new ProxmoxClient(httpClient);
            await client.LoginAsync(username, password, realm);
            var guests = await client.GetGuestStatusesAsync();

            await SaveIntegrationAsync(config, testConfig, secretKey, password);

            SetProxmoxStatus($"Connected - found {guests.Count} guest(s). Saved.");
        }
        catch (Exception ex)
        {
            SetProxmoxStatus("Failed: " + ex.Message);
        }
    }

    private async void OnPiHoleTestAndSaveClick(object sender, RoutedEventArgs e)
    {
        SetPiHoleStatus("Testing...");
        try
        {
            var baseUrl = PiHoleUrlTextBox.Text.Trim();
            var thumbprint = string.IsNullOrWhiteSpace(PiHoleThumbprintTextBox.Text) ? null : PiHoleThumbprintTextBox.Text.Trim();

            if (string.IsNullOrEmpty(baseUrl))
            {
                SetPiHoleStatus("Host URL is required.");
                return;
            }

            var config = await _configService.LoadAsync();
            var existing = config.Integrations.FirstOrDefault(i => i.Kind == PiHoleKind);

            var token = await ResolveSecretAsync(PiHoleTokenBox.Password, existing);
            if (string.IsNullOrEmpty(token))
            {
                SetPiHoleStatus("API token is required.");
                return;
            }

            var integrationId = existing?.Id ?? Guid.NewGuid().ToString("N");
            var secretKey = existing?.SecretKey ?? $"integration.{integrationId}";

            var testConfig = new IntegrationConfig
            {
                Id = integrationId,
                Kind = PiHoleKind,
                BaseUrl = baseUrl,
                SecretKey = secretKey,
                PinnedCertificateSha256Thumbprint = thumbprint,
            };

            using var httpClient = IntegrationHttpClientFactory.Create(testConfig, enableCookies: false);
            var client = new PiHoleClient(httpClient, token);
            var summary = await client.GetSummaryAsync();

            await SaveIntegrationAsync(config, testConfig, secretKey, token);

            SetPiHoleStatus($"Connected - status: {summary.Status}. Saved.");
        }
        catch (Exception ex)
        {
            SetPiHoleStatus("Failed: " + ex.Message);
        }
    }

    private async void OnUniFiTestAndSaveClick(object sender, RoutedEventArgs e)
    {
        SetUniFiStatus("Testing...");
        try
        {
            var baseUrl = UniFiUrlTextBox.Text.Trim();
            var username = UniFiUsernameTextBox.Text.Trim();
            var site = string.IsNullOrWhiteSpace(UniFiSiteTextBox.Text) ? "default" : UniFiSiteTextBox.Text.Trim();
            var thumbprint = string.IsNullOrWhiteSpace(UniFiThumbprintTextBox.Text) ? null : UniFiThumbprintTextBox.Text.Trim();

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(username))
            {
                SetUniFiStatus("Host URL and username are required.");
                return;
            }

            var config = await _configService.LoadAsync();
            var existing = config.Integrations.FirstOrDefault(i => i.Kind == UniFiKind);

            var password = await ResolveSecretAsync(UniFiPasswordBox.Password, existing);
            if (string.IsNullOrEmpty(password))
            {
                SetUniFiStatus("Password is required.");
                return;
            }

            var integrationId = existing?.Id ?? Guid.NewGuid().ToString("N");
            var secretKey = existing?.SecretKey ?? $"integration.{integrationId}";

            var testConfig = new IntegrationConfig
            {
                Id = integrationId,
                Kind = UniFiKind,
                BaseUrl = baseUrl,
                Username = username,
                Site = site,
                SecretKey = secretKey,
                PinnedCertificateSha256Thumbprint = thumbprint,
            };

            using var httpClient = IntegrationHttpClientFactory.Create(testConfig, enableCookies: true);
            var client = new UniFiClient(httpClient);
            await client.LoginAsync(username, password);
            var status = await client.GetSiteHealthAsync(site);

            await SaveIntegrationAsync(config, testConfig, secretKey, password);

            SetUniFiStatus($"Connected - {status.Subsystems.Count} subsystem(s) reporting. Saved.");
        }
        catch (Exception ex)
        {
            SetUniFiStatus("Failed: " + ex.Message);
        }
    }

    /// <summary>Returns the freshly entered secret, or - if the field was left blank - the previously saved one for this integration.</summary>
    private async Task<string?> ResolveSecretAsync(string enteredValue, IntegrationConfig? existing)
    {
        if (!string.IsNullOrEmpty(enteredValue))
        {
            return enteredValue;
        }

        if (existing?.SecretKey is not { } existingKey)
        {
            return null;
        }

        await _secretStore.LoadAsync();
        return _secretStore.GetSecret(existingKey);
    }

    private async Task SaveIntegrationAsync(AppConfig config, IntegrationConfig testConfig, string secretKey, string secretValue)
    {
        await _secretStore.LoadAsync();
        _secretStore.SetSecret(secretKey, secretValue);
        await _secretStore.SaveAsync();

        var updatedIntegrations = config.Integrations.Where(i => i.Kind != testConfig.Kind).ToList();
        updatedIntegrations.Add(testConfig);
        await _configService.SaveAsync(config with { Integrations = updatedIntegrations });
    }

    private async void OnProxmoxRemoveClick(object sender, RoutedEventArgs e)
    {
        await RemoveIntegrationAsync(ProxmoxKind, SetProxmoxStatus);
        ProxmoxPasswordBox.Password = string.Empty;
    }

    private async void OnPiHoleRemoveClick(object sender, RoutedEventArgs e)
    {
        await RemoveIntegrationAsync(PiHoleKind, SetPiHoleStatus);
        PiHoleTokenBox.Password = string.Empty;
    }

    private async void OnUniFiRemoveClick(object sender, RoutedEventArgs e)
    {
        await RemoveIntegrationAsync(UniFiKind, SetUniFiStatus);
        UniFiPasswordBox.Password = string.Empty;
    }

    private async Task RemoveIntegrationAsync(string kind, Action<string> setStatus)
    {
        try
        {
            var config = await _configService.LoadAsync();
            var existing = config.Integrations.FirstOrDefault(i => i.Kind == kind);
            if (existing is null)
            {
                setStatus("Nothing to remove.");
                return;
            }

            if (existing.SecretKey is { } secretKey)
            {
                await _secretStore.LoadAsync();
                _secretStore.RemoveSecret(secretKey);
                await _secretStore.SaveAsync();
            }

            var updated = config.Integrations.Where(i => i.Kind != kind).ToList();
            await _configService.SaveAsync(config with { Integrations = updated });

            setStatus("Removed.");
        }
        catch (Exception ex)
        {
            setStatus("Could not remove: " + ex.Message);
        }
    }

    private async void OnProxmoxDetectCertificateClick(object sender, RoutedEventArgs e)
    {
        await DetectCertificateAsync(ProxmoxUrlTextBox.Text, ProxmoxThumbprintTextBox, SetProxmoxStatus);
    }

    private async void OnPiHoleDetectCertificateClick(object sender, RoutedEventArgs e)
    {
        await DetectCertificateAsync(PiHoleUrlTextBox.Text, PiHoleThumbprintTextBox, SetPiHoleStatus);
    }

    private async void OnUniFiDetectCertificateClick(object sender, RoutedEventArgs e)
    {
        await DetectCertificateAsync(UniFiUrlTextBox.Text, UniFiThumbprintTextBox, SetUniFiStatus);
    }

    private static async Task DetectCertificateAsync(string baseUrlText, TextBox thumbprintTextBox, Action<string> setStatus)
    {
        try
        {
            if (!Uri.TryCreate(baseUrlText.Trim(), UriKind.Absolute, out var uri))
            {
                setStatus("Enter a valid host URL first.");
                return;
            }

            var certificate = await TlsCertificateProbe.FetchCertificateAsync(uri.Host, uri.Port);
            if (certificate is null)
            {
                setStatus("Could not retrieve a certificate from that host - is it using HTTPS?");
                return;
            }

            var thumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            var trusted = MessageBox.Show(
                $"Certificate thumbprint (SHA-256):\n{thumbprint}\n\nTrust this certificate for this connection?",
                "Detected Certificate",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;

            if (trusted)
            {
                thumbprintTextBox.Text = thumbprint;
                setStatus("Certificate thumbprint set - click Test & Save to apply it.");
            }
        }
        catch (Exception ex)
        {
            setStatus("Could not detect a certificate: " + ex.Message);
        }
    }

    private void SetProxmoxStatus(string text) => ProxmoxStatusText.Text = text;

    private void SetPiHoleStatus(string text) => PiHoleStatusText.Text = text;

    private void SetUniFiStatus(string text) => UniFiStatusText.Text = text;
}
