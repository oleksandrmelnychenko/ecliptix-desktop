using System;
using System.Security.Cryptography.X509Certificates;
using Ecliptix.Security.Pinning.Encryption;
using Ecliptix.Security.Pinning.Interceptors;
using Ecliptix.Security.Pinning.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ecliptix.Security.Pinning.Configuration;

public static class PinningServiceCollectionExtensions
{
    public static IServiceCollection AddSslPinning(this IServiceCollection services)
    {
        services.AddTransient<SecureHandshakeInterceptor>();

        return services;
    }

    public static IServiceCollection AddApplicationLayerSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ApplicationSecurityOptions>? configureOptions = null)
    {
        ApplicationSecurityOptions options = new();
        configuration.GetSection("ApplicationSecurity").Bind(options);
        services.AddSingleton(Options.Create(options));

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddSingleton<IApplicationLayerEncryption>(provider =>
        {
            ApplicationSecurityOptions options = provider.GetRequiredService<IOptions<ApplicationSecurityOptions>>().Value;
            IMessageSigning messageSigning = provider.GetRequiredService<IMessageSigning>();
            return new ApplicationLayerEncryptionService(messageSigning, options.ToEncryptionOptions());
        });

        services.AddSingleton<IMessageSigning>(provider =>
        {
            ApplicationSecurityOptions options = provider.GetRequiredService<IOptions<ApplicationSecurityOptions>>().Value;
            X509Certificate2? clientCert = GetClientCertificate(options.ClientCertificateThumbprint);
            return new MessageSigningService(clientCert);
        });

        services.AddSingleton<IKeyProvider>(provider =>
        {
            ApplicationSecurityOptions options = provider.GetRequiredService<IOptions<ApplicationSecurityOptions>>().Value;
            X509Certificate2? clientCert = GetClientCertificate(options.ClientCertificateThumbprint);

            if (!string.IsNullOrEmpty(options.ServerPublicKeyHex))
            {
                return new ConfigurationKeyProvider(options.ServerPublicKeyHex, clientCert);
            }

            throw new InvalidOperationException("Server public key must be configured");
        });

        services.AddTransient<SecureHandshakeInterceptor>(provider =>
        {
            ApplicationSecurityOptions options = provider.GetRequiredService<IOptions<ApplicationSecurityOptions>>().Value;

            if (!options.EnableApplicationLayerSecurity)
            {
                return new SecureHandshakeInterceptor();
            }

            IApplicationLayerEncryption encryption = provider.GetRequiredService<IApplicationLayerEncryption>();
            IKeyProvider keyProvider = provider.GetRequiredService<IKeyProvider>();

            return new SecureHandshakeInterceptor(encryption, keyProvider, options);
        });

        return services.AddSslPinning();
    }

    private static X509Certificate2? GetClientCertificate(string? thumbprint)
    {
        if (string.IsNullOrEmpty(thumbprint))
            return null;

        using X509Store store = new(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        X509Certificate2Collection certificates = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            thumbprint,
            false);

        return certificates.Count > 0 ? certificates[0] : null;
    }
}