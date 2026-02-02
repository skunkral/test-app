using System.Threading.Tasks;

namespace Viana.Core.Interfaces
{
    public interface IUpdateManager
    {
        Task CheckForUpdatesAsync();
    }
}