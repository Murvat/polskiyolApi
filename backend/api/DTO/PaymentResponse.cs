
public class PaymentResponse
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public int StudentId { get; set; }

    public string StudentName { get; set; } = string.Empty;

    public int Year { get; set; }
    public int Month { get; set; }

    public decimal ExpectedAmount { get; set; }

    public DateOnly DueDate { get; set; }
    public DateTime? PaidAt { get; set; }

    public PaymentStatus Status { get; set; }

    public static PaymentStatus GetStatus(
        Payment payment,
        DateOnly today)
    {
        if (payment.PaidAt.HasValue)
            return PaymentStatus.Paid;

        if (payment.DueDate < today)
            return PaymentStatus.Delayed;

        return PaymentStatus.Due;
    }

    public static PaymentResponse FromPayment(
        Payment payment,
        DateOnly today)
    {
        return new PaymentResponse
        {
            Id = payment.Id,
            TeacherId = payment.TeacherId,
            StudentId = payment.StudentId,
            StudentName =
                $"{payment.Student.FirstName} {payment.Student.LastName}",
            Year = payment.Year,
            Month = payment.Month,
            ExpectedAmount = payment.ExpectedAmount,
            DueDate = payment.DueDate,
            PaidAt = payment.PaidAt,
            Status = GetStatus(payment, today)
        };
    }
}
