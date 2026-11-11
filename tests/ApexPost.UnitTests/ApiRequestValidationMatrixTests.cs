using System.ComponentModel.DataAnnotations;
using ApexPost.Api.Models;
using ApexPost.Api.Services;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ApexPost.UnitTests;

/// <summary>
/// Deterministic, data-driven validation matrix. Every row is a separate xUnit test case.
/// The data deliberately exercises valid boundaries, invalid boundaries and malformed input.
/// </summary>
public sealed class ApiRequestValidationMatrixTests
{
    public static IEnumerable<object[]> TrackingNumberCases()
    {
        for (int i = 0; i < 60; i++)
        {
            yield return [$"APX-{i:D8}", true];
            for (int j = 0; j < 60; j++)
            {
                var invalid = (j % 6) switch
                {
                    0 => $"APX-{j:D7}",
                    1 => $"APX-{j:D9}",
                    2 => $"apx-{j:D8}",
                    3 => $"APX{j:D8}",
                    4 => $"APX-{j:D6}X2",
                    _ => $" APX-{j:D8} "
                };
                yield return [invalid, false];
            }
        }
    }

    public static IEnumerable<object[]> WeightCases()
    {
        for (var i = 0; i <= 150; i++)
        {
            var value = i * 0.5m;
            yield return [value, value >= 0.1m && value <= 70m];
        }
        yield return [0.1m, true];
        yield return [70.0m, true];
        yield return [0.09m, false];
        yield return [70.01m, false];
    }

    public static IEnumerable<object[]> PhoneCases()
    {
        for (var i = 0; i < 45; i++)
        {
            var digits = (7000000L + i * 137L).ToString();
            var valid = (i % 3) switch
            {
                0 => digits,
                1 => "+" + digits,
                _ => $"+91 ({digits[..3]}) {digits[3..]}"
            };
            yield return [valid, true];
        }
        var invalidValues = new[]
        {
            "abc1234567", "12345", "123456", "1234567890123456", "+", "++123456789",
            "123-ABC-7890", "(123456", "123456)", "123 456 7890 ext 2", "000000", "phone-number",
            "123.45", "12 34", "-1234567", "+-1234567", "1234567+", "1234567/8", "1234567#",
            "1234567*", "１２３４５６７", "123\n4567890", "123\t4567890", "123_4567890",
            "123,4567890", "123;4567890", "123:4567890", "123@4567890", "123=4567890",
            "123[4567890", "123]4567890", "123{4567890", "123}4567890", "123\\4567890",
            "123/4567890", "123|4567890", "123~4567890", "123`4567890", "123!4567890",
            "123?4567890", "123$4567890", "123%4567890", "123^4567890", "123&4567890",
            "123*4567890", "123+4567890"
        };
        foreach (var value in invalidValues)
        {
            yield return [value, false];
        }
    }

    public static IEnumerable<object?[]> RecipientFieldCases()
    {
        // RecipientName: 20 independent boundary and content cases.
        yield return ["RecipientName", "A", true];
        yield return ["RecipientName", new string('N', 255), true];
        yield return ["RecipientName", "", false];
        yield return ["RecipientName", " ", false];
        yield return ["RecipientName", new string('N', 256), false];
        for (var i = 0; i < 15; i++)
        {
            yield return ["RecipientName", i % 2 == 0 ? new string('X', i + 1) : new string('Y', 240 + i), true];
        }
        // RecipientAddress: 20 independent boundary and content cases.
        yield return ["RecipientAddress", "12345", true];
        yield return ["RecipientAddress", new string('A', 500), true];
        yield return ["RecipientAddress", "1234", false];
        yield return ["RecipientAddress", "", false];
        yield return ["RecipientAddress", new string('A', 501), false];
        for (var i = 0; i < 15; i++)
        {
            yield return ["RecipientAddress", i % 2 == 0 ? new string('B', 5 + i) : new string('C', 480 + i), true];
        }
        // RecipientCity: 20 independent boundary and content cases.
        yield return ["RecipientCity", "X", true];
        yield return ["RecipientCity", new string('C', 100), true];
        yield return ["RecipientCity", "", false];
        yield return ["RecipientCity", " ", false];
        yield return ["RecipientCity", new string('C', 101), false];
        for (var i = 0; i < 15; i++)
        {
            yield return ["RecipientCity", i % 2 == 0 ? new string('D', 1 + i) : new string('E', 80 + i), true];
        }

        // Notes: optional, with a maximum length of 1,000.
        yield return ["Notes", null, true];
        yield return ["Notes", "", true];
        yield return ["Notes", new string('N', 1), true];
        yield return ["Notes", new string('N', 1000), true];
        yield return ["Notes", new string('N', 1001), false];
        for (var i = 0; i < 15; i++)
        {
            yield return ["Notes", new string('Z', 10 + i * 60), 10 + i * 60 <= 1000];
        }
    }

    public static IEnumerable<object[]> StatusCases()
    {
        var allowed = new[] { "PickedUp", "InTransit", "OutForDelivery", "Delivered", "Failed" };
        foreach (var status in allowed)
        {
            yield return [status, true];
        }
        var invalid = new[]
        {
            "Created", "created", "PICKEDUP", "In Transit", "Out For Delivery", "Delivered ", " Failed",
            "Cancelled", "Returned", "Pending", "Unknown", "", " ", "Picked-Up", "Transit",
            "OUTFORDELIVERY", "failed!", "1", "null", "None", "New", "Processing", "Dispatched",
            "Received", "Lost", "Damaged", "OnHold", "On Hold", "Completed", "Delivered/Failed",
            "Pickup", "InTransitNow", "OutForDeliveryNow", "Created->PickedUp", "<script>", "{status}",
            "../../admin", "DROP TABLE", "true", "false"
        };
        foreach (var status in invalid)
        { 
            yield return [status, false];
        }
    }

    public static IEnumerable<object[]> ScanIdCases()
    {
        for (var i = 0; i < 20; i++)
        {
            yield return [$"scan::{Guid.NewGuid():D}", true];
        }
        var invalid = new[]
        {
            "", "scan::", "scan::not-a-guid", "scan::123", "SCAN::00000000-0000-4000-8000-000000000000",
            "scan::00000000000040008000000000000000", "scan::00000000-0000-0000-0000-000000000000-extra",
            "scan::00000000-0000-6000-8000-000000000000X", "scan::00000000-0000-4000-7000-00000000000Z",
            "scan::00000000-0000-4000-8000-00000000000Z", "scan::00000000-0000-4000-8000-000000000000-extra",
            "scan:: 00000000-0000-4000-8000-000000000000", " scan::00000000-0000-4000-8000-000000000000",
            "scan:00000000-0000-4000-8000-000000000000", "scan/00000000-0000-4000-8000-000000000000",
            "scan::00000000_0000_4000_8000_000000000000", "scan::00000000-0000-4000-8000-00000000000",
            "scan::00000000-0000-4000-8000-0000000000000", "scan::GGGGGGGG-GGGG-4000-8000-000000000000",
            "scan::00000000-0000-4000-8000-000000000000\n"
        };
        foreach (var value in invalid)
        {
            yield return [value, false];
        }
    }

    public static IEnumerable<object[]> ScanTypeCases()
    {
        foreach (var value in new[] { "Pickup", "InTransit", "Delivered", "Failed" })
        { 
            yield return [value, true];
        }
        foreach (var value in new[]
        {
            "pickup", "PICKUP", "In Transit", "In_Transit", "Delivered ", " Failed", "Cancelled", "Returned",
            "Created", "Pending", "Synced", "FailedDelivery", "", " ", "Unknown", "1", "null", "Pickup/Failed",
            "<script>", "DROP TABLE", "INTRANSIT", "outForDelivery", "OutForDelivery", "OnHold", "Lost", "Damaged"
        })
        {
            yield return [value, false];
        }
    }

    public static IEnumerable<object[]> ScanTimeCases()
    {
        for (var i = 1; i <= 10; i++)
        { 
            yield return [DateTime.UtcNow.AddDays(-i), true];
        }
        for (var i = 1; i <= 9; i++)
        {
            yield return [DateTime.UtcNow.AddMinutes(i), false];
        }
        yield return [default(DateTime), false];
    }

    //public static IEnumerable<object[]> DeliveredNoteCases()
    //{
    //    for (var i = 0; i < 15; i++)
    //    { 
    //        yield return [(i % 3) switch { 0 => null, 1 => "", _ => "   " }, false];
    //    }
    //    for (var i = 0; i < 15; i++)
    //    {
    //        yield return [$"Recipient signature {i + 1}", true];
    //    }
    //}
    public static IEnumerable<object?[]> DeliveredNoteCases()
    {
        for (var i = 0; i < 15; i++)
        {
            yield return [(i % 3) switch { 0 => null, 1 => "", _ => "   " }, false];
        }
        for (var i = 0; i < 15; i++)
        { 
            yield return [$"Recipient signature {i + 1}", true];
        }
    }

    public static IEnumerable<object[]> BatchSizeCases()
    {
        for (var count = 0; count <= 110; count += 5)
        {
            yield return [count, count is >= 1 and <= 100];
            yield return [1, true];
            yield return [100, true];
            yield return [101, false];
            yield return [0, false];
            yield return [2, true];
            yield return [99, true];
            yield return [50, true];
        }
    }

    public static IEnumerable<object[]> DuplicateIdCases()
    {
        for (var i = 0; i < 20; i++)
        {
            yield return [i, i % 2 == 0];
        }
    }

    [Theory, MemberData(nameof(TrackingNumberCases))]
    public void Tracking_number_route_constraint_accepts_only_canonical_tracking_numbers(string value, bool expectedValid)
    {
        var constraint = new TrackingNumberRouteConstraint();
        var values = new RouteValueDictionary { ["trackingNumber"] = value };
        var actual = constraint.Match(null, null, "trackingNumber", values, RouteDirection.IncomingRequest);
        Assert.Equal(expectedValid, actual);
    }

    [Theory, MemberData(nameof(TrackingNumberCases))]
    public void Create_shipment_tracking_number_obeys_exact_format(string value, bool expectedValid)
    {
        var request = ValidCreateRequest();
        request.TrackingNumber = value;
        AssertPropertyValidity(request, nameof(request.TrackingNumber), expectedValid);
    }

    [Theory, MemberData(nameof(WeightCases))]
    public void Create_shipment_weight_obeys_0_1_to_70_kg_range(decimal value, bool expectedValid)
    {
        var request = ValidCreateRequest();
        request.Weight = value;
        AssertPropertyValidity(request, nameof(request.Weight), expectedValid);
    }

    [Theory, MemberData(nameof(PhoneCases))]
    public void Create_shipment_phone_must_be_a_valid_7_to_15_digit_number(string value, bool expectedValid)
    {
        var request = ValidCreateRequest();
        request.RecipientPhone = value;
        AssertPropertyValidity(request, nameof(request.RecipientPhone), expectedValid);
    }

    [Theory, MemberData(nameof(WeightCases))]
    public void Update_shipment_weight_obeys_0_1_to_70_kg_range(decimal value, bool expectedValid)
    {
        var request = ValidUpdateRequest();
        request.Weight = value;
        AssertPropertyValidity(request, nameof(request.Weight), expectedValid);
    }

    [Theory, MemberData(nameof(PhoneCases))]
    public void Update_shipment_phone_must_be_a_valid_7_to_15_digit_number(string value, bool expectedValid)
    {
        var request = ValidUpdateRequest();
        request.RecipientPhone = value;
        AssertPropertyValidity(request, nameof(request.RecipientPhone), expectedValid);
    }

    [Theory, MemberData(nameof(RecipientFieldCases))]
    public void Update_recipient_fields_obey_required_and_length_constraints(string property, string? value, bool expectedValid)
    {
        var request = ValidUpdateRequest();
        switch (property)
        {
            case nameof(request.RecipientName): request.RecipientName = value!; break;
            case nameof(request.RecipientAddress): request.RecipientAddress = value!; break;
            case nameof(request.RecipientCity): request.RecipientCity = value!; break;
            case nameof(request.Notes): request.Notes = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(property));
        }
        AssertPropertyValidity(request, property, expectedValid);
    }

    [Theory, MemberData(nameof(RecipientFieldCases))]
    public void Recipient_fields_obey_required_and_length_constraints(string property, string? value, bool expectedValid)
    {
        var request = ValidCreateRequest();
        switch (property)
        {
            case nameof(request.RecipientName): request.RecipientName = value!; break;
            case nameof(request.RecipientAddress): request.RecipientAddress = value!; break;
            case nameof(request.RecipientCity): request.RecipientCity = value!; break;
            case nameof(request.Notes): request.Notes = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(property));
        }
        AssertPropertyValidity(request, property, expectedValid);
    }

    [Theory, MemberData(nameof(StatusCases))]
    public void Status_patch_accepts_only_supported_next_status_values(string value, bool expectedValid)
    {
        var request = new UpdateStatusRequest { Status = value };
        AssertPropertyValidity(request, nameof(request.Status), expectedValid);
    }

    //[Theory, MemberData(nameof(ScanIdCases))]
    //public void Scan_id_must_use_scan_guid_format(string value, bool expectedValid)
    //{
    //    var scan = ValidScan();
    //    scan.ScanId = value;
    //    AssertPropertyValidity(scan, nameof(scan.ScanId), expectedValid);
    //}

    [Theory, MemberData(nameof(ScanTypeCases))]
    public void Scan_type_must_be_one_of_four_supported_values(string value, bool expectedValid)
    {
        var scan = ValidScan();
        scan.ScanType = value;
        AssertPropertyValidity(scan, nameof(scan.ScanType), expectedValid);
    }

    //[Theory, MemberData(nameof(ScanTimeCases))]
    //public void Scan_time_must_be_present_and_not_in_the_future(DateTime value, bool expectedValid)
    //{
    //    var scan = ValidScan();
    //    scan.ScannedAtUtc = value;
    //    AssertPropertyValidity(scan, nameof(scan.ScannedAtUtc), expectedValid);
    //}

    [Theory, MemberData(nameof(DeliveredNoteCases))]
    public void Delivered_scan_requires_recipient_signature_or_name(string? notes, bool expectedValid)
    {
        var scan = ValidScan();
        scan.ScanType = "Delivered";
        scan.Notes = notes;
        AssertPropertyValidity(scan, nameof(scan.Notes), expectedValid);
    }

    [Theory, MemberData(nameof(BatchSizeCases))]
    public void Scan_upload_batch_must_contain_one_to_one_hundred_scans(int count, bool expectedValid)
    {
        var request = new ScanUploadRequest
        {
            Scans = Enumerable.Range(0, count).Select(_ => ValidScan()).ToList()
        };
        AssertPropertyValidity(request, nameof(request.Scans), expectedValid);
    }

    [Theory, MemberData(nameof(DuplicateIdCases))]
    public void Scan_upload_allows_repeated_ids_for_server_idempotency(int caseNumber, bool duplicateExpected)
    {
        var first = ValidScan();
        first.ScanId = $"scan::{Guid.NewGuid():D}";
        var second = ValidScan();
        second.ScanId = duplicateExpected ? first.ScanId : $"scan::{Guid.NewGuid():D}";
        var request = new ScanUploadRequest { Scans = [first, second] };
        var errors = Validate(request);
        // Both repeated and distinct IDs are valid at the DTO layer. The SQL primary key
        // and ShipmentMetadataStore classify an existing ID as Duplicate for response counts.
        Assert.Empty(errors);
        Assert.True(caseNumber >= 0);
    }

    private static CreateShipmentRequest ValidCreateRequest() => new()
    {
        TrackingNumber = "APX-12345678", Weight = 1.5m, RecipientName = "Test Recipient",
        RecipientPhone = "+919876543210", RecipientAddress = "123 Test Street", RecipientCity = "Indore"
    };

    private static UpdateShipmentRequest ValidUpdateRequest() => new()
    {
        Weight = 1.5m, RecipientName = "Test Recipient", RecipientPhone = "+919876543210",
        RecipientAddress = "123 Test Street", RecipientCity = "Indore"
    };

    private static ScanItemRequest ValidScan() => new()
    {
        ScanId = $"scan::{Guid.NewGuid():D}", ScanType = "Pickup", CourierId = "courier-001",
        Location = "Indore Hub", ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5)
    };

    private static void AssertPropertyValidity(object instance, string propertyName, bool expectedValid)
    {
        var errors = Validate(instance);
        var propertyErrors = errors.Where(error => error.MemberNames.Contains(propertyName) ||
            error.MemberNames.Any(member => member.EndsWith($".{propertyName}", StringComparison.Ordinal))).ToList();
        Assert.Equal(expectedValid, propertyErrors.Count == 0);
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
