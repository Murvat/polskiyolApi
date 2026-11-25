namespace PolskiYol.Features;

public static class CursorPage
{
    public static void SetNextCursor<T>(
        List<T> items, int limit, Func<T, int> getId, HttpResponse response)
    {
        if (items.Count <= limit)
            return;

        items.RemoveAt(limit);
        response.Headers["X-Next-Before-Id"] =
            getId(items[^1]).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
