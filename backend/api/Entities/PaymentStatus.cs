
using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentStatus
{
    Due,
    Paid,
    Delayed
}
