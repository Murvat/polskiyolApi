
public class PaymentSummaryResponse
{
    public decimal TotalExpected { get; set; }
    public decimal TotalPaid { get; set; }

    public decimal TotalOutstanding =>
        TotalExpected - TotalPaid;

    public decimal TotalDelayed { get; set; }

    public int PaidCount { get; set; }
    public int DueCount { get; set; }
    public int DelayedCount { get; set; }
}
