namespace CqrsLiteDemo.Commands;

public class AddQuoteCommand
{
    public int AuthorId { get; set; }

    public string Text { get; set; } = "";
}
