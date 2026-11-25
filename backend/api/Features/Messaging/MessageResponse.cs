
public class MessageResponse
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public MessageSenderType SenderType { get; set; }

    public static MessageResponse FromMessage(Message message)
    {
        return new MessageResponse
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            Content = message.Content,
            SentAt = message.SentAt,
            SenderType = message.SenderType
        };
    }
}
