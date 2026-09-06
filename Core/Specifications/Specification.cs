using System.Linq.Expressions;

namespace Core.Specifications;

public class Specification<T> : ISpecification<T> {
    public Expression<Func<T, bool>> Criteria { get; private set; }
    public List<Expression<Func<T, object>>> Includes { get; private set; }
    public List<string> IncludeStrings { get; private set; }
    public Expression<Func<T, object>> OrderBy { get; private set; }
    public Expression<Func<T, object>> OrderByDescending { get; private set; }
    public int Take { get; private set; }
    public int Skip { get; private set; }
    public bool IsPagingEnabled { get; private set; }

    public Specification() {
        Includes = new List<Expression<Func<T, object>>>();
        IncludeStrings = new List<string>();
    }

    public Specification(Expression<Func<T, bool>> criteria) : this() {
        Criteria = criteria;
    }
    
    public void AddInclude(Expression<Func<T, object>> includeExpression) {
        Includes.Add(includeExpression);
    }

    public void AddInclude(string includeString) {
        IncludeStrings.Add(includeString);
    }

    public void ApplyOrderBy(Expression<Func<T, object>> orderByExpression) {
        OrderBy = orderByExpression;
    }

    public void ApplyOrderByDescending(Expression<Func<T, object>> orderByDescendingExpression) {
        OrderByDescending = orderByDescendingExpression;
    }

    public void ApplyPaging(int skip, int take) {
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }
}
