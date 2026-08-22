namespace CqrsLiteDemo.Models.Write;

public class Quote
{
    public int Id { get; set; }

    public int AuthorId { get; set; }

    public string Text { get; set; } = "";
}
