using OrderApi.Models;

namespace OrderApi.Strategies;

public class OrderPricingStrategy : IOrderPricingStrategy
{
    public decimal CalculateTotal(Order order)
    {
        decimal total = order.Items.Sum(
            item => item.Price * item.Quantity);

        if (total > 1000)
            total *= 0.9m;

        if (order.Items.Count > 10)
            total += 50;

        return total;
    }
}
