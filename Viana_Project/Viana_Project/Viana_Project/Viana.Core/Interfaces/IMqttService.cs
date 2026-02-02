using System.Threading.Tasks;

namespace Viana.Core.Interfaces
{
    public interface IMqttService
    {
        Task ConnectAsync();
        Task SubscribeToTopicAsync(string topic);
    }
}