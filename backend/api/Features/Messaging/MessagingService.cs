using System.Security.Claims;

namespace PolskiYol.Features;

public static class MessagingService
{
    public static IQueryable<Conversation> GetAccessibleConversations(
        Guid currentUserId, ClaimsPrincipal principal, IQueryable<Conversation> source)
    {
        var query = source
            .Where(c => c.Student.TeacherId == c.TeacherId);

        if (principal.IsInRole("Teacher"))
        {
            return query.Where(c =>
                c.Teacher.UserId == currentUserId);
        }

        if (principal.IsInRole("Student"))
        {
            return query.Where(c =>
                c.Student.UserId == currentUserId);
        }

        return query.Where(c => false);
    }
 
    public static Message? CreateMessage(int conversationId, string content, ClaimsPrincipal principal, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return new Message
        {
            ConversationId = conversationId, Content = content.Trim(), SentAt = utcNow,
            SenderType = principal.IsInRole("Teacher") ? MessageSenderType.Teacher : MessageSenderType.Student
        };
    }
}
