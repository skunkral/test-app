using System.Threading.Tasks;
using Viana.Core.Interfaces;

namespace Viana.Infrastructure.Services
{
    public class MqttService : IMqttService
    {
        public async Task ConnectAsync()
        {
            // TODO: Implement MQTT connection
        }

        public async Task SubscribeToTopicAsync(string topic)
        {
            // TODO: Implement subscription
        }
    }
}