public class Message
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public Conversation Conversation { get; set; }
    public string Content { get; set; }
    public DateTime SentAt { get; set; }
    public MessageSenderType SenderType { get; set; }

}

public enum MessageSenderType
{
    Student,
    Teacher
}