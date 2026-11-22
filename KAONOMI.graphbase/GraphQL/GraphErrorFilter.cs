using HotChocolate;
using KAONOMI.graphbase.Storage;

namespace KAONOMI.graphbase.GraphQL;

/// <summary>
/// What a request asked that the graph refuses (<see cref="GraphException"/>) is told as it is, with its code
/// (<c>NOT_FOUND</c>, <c>INVALID_ARGUMENT</c>…); any other failure is logged, and told without its details.
/// </summary>
public sealed class GraphErrorFilter(GraphLog log) : IErrorFilter
{
    public IError OnError(IError error)
    {
        switch (error.Exception)
        {
            case GraphException refused:
                return error.WithMessage(refused.Message).WithCode(refused.Code);
            case ObjectDisposedException:
                return error.WithMessage("the server is stopping").WithCode("STOPPING");
            case { } failure:
                log.Error("graphql", $"{error.Path}: {failure.GetType().Name}: {failure.Message}");
                return error;
            default:
                return error;
        }
    }
}
