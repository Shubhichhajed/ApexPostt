namespace ApexPost.Domain;

public static class DecimalMoneyExtensions
{
    extension(decimal value)
    {
        public decimal RoundMoney() => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed class LowStockRule(int threshold)
{
    public int Threshold { get; } = Math.Max(0, threshold);
    public Predicate<StockItem> IsLow => stock => stock.Quantity <= Threshold;
}

public interface ISpecification<in T>
{
    bool IsSatisfiedBy(T candidate);
}

public sealed class LowStockSpecification(LowStockRule rule) : ISpecification<StockItem>
{
    public bool IsSatisfiedBy(StockItem candidate) => rule.IsLow(candidate);
}

public sealed class DomainNotifications
{
    public event Action<CounterTransaction>? TransactionCompleted;
    public event Action<StockItem>? StockLow;
    public event Action<MoneyOrder>? MoneyOrderIssued;
    public event Action<MoneyOrder>? HighValueMoneyOrderIssued;
    public event Action<CounterTransaction>? TransactionVoided;
    public event Action<CounterTransaction>? TransactionRefunded;
    public void RaiseTransactionCompleted(CounterTransaction transaction) => TransactionCompleted?.Invoke(transaction);
    public void RaiseStockLow(StockItem stock) => StockLow?.Invoke(stock);
    public void RaiseMoneyOrderIssued(MoneyOrder order) => MoneyOrderIssued?.Invoke(order);
    public void RaiseHighValueMoneyOrderIssued(MoneyOrder order) => HighValueMoneyOrderIssued?.Invoke(order);
    public void RaiseTransactionVoided(CounterTransaction transaction) => TransactionVoided?.Invoke(transaction);
    public void RaiseTransactionRefunded(CounterTransaction transaction) => TransactionRefunded?.Invoke(transaction);
}
