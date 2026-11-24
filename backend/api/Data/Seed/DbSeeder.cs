using Microsoft.EntityFrameworkCore;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        // If teachers already exist, assume the development database
        // has already been seeded.
        if (await context.Teachers.AnyAsync())
        {
            return;
        }

        var teacher1 = new Teacher
        {
            FirstName = "Anna",
            LastName = "Kowalska",
            PhoneNumber = "+48111222333"
        };

        var teacher2 = new Teacher
        {
            FirstName = "Piotr",
            LastName = "Nowak",
            PhoneNumber = "+48444555666"
        };

        var student1 = new Student
        {
            FirstName = "Jan",
            LastName = "Wiśniewski",
            PhoneNumber = "+48777111222",
            Teacher = teacher1
        };

        var student2 = new Student
        {
            FirstName = "Maria",
            LastName = "Zielińska",
            PhoneNumber = "+48777333444",
            Teacher = teacher1
        };

        var student3 = new Student
        {
            FirstName = "Adam",
            LastName = "Wójcik",
            PhoneNumber = "+48777555666",
            Teacher = teacher2
        };

        var lesson1 = new Lesson
        {
            Topic = "Polish grammar – cases",
            DurationMinutes = 60,
            StartsAt = DateTime.UtcNow.AddDays(1),
            Teacher = teacher1,
            Students = [student1, student2]
        };

        var lesson2 = new Lesson
        {
            Topic = "Polish conversation practice",
            DurationMinutes = 45,
            StartsAt = DateTime.UtcNow.AddDays(2),
            Teacher = teacher2,
            Students = [student3]
        };

        var material = new Material
        {
            Teacher = teacher1,
            FileName = "polish-cases.pdf",
            Description = "Introduction to Polish grammatical cases.",
            FileStorageKey = "development/materials/polish-cases.pdf"
        };

        var feedPost = new FeedPost
        {
            Teacher = teacher1,
            Title = "Welcome to PolskiYol",
            Content = "Welcome! New learning materials are now available.",
            CreatedAt = DateTime.UtcNow
        };

        var conversation = new Conversation
        {
            Teacher = teacher1,
            Student = student1,
            Messages =
            [
                new Message
                {
                    Content = "Dzień dobry! When is our next lesson?",
                    SentAt = DateTime.UtcNow.AddMinutes(-10),
                    SenderType = MessageSenderType.Student
                },
                new Message
                {
                    Content = "Dzień dobry! Our next lesson is tomorrow.",
                    SentAt = DateTime.UtcNow.AddMinutes(-5),
                    SenderType = MessageSenderType.Teacher
                }
            ]
        };

        var payment = new Payment
        {
            StudentId = 1,
            TeacherId = 1,
            Year = 2026,
            Month = 10,
            ExpectedAmount = 500m,
            DueDate = new DateOnly(2026, 10, 25),
            CreatedAt = DateTime.UtcNow,
            PaidAt = null
        };
        context.AddRange(
            teacher1,
            teacher2,
            student1,
            student2,
            student3,
            lesson1,
            lesson2,
            material,
            feedPost,
            conversation,
            payment);

        await context.SaveChangesAsync();
    }
}