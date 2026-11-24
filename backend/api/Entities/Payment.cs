
public class Payment
{
    public int Id { get; set; }

    public int TeacherId { get; set; }
    public Teacher Teacher { get; set; } = null!;

    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public int Year { get; set; }
    public int Month { get; set; }

    public decimal ExpectedAmount { get; set; }

    public DateOnly DueDate { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    public string? PaymentProofStorageKey { get; set; }
}
