using PolskiYol.Data;

namespace PolskiYol.Tests.Infrastructure;

internal sealed class MemoryApiData : IApiData
{
    private readonly Dictionary<Type, object> _tables = new();
    private int _nextId;

    public List<T> Rows<T>() where T : class
    {
        if (!_tables.TryGetValue(typeof(T), out var rows))
            _tables[typeof(T)] = rows = new List<T>();

        return (List<T>)rows;
    }

    public IQueryable<T> Query<T>(bool track = false) where T : class => new MemoryQuery<T>(Rows<T>());
    
    public void Add<T>(T entity) where T : class
    {
        var id = ++_nextId;
        switch (entity)
        {
            case Teacher teacher:
                teacher.Id = id;
                break;
            case Student student:
                student.Id = id;
                break;
            case Conversation conversation:
                conversation.Id = id;
                conversation.Teacher = Rows<Teacher>().Single(t => t.Id == conversation.TeacherId);
                conversation.Student = Rows<Student>().Single(s => s.Id == conversation.StudentId);
                break;
            case Message message:
                message.Id = id;
                message.Conversation = Rows<Conversation>().Single(c => c.Id == message.ConversationId);
                break;
            case FeedPost post:
                post.Id = id;
                post.Teacher = Rows<Teacher>().Single(t => t.Id == post.TeacherId);
                break;
            case Lesson lesson:
                lesson.Id = id;
                lesson.Teacher = Rows<Teacher>().Single(t => t.Id == lesson.TeacherId);
                break;
            case Payment payment:
                payment.Id = id;
                payment.Teacher = Rows<Teacher>().Single(t => t.Id == payment.TeacherId);
                break;
            case Material material:
                material.Id = id;
                material.Teacher = Rows<Teacher>().Single(t => t.Id == material.TeacherId);
                break;
            default:
                throw new InvalidOperationException($"No fixture mapping for {typeof(T).Name}.");
        }

        Rows<T>().Add(entity);
    }

    public void Remove<T>(T entity) where T : class => Rows<T>().Remove(entity);
    public bool FailNextSave { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        if (!FailNextSave)
            return Task.FromResult(1);

        FailNextSave = false;
        return Task.FromException<int>(new InvalidOperationException("Test write failed."));
    }
}
