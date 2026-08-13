using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrderApi.Models;
using OrderApi.Repositories;
using OrderApi.Services;
using OrderApi.Strategies;

namespace OrderApi.Tests;

[TestClass]
public class OrderServiceTests
{
    [TestMethod]
    public async Task CreateOrderAsync_CalculatesTotal()
    {
        var repository = new FakeOrderRepository();

        var service = new OrderService(
            repository,
            new OrderPricingStrategy(),
            NullLogger<OrderService>.Instance);

        var order = new Order
        {
            Customer = new Customer
            {
                Name = "Sumit",
                Email = "sumit@example.com"
            },
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ProductName = "Laptop",
                    Price = 800,
                    Quantity = 1
                }
            }
        };

        var result = await service.CreateOrderAsync(
            order,
            CancellationToken.None);

        Assert.AreEqual(800, result.Total);
        Assert.AreEqual("Created", result.Status);
    }

    [TestMethod]
    public async Task CreateOrderAsync_RejectsEmptyItems()
    {
        var repository = new FakeOrderRepository();

        var service = new OrderService(
            repository,
            new OrderPricingStrategy(),
            NullLogger<OrderService>.Instance);

        var order = new Order
        {
            Customer = new Customer
            {
                Name = "Sumit",
                Email = "sumit@example.com"
            },
            Items = new List<OrderItem>()
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => service.CreateOrderAsync(
                order,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateOrderAsync_AppliesDiscount_WhenTotalExceeds1000()
    {
        var repository = new FakeOrderRepository();

        var service = new OrderService(
            repository,
            new OrderPricingStrategy(),
            NullLogger<OrderService>.Instance);

        var order = new Order
        {
            Customer = new Customer
            {
                Name = "Sumit",
                Email = "sumit@example.com"
            },
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ProductName = "Laptop",
                    Price = 1200,
                    Quantity = 1
                }
            }
        };

        var result = await service.CreateOrderAsync(
            order,
            CancellationToken.None);

        Assert.AreEqual(1080, result.Total);
    }

    [TestMethod]
    public async Task CreateOrderAsync_RejectsNegativeQuantity()
    {
        var repository = new FakeOrderRepository();

        var service = new OrderService(
            repository,
            new OrderPricingStrategy(),
            NullLogger<OrderService>.Instance);

        var order = new Order
        {
            Customer = new Customer
            {
                Name = "Sumit",
                Email = "sumit@example.com"
            },
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ProductName = "Laptop",
                    Price = 800,
                    Quantity = -1
                }
            }
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => service.CreateOrderAsync(
                order,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateOrderAsync_RejectsMissingCustomerName()
    {
        var repository = new FakeOrderRepository();

        var service = new OrderService(
            repository,
            new OrderPricingStrategy(),
            NullLogger<OrderService>.Instance);

        var order = new Order
        {
            Customer = new Customer
            {
                Name = "",
                Email = "sumit@example.com"
            },
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ProductName = "Laptop",
                    Price = 800,
                    Quantity = 1
                }
            }
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => service.CreateOrderAsync(
                order,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateOrderAsync_RejectsMissingCustomerEmail()
    {
        var repository = new FakeOrderRepository();

        var service = new OrderService(
            repository,
            new OrderPricingStrategy(),
            NullLogger<OrderService>.Instance);

        var order = new Order
        {
            Customer = new Customer
            {
                Name = "Sumit",
                Email = ""
            },
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ProductName = "Laptop",
                    Price = 800,
                    Quantity = 1
                }
            }
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => service.CreateOrderAsync(
                order,
                CancellationToken.None));
    }

    private class FakeOrderRepository : IOrderRepository
    {
        public Task<Order> AddAsync(
            Order order,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(order);
        }
    }
}
