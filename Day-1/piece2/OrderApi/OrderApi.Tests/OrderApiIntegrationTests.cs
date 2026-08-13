using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrderApi.Models;

namespace OrderApi.Tests;

[TestClass]
public class OrderApiIntegrationTests
{
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    [TestInitialize]
    public void Setup()
    {
        factory = new WebApplicationFactory<Program>();
        client = factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        client.Dispose();
        factory.Dispose();
    }

    [TestMethod]
    public async Task PostOrder_ReturnsCreated()
    {
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

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            order);

        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode);
    }
}