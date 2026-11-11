using ApexPost.Domain;
using Xunit;

namespace ApexPost.UnitTests.Domain;

public sealed class ValueObjectAndTransactionTests
{
    [Fact]
    public void Branch_code_trims_whitespace_and_normalizes_case()
    {
        var branchCode = new BranchCode("  ind01 ");

        Assert.Equal("IND01", branchCode.Value);
        Assert.Equal("IND01", branchCode.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Branch_code_rejects_blank_values(string value)
    {
        Assert.Throws<DomainException>(() => new BranchCode(value));
    }

    [Fact]
    public void Branch_code_rejects_values_longer_than_twelve_characters()
    {
        Assert.Throws<DomainException>(() => new BranchCode("BRANCH-CODE-TOO-LONG"));
    }

    [Fact]
    public void Sku_trims_whitespace_and_normalizes_case()
    {
        Assert.Equal("APX-001", new Sku(" apx-001 ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sku_rejects_blank_values(string value)
    {
        Assert.Throws<DomainException>(() => new Sku(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Weight_rejects_zero_or_negative_values(decimal kilograms)
    {
        Assert.Throws<DomainException>(() => new Weight(kilograms));
    }

    [Fact]
    public void Money_addition_rounds_to_two_decimal_places()
    {
        var result = new Money(10.105m).Add(new Money(2.105m));

        Assert.Equal(12.21m, result.Amount);
        Assert.Equal("INR", result.Currency);
    }

    [Fact]
    public void Money_addition_rejects_different_currencies()
    {
        Assert.Throws<DomainException>(() => new Money(10m, "INR").Add(new Money(5m, "USD")));
    }

    [Fact]
    public void Tax_rate_returns_configured_rate_for_each_category()
    {
        Assert.Equal(0.18m, BusinessRules.TaxRate(TaxCategory.Standard));
        Assert.Equal(0.05m, BusinessRules.TaxRate(TaxCategory.Reduced));
        Assert.Equal(0m, BusinessRules.TaxRate(TaxCategory.ZeroRated));
    }

    [Fact]
    public void Transaction_cannot_be_paid_when_it_has_no_lines()
    {
        var transaction = new CounterTransaction();

        Assert.Throws<DomainException>(() => transaction.MarkPaid(PaymentMethod.Cash));
        Assert.Equal(TransactionStatus.Open, transaction.Status);
    }

    [Fact]
    public void Transaction_rejects_line_with_invalid_quantity_or_tax_rate()
    {
        var transaction = new CounterTransaction();

        Assert.Throws<DomainException>(() => transaction.AddLine(
            new TransactionLine("line-1", LineKind.RetailProduct, "Box", "APX-001", 0, 10m, 0.18m, 0m)));
        Assert.Throws<DomainException>(() => transaction.AddLine(
            new TransactionLine("line-2", LineKind.RetailProduct, "Box", "APX-001", 1, 10m, 1.1m, 21m)));
        Assert.Empty(transaction.Lines);
    }

    [Fact]
    public void Paid_transaction_can_be_voided_with_a_trimmed_reason()
    {
        var transaction = new CounterTransaction();
        transaction.AddLine(new TransactionLine("line-1", LineKind.RetailProduct, "Box", "APX-001", 1, 10m, 0m, 10m));
        transaction.MarkPaid(PaymentMethod.Cash);

        transaction.Void("  Duplicate transaction  ");

        Assert.Equal(TransactionStatus.Voided, transaction.Status);
        Assert.Equal("Duplicate transaction", transaction.Reason);
    }

    [Fact]
    public void Unpaid_transaction_cannot_be_refunded()
    {
        var transaction = new CounterTransaction();

        Assert.Throws<DomainException>(() => transaction.Refund("Customer request"));
        Assert.Equal(TransactionStatus.Open, transaction.Status);
    }

    [Fact]
    public void Money_order_policy_requires_verification_only_above_threshold()
    {
        var policy = new MoneyOrderPolicy(1000m);

        Assert.False(policy.RequiresIdentityVerification(1000m));
        Assert.True(policy.RequiresIdentityVerification(1000.01m));
    }
}
