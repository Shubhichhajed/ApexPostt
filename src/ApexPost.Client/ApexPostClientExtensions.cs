using System.Text.Json.Serialization;
using ApexPost.Client.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Client
{
    /// <summary>
    /// Hand-written additions that sit next to the generated client. Nothing here edits generated code:
    /// new members go into partial classes or extension methods.
    /// </summary>
    public static class ApexPostClientExtensions
    {
        /// <summary>Registers the generated client as a typed client through IHttpClientFactory.</summary>
        public static IHttpClientBuilder AddApexPostClient(this IServiceCollection services, Uri baseAddress)
        {
            ArgumentNullException.ThrowIfNull(baseAddress);
            return services.AddHttpClient<IApexPostClient, ApexPostClient>(client => client.BaseAddress = baseAddress);
        }

        /// <summary>Returns null instead of throwing when the shipment does not exist (HTTP 404).</summary>
        public static async Task<ShipmentResponseV2?> FindShipmentAsync(this IApexPostClient client, string trackingNumber,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await client.GetShipmentV2Async(trackingNumber, cancellationToken);
            }
            catch (ApexPostApiException ex) when (ex.StatusCode == 404)
            {
                return null;
            }
        }
    }
}

namespace ApexPost.Client.Generated
{
    /// <summary>Extends the generated DTO through its partial declaration.</summary>
    public partial class ShipmentResponseV2
    {
        /// <summary>One-line summary for console output.</summary>
        [JsonIgnore]
        public string Summary => $"{TrackingNumber} [{Status}] to {Recipient?.Name} ({Recipient?.City})";
    }
}
