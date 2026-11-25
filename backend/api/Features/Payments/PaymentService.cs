using System.Security.Claims;

namespace PolskiYol.Features;

public static class PaymentService
{
    public static IQueryable<Payment> GetAccessiblePayments(
        Guid currentUserId, ClaimsPrincipal principal, IQueryable<Payment> source)
    {
        var query = source;

        if (principal.IsInRole("Teacher"))
        {
            return query.Where(
                p => p.Teacher.UserId == currentUserId && p.Student.TeacherId == p.TeacherId);
        }

        if (principal.IsInRole("Student"))
        {
            return query.Where(
                p => p.Student.UserId == currentUserId);
        }

        return query.Where(p => false);
    }

    public static bool ValidPeriod(int year, int? month)
    {
        return year is >= 1 and <= 9999 &&
               (!month.HasValue ||
                month.Value is >= 1 and <= 12);
    }

    public static IQueryable<PaymentSummaryResponse> Summaries(IQueryable<Payment> query, DateOnly today) =>
        query
            .GroupBy(p => 1)
            .Select(g => new PaymentSummaryResponse
            {
                TotalExpected = g.Sum(p => p.ExpectedAmount),

                TotalPaid = g.Sum(p =>
                    p.PaidAt != null ? p.ExpectedAmount : 0m),

                TotalDelayed = g.Sum(p =>
                    p.PaidAt == null && p.DueDate < today
                        ? p.ExpectedAmount
                        : 0m),

                PaidCount = g.Count(p => p.PaidAt != null),

                DueCount = g.Count(p =>
                    p.PaidAt == null && p.DueDate >= today),

                DelayedCount = g.Count(p =>
                    p.PaidAt == null && p.DueDate < today)
            });

    public static IQueryable<Payment> ForMonth(IQueryable<Payment> query, int studentId, int year, int month) =>
        query.Where(payment => payment.StudentId == studentId && payment.Year == year && payment.Month == month);

    public static bool MarkPaid(Payment payment, DateTime utcNow)
    {
        if (payment.PaidAt is not null)
            return false;

        payment.PaidAt = utcNow;
        return true;
    }
}
