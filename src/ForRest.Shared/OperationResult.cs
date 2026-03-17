namespace ForRest.Shared;

public class OperationResult
{
    #region Constructors

    protected OperationResult(bool succeeded, string? errorMessage)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
    }

    #endregion

    #region Properties

    public bool Succeeded { get; }

    public string? ErrorMessage { get; }

    #endregion

    #region Public Methods

    public static OperationResult Success()
    {
        return new(true, null);
    }

    public static OperationResult Fail(string errorMessage)
    {
        return new(false, errorMessage);
    }

    #endregion
}

public sealed class OperationResult<T> : OperationResult
{
    #region Constructors

    private OperationResult(bool succeeded, string? errorMessage, T? value)
        : base(succeeded, errorMessage)
    {
        Value = value;
    }

    #endregion

    #region Properties

    public T? Value { get; }

    #endregion

    #region Public Methods

    public static OperationResult<T> Success(T value)
    {
        return new(true, null, value);
    }

    public static new OperationResult<T> Fail(string errorMessage)
    {
        return new(false, errorMessage, default);
    }

    #endregion
}
