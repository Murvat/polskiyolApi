using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace PolskiYol.Tests.Infrastructure;

// EF's async query operators can exercise ordinary LINQ against seeded data.
internal sealed class MemoryQuery<T> : EnumerableQuery<T>, IQueryable<T>, IAsyncEnumerable<T>
{
    public MemoryQuery(IEnumerable<T> items) : base(items) { }
    public MemoryQuery(Expression expression) : base(expression) { }

    IQueryProvider IQueryable.Provider => new AsyncProvider(this);

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default) =>
        new Enumerator(((IEnumerable<T>)this).GetEnumerator(), ct);

    private sealed class Enumerator(IEnumerator<T> inner, CancellationToken ct) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;
        public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
        public ValueTask<bool> MoveNextAsync()
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(inner.MoveNext());
        }
    }

    private sealed class AsyncProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        private static readonly MethodInfo FromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!;

        public IQueryable CreateQuery(Expression expression)
        {
            var elementType = inner.CreateQuery(expression).ElementType;
            return (IQueryable)Activator.CreateInstance(typeof(MemoryQuery<>).MakeGenericType(elementType), expression)!;
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new MemoryQuery<TElement>(expression);
        public object? Execute(Expression expression) => inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resultType = typeof(TResult).GetGenericArguments().Single();
            return (TResult)FromResult.MakeGenericMethod(resultType).Invoke(null, [inner.Execute(expression)])!;
        }
    }
}
