namespace OrderApi.Models;

public class Order
{
    public int Id { get; set; }

    public Customer Customer { get; set; } = new();

    public List<OrderItem> Items { get; set; } = new();

    public decimal Total { get; set; }

    public string Status { get; set; } = "Pending";
}

public class Customer
{
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public Address Address { get; set; } = new();
}

public class Address
{
    public string City { get; set; } = string.Empty;
}

public class OrderItem
{
    public string ProductName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int Quantity { get; set; }
}