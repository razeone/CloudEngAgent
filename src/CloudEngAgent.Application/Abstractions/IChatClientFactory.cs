using CloudEngAgent.Domain.Backends;
using Microsoft.Extensions.AI;

namespace CloudEngAgent.Application.Abstractions;

public interface IChatClientFactory
{
    IChatClient Create(BackendId backend);
}
