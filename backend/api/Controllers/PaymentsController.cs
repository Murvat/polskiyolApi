using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Authorize(Roles = "Teacher,Student")]
[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private const string MonthlyPaymentIndex = "IX_Payments_StudentId_Year_Month";
    private const string DuplicatePaymentMessage = "Payment for this month already exists.";

    private readonly AppDbContext _context;

    public PaymentsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/payments?year=2026&month=10
    // Optional: &status=Delayed&studentId=5
    [HttpGet]
    public async Task<ActionResult<List<PaymentResponse>>> GetPayments(
        [FromQuery] int year,
        [FromQuery] int? month,
        [FromQuery] PaymentStatus? status,
        [FromQuery] int? studentId,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        if (!ValidPeriod(year, month))
            return BadRequest("Invalid year or month.");

        if (status.HasValue && !Enum.IsDefined(status.Value))
            return BadRequest("Invalid payment status.");

        if (studentId.HasValue && studentId.Value <= 0)
            return BadRequest("Invalid student ID.");

        var today = Today();

        var query = PeriodQuery(currentUserId, year, month);

        if (studentId.HasValue)
        {
            query = query.Where(
                p => p.StudentId == studentId.Value);
        }

        if (status.HasValue)
        {
            query = status.Value switch
            {
                PaymentStatus.Paid => query.Where(
                    p => p.PaidAt != null),

                PaymentStatus.Due => query.Where(
                    p => p.PaidAt == null &&
                         p.DueDate >= today),

                PaymentStatus.Delayed => query.Where(
                    p => p.PaidAt == null &&
                         p.DueDate < today),

                _ => query.Where(p => false)
            };
        }

        var payments = await query
            .Include(p => p.Student)
            .OrderBy(p => p.DueDate)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        return Ok(payments
            .Select(p => PaymentResponse.FromPayment(p, today))
            .ToList());
    }

    // GET /api/payments/summary?year=2026&month=10
    // Optional: &studentId=5
    [HttpGet("summary")]
    public async Task<ActionResult<PaymentSummaryResponse>> GetSummary(
        [FromQuery] int year,
        [FromQuery] int? month,
        [FromQuery] int? studentId,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        if (!ValidPeriod(year, month))
            return BadRequest("Invalid year or month.");

        if (studentId.HasValue && studentId.Value <= 0)
            return BadRequest("Invalid student ID.");

        var query = PeriodQuery(currentUserId, year, month);

        if (studentId.HasValue)
        {
            query = query.Where(
                p => p.StudentId == studentId.Value);
        }

        return Ok(await CalculateSummaryAsync(
            query, Today(), ct));
    }

    // GET /api/payments/10
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PaymentResponse>> GetPayment(
        int id,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var payment = await GetAccessiblePayments(currentUserId)
            .Include(p => p.Student)
            .SingleOrDefaultAsync(p => p.Id == id, ct);

        if (payment is null)
            return NotFound();

        return Ok(PaymentResponse.FromPayment(payment, Today()));
    }

    // POST /api/payments
    [Authorize(Roles = "Teacher")]
    [HttpPost]
    public async Task<ActionResult<PaymentResponse>> CreatePayment(
        [FromBody] PaymentAddRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        if (!ValidPeriod(request.Year, request.Month))
            return BadRequest("Invalid year or month.");

        if (request.ExpectedAmount <= 0)
            return BadRequest("Amount must be greater than zero.");

        if (request.DueDate == default)
            return BadRequest("A valid due date is required.");

        var teacher = await _context.Teachers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                t => t.UserId == currentUserId, ct);

        if (teacher is null)
            return Forbid();

        var student = await _context.Students
            .AsTracking()
            .SingleOrDefaultAsync(s =>
                s.Id == request.StudentId &&
                s.TeacherId == teacher.Id, ct);

        if (student is null)
            return NotFound();

        var exists = await _context.Payments.AnyAsync(p =>
            p.StudentId == student.Id &&
            p.Year == request.Year &&
            p.Month == request.Month, ct);

        if (exists)
            return Conflict(DuplicatePaymentMessage);

        var payment = new Payment
        {
            TeacherId = teacher.Id,
            StudentId = student.Id,
            Student = student,
            Year = request.Year,
            Month = request.Month,
            ExpectedAmount = request.ExpectedAmount,
            DueDate = request.DueDate,
            CreatedAt = DateTime.UtcNow
        };

        _context.Payments.Add(payment);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException { Number: 2601 } sqlException &&
            sqlException.Message.Contains($"'{MonthlyPaymentIndex}'", StringComparison.Ordinal))
        {
            return Conflict(DuplicatePaymentMessage);
        }

        return CreatedAtAction(
            nameof(GetPayment),
            new { id = payment.Id },
            PaymentResponse.FromPayment(payment, Today()));
    }

    // PATCH /api/payments/10/paid
    [Authorize(Roles = "Teacher")]
    [HttpPatch("{id:int}/paid")]
    public async Task<IActionResult> MarkAsPaid(
        int id,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var payment = await _context.Payments
            .AsTracking()
            .SingleOrDefaultAsync(p =>
                p.Id == id &&
                p.Teacher.UserId == currentUserId && p.Student.TeacherId == p.TeacherId, ct);

        if (payment is null)
            return NotFound();

        if (payment.PaidAt == null)
        {
            payment.PaidAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    private IQueryable<Payment> GetAccessiblePayments(
        Guid currentUserId)
    {
        var query = _context.Payments.AsNoTracking();

        if (User.IsInRole("Teacher"))
        {
            return query.Where(
                p => p.Teacher.UserId == currentUserId && p.Student.TeacherId == p.TeacherId);
        }

        if (User.IsInRole("Student"))
        {
            return query.Where(
                p => p.Student.UserId == currentUserId);
        }

        return query.Where(p => false);
    }

    private IQueryable<Payment> PeriodQuery(
        Guid currentUserId,
        int year,
        int? month)
    {
        var query = GetAccessiblePayments(currentUserId)
            .Where(p => p.Year == year);

        if (month.HasValue)
        {
            query = query.Where(
                p => p.Month == month.Value);
        }

        return query;
    }

    private static bool ValidPeriod(int year, int? month)
    {
        return year is >= 1 and <= 9999 &&
               (!month.HasValue ||
                month.Value is >= 1 and <= 12);
    }

    private static DateOnly Today()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            "Europe/Warsaw");

        var localTime = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, timeZone);

        return DateOnly.FromDateTime(localTime);
    }

    private static async Task<PaymentSummaryResponse> CalculateSummaryAsync(
        IQueryable<Payment> query,
        DateOnly today,
        CancellationToken ct)
    {
        var summary = await query
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
            })
            .FirstOrDefaultAsync(ct);

        return summary ?? new PaymentSummaryResponse();
    }
}