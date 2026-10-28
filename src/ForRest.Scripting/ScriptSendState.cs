namespace ForRest.Scripting;

internal sealed class ScriptSendState(ScriptResponseApi responseApi)
{
    #region Private Fields

    private readonly object gate = new();
    private readonly List<ResponseSnapshot> responses = [];
    private readonly List<RequestSnapshot> requests = [];
    private int count;
    private ResponseSnapshot? lastResponse;

    #endregion

    #region Properties

    public int Count => Volatile.Read(ref count);

    public ResponseSnapshot? LastResponse => Volatile.Read(ref lastResponse);

    public IReadOnlyList<ResponseSnapshot> Responses
    {
        get
        {
            lock (gate)
            {
                return [.. responses];
            }
        }
    }

    public IReadOnlyList<RequestSnapshot> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

    #endregion

    #region Public Methods

    public bool TryReserve(int maximum)
    {
        var current = Count;
        while (current < maximum)
        {
            var observed = Interlocked.CompareExchange(ref count, current + 1, current);
            if (observed == current)
            {
                return true;
            }

            current = observed;
        }

        return false;
    }

    public void Record(ResponseSnapshot? response, RequestSnapshot request)
    {
        lock (gate)
        {
            if (response is not null)
            {
                responses.Add(response);
                requests.Add(request);
            }

            responseApi.Update(response);
            Volatile.Write(ref lastResponse, response);
        }
    }

    #endregion
}
