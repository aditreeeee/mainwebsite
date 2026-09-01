using System.Linq.Expressions;

namespace eGlobeSolutions.Web.Extensions;

public static class QueryableExtensions
{
    /// <summary>
    /// Filters a query to rows whose id is in the given list, without using
    /// <c>List&lt;int&gt;.Contains()</c>. EF Core 8 translates that to
    /// <c>OPENJSON(@param) WITH ([value] int '$')</c>, which requires SQL
    /// Server compatibility level 130+, and throws "Incorrect syntax near
    /// '$'" at level 100 (this project's required compat level, see
    /// README). Building an explicit <c>Id == a || Id == b || ...</c>
    /// expression instead compiles to a plain OR chain / literal IN list,
    /// which has always been supported. Only meant for small id lists (a
    /// single page's worth), the way it's used in EnquiriesController and
    /// ActivityLogController.
    /// </summary>
    public static IQueryable<T> WhereIdIn<T>(this IQueryable<T> query, Expression<Func<T, int>> idSelector, IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) return query.Where(_ => false);

        var parameter = idSelector.Parameters[0];
        Expression? body = null;

        foreach (var id in ids)
        {
            var equals = Expression.Equal(idSelector.Body, Expression.Constant(id));
            body = body is null ? equals : Expression.OrElse(body, equals);
        }

        var lambda = Expression.Lambda<Func<T, bool>>(body!, parameter);
        return query.Where(lambda);
    }
}
