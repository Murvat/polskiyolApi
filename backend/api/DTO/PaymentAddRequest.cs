
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class PaymentAddRequest : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int StudentId { get; set; }

    [Range(1, 9999)]
    public int Year { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }

    [Range(typeof(decimal), "0.01", "999999999.99")]
    public decimal ExpectedAmount { get; set; }

    public DateOnly DueDate { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (DueDate == default)
        {
            yield return new ValidationResult(
                "Due date is required.",
                new[] { nameof(DueDate) });
        }
    }
}
