using System.Reflection;
using ApexPost.Api.Controllers;
using ApexPost.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace ApexPost.UnitTests;

/// <summary>Guards the role policy requirements declared on the Week 4 API actions.</summary>
public sealed class ApiAuthorizationMetadataTests
{
    [Theory]
    [InlineData(typeof(ShipmentsController), "GetShipment", null)]
    [InlineData(typeof(ShipmentsV2Controller), "GetShipment", null)]
    [InlineData(typeof(ShipmentsController), "GetShipments", null)]
    [InlineData(typeof(ShipmentsV2Controller), "GetShipments", null)]
    public void Shipment_reads_require_authentication_without_a_role_restriction(
        Type controllerType, string actionName, string? expectedRole)
    {
        var authorize = GetAuthorize(controllerType, actionName);
        Assert.NotNull(authorize);
        Assert.Equal(expectedRole, authorize!.Roles);
    }

    [Theory]
    [InlineData(typeof(ShipmentsController), "UpdateShipmentStatus", "Courier")]
    [InlineData(typeof(ShipmentsV2Controller), "UpdateShipmentStatus", "Courier")]
    [InlineData(typeof(ShipmentsController), "UploadScans", "Courier")]
    [InlineData(typeof(ShipmentsV2Controller), "UploadScans", "Courier")]
    [InlineData(typeof(ShipmentsController), "DeleteShipment", "Admin")]
    [InlineData(typeof(ShipmentsV2Controller), "DeleteShipment", "Admin")]
    public void Mutating_actions_have_the_required_role(Type controllerType, string actionName, string expectedRole)
    {
        var authorize = GetAuthorize(controllerType, actionName);
        Assert.NotNull(authorize);
        Assert.Equal(expectedRole, authorize!.Roles);
    }

    [Fact]
    public void Partner_controller_is_anonymous_at_jwt_layer_and_marked_for_api_key_middleware()
    {
        Assert.NotNull(typeof(PartnerShipmentsController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(PartnerShipmentsController).GetCustomAttribute<RequirePartnerApiKeyAttribute>());
    }

    [Theory]
    [InlineData(typeof(ShipmentsController), "CreateShipment", "Admin")]
    [InlineData(typeof(ShipmentsV2Controller), "CreateShipment", "Admin")]
    [InlineData(typeof(ShipmentsController), "UpdateShipment", "Admin")]
    [InlineData(typeof(ShipmentsV2Controller), "UpdateShipment", "Admin")]
    public void Shipment_maintenance_is_admin_only(Type controllerType, string actionName, string expectedRole)
    {
        Assert.Equal(expectedRole, GetAuthorize(controllerType, actionName)?.Roles);
    }

    [Fact]
    public void V1_is_marked_deprecated_and_v2_is_not()
    {
        var v1 = typeof(ShipmentsController).GetCustomAttribute<Asp.Versioning.ApiVersionAttribute>();
        var v2 = typeof(ShipmentsV2Controller).GetCustomAttribute<Asp.Versioning.ApiVersionAttribute>();
        Assert.True(v1!.Deprecated);
        Assert.False(v2!.Deprecated);
    }

    private static AuthorizeAttribute? GetAuthorize(Type controllerType, string actionName)
    {
        var method = controllerType.GetMethod(actionName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        return method!.GetCustomAttribute<AuthorizeAttribute>();
    }
}
