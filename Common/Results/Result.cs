namespace Common.Results;

/// <summary>
/// Represents the result of an operation
/// </summary>
public class Result
{
    public bool IsSuccess { get; set; }
    public bool IsFailure => !IsSuccess;
    public string? ErrorMessage { get; set; }
    public List<string> Errors { get; set; } = new();

    protected Result(bool isSuccess, string? errorMessage = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        if (!string.IsNullOrEmpty(errorMessage))
        {
            Errors.Add(errorMessage);
        }
    }

    public static Result Success()
    {
        return new Result(true);
    }

    public static Result Failure(string errorMessage)
    {
        return new Result(false, errorMessage);
    }

    public static Result Failure(IEnumerable<string> errors)
    {
        return new Result(false)
        {
            Errors = errors.ToList()
        };
    }

    public Result AddError(string error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            Errors.Add(error);
        }
        return this;
    }

    public Result AddErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Errors.Add(error);
            }
        }
        return this;
    }
}

/// <summary>
/// Represents the result of an operation with a value
/// </summary>
/// <typeparam name="T">The type of the value</typeparam>
public class Result<T> : Result
{
    public T? Value { get; set; }

    protected Result(bool isSuccess, T? value = default, string? errorMessage = null) 
        : base(isSuccess, errorMessage)
    {
        Value = value;
    }

    public static Result<T> Success(T value)
    {
        return new Result<T>(true, value);
    }

    public new static Result<T> Failure(string errorMessage)
    {
        return new Result<T>(false, default, errorMessage);
    }

    public new static Result<T> Failure(IEnumerable<string> errors)
    {
        return new Result<T>(false, default)
        {
            Errors = errors.ToList()
        };
    }

    public new Result<T> AddError(string error)
    {
        base.AddError(error);
        return this;
    }

    public new Result<T> AddErrors(IEnumerable<string> errors)
    {
        base.AddErrors(errors);
        return this;
    }
}
