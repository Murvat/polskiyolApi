public class ConversationResponse
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int TeacherId { get; set; }

    public string StudentFirstName { get; set; } = string.Empty;
    public string StudentLastName { get; set; } = string.Empty;
}