using System.Linq.Expressions;

namespace WifiWatch.Data.Helpers;

public static class QueryableExtensions
{
    public static IOrderedQueryable<TSource> OrderByColumn<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> keySelector,
        bool descending
    ) => descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector);
}
