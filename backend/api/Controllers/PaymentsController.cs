using PolskiYol.Data;
using PolskiYol.Features;
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

    private readonly IApiData _data;

    public PaymentsController(IApiData data)
    {
        _data = data;
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

        if (!PaymentService.ValidPeriod(year, month))
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

        if (!PaymentService.ValidPeriod(year, month))
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

        if (!PaymentService.ValidPeriod(request.Year, request.Month))
            return BadRequest("Invalid year or month.");

        if (request.ExpectedAmount <= 0)
            return BadRequest("Amount must be greater than zero.");

        if (request.DueDate == default)
            return BadRequest("A valid due date is required.");

        var teacher = await _data.Query<Teacher>()
            .SingleOrDefaultAsync(
                t => t.UserId == currentUserId, ct);

        if (teacher is null)
            return Forbid();

        var student = await _data.Query<Student>(track: true)
            .SingleOrDefaultAsync(s =>
                s.Id == request.StudentId &&
                s.TeacherId == teacher.Id, ct);

        if (student is null)
            return NotFound();

        var exists = await PaymentService.ForMonth(_data.Query<Payment>(), student.Id, request.Year, request.Month).AnyAsync(ct);

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

        _data.Add(payment);

        try
        {
            await _data.SaveChangesAsync(ct);
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

        var payment = await _data.Query<Payment>(track: true)
            .SingleOrDefaultAsync(p =>
                p.Id == id &&
                p.Teacher.UserId == currentUserId && p.Student.TeacherId == p.TeacherId, ct);

        if (payment is null)
            return NotFound();

        if (PaymentService.MarkPaid(payment, DateTime.UtcNow))
            await _data.SaveChangesAsync(ct);

        return NoContent();
    }

    private IQueryable<Payment> GetAccessiblePayments(Guid currentUserId) =>
        PaymentService.GetAccessiblePayments(currentUserId, User, _data.Query<Payment>());

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
        var summary = await PaymentService.Summaries(query, today).FirstOrDefaultAsync(ct);

        return summary ?? new PaymentSummaryResponse();
    }
}